using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VictoriantChile.Simulation.Core.Numerics
{
    public static class Pcg32ErrorCodes
    {
        public const string InvalidSeed = "pcg32.invalid_seed";
        public const string InvalidKeyPart = "pcg32.invalid_key_part";
        public const string InvalidBound = "pcg32.invalid_bound";
        public const string CounterExhausted = "pcg32.counter_exhausted";
    }

    public sealed class Pcg32Exception : InvalidOperationException
    {
        public Pcg32Exception(string code, string message, string detail = null, Exception innerException = null)
            : base(message, innerException)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Detail = detail;
        }

        public string Code { get; }

        public string Detail { get; }
    }

    public sealed class Pcg32State : IEquatable<Pcg32State>
    {
        private const string InitDomainTag = "VictoriantChile/pcg32-v1/init";
        private const string EventSelectorDomainTag = "VictoriantChile/pcg32-v1/event-selector";
        private const ulong Multiplier = 6364136223846793005UL;
        private static readonly Regex KeyPartPattern = new Regex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

        public Pcg32State(ulong stateU64, ulong streamU64, ulong drawCountU64)
        {
            if ((streamU64 & 1UL) == 0UL)
            {
                throw new ArgumentException("PCG32 stream must be odd.", nameof(streamU64));
            }

            StateU64 = stateU64;
            StreamU64 = streamU64;
            DrawCountU64 = drawCountU64;
        }

        public ulong StateU64 { get; }

        public ulong StreamU64 { get; }

        public ulong DrawCountU64 { get; }

        public static string Algorithm => "pcg32-xsh-rr";

        public static string ContractVersion => "pcg32-v1";

        public static Pcg32State CreateFromSeed(int seed)
        {
            return CreateFromSeed((long)seed);
        }

        public static Pcg32State CreateFromSeed(long seed)
        {
            byte[] digest = ComputeSha256(BuildSequentialSeedPreimage(seed));
            return new Pcg32State(
                ReadUInt64LittleEndian(digest, 0),
                ReadUInt64LittleEndian(digest, 8) | 1UL,
                0UL);
        }

        public uint NextUInt32(out Pcg32State nextState)
        {
            if (DrawCountU64 == ulong.MaxValue)
            {
                throw new Pcg32Exception(
                    Pcg32ErrorCodes.CounterExhausted,
                    "PCG32 raw draws fail closed when draw_count_u64 is already UINT64_MAX.");
            }

            ulong oldState = StateU64;
            ulong newState = unchecked((oldState * Multiplier) + StreamU64);
            uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rotate = (int)(oldState >> 59);
            uint result = RotateRight(xorshifted, rotate);
            nextState = new Pcg32State(newState, StreamU64, DrawCountU64 + 1UL);
            return result;
        }

        public uint NextBoundedUInt32(uint exclusiveUpperBound, out Pcg32State nextState)
        {
            if (exclusiveUpperBound == 0U)
            {
                throw new Pcg32Exception(
                    Pcg32ErrorCodes.InvalidBound,
                    "PCG32 bounded draws require a strictly positive upper bound.",
                    nameof(exclusiveUpperBound));
            }

            ulong modulus = exclusiveUpperBound;
            uint threshold = (uint)(((1UL << 32) - modulus) % modulus);
            Pcg32State current = this;
            while (true)
            {
                uint sample = current.NextUInt32(out Pcg32State updated);
                current = updated;
                if (sample >= threshold)
                {
                    nextState = current;
                    return (uint)(sample % exclusiveUpperBound);
                }
            }
        }

        public Pcg32KeyedDraw DeriveKeyedDraw(int seed, int tick, string system, string template, ulong slot)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Tick cannot be negative.");
            }

            return DeriveKeyedDraw((long)seed, (ulong)tick, system, template, slot);
        }

        public Pcg32KeyedDraw DeriveKeyedDraw(long seed, ulong tick, string system, string template, ulong slot)
        {
            ValidateKeyPart(system, nameof(system));
            ValidateKeyPart(template, nameof(template));

            byte[] digest = ComputeSha256(BuildEventSelectorPreimage(seed, tick, system, template, slot));
            Pcg32State derived = new Pcg32State(
                ReadUInt64LittleEndian(digest, 0),
                ReadUInt64LittleEndian(digest, 8) | 1UL,
                0UL);
            uint sample = derived.NextUInt32(out _);
            return new Pcg32KeyedDraw(sample);
        }

        public string StateHex => StateU64.ToString("x16", CultureInfo.InvariantCulture);

        public string StreamHex => StreamU64.ToString("x16", CultureInfo.InvariantCulture);

        public string DrawCountHex => DrawCountU64.ToString("x16", CultureInfo.InvariantCulture);

        public bool Equals(Pcg32State other)
        {
            return other != null
                && StateU64 == other.StateU64
                && StreamU64 == other.StreamU64
                && DrawCountU64 == other.DrawCountU64;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Pcg32State);
        }

        private static void ValidateKeyPart(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new Pcg32Exception(
                    Pcg32ErrorCodes.InvalidKeyPart,
                    "Keyed draw string parts must be non-empty ASCII identifiers.",
                    name);
            }

            if (!KeyPartPattern.IsMatch(value))
            {
                throw new Pcg32Exception(
                    Pcg32ErrorCodes.InvalidKeyPart,
                    "Keyed draw string parts must match [a-z0-9][a-z0-9._-]*.",
                    name);
            }
        }

        private static uint RotateRight(uint value, int rotate)
        {
            int normalized = rotate & 31;
            return (value >> normalized) | (value << ((-normalized) & 31));
        }

        private static byte[] BuildSequentialSeedPreimage(long seed)
        {
            byte[] preimage = new byte[InitDomainTag.Length + 1 + sizeof(long)];
            WriteAscii(preimage, 0, InitDomainTag);
            preimage[InitDomainTag.Length] = 0x00;
            WriteUInt64LittleEndian(preimage, InitDomainTag.Length + 1, unchecked((ulong)seed));
            return preimage;
        }

        private static byte[] BuildEventSelectorPreimage(long seed, ulong tick, string system, string template, ulong slot)
        {
            byte[] systemUtf8 = EncodeStrictUtf8(system);
            byte[] templateUtf8 = EncodeStrictUtf8(template);
            byte[] preimage = new byte[
                EventSelectorDomainTag.Length
                + 1
                + sizeof(long)
                + sizeof(ulong)
                + sizeof(uint)
                + systemUtf8.Length
                + sizeof(uint)
                + templateUtf8.Length
                + sizeof(ulong)];
            int offset = 0;
            WriteAscii(preimage, offset, EventSelectorDomainTag);
            offset += EventSelectorDomainTag.Length;
            preimage[offset++] = 0x00;
            WriteUInt64LittleEndian(preimage, offset, unchecked((ulong)seed));
            offset += sizeof(long);
            WriteUInt64LittleEndian(preimage, offset, tick);
            offset += sizeof(ulong);
            WriteUInt32LittleEndian(preimage, offset, checked((uint)systemUtf8.Length));
            offset += sizeof(uint);
            Buffer.BlockCopy(systemUtf8, 0, preimage, offset, systemUtf8.Length);
            offset += systemUtf8.Length;
            WriteUInt32LittleEndian(preimage, offset, checked((uint)templateUtf8.Length));
            offset += sizeof(uint);
            Buffer.BlockCopy(templateUtf8, 0, preimage, offset, templateUtf8.Length);
            offset += templateUtf8.Length;
            WriteUInt64LittleEndian(preimage, offset, slot);
            return preimage;
        }

        private static byte[] ComputeSha256(byte[] input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(input);
            }
        }

        private static byte[] EncodeStrictUtf8(string value)
        {
            return new UTF8Encoding(false, true).GetBytes(value);
        }

        private static void WriteAscii(byte[] destination, int offset, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character > 0x7F)
                {
                    throw new Pcg32Exception(
                        Pcg32ErrorCodes.InvalidKeyPart,
                        "PCG32 domain tags and keyed identifiers must remain ASCII.",
                        value);
                }

                destination[offset + i] = (byte)character;
            }
        }

        private static void WriteUInt64LittleEndian(byte[] destination, int offset, ulong value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
            destination[offset + 4] = (byte)(value >> 32);
            destination[offset + 5] = (byte)(value >> 40);
            destination[offset + 6] = (byte)(value >> 48);
            destination[offset + 7] = (byte)(value >> 56);
        }

        private static void WriteUInt32LittleEndian(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static ulong ReadUInt64LittleEndian(IReadOnlyList<byte> bytes, int offset)
        {
            return ((ulong)bytes[offset])
                | ((ulong)bytes[offset + 1] << 8)
                | ((ulong)bytes[offset + 2] << 16)
                | ((ulong)bytes[offset + 3] << 24)
                | ((ulong)bytes[offset + 4] << 32)
                | ((ulong)bytes[offset + 5] << 40)
                | ((ulong)bytes[offset + 6] << 48)
                | ((ulong)bytes[offset + 7] << 56);
        }

    }

    public sealed class Pcg32KeyedDraw
    {
        public Pcg32KeyedDraw(uint sample)
        {
            Sample = sample;
        }

        public uint Sample { get; }
    }
}
