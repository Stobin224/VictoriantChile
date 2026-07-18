using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Causality
{
    public sealed class TickCausalBuffer
    {
        private readonly VisibleTargetCatalog _visibleTargets;
        private readonly Dictionary<TargetPath, MutableTargetRecord> _records;
        private bool _sealed;

        public TickCausalBuffer(int tick, VisibleTargetCatalog visibleTargets)
        {
            if (tick < 0)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidCause,
                    null,
                    tick,
                    "Tick cannot be negative.");
            }

            Tick = tick;
            _visibleTargets = visibleTargets ?? throw new ArgumentNullException(nameof(visibleTargets));
            _records = new Dictionary<TargetPath, MutableTargetRecord>();
        }

        public int Tick { get; }

        public void TrackTarget(TargetPath target, int initialValueS)
        {
            EnsureMutable();
            _visibleTargets.RequireVisible(target);
            if (_records.ContainsKey(target))
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.DuplicateTargetBaseline,
                    target.ToString(),
                    Tick,
                    "Each visible target can only be tracked once per tick.");
            }

            _records.Add(target, new MutableTargetRecord(initialValueS));
        }

        public void RecordContribution(TargetPath target, CauseRef cause, long realizedDeltaS)
        {
            EnsureMutable();
            _visibleTargets.RequireVisible(target);
            MutableTargetRecord record = RequireTrackedRecord(target, CausalLedgerErrorCodes.ContributionBeforeBaseline, "Contributions require a tracked baseline.");
            if (cause == null)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidCause,
                    target.ToString(),
                    Tick,
                    "Causal contributions require a non-null CauseRef.");
            }

            if (realizedDeltaS == 0)
            {
                return;
            }

            if (record.Closed)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.ContributionAfterClose,
                    target.ToString(),
                    Tick,
                    "Closed targets cannot receive new contributions.");
            }

            try
            {
                if (record.Contributions.TryGetValue(cause, out long existing))
                {
                    long updated = checked(existing + realizedDeltaS);
                    if (updated == 0)
                    {
                        record.Contributions.Remove(cause);
                    }
                    else
                    {
                        record.Contributions[cause] = updated;
                    }
                }
                else
                {
                    record.Contributions.Add(cause, realizedDeltaS);
                }
            }
            catch (OverflowException exception)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.ArithmeticOverflow,
                    target.ToString(),
                    Tick,
                    "Causal contribution accumulation overflowed.",
                    innerException: exception);
            }
        }

        public void CloseTarget(TargetPath target, int finalValueS)
        {
            EnsureMutable();
            _visibleTargets.RequireVisible(target);
            MutableTargetRecord record = RequireTrackedRecord(target, CausalLedgerErrorCodes.TargetNotTracked, "Targets must be tracked before they can be closed.");
            if (record.Closed)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.DuplicateClose,
                    target.ToString(),
                    Tick,
                    "Each tracked target can only be closed once per tick.");
            }

            long observedDeltaS;
            long causalDeltaS = 0;
            try
            {
                observedDeltaS = checked((long)finalValueS - record.InitialValueS);
                foreach (KeyValuePair<CauseRef, long> contribution in record.Contributions)
                {
                    causalDeltaS = checked(causalDeltaS + contribution.Value);
                }
            }
            catch (OverflowException exception)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.ArithmeticOverflow,
                    target.ToString(),
                    Tick,
                    "Closing a tracked target overflowed causal arithmetic.",
                    innerException: exception);
            }

            if (observedDeltaS != causalDeltaS)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.AccountingMismatch,
                    target.ToString(),
                    Tick,
                    "Target final minus initial does not equal the exact sum of recorded causal contributions.",
                    observedDeltaS,
                    causalDeltaS);
            }

            record.FinalValueS = finalValueS;
            record.Closed = true;
        }

        public TickCausalSnapshot Seal()
        {
            EnsureMutable();
            List<TargetPath> targets = new List<TargetPath>(_records.Keys);
            targets.Sort();
            List<TargetCausalSnapshot> auditedTargets = new List<TargetCausalSnapshot>();
            for (int i = 0; i < targets.Count; i++)
            {
                TargetPath target = targets[i];
                MutableTargetRecord record = _records[target];
                if (!record.Closed)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.UnclosedTarget,
                        target.ToString(),
                        Tick,
                        "All tracked targets must be closed before a causal tick can be sealed.");
                }

                long delta;
                try
                {
                    delta = checked((long)record.FinalValueS - record.InitialValueS);
                }
                catch (OverflowException exception)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.ArithmeticOverflow,
                        target.ToString(),
                        Tick,
                        "Sealing the causal tick overflowed while computing a target delta.",
                        innerException: exception);
                }

                auditedTargets.Add(new TargetCausalSnapshot(
                    target,
                    record.InitialValueS,
                    record.FinalValueS,
                    BuildContributions(record.Contributions)));
            }

            _sealed = true;
            return new TickCausalSnapshot(Tick, auditedTargets);
        }

        private static IReadOnlyList<CausalContribution> BuildContributions(Dictionary<CauseRef, long> contributions)
        {
            List<CausalContribution> values = new List<CausalContribution>();
            foreach (KeyValuePair<CauseRef, long> contribution in contributions)
            {
                if (contribution.Value != 0)
                {
                    values.Add(new CausalContribution(contribution.Key, contribution.Value));
                }
            }

            values.Sort((left, right) => left.Cause.CompareTo(right.Cause));
            return values;
        }

        private void EnsureMutable()
        {
            if (_sealed)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.MutationAfterSeal,
                    null,
                    Tick,
                    "Tick causal buffers cannot be mutated after Seal() has been called.");
            }
        }

        private MutableTargetRecord RequireTrackedRecord(TargetPath target, string notTrackedCode, string notTrackedMessage)
        {
            if (!_records.TryGetValue(target, out MutableTargetRecord record))
            {
                throw new CausalLedgerException(
                    notTrackedCode,
                    target.ToString(),
                    Tick,
                    notTrackedMessage);
            }

            return record;
        }

        private sealed class MutableTargetRecord
        {
            public MutableTargetRecord(int initialValueS)
            {
                InitialValueS = initialValueS;
                Contributions = new Dictionary<CauseRef, long>();
            }

            public int InitialValueS { get; }

            public int FinalValueS { get; set; }

            public bool Closed { get; set; }

            public Dictionary<CauseRef, long> Contributions { get; }
        }
    }

    public static class CausalPeriodAccumulator
    {
        public static PeriodCausalSnapshot Accumulate(IEnumerable<TickCausalSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }

            List<TickCausalSnapshot> ordered = new List<TickCausalSnapshot>();
            foreach (TickCausalSnapshot snapshot in snapshots)
            {
                if (snapshot == null)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.NonConsecutiveTicks,
                        null,
                        null,
                        "Causal period accumulation cannot accept null tick snapshots.");
                }

                ordered.Add(snapshot);
            }

            if (ordered.Count == 0)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.NonConsecutiveTicks,
                    null,
                    null,
                    "At least one tick snapshot is required for period accumulation.");
            }

            int expectedTick = ordered[0].Tick;
            Dictionary<TargetPath, MutableAggregateRecord> records = new Dictionary<TargetPath, MutableAggregateRecord>();
            for (int i = 0; i < ordered.Count; i++)
            {
                TickCausalSnapshot snapshot = ordered[i];
                if (snapshot.Tick != expectedTick)
                {
                    string code = snapshot.Tick < expectedTick ? CausalLedgerErrorCodes.DuplicateTick : CausalLedgerErrorCodes.NonConsecutiveTicks;
                    throw new CausalLedgerException(
                        code,
                        null,
                        snapshot.Tick,
                        "Tick snapshots must be provided in strictly consecutive ascending order.");
                }

                expectedTick = checked(expectedTick + 1);
                HashSet<TargetPath> seenThisTick = new HashSet<TargetPath>();
                for (int j = 0; j < snapshot.AuditedTargets.Count; j++)
                {
                    TargetCausalSnapshot target = snapshot.AuditedTargets[j];
                    seenThisTick.Add(target.Target);
                    if (!records.TryGetValue(target.Target, out MutableAggregateRecord aggregate))
                    {
                        aggregate = new MutableAggregateRecord(target.InitialValueS, snapshot.Tick);
                        records.Add(target.Target, aggregate);
                    }
                    else
                    {
                        if (aggregate.LastSeenTick != snapshot.Tick - 1)
                        {
                            throw new CausalLedgerException(
                                CausalLedgerErrorCodes.DiscontinuousTargetValues,
                                target.Target.ToString(),
                                snapshot.Tick,
                                "A previously audited target disappeared from an intermediate tick and later reappeared.");
                        }

                        if (aggregate.FinalValueS != target.InitialValueS)
                        {
                            throw new CausalLedgerException(
                                CausalLedgerErrorCodes.DiscontinuousTargetValues,
                                target.Target.ToString(),
                                snapshot.Tick,
                                "Target continuity broke across consecutive causal ticks.");
                        }
                    }

                    aggregate.FinalValueS = target.FinalValueS;
                    aggregate.LastSeenTick = snapshot.Tick;
                    for (int k = 0; k < target.Contributions.Count; k++)
                    {
                        CausalContribution contribution = target.Contributions[k];
                        try
                        {
                            if (aggregate.Contributions.TryGetValue(contribution.Cause, out long existing))
                            {
                                long updated = checked(existing + contribution.DeltaS);
                                if (updated == 0)
                                {
                                    aggregate.Contributions.Remove(contribution.Cause);
                                }
                                else
                                {
                                    aggregate.Contributions[contribution.Cause] = updated;
                                }
                            }
                            else
                            {
                                aggregate.Contributions.Add(contribution.Cause, contribution.DeltaS);
                            }
                        }
                        catch (OverflowException exception)
                        {
                            throw new CausalLedgerException(
                                CausalLedgerErrorCodes.ArithmeticOverflow,
                                target.Target.ToString(),
                                snapshot.Tick,
                                "Period causal accumulation overflowed while summing contributions.",
                                innerException: exception);
                        }
                    }
                }

                foreach (KeyValuePair<TargetPath, MutableAggregateRecord> pair in records)
                {
                    if (pair.Value.LastSeenTick == snapshot.Tick - 1 && !seenThisTick.Contains(pair.Key))
                    {
                        throw new CausalLedgerException(
                            CausalLedgerErrorCodes.DiscontinuousTargetValues,
                            pair.Key.ToString(),
                            snapshot.Tick,
                            "A previously audited target disappeared from the audited surface before the period ended.");
                    }
                }
            }

            List<TargetPath> keys = new List<TargetPath>(records.Keys);
            keys.Sort();
            List<TargetCausalSnapshot> auditedTargets = new List<TargetCausalSnapshot>();
            for (int i = 0; i < keys.Count; i++)
            {
                TargetPath target = keys[i];
                MutableAggregateRecord aggregate = records[target];
                auditedTargets.Add(new TargetCausalSnapshot(
                    target,
                    aggregate.InitialValueS,
                    aggregate.FinalValueS,
                    BuildContributions(aggregate.Contributions)));
            }

            return new PeriodCausalSnapshot(
                ordered[0].Tick,
                ordered[ordered.Count - 1].Tick,
                ordered.Count,
                auditedTargets);
        }

        private static IReadOnlyList<CausalContribution> BuildContributions(Dictionary<CauseRef, long> contributions)
        {
            List<CausalContribution> values = new List<CausalContribution>();
            foreach (KeyValuePair<CauseRef, long> contribution in contributions)
            {
                if (contribution.Value != 0)
                {
                    values.Add(new CausalContribution(contribution.Key, contribution.Value));
                }
            }

            values.Sort((left, right) => left.Cause.CompareTo(right.Cause));
            return values;
        }

        private sealed class MutableAggregateRecord
        {
            public MutableAggregateRecord(int initialValueS, int firstSeenTick)
            {
                InitialValueS = initialValueS;
                FinalValueS = initialValueS;
                LastSeenTick = firstSeenTick;
                Contributions = new Dictionary<CauseRef, long>();
            }

            public int InitialValueS { get; }

            public int FinalValueS { get; set; }

            public int LastSeenTick { get; set; }

            public Dictionary<CauseRef, long> Contributions { get; }
        }
    }

    public static class CausalTopNProjector
    {
        public static CausalTopNProjection Project(TickCausalSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return Project(snapshot.ChangedTargets);
        }

        public static CausalTopNProjection Project(PeriodCausalSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return Project(snapshot.ChangedTargets);
        }

        private static CausalTopNProjection Project(IReadOnlyList<TargetCausalSnapshot> targets)
        {
            List<TargetCausalSnapshot> orderedTargets = new List<TargetCausalSnapshot>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].DeltaTotalS != 0)
                {
                    orderedTargets.Add(targets[i]);
                }
            }

            orderedTargets.Sort(CompareTargets);
            if (orderedTargets.Count > 10)
            {
                orderedTargets.RemoveRange(10, orderedTargets.Count - 10);
            }

            List<ProjectedCausalTarget> projected = new List<ProjectedCausalTarget>();
            for (int i = 0; i < orderedTargets.Count; i++)
            {
                TargetCausalSnapshot target = orderedTargets[i];
                List<CausalContribution> orderedContributions = new List<CausalContribution>(target.Contributions);
                orderedContributions.Sort(CompareContributions);
                List<ProjectedCausalCause> topCauses = new List<ProjectedCausalCause>();
                long otherDeltaS = 0;
                for (int j = 0; j < orderedContributions.Count; j++)
                {
                    CausalContribution contribution = orderedContributions[j];
                    if (j < 3)
                    {
                        topCauses.Add(new ProjectedCausalCause(contribution.Cause, contribution.DeltaS));
                    }
                    else
                    {
                        otherDeltaS = checked(otherDeltaS + contribution.DeltaS);
                    }
                }

                long projectedSum = otherDeltaS;
                for (int j = 0; j < topCauses.Count; j++)
                {
                    projectedSum = checked(projectedSum + topCauses[j].DeltaS);
                }

                if (projectedSum != target.DeltaTotalS)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.AccountingMismatch,
                        target.Target.ToString(),
                        null,
                        "Top-N projection did not preserve exact causal accounting.",
                        target.DeltaTotalS,
                        projectedSum);
                }

                projected.Add(new ProjectedCausalTarget(
                    target.Target,
                    target.InitialValueS,
                    target.FinalValueS,
                    target.DeltaTotalS,
                    topCauses,
                    otherDeltaS));
            }

            return new CausalTopNProjection(projected);
        }

        private static int CompareTargets(TargetCausalSnapshot left, TargetCausalSnapshot right)
        {
            int magnitudeCompare = AbsMagnitude(right.DeltaTotalS).CompareTo(AbsMagnitude(left.DeltaTotalS));
            if (magnitudeCompare != 0)
            {
                return magnitudeCompare;
            }

            return left.Target.CompareTo(right.Target);
        }

        private static int CompareContributions(CausalContribution left, CausalContribution right)
        {
            int magnitudeCompare = AbsMagnitude(right.DeltaS).CompareTo(AbsMagnitude(left.DeltaS));
            if (magnitudeCompare != 0)
            {
                return magnitudeCompare;
            }

            return left.Cause.CompareTo(right.Cause);
        }

        private static ulong AbsMagnitude(long value)
        {
            if (value >= 0)
            {
                return (ulong)value;
            }

            return value == long.MinValue ? ((ulong)long.MaxValue) + 1UL : (ulong)(-value);
        }
    }

    public static class InterestGroupCloutNormalizationLedger
    {
        public static IReadOnlyList<CausalTargetContribution> BuildContributions(
            IEnumerable<InterestGroupCloutValue> beforeValues,
            IEnumerable<InterestGroupCloutValue> afterValues)
        {
            IReadOnlyList<InterestGroupCloutValue> before = Snapshot(beforeValues, "beforeValues");
            IReadOnlyList<InterestGroupCloutValue> after = Snapshot(afterValues, "afterValues");
            if (before.Count != after.Count)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidNormalization,
                    "igs.*.clout",
                    null,
                    "Clout normalization requires the same interest group IDs before and after normalization.");
            }

            long afterTotal = 0;
            for (int i = 0; i < before.Count; i++)
            {
                if (!string.Equals(before[i].InterestGroupId, after[i].InterestGroupId, StringComparison.Ordinal))
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidNormalization,
                        "igs.*.clout",
                        null,
                        "Clout normalization requires identical sorted interest group IDs before and after normalization.");
                }

                if (after[i].CloutS < 0 || after[i].CloutS > FixedMath.HundredS)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidNormalization,
                        InitialTargetRegistry.InterestGroupClout(after[i].InterestGroupId).ToString(),
                        null,
                        "Normalized IG clout must remain within the inclusive range 0..10000.");
                }

                afterTotal = checked(afterTotal + after[i].CloutS);
            }

            if (afterTotal != FixedMath.HundredS)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidNormalization,
                    "igs.*.clout",
                    null,
                    "Normalized IG clout must sum exactly 10000.");
            }

            List<CausalTargetContribution> contributions = new List<CausalTargetContribution>();
            for (int i = 0; i < before.Count; i++)
            {
                long delta = checked((long)after[i].CloutS - before[i].CloutS);
                if (delta != 0)
                {
                    contributions.Add(new CausalTargetContribution(
                        InitialTargetRegistry.InterestGroupClout(after[i].InterestGroupId),
                        CauseRef.SystemIgCloutNormalize,
                        delta));
                }
            }

            return Array.AsReadOnly(contributions.ToArray());
        }

        private static IReadOnlyList<InterestGroupCloutValue> Snapshot(IEnumerable<InterestGroupCloutValue> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            List<InterestGroupCloutValue> snapshot = new List<InterestGroupCloutValue>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (InterestGroupCloutValue value in values)
            {
                if (value == null)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidNormalization,
                        "igs.*.clout",
                        null,
                        "Clout normalization inputs cannot contain null entries.");
                }

                if (!seen.Add(value.InterestGroupId))
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidNormalization,
                        "igs.*.clout",
                        null,
                        "Clout normalization inputs cannot contain duplicate interest group IDs.");
                }

                snapshot.Add(value);
            }

            snapshot.Sort(delegate(InterestGroupCloutValue left, InterestGroupCloutValue right)
            {
                return string.Compare(left.InterestGroupId, right.InterestGroupId, StringComparison.Ordinal);
            });
            return Array.AsReadOnly(snapshot.ToArray());
        }
    }
}
