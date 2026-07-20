using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Aggregation
{
    public sealed class AggregationEngine
    {
        private static readonly TargetPath LegitimacyMetric = TargetPath.Parse("metrics.legitimacy");
        private static readonly TargetPath PerformanceTarget = TargetPath.Parse("internals.legitimacy.performance");
        private static readonly TargetPath SocialTensionLoadTarget = TargetPath.Parse("internals.legitimacy.social_tension_load");
        private static readonly TargetPath EconomyMetric = TargetPath.Parse("metrics.economy");
        private static readonly TargetPath SecurityMetric = TargetPath.Parse("metrics.security");
        private static readonly TargetPath GovernabilityMetric = TargetPath.Parse("metrics.governability");
        private static readonly TargetPath SocialTensionMetric = TargetPath.Parse("metrics.social_tension");
        private static readonly TargetPath[] PrimaryMetricOrder =
        {
            TargetPath.Parse("metrics.economy"),
            TargetPath.Parse("metrics.security"),
            TargetPath.Parse("metrics.social_tension"),
            TargetPath.Parse("metrics.public_agenda"),
            TargetPath.Parse("metrics.information_quality"),
            TargetPath.Parse("metrics.governability"),
            TargetPath.Parse("metrics.legislative_capacity"),
            TargetPath.Parse("metrics.party_organization"),
            TargetPath.Parse("metrics.internal_cohesion")
        };

        private readonly AggregationRuntimePlan _plan;
        private readonly TargetConfigCatalog _targetConfigs;
        private readonly ReadOnlyCollection<AggregationTargetBinding> _reversionTargets;
        private readonly ReadOnlyCollection<AggregationRuleBinding> _derivedRules;
        private readonly ReadOnlyCollection<AggregationMetricBinding> _primaryMetrics;
        private readonly ReadOnlyCollection<AggregationMetricBinding> _legitimacyMetrics;
        private readonly ReadOnlyDictionary<TargetPath, CauseRef> _reversionCausesByTarget;

        public AggregationEngine(AggregationRuntimePlan plan, TargetConfigCatalog targetConfigs)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _targetConfigs = targetConfigs ?? throw new ArgumentNullException(nameof(targetConfigs));
            ValidateCanonicalPlanShape(plan);
            _reversionTargets = Array.AsReadOnly(BindReversion(plan, targetConfigs).ToArray());
            _reversionCausesByTarget = BuildReversionCauseLookup(_reversionTargets);
            _derivedRules = Array.AsReadOnly(BindDerived(plan, targetConfigs).ToArray());
            _primaryMetrics = Array.AsReadOnly(BindMetrics(plan.PrimaryMetrics, targetConfigs).ToArray());
            _legitimacyMetrics = Array.AsReadOnly(BindMetrics(plan.Legitimacy, targetConfigs).ToArray());
        }

        public IReadOnlyList<AggregationTargetBinding> ReversionTargets => _reversionTargets;

        public IReadOnlyList<AggregationRuleBinding> DerivedRules => _derivedRules;

        public IReadOnlyList<AggregationMetricBinding> PrimaryMetricBindings => _primaryMetrics;

        public IReadOnlyList<AggregationMetricBinding> LegitimacyMetricBindings => _legitimacyMetrics;

        public bool TryGetReversionCause(TargetPath target, out CauseRef cause)
        {
            if (!AggregationCauseMaterializer.IsInternalTarget(target))
            {
                cause = null;
                return false;
            }

            return _reversionCausesByTarget.TryGetValue(target, out cause);
        }

        public CauseRef GetReversionCause(TargetPath target)
        {
            if (!AggregationCauseMaterializer.IsInternalTarget(target))
            {
                throw new ArgumentException("Reversion cause requires a valid internals.*.* target.", nameof(target));
            }

            if (TryGetReversionCause(target, out CauseRef cause))
            {
                return cause;
            }

            throw new KeyNotFoundException("Reversion target is not present in the concrete aggregation binding.");
        }

        public GameState RevertInternals(GameState current)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            List<AggregationPlannedMutation> mutations = new List<AggregationPlannedMutation>();
            try
            {
                for (int i = 0; i < _reversionTargets.Count; i++)
                {
                    AggregationTargetBinding binding = _reversionTargets[i];
                    int beforeS = ReadInternal(current, binding.Target);
                    int afterS = ComputeReversionFinal(beforeS, binding.AlphaPpm, binding.Config);
                    mutations.Add(new AggregationPlannedMutation(binding.Target, beforeS, afterS, binding.Cause));
                }
            }
            catch (OverflowException exception)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.ArithmeticOverflow, "reversion", "Internal reversion arithmetic overflowed.", exception);
            }

            return ApplyPlannedMutations(current, mutations);
        }

        public GameState DeriveInternals(GameState current)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            List<AggregationPlannedMutation> mutations = new List<AggregationPlannedMutation>();
            try
            {
                for (int i = 0; i < _derivedRules.Count; i++)
                {
                    AggregationRuleBinding binding = _derivedRules[i];
                    DerivedAggregationRuleRuntime rule = binding.Rule;
                    int beforeS = ReadInternal(current, rule.Target);
                    int rawValueS = EvaluateExpression(current, rule.Expression);
                    int afterS = binding.Config.Clamp(rawValueS);
                    mutations.Add(new AggregationPlannedMutation(rule.Target, beforeS, afterS, rule.Cause));
                }
            }
            catch (OverflowException exception)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.ArithmeticOverflow, "derived", "Derived internals arithmetic overflowed.", exception);
            }

            return ApplyPlannedMutations(current, mutations);
        }

        public GameState AggregatePrimaryMetrics(GameState current, TickCausalBuffer causalBuffer)
        {
            return AggregateMetricPass(current, causalBuffer, _primaryMetrics);
        }

        public GameState AggregateLegitimacy(GameState current, TickCausalBuffer causalBuffer)
        {
            return AggregateMetricPass(current, causalBuffer, _legitimacyMetrics);
        }

        private GameState AggregateMetricPass(
            GameState current,
            TickCausalBuffer causalBuffer,
            IReadOnlyList<AggregationMetricBinding> metrics)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (causalBuffer == null)
            {
                throw new ArgumentNullException(nameof(causalBuffer));
            }

            List<AggregationPlannedMutation> mutations = new List<AggregationPlannedMutation>();
            List<CausalTargetContribution> contributions = new List<CausalTargetContribution>();
            try
            {
                for (int i = 0; i < metrics.Count; i++)
                {
                    AggregationMetricBinding binding = metrics[i];
                    AggregationMetricRuntime metric = binding.Metric;
                    int currentMetricS = ReadMetric(current, metric.Metric);
                    int[] componentValues = ReadComponentValues(current, metric);
                    MetricComputation final = ComputeMetricFinal(currentMetricS, componentValues, metric, binding.MetricConfig);
                    mutations.Add(new AggregationPlannedMutation(metric.Metric, currentMetricS, final.FinalMetricS, metric.BaseCause));
                    AddMetricContributions(contributions, metric, binding.MetricConfig, currentMetricS, componentValues, final.FinalMetricS);
                }
            }
            catch (OverflowException exception)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.ArithmeticOverflow, "metrics", "Metric aggregation arithmetic overflowed.", exception);
            }

            GameState candidate = ApplyPlannedMutations(current, mutations);
            try
            {
                causalBuffer.RecordContributionsBatch(contributions);
            }
            catch (CausalLedgerException exception)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.LedgerRejected, exception.Target, "Aggregation causal ledger batch was rejected.", exception);
            }

            return candidate;
        }

        private static void ValidateCanonicalPlanShape(AggregationRuntimePlan plan)
        {
            if (plan.MetricsByTarget.Count != InitialTargetRegistry.Metrics.Count)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetMissing, "metrics.*", "Aggregation plan must contain the exact MVP metric set.");
            }

            for (int i = 0; i < InitialTargetRegistry.Metrics.Count; i++)
            {
                if (!plan.MetricsByTarget.ContainsKey(InitialTargetRegistry.Metrics[i]))
                {
                    throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetMissing, InitialTargetRegistry.Metrics[i], "Aggregation plan is missing an MVP metric.");
                }
            }

            for (int i = 0; i < PrimaryMetricOrder.Length; i++)
            {
                if (plan.PrimaryMetrics.Metrics[i].Metric != PrimaryMetricOrder[i])
                {
                    throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetMissing, plan.PrimaryMetrics.Metrics[i].Metric, "Primary metric pass must preserve the canonical primary metric order.");
                }
            }

            if (plan.Legitimacy.Metrics.Count != 1 || plan.Legitimacy.Metrics[0].Metric != LegitimacyMetric)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetMissing, "metrics.legitimacy", "Legitimacy metric pass must contain only metrics.legitimacy.");
            }

            if (plan.InternalReversion.Groups.Count != 10)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.ReversionUncoveredTarget, "internals.*", "Aggregation plan must contain exactly ten reversion groups.");
            }

            if (plan.InternalReversion.SkipTargets.Count != 2
                || plan.InternalReversion.SkipTargets[0] != PerformanceTarget
                || plan.InternalReversion.SkipTargets[1] != SocialTensionLoadTarget)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.ReversionSkipUnmatched, "internals.legitimacy.*", "Aggregation plan must contain the two contractual reversion skips.");
            }

            if (plan.DerivedInternals.Rules.Count != 2
                || plan.DerivedInternals.Rules[0].Target != PerformanceTarget
                || plan.DerivedInternals.Rules[1].Target != SocialTensionLoadTarget)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetMissing, "derived", "Aggregation plan must contain the two contractual derived rules.");
            }
        }

        private static List<AggregationTargetBinding> BindReversion(AggregationRuntimePlan plan, TargetConfigCatalog targetConfigs)
        {
            Dictionary<TargetPath, int> matchCounts = new Dictionary<TargetPath, int>();
            HashSet<TargetPath> skipped = new HashSet<TargetPath>();
            List<AggregationTargetBinding> result = new List<AggregationTargetBinding>();

            for (int i = 0; i < InitialTargetRegistry.Internals.Count; i++)
            {
                matchCounts.Add(InitialTargetRegistry.Internals[i], 0);
            }

            for (int groupIndex = 0; groupIndex < plan.InternalReversion.Groups.Count; groupIndex++)
            {
                AggregationReversionGroupRuntime group = plan.InternalReversion.Groups[groupIndex];
                int matched = 0;
                for (int targetIndex = 0; targetIndex < InitialTargetRegistry.Internals.Count; targetIndex++)
                {
                    TargetPath target = InitialTargetRegistry.Internals[targetIndex];
                    if (!group.Pattern.Matches(target))
                    {
                        continue;
                    }

                    matched++;
                    int nextCount = matchCounts[target] + 1;
                    matchCounts[target] = nextCount;
                    if (nextCount > 1)
                    {
                        throw new AggregationExecutionException(AggregationExecutionErrorCodes.ReversionOverlap, target, "Reversion groups overlap on a concrete internal target.");
                    }
                }

                if (matched == 0)
                {
                    throw new AggregationExecutionException(AggregationExecutionErrorCodes.ReversionPatternNoMatch, group.Pattern.ToString(), "Reversion group pattern matched no concrete internals.");
                }
            }

            for (int i = 0; i < plan.InternalReversion.SkipTargets.Count; i++)
            {
                TargetPath skip = plan.InternalReversion.SkipTargets[i];
                if (!matchCounts.TryGetValue(skip, out int count) || count != 1)
                {
                    throw new AggregationExecutionException(AggregationExecutionErrorCodes.ReversionSkipUnmatched, skip, "Reversion skip target must exist and match exactly one group.");
                }

                skipped.Add(skip);
            }

            for (int i = 0; i < InitialTargetRegistry.Internals.Count; i++)
            {
                TargetPath target = InitialTargetRegistry.Internals[i];
                if (matchCounts[target] != 1)
                {
                    throw new AggregationExecutionException(AggregationExecutionErrorCodes.ReversionUncoveredTarget, target, "Every MVP internal target must be covered by exactly one reversion group before skips.");
                }
            }

            for (int groupIndex = 0; groupIndex < plan.InternalReversion.Groups.Count; groupIndex++)
            {
                AggregationReversionGroupRuntime group = plan.InternalReversion.Groups[groupIndex];
                for (int targetIndex = 0; targetIndex < InitialTargetRegistry.Internals.Count; targetIndex++)
                {
                    TargetPath target = InitialTargetRegistry.Internals[targetIndex];
                    if (!group.Pattern.Matches(target) || skipped.Contains(target))
                    {
                        continue;
                    }

                    result.Add(new AggregationTargetBinding(
                        target,
                        RequireSetConfig(targetConfigs, target),
                        AggregationCauseMaterializer.MaterializeReversion(target),
                        group.AlphaPpm));
                }
            }

            return result;
        }

        private static ReadOnlyDictionary<TargetPath, CauseRef> BuildReversionCauseLookup(IReadOnlyList<AggregationTargetBinding> bindings)
        {
            Dictionary<TargetPath, CauseRef> lookup = new Dictionary<TargetPath, CauseRef>();
            for (int i = 0; i < bindings.Count; i++)
            {
                AggregationTargetBinding binding = bindings[i];
                lookup.Add(binding.Target, binding.Cause);
            }

            return new ReadOnlyDictionary<TargetPath, CauseRef>(lookup);
        }

        private static List<AggregationRuleBinding> BindDerived(AggregationRuntimePlan plan, TargetConfigCatalog targetConfigs)
        {
            List<AggregationRuleBinding> result = new List<AggregationRuleBinding>(plan.DerivedInternals.Rules.Count);
            for (int i = 0; i < plan.DerivedInternals.Rules.Count; i++)
            {
                DerivedAggregationRuleRuntime rule = plan.DerivedInternals.Rules[i];
                TargetConfig config = ResolveConfig(targetConfigs, rule.Target);
                RequireSetAllowed(config, rule.Target);
                result.Add(new AggregationRuleBinding(rule, config));
            }

            return result;
        }

        private static List<AggregationMetricBinding> BindMetrics(AggregationMetricsPassRuntime pass, TargetConfigCatalog targetConfigs)
        {
            List<AggregationMetricBinding> result = new List<AggregationMetricBinding>(pass.Metrics.Count);
            HashSet<TargetPath> outputs = new HashSet<TargetPath>();
            for (int i = 0; i < pass.Metrics.Count; i++)
            {
                AggregationMetricRuntime metric = pass.Metrics[i];
                if (!outputs.Add(metric.Metric))
                {
                    throw new AggregationExecutionException(AggregationExecutionErrorCodes.DuplicateOutputTarget, metric.Metric, "Metric pass contains duplicate output targets.");
                }

                TargetConfig metricConfig = ResolveConfig(targetConfigs, metric.Metric);
                RequireSetAllowed(metricConfig, metric.Metric);
                TargetConfig[] componentConfigs = new TargetConfig[metric.Components.Count];
                for (int j = 0; j < metric.Components.Count; j++)
                {
                    componentConfigs[j] = ResolveConfig(targetConfigs, metric.Components[j].Target);
                }

                result.Add(new AggregationMetricBinding(metric, metricConfig, componentConfigs));
            }

            return result;
        }

        private static TargetConfig ResolveConfig(TargetConfigCatalog targetConfigs, TargetPath target)
        {
            if (!targetConfigs.TryResolve(target, out TargetConfig config))
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetConfigMissing, target, "Aggregation target did not resolve to TargetConfig.");
            }

            return config;
        }

        private static TargetConfig RequireSetConfig(TargetConfigCatalog targetConfigs, TargetPath target)
        {
            TargetConfig config = ResolveConfig(targetConfigs, target);
            RequireSetAllowed(config, target);
            return config;
        }

        private static void RequireSetAllowed(TargetConfig config, TargetPath target)
        {
            if (!config.Allows(TargetOperation.Set))
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetConfigMissing, target, "Aggregation output target must allow SET.");
            }
        }

        private static int ComputeReversionFinal(int currentS, int alphaPpm, TargetConfig config)
        {
            long distanceS = checked((long)AggregationRuntimePlan.RequiredMidS - currentS);
            long numerator = checked(distanceS * alphaPpm);
            long deltaS = FixedMath.RoundDivide(numerator, AggregationRuntimePlan.PpmDenominator);
            long preFinalS = checked((long)currentS + deltaS);
            return ClampToInt(preFinalS, config);
        }

        private static int EvaluateExpression(GameState current, AggregationExpressionRuntime expression)
        {
            if (expression.Kind == AggregationExpressionKindRuntime.Copy)
            {
                return ReadMetric(current, expression.Target.Value);
            }

            long sum = 0;
            for (int i = 0; i < expression.Targets.Count; i++)
            {
                sum = checked(sum + ReadMetric(current, expression.Targets[i]));
            }

            return checked((int)FixedMath.RoundDivide(sum, expression.Targets.Count));
        }

        private static int[] ReadComponentValues(GameState current, AggregationMetricRuntime metric)
        {
            int[] values = new int[metric.Components.Count];
            for (int i = 0; i < metric.Components.Count; i++)
            {
                values[i] = ReadInternal(current, metric.Components[i].Target);
            }

            return values;
        }

        private static MetricComputation ComputeMetricFinal(
            int currentMetricS,
            IReadOnlyList<int> componentValues,
            AggregationMetricRuntime metric,
            TargetConfig metricConfig)
        {
            long numerator = 0;
            for (int i = 0; i < metric.Components.Count; i++)
            {
                long offset = checked((long)componentValues[i] - AggregationRuntimePlan.RequiredMidS);
                numerator = checked(numerator + checked((long)metric.Components[i].WeightPpm * offset));
            }

            long weightedOffsetS = FixedMath.RoundDivide(numerator, AggregationRuntimePlan.PpmDenominator);
            long targetUnclampedS = checked((long)AggregationRuntimePlan.RequiredMidS + weightedOffsetS);
            int targetS = ClampToInt(targetUnclampedS, metricConfig);
            long distanceToTargetS = checked((long)targetS - currentMetricS);
            long elasticNumerator = checked(distanceToTargetS * metric.AlphaPpm);
            long elasticDeltaS = FixedMath.RoundDivide(elasticNumerator, AggregationRuntimePlan.PpmDenominator);
            int cappedDeltaS = ClampLongToInt(elasticDeltaS, -metric.CapPerWeekS, metric.CapPerWeekS);
            long preFinalS = checked((long)currentMetricS + cappedDeltaS);
            int finalMetricS = ClampToInt(preFinalS, metricConfig);
            return new MetricComputation(finalMetricS);
        }

        private static void AddMetricContributions(
            List<CausalTargetContribution> contributions,
            AggregationMetricRuntime metric,
            TargetConfig metricConfig,
            int currentMetricS,
            IReadOnlyList<int> componentValues,
            int finalMetricS)
        {
            int[] vector = new int[componentValues.Count];
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = AggregationRuntimePlan.RequiredMidS;
            }

            int previousF = ComputeMetricFinal(currentMetricS, vector, metric, metricConfig).FinalMetricS;
            long baseDeltaS = checked((long)previousF - currentMetricS);
            if (baseDeltaS != 0)
            {
                contributions.Add(new CausalTargetContribution(metric.Metric, metric.BaseCause, baseDeltaS));
            }

            long causalTotalS = baseDeltaS;
            for (int i = 0; i < metric.Components.Count; i++)
            {
                vector[i] = componentValues[i];
                int nextF = ComputeMetricFinal(currentMetricS, vector, metric, metricConfig).FinalMetricS;
                long deltaS = checked((long)nextF - previousF);
                causalTotalS = checked(causalTotalS + deltaS);
                if (deltaS != 0)
                {
                    contributions.Add(new CausalTargetContribution(metric.Metric, metric.Components[i].Cause, deltaS));
                }

                previousF = nextF;
            }

            long observedTotalS = checked((long)finalMetricS - currentMetricS);
            if (causalTotalS != observedTotalS)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.CausalAccountingMismatch, metric.Metric, "Aggregation marginal attribution did not telescope to final metric delta.");
            }
        }

        private GameState ApplyPlannedMutations(GameState current, IReadOnlyList<AggregationPlannedMutation> mutations)
        {
            GameState candidate = current;
            GameStateMutator mutator = new GameStateMutator();
            for (int i = 0; i < mutations.Count; i++)
            {
                AggregationPlannedMutation mutation = mutations[i];
                if (mutation.DeltaS == 0)
                {
                    continue;
                }

                StateMutationResult result = mutator.Apply(
                    candidate,
                    new TargetMutation(mutation.Target, TargetOperation.Set, mutation.AfterS),
                    _targetConfigs);
                if (!result.Success || result.State == null)
                {
                    string detail = result.Diagnostics.Count == 0 ? mutation.Target.ToString() : result.Diagnostics[0].Code + ":" + result.Diagnostics[0].Target;
                    throw new AggregationExecutionException(AggregationExecutionErrorCodes.MutationFailed, detail, "Aggregation state mutation failed closed.");
                }

                candidate = result.State;
            }

            return candidate;
        }

        private static int ReadMetric(GameState current, TargetPath target)
        {
            if (target.Namespace == "metrics" && target.SegmentCount == 2 && current.MetricsById.TryGetValue(target[1], out MetricState metric))
            {
                return metric.ValueS;
            }

            throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetMissing, target, "Aggregation metric target is missing from GameState.");
        }

        private static int ReadInternal(GameState current, TargetPath target)
        {
            if (target.Namespace == "internals"
                && target.SegmentCount == 3
                && current.InternalsByDomain.TryGetValue(target[1], out InternalDomainState domain)
                && domain.ComponentsById.TryGetValue(target[2], out InternalValueState value))
            {
                return value.ValueS;
            }

            throw new AggregationExecutionException(AggregationExecutionErrorCodes.TargetMissing, target, "Aggregation internal target is missing from GameState.");
        }

        private static int ClampToInt(long value, TargetConfig config)
        {
            if (value < config.MinS)
            {
                return config.MinS;
            }

            if (value > config.MaxS)
            {
                return config.MaxS;
            }

            try
            {
                return checked((int)value);
            }
            catch (OverflowException exception)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.OutOfRangeConversion, "aggregation", "Aggregation result could not be represented as int.", exception);
            }
        }

        private static int ClampLongToInt(long value, int minInclusive, int maxInclusive)
        {
            if (value < minInclusive)
            {
                return minInclusive;
            }

            if (value > maxInclusive)
            {
                return maxInclusive;
            }

            try
            {
                return checked((int)value);
            }
            catch (OverflowException exception)
            {
                throw new AggregationExecutionException(AggregationExecutionErrorCodes.OutOfRangeConversion, "aggregation", "Aggregation delta could not be represented as int.", exception);
            }
        }

        private sealed class MetricComputation
        {
            public MetricComputation(int finalMetricS)
            {
                FinalMetricS = finalMetricS;
            }

            public int FinalMetricS { get; }
        }
    }
}
