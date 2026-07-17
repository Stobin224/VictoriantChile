using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.Numerics;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class InterestGroupCloutValue
    {
        public InterestGroupCloutValue(string interestGroupId, int cloutS)
        {
            if (string.IsNullOrEmpty(interestGroupId))
            {
                throw new ArgumentException("Interest group ID cannot be null or empty.", nameof(interestGroupId));
            }

            InterestGroupId = interestGroupId;
            CloutS = cloutS;
        }

        public string InterestGroupId { get; }

        public int CloutS { get; }
    }

    public static class CloutNormalizer
    {
        public static IReadOnlyList<InterestGroupCloutValue> Normalize(IEnumerable<InterestGroupCloutValue> rawValues)
        {
            IReadOnlyList<InterestGroupCloutValue> sorted = StateCollection.SnapshotSorted(rawValues, item => item.InterestGroupId, nameof(rawValues));
            if (sorted.Count == 0)
            {
                throw new ArgumentException("At least one clout value is required.", nameof(rawValues));
            }

            long total = 0;
            string residueWinnerId = null;
            int residueWinnerRaw = int.MinValue;
            for (int i = 0; i < sorted.Count; i++)
            {
                InterestGroupCloutValue value = sorted[i];
                if (value.CloutS < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(rawValues), "Clout values cannot be negative.");
                }

                checked
                {
                    total += value.CloutS;
                }

                if (value.CloutS > residueWinnerRaw
                    || (value.CloutS == residueWinnerRaw && string.Compare(value.InterestGroupId, residueWinnerId, StringComparison.Ordinal) < 0))
                {
                    residueWinnerId = value.InterestGroupId;
                    residueWinnerRaw = value.CloutS;
                }
            }

            if (total == 0)
            {
                throw new ArgumentException("Total clout cannot be zero.", nameof(rawValues));
            }

            List<InterestGroupCloutValue> normalized = new List<InterestGroupCloutValue>(sorted.Count);
            int normalizedTotal = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                InterestGroupCloutValue value = sorted[i];
                long product = checked((long)value.CloutS * FixedMath.HundredS);
                int baseValue = checked((int)(product / total));
                normalized.Add(new InterestGroupCloutValue(value.InterestGroupId, baseValue));
                normalizedTotal = checked(normalizedTotal + baseValue);
            }

            int residue = FixedMath.HundredS - normalizedTotal;
            for (int i = 0; i < normalized.Count; i++)
            {
                InterestGroupCloutValue value = normalized[i];
                int finalValue = value.CloutS;
                if (value.InterestGroupId == residueWinnerId)
                {
                    finalValue = checked(finalValue + residue);
                }

                if (finalValue < 0 || finalValue > FixedMath.HundredS)
                {
                    throw new OverflowException("Normalized clout is outside the valid range.");
                }

                normalized[i] = new InterestGroupCloutValue(value.InterestGroupId, finalValue);
            }

            int finalTotal = 0;
            for (int i = 0; i < normalized.Count; i++)
            {
                finalTotal = checked(finalTotal + normalized[i].CloutS);
            }

            if (finalTotal != FixedMath.HundredS)
            {
                throw new InvalidOperationException("Normalized clout total does not equal 10000.");
            }

            return System.Array.AsReadOnly(normalized.ToArray());
        }
    }
}
