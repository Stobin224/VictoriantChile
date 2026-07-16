using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Simulation.Core.Numerics;

namespace VictoriantChile.Simulation.Core.Targets
{
    public sealed class TargetConfig
    {
        private readonly TargetOperation[] _allowedOperations;
        private readonly ReadOnlyCollection<TargetOperation> _allowedOperationsView;

        public TargetConfig(
            TargetPattern pattern,
            int scale,
            int minS,
            int maxS,
            int defaultS,
            IEnumerable<TargetOperation> allowedOperations,
            string normalizeGroup = null)
        {
            if (!pattern.IsValid)
            {
                throw new ArgumentException("Pattern must be a valid target pattern.", nameof(pattern));
            }

            if (scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be positive.");
            }

            if (minS > maxS)
            {
                throw new ArgumentException("Minimum cannot be greater than maximum.", nameof(minS));
            }

            if (defaultS < minS || defaultS > maxS)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultS), "Default must be inside the configured range.");
            }

            if (allowedOperations == null)
            {
                throw new ArgumentNullException(nameof(allowedOperations));
            }

            _allowedOperations = SnapshotOperations(allowedOperations);
            if (_allowedOperations.Length == 0)
            {
                throw new ArgumentException("At least one operation must be allowed.", nameof(allowedOperations));
            }
            _allowedOperationsView = Array.AsReadOnly(_allowedOperations);

            if (normalizeGroup != null && !TargetSyntax.IsNormalizeGroup(normalizeGroup))
            {
                throw new ArgumentException("Normalize group must be ASCII lowercase dot-separated text.", nameof(normalizeGroup));
            }

            Pattern = pattern;
            Scale = scale;
            MinS = minS;
            MaxS = maxS;
            DefaultS = defaultS;
            NormalizeGroup = normalizeGroup;
        }

        public TargetPattern Pattern { get; }

        public int Scale { get; }

        public int MinS { get; }

        public int MaxS { get; }

        public int DefaultS { get; }

        public string NormalizeGroup { get; }

        public IReadOnlyList<TargetOperation> AllowedOperations => _allowedOperationsView;

        public bool Allows(TargetOperation operation)
        {
            for (int i = 0; i < _allowedOperations.Length; i++)
            {
                if (_allowedOperations[i] == operation)
                {
                    return true;
                }
            }

            return false;
        }

        public int Clamp(int value)
        {
            return FixedMath.Clamp(value, MinS, MaxS);
        }

        private static TargetOperation[] SnapshotOperations(IEnumerable<TargetOperation> operations)
        {
            List<TargetOperation> result = new List<TargetOperation>();
            foreach (TargetOperation operation in operations)
            {
                if (!Enum.IsDefined(typeof(TargetOperation), operation))
                {
                    throw new ArgumentOutOfRangeException(nameof(operations), "Unknown target operation.");
                }

                if (result.Contains(operation))
                {
                    throw new ArgumentException("Duplicate operations are not allowed.", nameof(operations));
                }

                result.Add(operation);
            }

            return result.ToArray();
        }
    }
}
