from __future__ import annotations

import hashlib
import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
VECTORS_PATH = ROOT / "tests" / "scheduler" / "pcg32_v1_vectors.json"
MASK64 = (1 << 64) - 1
MASK32 = (1 << 32) - 1
KEY_PATTERN = re.compile(r"^[a-z0-9][a-z0-9._-]*$")
INIT_DOMAIN = b"VictoriantChile/pcg32-v1/init"
KEYED_DOMAIN = b"VictoriantChile/pcg32-v1/event-selector"
MULTIPLIER = 6364136223846793005

INITIALIZATION_CASES = [
    0,
    1,
    424242,
    -1,
    9223372036854775807,
    -9223372036854775808,
]

KEYED_CASES = [
    {
        "name": "nominal",
        "seed_i64": "424242",
        "tick_u64": "7",
        "system": "events",
        "template": "evt_a",
        "slot_u64": "0",
    },
    {
        "name": "tick_zero",
        "seed_i64": "424242",
        "tick_u64": "0",
        "system": "events",
        "template": "evt_a",
        "slot_u64": "0",
    },
    {
        "name": "tick_high",
        "seed_i64": "424242",
        "tick_u64": "18446744073709551615",
        "system": "events",
        "template": "evt_a",
        "slot_u64": "0",
    },
    {
        "name": "slot_one",
        "seed_i64": "424242",
        "tick_u64": "7",
        "system": "events",
        "template": "evt_a",
        "slot_u64": "1",
    },
    {
        "name": "collision_guard_a",
        "seed_i64": "1",
        "tick_u64": "2",
        "system": "a",
        "template": "bc",
        "slot_u64": "0",
    },
    {
        "name": "collision_guard_b",
        "seed_i64": "1",
        "tick_u64": "2",
        "system": "ab",
        "template": "c",
        "slot_u64": "0",
    },
    {
        "name": "long_names",
        "seed_i64": "-1",
        "tick_u64": "5",
        "system": "eventscheduler",
        "template": "evt_template.long-01",
        "slot_u64": "123456789",
    },
    {
        "name": "unrelated_template_a",
        "seed_i64": "424242",
        "tick_u64": "7",
        "system": "events",
        "template": "evt_keep",
        "slot_u64": "0",
    },
    {
        "name": "unrelated_template_b",
        "seed_i64": "424242",
        "tick_u64": "7",
        "system": "events",
        "template": "evt_other",
        "slot_u64": "0",
    },
]


def int64_le_twos_complement(value: int) -> bytes:
    if value < -(1 << 63) or value > (1 << 63) - 1:
        raise ValueError("seed outside signed int64 range")
    return ((value + (1 << 64)) & MASK64).to_bytes(8, "little", signed=False)


def uint64_le(value: int) -> bytes:
    if value < 0 or value > MASK64:
        raise ValueError("value outside uint64 range")
    return value.to_bytes(8, "little", signed=False)


def uint32_le(value: int) -> bytes:
    if value < 0 or value > MASK32:
        raise ValueError("value outside uint32 range")
    return value.to_bytes(4, "little", signed=False)


def validate_key_part(value: str) -> None:
    if not KEY_PATTERN.fullmatch(value):
        raise ValueError("invalid keyed identifier")
    if any(ord(ch) > 0x7F for ch in value):
        raise ValueError("non-ascii keyed identifier")


def sequential_preimage(seed: int) -> bytes:
    return INIT_DOMAIN + b"\x00" + int64_le_twos_complement(seed)


def keyed_preimage(seed: int, tick: int, system: str, template: str, slot: int) -> bytes:
    validate_key_part(system)
    validate_key_part(template)
    system_utf8 = system.encode("utf-8", errors="strict")
    template_utf8 = template.encode("utf-8", errors="strict")
    return (
        KEYED_DOMAIN
        + b"\x00"
        + int64_le_twos_complement(seed)
        + uint64_le(tick)
        + uint32_le(len(system_utf8))
        + system_utf8
        + uint32_le(len(template_utf8))
        + template_utf8
        + uint64_le(slot)
    )


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def rotate_right_32(value: int, rotation: int) -> int:
    normalized = rotation & 31
    return ((value >> normalized) | ((value << ((-normalized) & 31)) & MASK32)) & MASK32


def next_uint32(state_u64: int, stream_u64: int, draw_count_u64: int) -> tuple[int, int, int, int]:
    if draw_count_u64 == MASK64:
        raise OverflowError("counter exhausted")
    old_state = state_u64
    new_state = ((old_state * MULTIPLIER) + stream_u64) & MASK64
    xorshifted = (((old_state >> 18) ^ old_state) >> 27) & MASK32
    rotation = (old_state >> 59) & 0x1F
    output = rotate_right_32(xorshifted, rotation)
    return output, new_state, stream_u64, draw_count_u64 + 1


def next_bounded_uint32(state_u64: int, stream_u64: int, draw_count_u64: int, bound: int) -> tuple[int, int, int, int]:
    if bound <= 0 or bound > MASK32:
        raise ValueError("bound must be uint32 > 0")
    threshold = ((1 << 32) - bound) % bound
    current_state = state_u64
    current_count = draw_count_u64
    while True:
        raw, current_state, _, current_count = next_uint32(current_state, stream_u64, current_count)
        if raw >= threshold:
            return raw % bound, current_state, stream_u64, current_count


