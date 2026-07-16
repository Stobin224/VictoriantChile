using System;
using System.Collections.Generic;

namespace VictoriantChile.Simulation.Core.Targets
{
    public sealed class TargetConfigCatalog
    {
        private readonly TargetConfig[] _configs;

        public TargetConfigCatalog(IEnumerable<TargetConfig> configs)
        {
            if (configs == null)
            {
                throw new ArgumentNullException(nameof(configs));
            }

            List<TargetConfig> snapshot = new List<TargetConfig>();
            HashSet<string> patterns = new HashSet<string>(StringComparer.Ordinal);
            foreach (TargetConfig config in configs)
            {
                if (config == null)
                {
                    throw new ArgumentNullException(nameof(configs), "Catalog cannot contain null configs.");
                }

                string pattern = config.Pattern.ToString();
                if (!patterns.Add(pattern))
                {
                    throw new ArgumentException($"Duplicate target config pattern: {pattern}", nameof(configs));
                }

                snapshot.Add(config);
            }

            _configs = snapshot.ToArray();
        }

        public int Count => _configs.Length;

        public bool TryResolve(TargetPath path, out TargetConfig config)
        {
            config = null;
            if (!path.IsValid)
            {
                return false;
            }

            int bestExact = -1;
            int bestLiteralCount = -1;
            int bestLength = -1;

            for (int i = 0; i < _configs.Length; i++)
            {
                TargetConfig candidate = _configs[i];
                TargetPattern pattern = candidate.Pattern;
                if (!pattern.Matches(path))
                {
                    continue;
                }

                int exact = pattern.IsExact ? 1 : 0;
                int literalCount = pattern.LiteralSegmentCount;
                int length = pattern.CanonicalLength;
                bool better = exact > bestExact
                    || (exact == bestExact && literalCount > bestLiteralCount)
                    || (exact == bestExact && literalCount == bestLiteralCount && length > bestLength);

                if (better)
                {
                    config = candidate;
                    bestExact = exact;
                    bestLiteralCount = literalCount;
                    bestLength = length;
                }
            }

            return config != null;
        }

        public TargetConfig Resolve(TargetPath path)
        {
            if (TryResolve(path, out TargetConfig config))
            {
                return config;
            }

            throw new KeyNotFoundException($"No target config matches path: {path}");
        }
    }
}
