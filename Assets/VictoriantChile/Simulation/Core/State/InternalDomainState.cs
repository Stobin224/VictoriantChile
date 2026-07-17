using System;
using System.Collections.Generic;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class InternalValueState
    {
        public InternalValueState(string componentId, int valueS)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("Component ID cannot be null or empty.", nameof(componentId));
            }

            ComponentId = componentId;
            ValueS = valueS;
        }

        public string ComponentId { get; }

        public int ValueS { get; }
    }

    public sealed class InternalDomainState
    {
        public InternalDomainState(string domain, IEnumerable<InternalValueState> components)
        {
            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException("Domain cannot be null or empty.", nameof(domain));
            }

            Domain = domain;
            Components = StateCollection.SnapshotSorted(components, item => item.ComponentId, nameof(components));
            ComponentsById = StateCollection.MapById(Components, item => item.ComponentId);
        }

        public string Domain { get; }

        public IReadOnlyList<InternalValueState> Components { get; }

        public IReadOnlyDictionary<string, InternalValueState> ComponentsById { get; }
    }
}
