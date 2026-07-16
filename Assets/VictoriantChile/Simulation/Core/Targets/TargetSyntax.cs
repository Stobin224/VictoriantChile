using System;

namespace VictoriantChile.Simulation.Core.Targets
{
    internal static class TargetSyntax
    {
        private static readonly string[] StaticRegionFields =
        {
            "admin_capS",
            "industry_capS",
            "extractive_capS",
            "social_capS",
            "populationS"
        };

        internal static bool TrySplit(string value, bool allowWildcard, out string[] segments, out string error)
        {
            segments = Array.Empty<string>();
            error = string.Empty;

            if (value == null)
            {
                error = "Target text cannot be null.";
                return false;
            }

            if (value.Length == 0)
            {
                error = "Target text cannot be empty.";
                return false;
            }

            if (value[0] == ' ' || value[value.Length - 1] == ' ')
            {
                error = "Target text cannot contain leading or trailing spaces.";
                return false;
            }

            segments = value.Split('.');
            if (!IsKnownArity(segments))
            {
                error = "Target namespace or segment count is invalid.";
                return false;
            }

            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (allowWildcard && segment == "*")
                {
                    continue;
                }

                if (i == 2 && IsLowercaseStaticRegionLookalike(segments))
                {
                    error = "Static regional fields must use their exact canonical casing.";
                    return false;
                }

                if (i == 2 && IsStaticRegionField(segment))
                {
                    continue;
                }

                if (!IsLiteralSegment(segment))
                {
                    error = "Target segment must be ASCII lowercase snake_case or a full wildcard where allowed.";
                    return false;
                }
            }

            if (segments.Length == 3 && IsStaticRegionField(segments[2]) && !IsValidStaticRegionReference(segments, allowWildcard))
            {
                error = "Static regional fields are only valid as regions.<region_id>.<field> or regions.*.<field> patterns.";
                return false;
            }

            return true;
        }

        internal static bool IsKnownArity(string[] segments)
        {
            if (segments.Length == 2 && segments[0] == "metrics")
            {
                return true;
            }

            if (segments.Length != 3)
            {
                return false;
            }

            return segments[0] == "regions"
                || segments[0] == "igs"
                || segments[0] == "movements"
                || segments[0] == "internals";
        }

        internal static bool IsLiteralSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
            {
                return false;
            }

            if (segment[0] < 'a' || segment[0] > 'z')
            {
                return false;
            }

            for (int i = 1; i < segment.Length; i++)
            {
                char c = segment[i];
                bool valid = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsStaticRegionField(string segment)
        {
            for (int i = 0; i < StaticRegionFields.Length; i++)
            {
                if (segment == StaticRegionFields[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidStaticRegionReference(string[] segments, bool allowWildcard)
        {
            return segments.Length == 3
                && segments[0] == "regions"
                && (IsLiteralSegment(segments[1]) || (allowWildcard && segments[1] == "*"));
        }

        private static bool IsLowercaseStaticRegionLookalike(string[] segments)
        {
            if (segments.Length != 3 || segments[0] != "regions")
            {
                return false;
            }

            string field = segments[2];
            return field == "admin_caps"
                || field == "industry_caps"
                || field == "extractive_caps"
                || field == "social_caps"
                || field == "populations";
        }

        internal static bool IsNormalizeGroup(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string[] segments = value.Split('.');
            for (int i = 0; i < segments.Length; i++)
            {
                if (!IsLiteralSegment(segments[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
