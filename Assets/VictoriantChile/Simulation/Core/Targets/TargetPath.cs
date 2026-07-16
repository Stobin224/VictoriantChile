using System;

namespace VictoriantChile.Simulation.Core.Targets
{
    public readonly struct TargetPath : IEquatable<TargetPath>, IComparable<TargetPath>
    {
        private readonly string _canonical;
        private readonly string[] _segments;

        private TargetPath(string canonical, string[] segments)
        {
            _canonical = canonical;
            _segments = segments;
        }

        internal bool IsValid => _canonical != null;

        public string Namespace => _segments == null ? string.Empty : _segments[0];

        public int SegmentCount => _segments == null ? 0 : _segments.Length;

        public string this[int index]
        {
            get
            {
                if (_segments == null)
                {
                    throw new IndexOutOfRangeException("TargetPath is not initialized.");
                }

                return _segments[index];
            }
        }

        public static TargetPath Parse(string value)
        {
            if (!TryParse(value, out TargetPath path))
            {
                throw new ArgumentException($"Invalid target path: {value ?? "<null>"}", nameof(value));
            }

            return path;
        }

        public static bool TryParse(string value, out TargetPath path)
        {
            path = default;
            if (!TargetSyntax.TrySplit(value, false, out string[] segments, out _))
            {
                return false;
            }

            path = new TargetPath(value, segments);
            return true;
        }

        public override string ToString()
        {
            return _canonical ?? string.Empty;
        }

        public bool Equals(TargetPath other)
        {
            return string.Equals(_canonical, other._canonical, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TargetPath other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(_canonical ?? string.Empty);
        }

        public int CompareTo(TargetPath other)
        {
            return string.Compare(_canonical, other._canonical, StringComparison.Ordinal);
        }

        public static bool operator ==(TargetPath left, TargetPath right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TargetPath left, TargetPath right)
        {
            return !left.Equals(right);
        }
    }
}
