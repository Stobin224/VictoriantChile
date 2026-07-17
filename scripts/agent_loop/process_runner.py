from __future__ import annotations

import os
import signal
import subprocess
import sys
from pathlib import Path

from .models import ProcessResult


def truncate(text: str, limit: int = 20000) -> str:
    if len(text) <= limit:
        return text
    return text[:limit] + "\n[truncated]\n"


class ProcessRunner:
    def run(self, argv: list[str], cwd: Path, timeout_seconds: int) -> ProcessResult:
        if timeout_seconds <= 0:
            raise ValueError("timeout_seconds must be positive")
        popen_kwargs = {
            "cwd": cwd,
            "stdout": subprocess.PIPE,
            "stderr": subprocess.PIPE,
            "text": True,
            "shell": False,
        }
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
