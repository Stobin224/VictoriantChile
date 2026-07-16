using System;

namespace VictoriantChile.Simulation.Core.Targets
{
    public readonly struct TargetPattern : IEquatable<TargetPattern>
    {
        private readonly string _canonical;
        private readonly string[] _segments;

        private TargetPattern(string canonical, string[] segments)
        {
            _canonical = canonical;
            _segments = segments;
        }

        internal bool IsValid => _canonical != null;

        public int SegmentCount => _segments == null ? 0 : _segments.Length;

        public bool IsExact => IsValid && LiteralSegmentCount == SegmentCount;

        public int LiteralSegmentCount
        {
            get
            {
                if (_segments == null)
                {
                    return 0;
                }

                int count = 0;
                for (int i = 0; i < _segments.Length; i++)
                {
                    if (_segments[i] != "*")
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int CanonicalLength => _canonical == null ? 0 : _canonical.Length;

        public string this[int index]
        {
            get
            {
                if (_segments == null)
                {
                    throw new IndexOutOfRangeException("TargetPattern is not initialized.");
                }

                return _segments[index];
            }
        }

        public static TargetPattern Parse(string value)
        {
            if (!TryParse(value, out TargetPattern pattern))
            {
                throw new ArgumentException($"Invalid target pattern: {value ?? "<null>"}", nameof(value));
            }

            return pattern;
        }

        public static bool TryParse(string value, out TargetPattern pattern)
        {
            pattern = default;
            if (!TargetSyntax.TrySplit(value, true, out string[] segments, out _))
            {
                return false;
            }

            pattern = new TargetPattern(value, segments);
            return true;
        }

        public bool Matches(TargetPath path)
        {
            if (!IsValid || !path.IsValid || path.SegmentCount != SegmentCount)
            {
                return false;
            }

            for (int i = 0; i < SegmentCount; i++)
            {
                string segment = _segments[i];
                if (segment != "*" && segment != path[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString()
        {
            return _canonical ?? string.Empty;
        }

        public bool Equals(TargetPattern other)
        {
            return string.Equals(_canonical, other._canonical, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TargetPattern other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(_canonical ?? string.Empty);
        }

        public static bool operator ==(TargetPattern left, TargetPattern right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TargetPattern left, TargetPattern right)
        {
            return !left.Equals(right);
        }
    }
}
