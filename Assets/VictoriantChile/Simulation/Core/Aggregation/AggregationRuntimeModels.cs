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

        public static CauseRef Materialize(AggregationCauseBase causeBase, TargetPath target)
        {
            if (!target.IsValid)
            {
                throw new ArgumentException("Target must be valid.", nameof(target));
            }

            return new CauseRef(CauseCategory.System, BaseId(causeBase) + "." + target.ToString());
        }

        public static CauseRef MaterializeAggregationComponent(TargetPath metric, TargetPath component)
        {
            if (!metric.IsValid)
            {
                throw new ArgumentException("Metric target must be valid.", nameof(metric));
            }

            if (!component.IsValid)
            {
                throw new ArgumentException("Component target must be valid.", nameof(component));
            }

            return new CauseRef(CauseCategory.System, AggregationId + "." + metric.ToString() + "." + component.ToString());
        }

        public static CauseRef MaterializeReversion(TargetPath target)
        {
            return Materialize(AggregationCauseBase.Reversion, target);
        }

        public static CauseRef MaterializeDerived(TargetPath target)
        {
            return Materialize(AggregationCauseBase.Derived, target);
        }

        public static CauseRef MaterializeAggregationMetric(TargetPath metric)
        {
            return Materialize(AggregationCauseBase.Aggregation, metric);
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

        public CauseRef GetReversionCause(TargetPath target)
        {
            return InternalReversion.MaterializeCause(target);
        }

        public CauseRef GetDerivedCause(TargetPath target)
        {
            for (int i = 0; i < DerivedInternals.Rules.Count; i++)
            {
                DerivedAggregationRuleRuntime rule = DerivedInternals.Rules[i];
                if (rule.Target == target)
                {
                    return rule.Cause;
                }
            }

            return AggregationCauseMaterializer.Materialize(AggregationCauseBase.Derived, target);
        }

        public CauseRef GetMetricCause(TargetPath metric)
        {
            if (TryGetMetric(metric, out AggregationMetricRuntime runtime))
            {
                return runtime.BaseCause;
            }

            return AggregationCauseMaterializer.Materialize(AggregationCauseBase.Aggregation, metric);
        }

        public CauseRef GetComponentCause(TargetPath metric, TargetPath component)
        {
            if (TryGetMetric(metric, out AggregationMetricRuntime runtime))
            {
                for (int i = 0; i < runtime.Components.Count; i++)
                {
                    WeightedTargetComponentRuntime candidate = runtime.Components[i];
                    if (candidate.Target == component)
                    {
                        return candidate.Cause;
                    }
                }
            }

            return AggregationCauseMaterializer.MaterializeAggregationComponent(metric, component);
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

                skipSnapshot.Add(skipTargets[i]);
            }

            Groups = Array.AsReadOnly(groupSnapshot.ToArray());
            SkipTargets = Array.AsReadOnly(skipSnapshot.ToArray());
            CauseBase = AggregationCauseBase.Reversion;
        }

        public IReadOnlyList<AggregationReversionGroupRuntime> Groups { get; }

        public IReadOnlyList<TargetPath> SkipTargets { get; }

        public AggregationCauseBase CauseBase { get; }

        public CauseRef MaterializeCause(TargetPath target)
        {
            return AggregationCauseMaterializer.Materialize(CauseBase, target);
        }
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

                componentSnapshot.Add(components[i]);
            }

            Metric = metric;
            HalfLifeWeeks = halfLifeWeeks;
            AlphaPpm = alphaPpm;
            CapPerWeekS = capPerWeekS;
            Components = Array.AsReadOnly(componentSnapshot.ToArray());
            BaseCause = AggregationCauseMaterializer.Materialize(AggregationCauseBase.Aggregation, metric);
            for (int i = 0; i < componentSnapshot.Count; i++)
            {
                componentSnapshot[i].AttachCause(metric);
            }
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
        public WeightedTargetComponentRuntime(TargetPath target, int weightPpm)
        {
            if (!target.IsValid)
            {
                throw new ArgumentException("Component target must be valid.", nameof(target));
            }

            if (weightPpm == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weightPpm), "Component weight must not be zero.");
            }

            Target = target;
            WeightPpm = weightPpm;
        }

        public TargetPath Target { get; }

        public int WeightPpm { get; }

        public CauseRef Cause { get; private set; }

        internal void AttachCause(TargetPath metric)
        {
            if (Cause != null)
            {
                throw new InvalidOperationException("Component cause has already been materialized.");
            }

            Cause = AggregationCauseMaterializer.MaterializeAggregationComponent(metric, Target);
        }
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
            Cause = AggregationCauseMaterializer.Materialize(AggregationCauseBase.Derived, target);
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
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!targets[i].IsValid)
                    {
                        throw new ArgumentException("Expression targets must be valid concrete target paths.", nameof(targets));
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
