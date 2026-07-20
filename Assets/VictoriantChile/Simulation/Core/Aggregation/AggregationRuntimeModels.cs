using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Aggregation
{
    public enum AggregationExpressionKindRuntime
    {
        Avg,
        Copy
    }

    public enum AggregationRoundingModeRuntime
    {
        HalfAwayFromZero
    }

    public enum AggregationCauseBase
    {
        Reversion,
        Derived,
        Aggregation
    }

    public static class AggregationCauseMaterializer
    {
        private const string ReversionId = "REVERSION";
        private const string DerivedId = "DERIVED";
        private const string AggregationId = "AGG";

        public static CauseRef MaterializeAggregationComponent(TargetPath metric, TargetPath component)
        {
            ValidateMetricTarget(metric, nameof(metric));
            ValidateInternalTarget(component, nameof(component));

            return new CauseRef(CauseCategory.System, AggregationId + "." + metric.ToString() + "." + component.ToString());
        }

        public static CauseRef MaterializeReversion(TargetPath target)
        {
            ValidateInternalTarget(target, nameof(target));
            return new CauseRef(CauseCategory.System, BaseId(AggregationCauseBase.Reversion) + "." + target.ToString());
        }

        public static CauseRef MaterializeDerived(TargetPath target)
        {
            ValidateInternalTarget(target, nameof(target));
            return new CauseRef(CauseCategory.System, BaseId(AggregationCauseBase.Derived) + "." + target.ToString());
        }

        public static CauseRef MaterializeAggregationMetric(TargetPath metric)
        {
            ValidateMetricTarget(metric, nameof(metric));
            return new CauseRef(CauseCategory.System, BaseId(AggregationCauseBase.Aggregation) + "." + metric.ToString());
        }

        internal static bool IsMetricTarget(TargetPath target)
        {
            return target.IsValid
                && target.SegmentCount == 2
                && string.Equals(target.Namespace, "metrics", StringComparison.Ordinal);
        }

        internal static bool IsInternalTarget(TargetPath target)
        {
            return target.IsValid
                && target.SegmentCount == 3
                && string.Equals(target.Namespace, "internals", StringComparison.Ordinal);
        }

        private static void ValidateMetricTarget(TargetPath target, string parameterName)
        {
            if (!IsMetricTarget(target))
            {
                throw new ArgumentException("Target must be a valid metrics.* path.", parameterName);
            }
        }

        private static void ValidateInternalTarget(TargetPath target, string parameterName)
        {
            if (!IsInternalTarget(target))
            {
                throw new ArgumentException("Target must be a valid internals.* path.", parameterName);
            }
        }

        private static string BaseId(AggregationCauseBase causeBase)
        {
            if (causeBase == AggregationCauseBase.Reversion) { return ReversionId; }
            if (causeBase == AggregationCauseBase.Derived) { return DerivedId; }
            if (causeBase == AggregationCauseBase.Aggregation) { return AggregationId; }
            throw new ArgumentOutOfRangeException(nameof(causeBase), causeBase, "Unknown aggregation cause base.");
        }
    }

    public sealed class AggregationRuntimePlan
    {
        public const int PpmDenominator = 1_000_000;
        public const int RequiredScale = 100;
        public const int RequiredMidS = 5000;
        public const int RequiredPrimaryMetricCount = 9;

        private static readonly TargetPath LegitimacyMetric = TargetPath.Parse("metrics.legitimacy");
        private readonly ReadOnlyDictionary<TargetPath, AggregationMetricRuntime> _metricsByTarget;

        public AggregationRuntimePlan(
            int scale,
            int midS,
            AggregationRoundingModeRuntime rounding,
            AggregationReversionPassRuntime internalReversion,
            AggregationDerivedPassRuntime derivedInternals,
            AggregationMetricsPassRuntime primaryMetrics,
            AggregationMetricsPassRuntime legitimacy)
        {
            if (scale != RequiredScale)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Aggregation scale must be exactly 100.");
            }

            if (midS != RequiredMidS)
            {
                throw new ArgumentOutOfRangeException(nameof(midS), "Aggregation midS must be exactly 5000.");
            }

            if (!Enum.IsDefined(typeof(AggregationRoundingModeRuntime), rounding)
                || rounding != AggregationRoundingModeRuntime.HalfAwayFromZero)
            {
                throw new ArgumentOutOfRangeException(nameof(rounding), "Aggregation rounding must be HALF_AWAY_FROM_ZERO.");
            }

            if (internalReversion == null)
            {
                throw new ArgumentNullException(nameof(internalReversion));
            }

            if (derivedInternals == null)
            {
                throw new ArgumentNullException(nameof(derivedInternals));
            }

            if (primaryMetrics == null)
            {
                throw new ArgumentNullException(nameof(primaryMetrics));
            }

            if (legitimacy == null)
            {
                throw new ArgumentNullException(nameof(legitimacy));
            }

            if (primaryMetrics.Metrics.Count != RequiredPrimaryMetricCount)
            {
                throw new ArgumentException("Primary metrics pass must contain exactly 9 metrics.", nameof(primaryMetrics));
            }

            if (legitimacy.Metrics.Count != 1 || legitimacy.Metrics[0].Metric != LegitimacyMetric)
            {
                throw new ArgumentException("Legitimacy pass must contain exactly metrics.legitimacy.", nameof(legitimacy));
            }

            Dictionary<TargetPath, AggregationMetricRuntime> lookup = new Dictionary<TargetPath, AggregationMetricRuntime>();
            AddMetricsToLookup(lookup, primaryMetrics.Metrics, nameof(primaryMetrics));
            AddMetricsToLookup(lookup, legitimacy.Metrics, nameof(legitimacy));
            _metricsByTarget = new ReadOnlyDictionary<TargetPath, AggregationMetricRuntime>(lookup);

            Scale = scale;
            MidS = midS;
            Rounding = rounding;
            InternalReversion = internalReversion;
            DerivedInternals = derivedInternals;
            PrimaryMetrics = primaryMetrics;
            Legitimacy = legitimacy;
        }

        public int Scale { get; }

        public int MidS { get; }

        public AggregationRoundingModeRuntime Rounding { get; }

        public AggregationReversionPassRuntime InternalReversion { get; }

        public AggregationDerivedPassRuntime DerivedInternals { get; }

        public AggregationMetricsPassRuntime PrimaryMetrics { get; }

        public AggregationMetricsPassRuntime Legitimacy { get; }

        public IReadOnlyDictionary<TargetPath, AggregationMetricRuntime> MetricsByTarget => _metricsByTarget;

        public bool TryGetMetric(TargetPath target, out AggregationMetricRuntime metric)
        {
            if (!target.IsValid)
            {
                metric = null;
                return false;
            }

            return _metricsByTarget.TryGetValue(target, out metric);
        }

        public bool TryGetDerivedCause(TargetPath target, out CauseRef cause)
        {
            if (!AggregationCauseMaterializer.IsInternalTarget(target))
            {
                cause = null;
                return false;
            }

            for (int i = 0; i < DerivedInternals.Rules.Count; i++)
            {
                DerivedAggregationRuleRuntime rule = DerivedInternals.Rules[i];
                if (rule.Target == target)
                {
                    cause = rule.Cause;
                    return true;
                }
            }

            cause = null;
            return false;
        }

        public CauseRef GetDerivedCause(TargetPath target)
        {
            if (!AggregationCauseMaterializer.IsInternalTarget(target))
            {
                throw new ArgumentException("Derived cause requires a valid internals.* target.", nameof(target));
            }

            if (TryGetDerivedCause(target, out CauseRef cause))
            {
                return cause;
            }

            throw new KeyNotFoundException("Derived cause target is not present in the aggregation plan.");
        }

        public bool TryGetMetricCause(TargetPath metric, out CauseRef cause)
        {
            if (!AggregationCauseMaterializer.IsMetricTarget(metric))
            {
                cause = null;
                return false;
            }

            if (TryGetMetric(metric, out AggregationMetricRuntime runtime))
            {
                cause = runtime.BaseCause;
                return true;
            }

            cause = null;
            return false;
        }

        public CauseRef GetMetricCause(TargetPath metric)
        {
            if (!AggregationCauseMaterializer.IsMetricTarget(metric))
            {
                throw new ArgumentException("Aggregation metric cause requires a valid metrics.* target.", nameof(metric));
            }

            if (TryGetMetricCause(metric, out CauseRef cause))
            {
                return cause;
            }

            throw new KeyNotFoundException("Aggregation metric is not present in the aggregation plan.");
        }

        public bool TryGetComponentCause(TargetPath metric, TargetPath component, out CauseRef cause)
        {
            if (!AggregationCauseMaterializer.IsMetricTarget(metric)
                || !AggregationCauseMaterializer.IsInternalTarget(component))
            {
                cause = null;
                return false;
            }

            if (TryGetMetric(metric, out AggregationMetricRuntime runtime))
            {
                for (int i = 0; i < runtime.Components.Count; i++)
                {
                    WeightedTargetComponentRuntime candidate = runtime.Components[i];
                    if (candidate.Target == component)
                    {
                        cause = candidate.Cause;
                        return true;
                    }
                }
            }

            cause = null;
            return false;
        }

        public CauseRef GetComponentCause(TargetPath metric, TargetPath component)
        {
            if (!AggregationCauseMaterializer.IsMetricTarget(metric))
            {
                throw new ArgumentException("Aggregation component cause requires a valid metrics.* metric target.", nameof(metric));
            }

            if (!AggregationCauseMaterializer.IsInternalTarget(component))
            {
                throw new ArgumentException("Aggregation component cause requires a valid internals.* component target.", nameof(component));
            }

            if (TryGetComponentCause(metric, component, out CauseRef cause))
            {
                return cause;
            }

            throw new KeyNotFoundException("Aggregation component pair is not present in the aggregation plan.");
        }

        private static void AddMetricsToLookup(
            Dictionary<TargetPath, AggregationMetricRuntime> lookup,
            IReadOnlyList<AggregationMetricRuntime> metrics,
            string parameterName)
        {
            for (int i = 0; i < metrics.Count; i++)
            {
                AggregationMetricRuntime metric = metrics[i];
                if (lookup.ContainsKey(metric.Metric))
                {
                    throw new ArgumentException("Duplicate metric target " + metric.Metric.ToString() + ".", parameterName);
                }

                lookup.Add(metric.Metric, metric);
            }
        }
    }

    public sealed class AggregationReversionPassRuntime
    {
        public AggregationReversionPassRuntime(
            IReadOnlyList<AggregationReversionGroupRuntime> groups,
            IReadOnlyList<TargetPath> skipTargets)
        {
            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            if (groups.Count == 0)
            {
                throw new ArgumentException("Reversion pass must have at least one group.", nameof(groups));
            }

            List<AggregationReversionGroupRuntime> groupSnapshot = new List<AggregationReversionGroupRuntime>(groups.Count);
            HashSet<TargetPattern> seenPatterns = new HashSet<TargetPattern>();
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] == null)
                {
                    throw new ArgumentException("Reversion groups cannot contain null entries.", nameof(groups));
                }

                if (!seenPatterns.Add(groups[i].Pattern))
                {
                    throw new ArgumentException("Reversion groups cannot contain duplicate patterns.", nameof(groups));
                }

                groupSnapshot.Add(groups[i]);
            }

            if (skipTargets == null)
            {
                throw new ArgumentNullException(nameof(skipTargets));
            }

            List<TargetPath> skipSnapshot = new List<TargetPath>(skipTargets.Count);
            HashSet<TargetPath> seenSkipTargets = new HashSet<TargetPath>();
            for (int i = 0; i < skipTargets.Count; i++)
            {
                if (!skipTargets[i].IsValid)
                {
                    throw new ArgumentException("Reversion skip targets must be valid concrete target paths.", nameof(skipTargets));
                }

                if (!seenSkipTargets.Add(skipTargets[i]))
                {
                    throw new ArgumentException("Reversion skip targets cannot contain duplicates.", nameof(skipTargets));
                }

                if (!AggregationCauseMaterializer.IsInternalTarget(skipTargets[i]))
                {
                    throw new ArgumentException("Reversion skip targets must be internals.* target paths.", nameof(skipTargets));
                }

                skipSnapshot.Add(skipTargets[i]);
            }

            Groups = Array.AsReadOnly(groupSnapshot.ToArray());
            SkipTargets = Array.AsReadOnly(skipSnapshot.ToArray());
            CauseBase = AggregationCauseBase.Reversion;
        }

        public IReadOnlyList<AggregationReversionGroupRuntime> Groups { get; }

        public IReadOnlyList<TargetPath> SkipTargets { get; }

        public AggregationCauseBase CauseBase { get; }

    }

    public sealed class AggregationDerivedPassRuntime
    {
        public AggregationDerivedPassRuntime(IReadOnlyList<DerivedAggregationRuleRuntime> rules)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            if (rules.Count == 0)
            {
                throw new ArgumentException("Derived internals pass must have at least one rule.", nameof(rules));
            }

            List<DerivedAggregationRuleRuntime> ruleSnapshot = new List<DerivedAggregationRuleRuntime>(rules.Count);
            HashSet<TargetPath> seenTargets = new HashSet<TargetPath>();
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null)
                {
                    throw new ArgumentException("Derived rules cannot contain null entries.", nameof(rules));
                }

                if (!seenTargets.Add(rules[i].Target))
                {
                    throw new ArgumentException("Derived rules cannot contain duplicate targets.", nameof(rules));
                }

                ruleSnapshot.Add(rules[i]);
            }

            Rules = Array.AsReadOnly(ruleSnapshot.ToArray());
        }

        public IReadOnlyList<DerivedAggregationRuleRuntime> Rules { get; }
    }

    public sealed class AggregationMetricsPassRuntime
    {
        public AggregationMetricsPassRuntime(IReadOnlyList<AggregationMetricRuntime> metrics)
        {
            if (metrics == null)
            {
                throw new ArgumentNullException(nameof(metrics));
            }

            if (metrics.Count == 0)
            {
                throw new ArgumentException("Metrics pass must have at least one metric.", nameof(metrics));
            }

            List<AggregationMetricRuntime> metricSnapshot = new List<AggregationMetricRuntime>(metrics.Count);
            HashSet<TargetPath> seenMetrics = new HashSet<TargetPath>();
            for (int i = 0; i < metrics.Count; i++)
            {
                if (metrics[i] == null)
                {
                    throw new ArgumentException("Metrics cannot contain null entries.", nameof(metrics));
                }

                if (!seenMetrics.Add(metrics[i].Metric))
                {
                    throw new ArgumentException("Metrics cannot contain duplicate targets.", nameof(metrics));
                }

                metricSnapshot.Add(metrics[i]);
            }

            Metrics = Array.AsReadOnly(metricSnapshot.ToArray());
        }

        public IReadOnlyList<AggregationMetricRuntime> Metrics { get; }
    }

    public sealed class AggregationReversionGroupRuntime
    {
        public AggregationReversionGroupRuntime(TargetPattern pattern, int halfLifeWeeks, int alphaPpm)
        {
            if (!pattern.IsValid)
            {
                throw new ArgumentException("Reversion group pattern must be valid.", nameof(pattern));
            }

            if (pattern.SegmentCount != 3 || !string.Equals(pattern[0], "internals", StringComparison.Ordinal))
            {
                throw new ArgumentException("Reversion group pattern must be a three-segment internals.* pattern.", nameof(pattern));
            }

            if (halfLifeWeeks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfLifeWeeks), "halfLifeWeeks must be positive.");
            }

            if (alphaPpm <= 0 || alphaPpm > AggregationRuntimePlan.PpmDenominator)
            {
                throw new ArgumentOutOfRangeException(nameof(alphaPpm), "alphaPpm must be in 1..1_000_000.");
            }

            Pattern = pattern;
            HalfLifeWeeks = halfLifeWeeks;
            AlphaPpm = alphaPpm;
        }

        public TargetPattern Pattern { get; }

        public int HalfLifeWeeks { get; }

        public int AlphaPpm { get; }
    }

    public sealed class AggregationMetricRuntime
    {
        public AggregationMetricRuntime(
            TargetPath metric,
            int halfLifeWeeks,
            int alphaPpm,
            int capPerWeekS,
            IReadOnlyList<WeightedTargetComponentRuntime> components)
        {
            if (!metric.IsValid)
            {
                throw new ArgumentException("Metric target must be valid.", nameof(metric));
            }

            if (!AggregationCauseMaterializer.IsMetricTarget(metric) || metric.SegmentCount != 2)
            {
                throw new ArgumentException("Metric target must be metrics.*.", nameof(metric));
            }

            if (halfLifeWeeks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfLifeWeeks), "halfLifeWeeks must be positive.");
            }

            if (alphaPpm <= 0 || alphaPpm > AggregationRuntimePlan.PpmDenominator)
            {
                throw new ArgumentOutOfRangeException(nameof(alphaPpm), "alphaPpm must be in 1..1_000_000.");
            }

            if (capPerWeekS < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capPerWeekS), "capPerWeekS must be non-negative.");
            }

            if (components == null)
            {
                throw new ArgumentNullException(nameof(components));
            }

            if (components.Count == 0)
            {
                throw new ArgumentException("Metric must have at least one component.", nameof(components));
            }

            List<WeightedTargetComponentRuntime> componentSnapshot = new List<WeightedTargetComponentRuntime>(components.Count);
            HashSet<TargetPath> seenComponents = new HashSet<TargetPath>();
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] == null)
                {
                    throw new ArgumentException("Metric components cannot contain null entries.", nameof(components));
                }

                if (!seenComponents.Add(components[i].Target))
                {
                    throw new ArgumentException("Metric components cannot contain duplicate targets.", nameof(components));
                }

                if (components[i].Metric != metric)
                {
                    throw new ArgumentException("Metric components must be precompiled for the same metric target.", nameof(components));
                }

                componentSnapshot.Add(new WeightedTargetComponentRuntime(metric, components[i].Target, components[i].WeightPpm));
            }

            Metric = metric;
            HalfLifeWeeks = halfLifeWeeks;
            AlphaPpm = alphaPpm;
            CapPerWeekS = capPerWeekS;
            Components = Array.AsReadOnly(componentSnapshot.ToArray());
            BaseCause = AggregationCauseMaterializer.MaterializeAggregationMetric(metric);
        }

        public TargetPath Metric { get; }

        public int HalfLifeWeeks { get; }

        public int AlphaPpm { get; }

        public int CapPerWeekS { get; }

        public IReadOnlyList<WeightedTargetComponentRuntime> Components { get; }

        public CauseRef BaseCause { get; }
    }

    public sealed class WeightedTargetComponentRuntime
    {
        public WeightedTargetComponentRuntime(TargetPath metric, TargetPath target, int weightPpm)
        {
            if (!AggregationCauseMaterializer.IsMetricTarget(metric))
            {
                throw new ArgumentException("Component cause metric must be a valid metrics.* target.", nameof(metric));
            }

            if (metric.SegmentCount != 2)
            {
                throw new ArgumentException("Component cause metric must be metrics.*.", nameof(metric));
            }

            if (!AggregationCauseMaterializer.IsInternalTarget(target))
            {
                throw new ArgumentException("Component target must be a valid internals.* target.", nameof(target));
            }

            if (target.SegmentCount != 3)
            {
                throw new ArgumentException("Component target must be internals.*.*.", nameof(target));
            }

            if (weightPpm == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weightPpm), "Component weight must not be zero.");
            }

            Metric = metric;
            Target = target;
            WeightPpm = weightPpm;
            Cause = AggregationCauseMaterializer.MaterializeAggregationComponent(metric, target);
        }

        public TargetPath Metric { get; }

        public TargetPath Target { get; }

        public int WeightPpm { get; }

        public CauseRef Cause { get; }
    }

    public sealed class DerivedAggregationRuleRuntime
    {
        public DerivedAggregationRuleRuntime(
            TargetPath target,
            TargetOperation operation,
            AggregationExpressionRuntime expression)
        {
            if (!target.IsValid)
            {
                throw new ArgumentException("Rule target must be valid.", nameof(target));
            }

            if (!AggregationCauseMaterializer.IsInternalTarget(target) || target.SegmentCount != 3)
            {
                throw new ArgumentException("Rule target must be internals.*.*.", nameof(target));
            }

            if (expression == null)
            {
                throw new ArgumentNullException(nameof(expression));
            }

            if (!Enum.IsDefined(typeof(TargetOperation), operation) || operation != TargetOperation.Set)
            {
                throw new ArgumentOutOfRangeException(nameof(operation), "Derived aggregation rules only support SET.");
            }

            Target = target;
            Operation = operation;
            Expression = expression;
            Cause = AggregationCauseMaterializer.MaterializeDerived(target);
        }

        public TargetPath Target { get; }

        public TargetOperation Operation { get; }

        public AggregationExpressionRuntime Expression { get; }

        public CauseRef Cause { get; }
    }

    public sealed class AggregationExpressionRuntime
    {
        public AggregationExpressionRuntime(
            AggregationExpressionKindRuntime kind,
            TargetPath? target,
            IReadOnlyList<TargetPath> targets)
        {
            if (!Enum.IsDefined(typeof(AggregationExpressionKindRuntime), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), "Unknown aggregation expression kind.");
            }

            if (kind == AggregationExpressionKindRuntime.Copy && (!target.HasValue || !target.Value.IsValid))
            {
                throw new ArgumentException("COPY expression requires a valid target.", nameof(target));
            }

            if (kind == AggregationExpressionKindRuntime.Copy
                && (!AggregationCauseMaterializer.IsMetricTarget(target.Value) || target.Value.SegmentCount != 2))
            {
                throw new ArgumentException("COPY expression target must be metrics.*.", nameof(target));
            }

            if (kind == AggregationExpressionKindRuntime.Copy && targets != null && targets.Count != 0)
            {
                throw new ArgumentException("COPY expression requires zero plural targets.", nameof(targets));
            }

            if (kind == AggregationExpressionKindRuntime.Avg && target.HasValue)
            {
                throw new ArgumentException("AVG expression must not declare a singular target.", nameof(target));
            }

            if (kind == AggregationExpressionKindRuntime.Avg && (targets == null || targets.Count == 0))
            {
                throw new ArgumentException("AVG expression requires at least one target.", nameof(targets));
            }

            Kind = kind;
            Target = target;

            List<TargetPath> targetSnapshot = new List<TargetPath>();
            if (targets != null)
            {
                HashSet<TargetPath> seenTargets = new HashSet<TargetPath>();
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!targets[i].IsValid)
                    {
                        throw new ArgumentException("Expression targets must be valid concrete target paths.", nameof(targets));
                    }

                    if (!AggregationCauseMaterializer.IsMetricTarget(targets[i]) || targets[i].SegmentCount != 2)
                    {
                        throw new ArgumentException("Expression targets must be metrics.* target paths.", nameof(targets));
                    }

                    if (!seenTargets.Add(targets[i]))
                    {
                        throw new ArgumentException("Expression targets cannot contain duplicates.", nameof(targets));
                    }

                    targetSnapshot.Add(targets[i]);
                }
            }

            Targets = Array.AsReadOnly(targetSnapshot.ToArray());
        }

        public AggregationExpressionKindRuntime Kind { get; }

        public TargetPath? Target { get; }

        public IReadOnlyList<TargetPath> Targets { get; }
    }
}
