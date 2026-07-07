using System;
using System.Collections.Generic;

namespace VictoriantChile.Simulation
{
    /// <summary>
    /// Identidad inmutable de un valor concreto del GameState.
    /// Los patrones con "*" se representarán por un tipo distinto.
    /// Contrato: CON-SIM-001.
    /// </summary>
    public sealed class TargetPath : IEquatable<TargetPath>
    {
        private static readonly IReadOnlyDictionary<string, int> SegmentCounts =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["metrics"] = 2,
                ["internals"] = 3,
                ["regions"] = 3,
                ["igs"] = 3,
                ["movements"] = 3
            };

        private readonly string[] _segments;

        private TargetPath(string value, string[] segments)
        {
            Value = value;
            _segments = segments;
        }

        public string Value { get; }

        public string Root => _segments[0];

        public int SegmentCount => _segments.Length;

        public string this[int index] => _segments[index];

        public static TargetPath Parse(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            string[] segments = value.Split('.');
            if (segments.Length == 0 ||
                !SegmentCounts.TryGetValue(segments[0], out int expectedCount))
            {
                throw NewFormatException(value, "namespace desconocido");
            }

            if (segments.Length != expectedCount)
            {
                throw NewFormatException(
                    value,
                    $"se esperaban {expectedCount} segmentos y llegaron {segments.Length}");
            }

            for (int index = 0; index < segments.Length; index++)
            {
                if (!IsValidSegment(segments[index]))
                {
                    throw NewFormatException(value, $"segmento inválido en posición {index}");
                }
            }

            return new TargetPath(value, segments);
        }

        public bool Equals(TargetPath other)
        {
            return other != null &&
                   string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TargetPath);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(TargetPath left, TargetPath right)
        {
            return EqualityComparer<TargetPath>.Default.Equals(left, right);
        }

        public static bool operator !=(TargetPath left, TargetPath right)
        {
            return !(left == right);
        }

        private static bool IsValidSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment) || segment == "*")
            {
                return false;
            }

            // Los campos fixed-point del Content Pack pueden terminar en "S",
            // por ejemplo industry_capS. El resto conserva snake_case minúsculo.
            int length = segment.EndsWith("S", StringComparison.Ordinal)
                ? segment.Length - 1
                : segment.Length;

            if (length == 0 || !IsLowerAsciiLetter(segment[0]))
            {
                return false;
            }

            bool previousWasUnderscore = false;
            for (int index = 0; index < length; index++)
            {
                char character = segment[index];
                if (character == '_')
                {
                    if (index == 0 || index == length - 1 || previousWasUnderscore)
                    {
                        return false;
                    }

                    previousWasUnderscore = true;
                    continue;
                }

                if (!IsLowerAsciiLetter(character) && !IsAsciiDigit(character))
                {
                    return false;
                }

                previousWasUnderscore = false;
            }

            return true;
        }

        private static bool IsLowerAsciiLetter(char character)
        {
            return character >= 'a' && character <= 'z';
        }

        private static bool IsAsciiDigit(char character)
        {
            return character >= '0' && character <= '9';
        }

        private static FormatException NewFormatException(string value, string reason)
        {
            return new FormatException($"TargetPath inválido '{value}': {reason}.");
        }
    }
}
