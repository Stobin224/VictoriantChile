from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
import uuid
from pathlib import Path

from scripts.agent_loop.codex_client import CodexClient, parse_jsonl_bytes
from scripts.agent_loop.jsonl_stream import IncrementalJsonlDecoder, JsonlStreamError, decode_jsonl_bytes
from scripts.agent_loop.models import RawProcessResult


ROOT = Path(__file__).resolve().parents[2]
SESSION_ID = "11111111-1111-1111-1111-111111111111"


def record(data: dict) -> bytes:
    return (json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")


def event_stream() -> bytes:
    return b"".join(
        [
            record({"type": "thread.started", "thread_id": SESSION_ID}),
            record({"type": "turn.completed", "usage": {"input_tokens": 2, "output_tokens": 3, "cached_input_tokens": 1}}),
            record({"type": "final_message", "message": "{\"status\":\"passed\",\"message\":\"jsonl-smoke\"}"}),
        ]
    )


def decode(chunks: list[bytes]) -> tuple[dict, ...]:
    return decode_jsonl_bytes(chunks).events


class AgentLoopJsonlStreamTest(unittest.TestCase):
    def test_one_object_one_chunk_and_lf(self) -> None:
        events = decode([record({"type": "thread.started", "thread_id": SESSION_ID})])
        self.assertEqual("thread.started", events[0]["type"])

    def test_object_split_in_two_chunks_and_inside_string(self) -> None:
        payload = record({"type": "final_message", "message": "a" * 220})
        cut = payload.index(b"a" * 20) + 7
        events = decode([payload[:cut], payload[cut:]])
        self.assertEqual("a" * 220, events[0]["message"])

    def test_cut_at_every_possible_byte_and_one_byte_chunks_are_equivalent(self) -> None:
        payload = record({"type": "final_message", "message": "quoted \\\" text and escaped \\n newline"})
        expected = decode([payload])
        for cut in range(1, len(payload)):
            self.assertEqual(expected, decode([payload[:cut], payload[cut:]]))
        self.assertEqual(expected, decode([bytes([byte]) for byte in payload]))

    def test_multiple_objects_in_one_chunk_and_order_preserved(self) -> None:
        events = decode([event_stream()])
        self.assertEqual(["thread.started", "turn.completed", "final_message"], [event["type"] for event in events])

    def test_chunk_boundaries_not_aligned_to_newline_and_crlf(self) -> None:
        first = json.dumps({"type": "thread.started", "thread_id": SESSION_ID}).encode("utf-8") + b"\r\n"
        second = json.dumps({"type": "final_message", "message": "ok"}).encode("utf-8") + b"\r\n"
        events = decode([first[:5], first[5:] + second[:3], second[3:]])
        self.assertEqual(2, len(events))
        self.assertEqual("ok", events[1]["message"])

    def test_last_line_without_newline_and_truncated_last_line(self) -> None:
        payload = json.dumps({"type": "final_message", "message": "ok"}).encode("utf-8")
        self.assertEqual("ok", decode([payload])[0]["message"])
        with self.assertRaisesRegex(JsonlStreamError, "jsonl.malformed"):
            decode([payload[:-2]])

    def test_escaped_newline_and_quotes_remain_inside_string(self) -> None:
        events = decode([record({"type": "final_message", "message": "line\\nwith \"quotes\""})])
        self.assertEqual("line\\nwith \"quotes\"", events[0]["message"])

    def test_utf8_multibyte_split_and_invalid_utf8(self) -> None:
        payload = record({"type": "final_message", "message": "Ñuble"})
        index = payload.index("Ñ".encode("utf-8"))
        events = decode([payload[: index + 1], payload[index + 1 :]])
        self.assertEqual("Ñuble", events[0]["message"])
        with self.assertRaisesRegex(JsonlStreamError, "jsonl.invalid_utf8"):
            decode([b'{"type":"final_message","message":"\xff"}\n'])

    def test_bom_rejected_and_empty_lines_ignored(self) -> None:
        with self.assertRaisesRegex(JsonlStreamError, "jsonl.bom"):
            decode([b"\xef\xbb\xbf" + record({"type": "thread.started"})])
        events = decode([b"\n", record({"type": "thread.started"}), b"\n"])
        self.assertEqual(1, len(events))

    def test_non_json_lines_before_and_between_events_fail(self) -> None:
        with self.assertRaisesRegex(JsonlStreamError, "jsonl.malformed"):
            decode([b"warning\n", record({"type": "thread.started"})])
        with self.assertRaisesRegex(JsonlStreamError, "jsonl.malformed"):
            decode([record({"type": "thread.started"}), b"warning\n", record({"type": "final_message", "message": "ok"})])

    def test_json_root_must_be_object(self) -> None:
        for payload in (b"[]\n", b'"x"\n', b"null\n", b"1\n", b"true\n"):
            with self.subTest(payload=payload):
                with self.assertRaisesRegex(JsonlStreamError, "jsonl.root_not_object"):
                    decode([payload])

    def test_limits_total_line_and_events(self) -> None:
        with self.assertRaisesRegex(JsonlStreamError, "jsonl.total_limit"):
            decode_jsonl_bytes([record({"type": "x"})], max_total_bytes=2)
        with self.assertRaisesRegex(JsonlStreamError, "jsonl.line_limit"):
            decode_jsonl_bytes([record({"type": "x", "message": "abcdef"})], max_line_bytes=5)
        with self.assertRaisesRegex(JsonlStreamError, "jsonl.event_limit"):
            decode_jsonl_bytes([record({"type": "x"}), record({"type": "y"})], max_events=1)

    def test_thread_turn_usage_and_final_message_split(self) -> None:
        payload = event_stream()
        parsed = parse_jsonl_bytes([payload[:7], payload[7:31], payload[31:]][0] + b"")
        self.assertEqual(None, parsed["session_id"])
        parsed = parse_jsonl_bytes(payload)
        self.assertEqual(SESSION_ID, parsed["session_id"])
        self.assertEqual(2, parsed["usage"].input_tokens)
        self.assertIn("jsonl-smoke", parsed["final_message"])
        self.assertEqual([], parsed["errors"])

    def test_missing_required_events_are_errors(self) -> None:
        parsed = parse_jsonl_bytes(record({"type": "final_message", "message": "ok"}))
        self.assertIn("missing thread.started", "\n".join(parsed["errors"]))
        self.assertIn("missing thread_id", "\n".join(parsed["errors"]))
        self.assertIn("missing turn.completed", "\n".join(parsed["errors"]))

    def test_turn_failed_is_terminal_error(self) -> None:
        payload = b"".join(
            [
                record({"type": "thread.started", "thread_id": SESSION_ID}),
                record({"type": "turn.failed", "error": {"message": "boom"}}),
                record({"type": "final_message", "message": "failed"}),
            ]
        )
        parsed = parse_jsonl_bytes(payload)
        self.assertIn("turn.failed", "\n".join(parsed["errors"]))

    def test_item_completed_output_schema_shape_can_provide_final_response(self) -> None:
        payload = b"".join(
            [
                record({"type": "thread.started", "thread_id": SESSION_ID}),
                record({"type": "turn.completed"}),
                record({"type": "item.completed", "item": {"content": [{"type": "output_text", "text": "{\"status\":\"passed\"}"}]}}),
            ]
        )
        parsed = parse_jsonl_bytes(payload)
        self.assertEqual("{\"status\":\"passed\"}", parsed["final_message"])
        self.assertEqual([], parsed["errors"])

    def test_incremental_decoder_streaming_order_and_eof(self) -> None:
        decoder = IncrementalJsonlDecoder()
        produced: list[dict] = []
        for chunk in [event_stream()[:10], event_stream()[10:80], event_stream()[80:]]:
            produced.extend(decoder.feed(chunk))
        result = decoder.finish()
        self.assertEqual([event["type"] for event in result.events], [event["type"] for event in produced])
        self.assertTrue(result.stdout_sha256.startswith("sha256:"))

    def test_eof_with_incomplete_utf8_reports_partial(self) -> None:
        decoder = IncrementalJsonlDecoder()
        decoder.feed(b'{"type":"final_message","message":"\xc3')
        with self.assertRaises(JsonlStreamError) as context:
            decoder.finish()
        self.assertTrue(context.exception.partial_eof)
        self.assertEqual("jsonl.invalid_utf8", context.exception.code)

    def test_diagnostic_has_stable_line_offset_hash_and_excerpt(self) -> None:
        parsed = parse_jsonl_bytes(b'{"type":"thread.started"}\n{"type":"final_message","message":"unterminated')
        error = "\n".join(parsed["errors"])
        self.assertIn("code=jsonl.malformed", error)
        self.assertIn("line=2", error)
        self.assertIn("stdout_sha256=sha256:", error)
        self.assertIn("stdout_size=", error)
        self.assertIn("partial_eof=true", error)
        self.assertIn("excerpt=", error)

    def test_same_stream_different_partitions_produces_same_events(self) -> None:
        payload = event_stream()
        partitions = [
            [payload],
            [payload[:1], payload[1:50], payload[50:]],
            [bytes([byte]) for byte in payload],
        ]
        expected = decode(partitions[0])
        for chunks in partitions[1:]:
            self.assertEqual(expected, decode(chunks))

    def test_sanitized_regression_for_unterminated_string_failure_shape(self) -> None:
        payload = record({"type": "final_message", "message": "x" * 220})
        cut = 174
        first = payload[:cut]
        second = payload[cut:]
        with self.assertRaises(json.JSONDecodeError):
            json.loads(first.decode("utf-8"))
        events = decode([first, second])
        self.assertEqual("x" * 220, events[0]["message"])

    def test_stderr_warnings_are_separate_and_raw_evidence_is_ignored(self) -> None:
        run_id = f"unit-jsonl-{uuid.uuid4().hex}"
        evidence_dir = ROOT / ".agent-loop" / "runs" / run_id

        class FakeRunner:
            def run_bytes(self, argv, cwd, timeout_seconds, *, max_stdout_bytes, max_stderr_bytes):
                return RawProcessResult(tuple(argv), 0, event_stream(), b"warning on stderr\n")

        try:
            client = CodexClient("codex", ROOT, FakeRunner(), evidence_dir)
            result = client.exec("prompt", sandbox="read-only", output_schema=None, timeout_seconds=1)
            self.assertTrue(result.ok, result.errors)
            self.assertIn("warning", result.stderr)
            self.assertTrue((evidence_dir / "codex-turn-001-stdout.raw").exists())
            self.assertTrue((evidence_dir / "codex-turn-001-stderr.log").exists())
            status = subprocess.run(["git", "ls-files", "--others", "--exclude-standard"], cwd=ROOT, capture_output=True, text=True, shell=False, check=True)
            self.assertNotIn(run_id, status.stdout)
        finally:
            if evidence_dir.exists():
                for path in sorted(evidence_dir.rglob("*"), reverse=True):
                    if path.is_file():
                        path.unlink()
                    elif path.is_dir():
                        path.rmdir()
                if evidence_dir.exists():
                    evidence_dir.rmdir()

    def test_stdout_and_stderr_limits_are_reported_by_client(self) -> None:
        class FakeRunner:
            def run_bytes(self, argv, cwd, timeout_seconds, *, max_stdout_bytes, max_stderr_bytes):
                return RawProcessResult(tuple(argv), None, b"{", b"err", stdout_limited=True, stderr_limited=True)

        result = CodexClient("codex", ROOT, FakeRunner()).exec("prompt", sandbox="read-only", output_schema=None, timeout_seconds=1)
        self.assertFalse(result.ok)
        self.assertIn("stdout exceeded", "\n".join(result.errors))
        self.assertIn("stderr exceeded", "\n".join(result.errors))

    def test_process_exit_nonzero_with_valid_stream_preserves_specific_result(self) -> None:
        class FakeRunner:
            def run_bytes(self, argv, cwd, timeout_seconds, *, max_stdout_bytes, max_stderr_bytes):
                return RawProcessResult(tuple(argv), 9, event_stream(), b"")

        result = CodexClient("codex", ROOT, FakeRunner()).exec("prompt", sandbox="read-only", output_schema=None, timeout_seconds=1)
        self.assertFalse(result.ok)
        self.assertEqual(9, result.exit_code)
        self.assertIn("codex exited with code 9", result.errors)
        self.assertIn("jsonl-smoke", result.final_message)


if __name__ == "__main__":
    unittest.main()
