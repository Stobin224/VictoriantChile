using System;
using System.Collections.Generic;

namespace VictoriantChile.Simulation.Core.Causality
{
    public enum CauseCategory
    {
        Decision = 1,
        Event = 2,
        Reform = 3,
        Movement = 4,
        Modifier = 5,
        System = 6
    }

    public static class CausalLedgerErrorCodes
    {
        public const string InvalidCause = "causal.invalid_cause";
        public const string InvalidParent = "causal.invalid_parent";
        public const string NonVisibleTarget = "causal.non_visible_target";
        public const string InvalidTargetOrder = "causal.invalid_target_order";
        public const string DuplicateTargetBaseline = "causal.duplicate_target_baseline";
        public const string TargetNotTracked = "causal.target_not_tracked";
        public const string ContributionBeforeBaseline = "causal.contribution_before_baseline";
        public const string ContributionAfterClose = "causal.contribution_after_close";
        public const string DuplicateClose = "causal.duplicate_close";
        public const string UnclosedTarget = "causal.unclosed_target";
        public const string AccountingMismatch = "causal.accounting_mismatch";
        public const string ArithmeticOverflow = "causal.arithmetic_overflow";
        public const string NonConsecutiveTicks = "causal.non_consecutive_ticks";
        public const string DuplicateTick = "causal.duplicate_tick";
        public const string DiscontinuousTargetValues = "causal.discontinuous_target_values";
        public const string InvalidNormalization = "causal.invalid_normalization";
        public const string MutationAfterSeal = "causal.mutation_after_seal";
    }

