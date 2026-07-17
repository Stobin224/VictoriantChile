using System;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class ContentFileIdentity : IEquatable<ContentFileIdentity>
    {
        public ContentFileIdentity(string relativePath, string canonicalHash)
        {
            RelativePath = RequireText(relativePath, nameof(relativePath));
            CanonicalHash = RequireText(canonicalHash, nameof(canonicalHash));
        }

        public string RelativePath { get; }

        public string CanonicalHash { get; }

        public bool Equals(ContentFileIdentity other)
        {
            return other != null
                && string.Equals(RelativePath, other.RelativePath, StringComparison.Ordinal)
                && string.Equals(CanonicalHash, other.CanonicalHash, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ContentFileIdentity);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(RelativePath) * 397)
                    ^ StringComparer.Ordinal.GetHashCode(CanonicalHash);
            }
        }

        private static string RequireText(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value cannot be null or empty.", name);
            }

            return value;
        }
    }
}
