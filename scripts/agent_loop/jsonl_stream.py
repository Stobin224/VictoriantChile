from __future__ import annotations

import codecs
import hashlib
import json
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class JsonlStreamError(Exception):
    code: str
    line: int
    byte_offset: int
    message: str
    excerpt: str
    partial_eof: bool = False

    def __str__(self) -> str:
        partial = " partial_eof=true" if self.partial_eof else ""
        return f"{self.code} line={self.line} byte_offset={self.byte_offset}{partial}: {self.message}; excerpt={self.excerpt!r}"


@dataclass(frozen=True)
class JsonlDecodeResult:
    events: tuple[dict[str, Any], ...]
    stdout_sha256: str
    stdout_size: int


def short_excerpt(text: str, limit: int = 160) -> str:
    cleaned = text.replace("\r", "\\r").replace("\n", "\\n")
    if len(cleaned) <= limit:
        return cleaned
    return cleaned[:limit] + "...[truncated]"


class IncrementalJsonlDecoder:
    def __init__(self, *, max_total_bytes: int = 2_000_000, max_line_bytes: int = 1_000_000, max_events: int = 10_000) -> None:
        if max_total_bytes <= 0 or max_line_bytes <= 0 or max_events <= 0:
            raise ValueError("JSONL limits must be positive")
        self.max_total_bytes = max_total_bytes
        self.max_line_bytes = max_line_bytes
        self.max_events = max_events
        self._decoder = codecs.getincrementaldecoder("utf-8")("strict")
        self._line_parts: list[str] = []
        self._line_bytes = 0
        self._line_number = 1
        self._line_start_offset = 0
        self._total_bytes = 0
        self._events: list[dict[str, Any]] = []
        self._sha = hashlib.sha256()
        self._started = False

    @property
    def events(self) -> tuple[dict[str, Any], ...]:
        return tuple(self._events)

    def feed(self, chunk: bytes) -> tuple[dict[str, Any], ...]:
        if not isinstance(chunk, bytes):
            raise TypeError("chunk must be bytes")
        if not chunk:
            return ()
        if self._total_bytes + len(chunk) > self.max_total_bytes:
            raise self._error("jsonl.total_limit", f"stdout exceeds {self.max_total_bytes} bytes")
        self._total_bytes += len(chunk)
        self._sha.update(chunk)
        try:
            text = self._decoder.decode(chunk, final=False)
        except UnicodeDecodeError as exc:
            raise self._error("jsonl.invalid_utf8", str(exc), byte_offset=self._total_bytes - len(chunk) + exc.start) from exc
        return self._feed_text(text)

    def finish(self) -> JsonlDecodeResult:
        try:
            text = self._decoder.decode(b"", final=True)
        except UnicodeDecodeError as exc:
            raise self._error("jsonl.invalid_utf8", str(exc), byte_offset=self._total_bytes, partial_eof=True) from exc
        self._feed_text(text)
        if self._line_parts:
            line = "".join(self._line_parts)
            self._parse_line(line, partial_eof=True)
            self._line_parts = []
            self._line_bytes = 0
        return JsonlDecodeResult(tuple(self._events), "sha256:" + self._sha.hexdigest(), self._total_bytes)

    def _feed_text(self, text: str) -> tuple[dict[str, Any], ...]:
        before = len(self._events)
        for char in text:
            if not self._started:
                self._started = True
                if char == "\ufeff":
                    raise self._error("jsonl.bom", "UTF-8 BOM is not allowed")
            if char == "\n":
                line = "".join(self._line_parts)
                if line.endswith("\r"):
                    line = line[:-1]
                self._parse_line(line)
                self._line_parts = []
                self._line_bytes = 0
                self._line_number += 1
                self._line_start_offset = self._total_bytes
                continue
            self._line_parts.append(char)
            self._line_bytes += len(char.encode("utf-8"))
            if self._line_bytes > self.max_line_bytes:
                raise self._error("jsonl.line_limit", f"JSONL line exceeds {self.max_line_bytes} bytes")
        return tuple(self._events[before:])

    def _parse_line(self, line: str, *, partial_eof: bool = False) -> None:
        if line == "":
            return
        if len(self._events) >= self.max_events:
            raise self._error("jsonl.event_limit", f"JSONL event count exceeds {self.max_events}", excerpt=line, partial_eof=partial_eof)
        try:
            event = json.loads(line)
        except json.JSONDecodeError as exc:
            raise self._error("jsonl.malformed", str(exc), excerpt=line, partial_eof=partial_eof) from exc
        if not isinstance(event, dict):
            raise self._error("jsonl.root_not_object", "JSONL record root must be an object", excerpt=line, partial_eof=partial_eof)
        self._events.append(event)

    def _error(
        self,
        code: str,
        message: str,
        *,
        byte_offset: int | None = None,
        excerpt: str | None = None,
        partial_eof: bool = False,
    ) -> JsonlStreamError:
        return JsonlStreamError(
            code,
            self._line_number,
            self._line_start_offset if byte_offset is None else byte_offset,
            message,
            short_excerpt("".join(self._line_parts) if excerpt is None else excerpt),
            partial_eof,
        )


def decode_jsonl_bytes(
    chunks: list[bytes] | tuple[bytes, ...],
    *,
    max_total_bytes: int = 2_000_000,
    max_line_bytes: int = 1_000_000,
    max_events: int = 10_000,
) -> JsonlDecodeResult:
    decoder = IncrementalJsonlDecoder(max_total_bytes=max_total_bytes, max_line_bytes=max_line_bytes, max_events=max_events)
    for chunk in chunks:
        decoder.feed(chunk)
    return decoder.finish()
