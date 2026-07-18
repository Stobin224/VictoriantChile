from __future__ import annotations

import os
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path

from .models import ProcessResult, RawProcessResult


def truncate(text: str, limit: int = 20000) -> str:
    if len(text) <= limit:
        return text
    return text[:limit] + "\n[truncated]\n"


class ProcessRunner:
    def run(self, argv: list[str], cwd: Path, timeout_seconds: int, *, env: dict[str, str] | None = None) -> ProcessResult:
        if timeout_seconds <= 0:
            raise ValueError("timeout_seconds must be positive")
        popen_kwargs = {
            "cwd": cwd,
            "stdout": subprocess.PIPE,
            "stderr": subprocess.PIPE,
            "text": True,
            "shell": False,
        }
        if env is not None:
            popen_kwargs["env"] = env
        if sys.platform.startswith("win"):
            popen_kwargs["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP
        else:
            popen_kwargs["start_new_session"] = True
        try:
            process = subprocess.Popen(argv, **popen_kwargs)
        except OSError as exc:
            return ProcessResult(tuple(argv), None, "", str(exc))
        try:
            stdout, stderr = process.communicate(timeout=timeout_seconds)
            return ProcessResult(tuple(argv), process.returncode, truncate(stdout or ""), truncate(stderr or ""))
        except subprocess.TimeoutExpired:
            self.terminate_tree(process)
            try:
                stdout, stderr = process.communicate(timeout=5)
            except subprocess.TimeoutExpired:
                if process.poll() is None:
                    process.kill()
                stdout, stderr = process.communicate(timeout=5)
            return ProcessResult(tuple(argv), None, truncate(stdout or ""), truncate((stderr or "") + "\nprocess timed out"), True)

    def run_bytes(
        self,
        argv: list[str],
        cwd: Path,
        timeout_seconds: int,
        *,
        max_stdout_bytes: int,
        max_stderr_bytes: int,
        env: dict[str, str] | None = None,
    ) -> RawProcessResult:
        if timeout_seconds <= 0:
            raise ValueError("timeout_seconds must be positive")
        if max_stdout_bytes <= 0 or max_stderr_bytes <= 0:
            raise ValueError("byte limits must be positive")
        popen_kwargs = {
            "cwd": cwd,
            "stdout": subprocess.PIPE,
            "stderr": subprocess.PIPE,
            "shell": False,
        }
        if env is not None:
            popen_kwargs["env"] = env
        if sys.platform.startswith("win"):
            popen_kwargs["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP
        else:
            popen_kwargs["start_new_session"] = True
        try:
            process = subprocess.Popen(argv, **popen_kwargs)
        except OSError as exc:
            return RawProcessResult(tuple(argv), None, b"", str(exc).encode("utf-8", errors="replace"))

        stdout_chunks: list[bytes] = []
        stderr_chunks: list[bytes] = []
        limit_event = threading.Event()
        stdout_limited = threading.Event()
        stderr_limited = threading.Event()

        def pump(stream, chunks: list[bytes], limit: int, limited: threading.Event) -> None:
            total = 0
            try:
                while True:
                    chunk = stream.read(4096)
                    if not chunk:
                        break
                    remaining = limit - total
                    if remaining <= 0:
                        limited.set()
                        limit_event.set()
                        break
                    if len(chunk) > remaining:
                        chunks.append(chunk[:remaining])
                        limited.set()
                        limit_event.set()
                        break
                    chunks.append(chunk)
                    total += len(chunk)
            finally:
                try:
                    stream.close()
                except OSError:
                    pass

        stdout_thread = threading.Thread(target=pump, args=(process.stdout, stdout_chunks, max_stdout_bytes, stdout_limited), daemon=True)
        stderr_thread = threading.Thread(target=pump, args=(process.stderr, stderr_chunks, max_stderr_bytes, stderr_limited), daemon=True)
        stdout_thread.start()
        stderr_thread.start()

        deadline = time.monotonic() + timeout_seconds
        timed_out = False
        while process.poll() is None:
            if limit_event.is_set():
                self.terminate_tree(process)
                break
            if time.monotonic() >= deadline:
                timed_out = True
                self.terminate_tree(process)
                break
            time.sleep(0.02)

        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            if process.poll() is None:
                process.kill()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                pass

        stdout_thread.join(timeout=5)
        stderr_thread.join(timeout=5)
        stdout = b"".join(stdout_chunks)
        stderr = b"".join(stderr_chunks)
        if timed_out:
            stderr += b"\nprocess timed out"
        return RawProcessResult(
            tuple(argv),
            process.returncode if not timed_out and not limit_event.is_set() else None,
            stdout,
            stderr,
            timed_out,
            stdout_limited.is_set(),
            stderr_limited.is_set(),
        )

    def terminate_tree(self, process: subprocess.Popen[str]) -> None:
        if sys.platform.startswith("win"):
            try:
                taskkill = subprocess.run(
                    ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                    capture_output=True,
                    text=True,
                    shell=False,
                    timeout=5,
                )
            except (OSError, subprocess.TimeoutExpired):
                if process.poll() is None:
                    process.terminate()
                return
            if taskkill.returncode != 0 and process.poll() is None:
                process.terminate()
            return
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except OSError:
            return
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except OSError:
                pass