    public sealed class CausalLedgerException : InvalidOperationException
    {
        public CausalLedgerException(
            string code,
            string target,
            int? tick,
            string message,
            long? observedDeltaS = null,
            long? causalDeltaS = null,
            Exception innerException = null)
            : base(message, innerException)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("Exception code cannot be null or empty.", nameof(code));
            }

            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Exception message cannot be null or empty.", nameof(message));
            }

            Code = code;
            Target = target;
            Tick = tick;
            ObservedDeltaS = observedDeltaS;
            CausalDeltaS = causalDeltaS;
        }

        public string Code { get; }

        public string Target { get; }

        public int? Tick { get; }

        public long? ObservedDeltaS { get; }

        public long? CausalDeltaS { get; }
    }

    public sealed class CauseRef : IEquatable<CauseRef>, IComparable<CauseRef>
    {
        public static readonly CauseRef SystemClamp = new CauseRef(CauseCategory.System, "CLAMP");
        public static readonly CauseRef SystemRounding = new CauseRef(CauseCategory.System, "ROUNDING");
        public static readonly CauseRef SystemIgCloutNormalize = new CauseRef(CauseCategory.System, "IG_CLOUT_NORMALIZE");

        public CauseRef(CauseCategory category, string id, CauseRef parent = null)
        {
            ValidateCategory(category);
            ValidateIdentifier(id);
            ValidateParent(category, parent);

            Category = category;
            Id = id;
            Parent = parent;
            DisplayText = CategoryToString(category) + ":" + id;
            CanonicalKey = parent == null
                ? DisplayText
                : DisplayText + "|parent=" + parent.CanonicalKey;
        }

        public CauseCategory Category { get; }

        public string Id { get; }

        public CauseRef Parent { get; }

        public string DisplayText { get; }

        public string CanonicalKey { get; }

        public override string ToString()
        {
            return CanonicalKey;
        }

        public bool Equals(CauseRef other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return Category == other.Category
                && string.Equals(Id, other.Id, StringComparison.Ordinal)
                && Equals(Parent, other.Parent);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as CauseRef);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (int)Category;
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Id);
                hash = (hash * 31) + (Parent == null ? 0 : Parent.GetHashCode());
                return hash;
            }
        }

        public int CompareTo(CauseRef other)
        {
            if (ReferenceEquals(other, null))
            {
                return 1;
            }

            int categoryCompare = ((int)Category).CompareTo((int)other.Category);
            if (categoryCompare != 0)
            {
                return categoryCompare;
            }

            int idCompare = string.Compare(Id, other.Id, StringComparison.Ordinal);
            if (idCompare != 0)
            {
                return idCompare;
            }

            if (Parent == null && other.Parent == null)
            {
                return 0;
            }

            if (Parent == null)
            {
                return -1;
            }

            if (other.Parent == null)
            {
                return 1;
            }

            return Parent.CompareTo(other.Parent);
        }

        private static void ValidateCategory(CauseCategory category)
        {
            if (category < CauseCategory.Decision || category > CauseCategory.System)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidCause,
                    null,
                    null,
                    "Cause category must be one of the six supported values.");
            }
        }

        private static void ValidateIdentifier(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidCause,
                    null,
                    null,
                    "Cause ID cannot be null or empty.");
            }

            if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidCause,
                    null,
                    null,
                    "Cause ID cannot contain leading or trailing whitespace.");
            }

            for (int i = 0; i < id.Length; i++)
            {
                char value = id[i];
                if (char.IsWhiteSpace(value)
                    || char.IsControl(value)
                    || value > 127
                    || value == ':'
                    || value == '|')
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidCause,
                        null,
                        null,
                        "Cause ID must be printable ASCII without whitespace, colon or pipe.");
                }
            }
        }

        private static void ValidateParent(CauseCategory category, CauseRef parent)
        {
            if (category == CauseCategory.Modifier)
            {
                if (parent == null)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidParent,
                        null,
                        null,
                        "Modifier causes require exactly one non-null parent.");
                }

                if (parent.Parent != null)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidParent,
                        null,
                        null,
                        "Cause parent chains cannot exceed one level.");
                }

                if (parent.Category != CauseCategory.Decision
                    && parent.Category != CauseCategory.Event
                    && parent.Category != CauseCategory.Reform
                    && parent.Category != CauseCategory.Movement)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidParent,
                        null,
                        null,
                        "Modifier parents must be DECISION, EVENT, REFORM or MOVEMENT.");
                }

                return;
            }

            if (parent != null)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidParent,
                    null,
                    null,
                    "Only modifier causes may have a parent in causal ledger v1.");
            }
        }

        private static string CategoryToString(CauseCategory category)
        {
            if (category == CauseCategory.Decision) { return "DECISION"; }
            if (category == CauseCategory.Event) { return "EVENT"; }
            if (category == CauseCategory.Reform) { return "REFORM"; }
            if (category == CauseCategory.Movement) { return "MOVEMENT"; }
            if (category == CauseCategory.Modifier) { return "MODIFIER"; }
            if (category == CauseCategory.System) { return "SYSTEM"; }
            throw new CausalLedgerException(
                CausalLedgerErrorCodes.InvalidCause,
                null,
                null,
                "Cause category must be one of the six supported values.");
        }
    }

    public sealed class CausalContribution
    {
        public CausalContribution(CauseRef cause, long deltaS)
        {
            if (cause == null)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidCause,
                    null,
                    null,
                    "Causal contributions require a non-null CauseRef.");
            }

            Cause = cause;
            DeltaS = deltaS;
        }

        public CauseRef Cause { get; }

        public long DeltaS { get; }
    }

    public sealed class CausalTargetContribution
    {
        public CausalTargetContribution(Targets.TargetPath target, CauseRef cause, long deltaS)
        {
            if (!target.IsValid)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.NonVisibleTarget,
                    "<invalid>",
                    null,
                    "Target contributions require a valid concrete target path.");
            }

            if (cause == null)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidCause,
                    target.ToString(),
                    null,
                    "Target contributions require a non-null CauseRef.");
            }

            Target = target;
            Cause = cause;
            DeltaS = deltaS;
        }

        public Targets.TargetPath Target { get; }

        public CauseRef Cause { get; }

        public long DeltaS { get; }
    }

    public sealed class TargetCausalSnapshot
    {
        public TargetCausalSnapshot(Targets.TargetPath target, int initialValueS, int finalValueS, IEnumerable<CausalContribution> contributions)
        {
            if (!target.IsValid)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.NonVisibleTarget,
                    "<invalid>",
                    null,
                    "Target causal snapshots require a valid target path.");
            }

            List<CausalContribution> snapshot = SnapshotContributions(contributions, target.ToString());
            long deltaTotalS;
            long sum = 0;
            try
            {
                deltaTotalS = checked((long)finalValueS - initialValueS);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    sum = checked(sum + snapshot[i].DeltaS);
                }
            }
            catch (OverflowException exception)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.ArithmeticOverflow,
                    target.ToString(),
                    null,
                    "Causal snapshot arithmetic overflowed.",
                    innerException: exception);
            }

            if (sum != deltaTotalS)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.AccountingMismatch,
                    target.ToString(),
                    null,
                    "Target causal snapshot does not balance final minus initial against summed contributions.",
                    deltaTotalS,
                    sum);
            }

            Target = target;
            InitialValueS = initialValueS;
            FinalValueS = finalValueS;
            DeltaTotalS = deltaTotalS;
            Contributions = Array.AsReadOnly(snapshot.ToArray());
        }

        public Targets.TargetPath Target { get; }

        public int InitialValueS { get; }

        public int FinalValueS { get; }

        public long DeltaTotalS { get; }

        public IReadOnlyList<CausalContribution> Contributions { get; }

        private static List<CausalContribution> SnapshotContributions(IEnumerable<CausalContribution> contributions, string target)
        {
            if (contributions == null)
            {
                throw new ArgumentNullException(nameof(contributions));
            }

            List<CausalContribution> snapshot = new List<CausalContribution>();
            HashSet<CauseRef> seen = new HashSet<CauseRef>();
            foreach (CausalContribution contribution in contributions)
            {
                if (contribution == null)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidCause,
                        target,
                        null,
                        "Causal contribution collections cannot contain null entries.");
                }

                if (contribution.DeltaS == 0)
                {
                    continue;
                }

                if (!seen.Add(contribution.Cause))
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidCause,
                        target,
                        null,
                        "Duplicate CauseRef entries are not allowed inside a causal snapshot.");
                }

                snapshot.Add(contribution);
            }

            snapshot.Sort((left, right) => left.Cause.CompareTo(right.Cause));
            return snapshot;
        }
    }

    public sealed class TickCausalSnapshot
    {
        public TickCausalSnapshot(int tick, IEnumerable<TargetCausalSnapshot> auditedTargets)
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
            AuditedTargets = SnapshotTargets(auditedTargets);
            ChangedTargets = FilterChangedTargets(AuditedTargets);
        }

        public int Tick { get; }

        public IReadOnlyList<TargetCausalSnapshot> AuditedTargets { get; }

        public IReadOnlyList<TargetCausalSnapshot> ChangedTargets { get; }

        internal static IReadOnlyList<TargetCausalSnapshot> SnapshotTargets(IEnumerable<TargetCausalSnapshot> targets)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            List<TargetCausalSnapshot> snapshot = new List<TargetCausalSnapshot>();
            HashSet<Targets.TargetPath> seen = new HashSet<Targets.TargetPath>();
            foreach (TargetCausalSnapshot target in targets)
            {
                if (target == null)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.NonVisibleTarget,
                        null,
                        null,
                        "Tick causal snapshots cannot contain null targets.");
                }

                if (!seen.Add(target.Target))
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidTargetOrder,
                        target.Target.ToString(),
                        null,
                        "Tick causal snapshots cannot contain duplicate target entries.");
                }

                snapshot.Add(target);
            }

            snapshot.Sort((left, right) => left.Target.CompareTo(right.Target));
            return Array.AsReadOnly(snapshot.ToArray());
        }

        private static IReadOnlyList<TargetCausalSnapshot> FilterChangedTargets(IReadOnlyList<TargetCausalSnapshot> targets)
        {
            List<TargetCausalSnapshot> changed = new List<TargetCausalSnapshot>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].DeltaTotalS != 0)
                {
                    changed.Add(targets[i]);
                }
            }

            return Array.AsReadOnly(changed.ToArray());
        }
    }

    public sealed class PeriodCausalSnapshot
    {
        public PeriodCausalSnapshot(int tickStart, int tickEnd, int tickCount, IEnumerable<TargetCausalSnapshot> auditedTargets)
        {
            if (tickStart < 0 || tickEnd < tickStart || tickCount <= 0 || tickEnd - tickStart + 1 != tickCount)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.NonConsecutiveTicks,
                    null,
                    null,
                    "Period causal snapshots require a valid consecutive tick range.");
            }

            TickStart = tickStart;
            TickEnd = tickEnd;
            TickCount = tickCount;
            AuditedTargets = TickCausalSnapshot.SnapshotTargets(auditedTargets);
            ChangedTargets = FilterChangedTargets(AuditedTargets);
        }

        public int TickStart { get; }

        public int TickEnd { get; }

        public int TickCount { get; }

        public IReadOnlyList<TargetCausalSnapshot> AuditedTargets { get; }

        public IReadOnlyList<TargetCausalSnapshot> ChangedTargets { get; }

        private static IReadOnlyList<TargetCausalSnapshot> FilterChangedTargets(IReadOnlyList<TargetCausalSnapshot> targets)
        {
            List<TargetCausalSnapshot> changed = new List<TargetCausalSnapshot>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].DeltaTotalS != 0)
                {
                    changed.Add(targets[i]);
                }
            }

            return Array.AsReadOnly(changed.ToArray());
        }
    }

    public sealed class ProjectedCausalCause
    {
        public ProjectedCausalCause(CauseRef cause, long deltaS)
        {
            if (cause == null)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidCause,
                    null,
                    null,
                    "Projected causes require a non-null CauseRef.");
            }

            Cause = cause;
            DeltaS = deltaS;
        }

        public CauseRef Cause { get; }

        public long DeltaS { get; }
    }

    public sealed class ProjectedCausalTarget
    {
        public ProjectedCausalTarget(
            Targets.TargetPath target,
            int initialValueS,
            int finalValueS,
            long deltaTotalS,
            IEnumerable<ProjectedCausalCause> topCauses,
            long otherDeltaS)
        {
            if (!target.IsValid)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.NonVisibleTarget,
                    "<invalid>",
                    null,
                    "Projected targets require a valid target path.");
            }

            Target = target;
            InitialValueS = initialValueS;
            FinalValueS = finalValueS;
            DeltaTotalS = deltaTotalS;
            TopCauses = Array.AsReadOnly(new List<ProjectedCausalCause>(topCauses ?? new ProjectedCausalCause[0]).ToArray());
            OtherDeltaS = otherDeltaS;
        }

        public Targets.TargetPath Target { get; }

        public int InitialValueS { get; }

        public int FinalValueS { get; }

        public long DeltaTotalS { get; }

        public IReadOnlyList<ProjectedCausalCause> TopCauses { get; }

        public long OtherDeltaS { get; }
    }

    public sealed class CausalTopNProjection
    {
        public CausalTopNProjection(IEnumerable<ProjectedCausalTarget> targets)
        {
            Targets = Array.AsReadOnly(new List<ProjectedCausalTarget>(targets ?? new ProjectedCausalTarget[0]).ToArray());
        }

        public IReadOnlyList<ProjectedCausalTarget> Targets { get; }
    }
}