def create_state_from_seed(seed: int) -> tuple[int, int, int, str, str]:
    preimage = sequential_preimage(seed)
    digest = hashlib.sha256(preimage).digest()
    state = int.from_bytes(digest[0:8], "little", signed=False)
    stream = int.from_bytes(digest[8:16], "little", signed=False) | 1
    return state, stream, 0, preimage.hex(), digest.hex()


def derive_keyed_draw(seed: int, tick: int, system: str, template: str, slot: int) -> dict[str, object]:
    preimage = keyed_preimage(seed, tick, system, template, slot)
    digest = hashlib.sha256(preimage).digest()
    state = int.from_bytes(digest[0:8], "little", signed=False)
    stream = int.from_bytes(digest[8:16], "little", signed=False) | 1
    sample, _, _, _ = next_uint32(state, stream, 0)
    return {
        "preimage_hex": preimage.hex(),
        "digest_sha256": digest.hex(),
        "keyed_state_u64": f"{state:016x}",
        "keyed_stream_u64": f"{stream:016x}",
        "keyed_draw_u32": sample,
    }


def build_vectors() -> dict[str, object]:
    initialization_cases = []
    for seed in INITIALIZATION_CASES:
        state, stream, draw_count, preimage_hex, digest_hex = create_state_from_seed(seed)
        draws = []
        current_state = state
        current_stream = stream
        current_count = draw_count
        for _ in range(3):
            sample, current_state, current_stream, current_count = next_uint32(current_state, current_stream, current_count)
            draws.append(sample)

        initialization_cases.append(
            {
                "seed_i64": str(seed),
                "preimage_hex": preimage_hex,
                "digest_sha256": digest_hex,
                "initial_state_u64": f"{state:016x}",
                "initial_stream_u64": f"{stream:016x}",
                "draws_u32": draws,
                "final_state_u64": f"{current_state:016x}",
                "final_stream_u64": f"{current_stream:016x}",
                "final_draw_count_u64": f"{current_count:016x}",
            }
        )

    keyed_cases = []
    for case in KEYED_CASES:
        derived = derive_keyed_draw(
            int(case["seed_i64"]),
            int(case["tick_u64"]),
            case["system"],
            case["template"],
            int(case["slot_u64"]),
        )
        keyed_cases.append({**case, **derived})

    return {
        "contract": {
            "algorithm": "pcg32-xsh-rr",
            "contract_version": "pcg32-v1",
            "multiplier_u64_decimal": str(MULTIPLIER),
            "byte_order": "little-endian",
            "warmup_draws": 0,
            "serialized_fields": ["state_u64", "stream_u64", "draw_count_u64"],
            "global_keyed_draw_consumption": False,
        },
        "initialization_cases": initialization_cases,
        "keyed_cases": keyed_cases,
    }


class RngContractTest(unittest.TestCase):
    def test_vector_file_encoding_and_newlines_are_stable(self) -> None:
        raw = VECTORS_PATH.read_bytes()
        raw.decode("utf-8")
        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
        self.assertNotIn(b"\r", raw)
        self.assertTrue(raw.endswith(b"\n"))
        self.assertFalse(raw.endswith(b"\n\n"))

    def test_independent_oracle_recomputes_vector_file_exactly(self) -> None:
        expected = build_vectors()
        actual = json.loads(VECTORS_PATH.read_text(encoding="utf-8"))
        self.assertEqual(expected, actual)

    def test_collision_guard_cases_require_length_framing(self) -> None:
        a = next(case for case in KEYED_CASES if case["name"] == "collision_guard_a")
        b = next(case for case in KEYED_CASES if case["name"] == "collision_guard_b")
        self.assertEqual(a["seed_i64"], b["seed_i64"])
        self.assertEqual(a["tick_u64"], b["tick_u64"])
        self.assertEqual(a["slot_u64"], b["slot_u64"])
        naive_a = f"{a['seed_i64']}|{a['tick_u64']}|{a['system']}{a['template']}|{a['slot_u64']}"
        naive_b = f"{b['seed_i64']}|{b['tick_u64']}|{b['system']}{b['template']}|{b['slot_u64']}"
        self.assertEqual(naive_a, naive_b)
        self.assertNotEqual(
            derive_keyed_draw(int(a["seed_i64"]), int(a["tick_u64"]), a["system"], a["template"], int(a["slot_u64"]))["preimage_hex"],
            derive_keyed_draw(int(b["seed_i64"]), int(b["tick_u64"]), b["system"], b["template"], int(b["slot_u64"]))["preimage_hex"],
        )

    def test_reference_bounded_draw_contract_is_exact(self) -> None:
        state, stream, count, _, _ = create_state_from_seed(424242)

        value, state_a, stream_a, count_a = next_bounded_uint32(state, stream, count, 1)
        self.assertEqual(0, value)
        self.assertEqual(1, count_a)
        self.assertEqual(stream, stream_a)
        self.assertNotEqual(state, state_a)

        value, _, _, _ = next_bounded_uint32(state, stream, count, MASK32)
        self.assertGreaterEqual(value, 0)
        self.assertLessEqual(value, MASK32 - 1)

        with self.assertRaises(ValueError):
            next_bounded_uint32(state, stream, count, 0)

        rejection_seed_state, rejection_seed_stream, _, _, _ = create_state_from_seed(1)
        _, _, _, rejection_count = next_bounded_uint32(rejection_seed_state, rejection_seed_stream, 0, 3)
        self.assertGreaterEqual(rejection_count, 1)

        with self.assertRaises(OverflowError):
            next_bounded_uint32(state, stream, MASK64, 2)


if __name__ == "__main__":
    unittest.main()
