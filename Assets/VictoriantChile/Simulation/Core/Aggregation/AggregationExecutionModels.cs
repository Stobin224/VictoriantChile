using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Aggregation
{
    public static class AggregationExecutionErrorCodes
    {
        public const string TargetMissing = "aggregation.target_missing";
        public const string TargetConfigMissing = "aggregation.target_config_missing";
        public const string ReversionPatternNoMatch = "aggregation.reversion_pattern_no_match";
        public const string ReversionOverlap = "aggregation.reversion_overlap";
        public const string ReversionUncoveredTarget = "aggregation.reversion_uncovered_target";
        public const string ReversionSkipUnmatched = "aggregation.reversion_skip_unmatched";
        public const string DuplicateOutputTarget = "aggregation.duplicate_output_target";
        public const string ArithmeticOverflow = "aggregation.arithmetic_overflow";
        public const string OutOfRangeConversion = "aggregation.out_of_range_conversion";
        public const string MutationFailed = "aggregation.mutation_failed";
        public const string CausalAccountingMismatch = "aggregation.causal_accounting_mismatch";
        public const string LedgerRejected = "aggregation.ledger_rejected";
    }

    public sealed class AggregationExecutionException : InvalidOperationException
    {
        public AggregationExecutionException(string code, TargetPath target, string message, Exception innerException = null)
            : this(code, target.IsValid ? target.ToString() : "<invalid>", message, innerException)
        {
        }

        public AggregationExecutionException(string code, string target, string message, Exception innerException = null)
            : base(message, innerException)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("Aggregation execution code cannot be null or empty.", nameof(code));
            }

            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Aggregation execution message cannot be null or empty.", nameof(message));
            }

            Code = code;
            Target = target ?? string.Empty;
        }

        public string Code { get; }

        public string Target { get; }
    }

    internal sealed class AggregationPlannedMutation
    {
        public AggregationPlannedMutation(TargetPath target, int beforeS, int afterS, CauseRef cause)
        {
            if (!target.IsValid)
            {
                throw new ArgumentException("Planned mutation target must be valid.", nameof(target));
            }

            Target = target;
            BeforeS = beforeS;
            AfterS = afterS;
            DeltaS = checked(afterS - beforeS);
            Cause = cause ?? throw new ArgumentNullException(nameof(cause));
        }

        public TargetPath Target { get; }

        public int BeforeS { get; }

        public int AfterS { get; }

        public int DeltaS { get; }

        public CauseRef Cause { get; }
    }

    public sealed class AggregationTargetBinding
    {
        public AggregationTargetBinding(TargetPath target, TargetConfig config, CauseRef cause, int alphaPpm)
        {
            Target = target;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Cause = cause ?? throw new ArgumentNullException(nameof(cause));
            AlphaPpm = alphaPpm;
        }

        public TargetPath Target { get; }

        public TargetConfig Config { get; }

        public CauseRef Cause { get; }

        public int AlphaPpm { get; }
    }

    public sealed class AggregationRuleBinding
    {
        public AggregationRuleBinding(DerivedAggregationRuleRuntime rule, TargetConfig config)
        {
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public DerivedAggregationRuleRuntime Rule { get; }

        public TargetConfig Config { get; }
    }

    public sealed class AggregationMetricBinding
    {
        public AggregationMetricBinding(
            AggregationMetricRuntime metric,
            TargetConfig metricConfig,
            IEnumerable<TargetConfig> componentConfigs)
        {
            Metric = metric ?? throw new ArgumentNullException(nameof(metric));
            MetricConfig = metricConfig ?? throw new ArgumentNullException(nameof(metricConfig));
            ComponentConfigs = Array.AsReadOnly(new List<TargetConfig>(componentConfigs ?? throw new ArgumentNullException(nameof(componentConfigs))).ToArray());
            if (ComponentConfigs.Count != metric.Components.Count)
            {
                throw new ArgumentException("Metric binding component config count must match component count.", nameof(componentConfigs));
            }
        }

        public AggregationMetricRuntime Metric { get; }

        public TargetConfig MetricConfig { get; }

        public IReadOnlyList<TargetConfig> ComponentConfigs { get; }
    }
}
