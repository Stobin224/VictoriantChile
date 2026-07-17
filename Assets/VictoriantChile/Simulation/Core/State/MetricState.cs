using System;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class MetricState
    {
        public MetricState(string metricId, int valueS)
        {
            if (string.IsNullOrEmpty(metricId))
            {
                throw new ArgumentException("Metric ID cannot be null or empty.", nameof(metricId));
            }

            MetricId = metricId;
            ValueS = valueS;
        }

        public string MetricId { get; }

        public int ValueS { get; }
    }
}
