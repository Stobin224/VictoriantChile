using System;
using System.Collections.Generic;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class GameState
    {
        public const int CurrentStateSchemaVersion = 1;

        public GameState(
            int rngSeed,
            GameStateContentMetadata contentMetadata,
            IEnumerable<MetricState> metrics,
            IEnumerable<InternalDomainState> internals,
            IEnumerable<RegionState> regions,
            IEnumerable<InterestGroupState> interestGroups,
            IEnumerable<MovementState> movements)
        {
            ContentMetadata = contentMetadata ?? throw new ArgumentNullException(nameof(contentMetadata));
            RngSeed = rngSeed;
            Metrics = StateCollection.SnapshotSorted(metrics, item => item.MetricId, nameof(metrics));
            MetricsById = StateCollection.MapById(Metrics, item => item.MetricId);
            Internals = StateCollection.SnapshotSorted(internals, item => item.Domain, nameof(internals));
            InternalsByDomain = StateCollection.MapById(Internals, item => item.Domain);
            Regions = StateCollection.SnapshotSorted(regions, item => item.RegionId, nameof(regions));
            RegionsById = StateCollection.MapById(Regions, item => item.RegionId);
            InterestGroups = StateCollection.SnapshotSorted(interestGroups, item => item.InterestGroupId, nameof(interestGroups));
            InterestGroupsById = StateCollection.MapById(InterestGroups, item => item.InterestGroupId);
            Movements = StateCollection.SnapshotSorted(movements, item => item.MovementId, nameof(movements));
            MovementsById = StateCollection.MapById(Movements, item => item.MovementId);
        }

        public int StateSchemaVersion => CurrentStateSchemaVersion;

        public int Tick => 0;

        public int RngSeed { get; }

        public GameStateContentMetadata ContentMetadata { get; }

        public IReadOnlyList<MetricState> Metrics { get; }

        public IReadOnlyDictionary<string, MetricState> MetricsById { get; }

        public IReadOnlyList<InternalDomainState> Internals { get; }

        public IReadOnlyDictionary<string, InternalDomainState> InternalsByDomain { get; }

        public IReadOnlyList<RegionState> Regions { get; }

        public IReadOnlyDictionary<string, RegionState> RegionsById { get; }

        public IReadOnlyList<InterestGroupState> InterestGroups { get; }

        public IReadOnlyDictionary<string, InterestGroupState> InterestGroupsById { get; }

        public IReadOnlyList<MovementState> Movements { get; }

        public IReadOnlyDictionary<string, MovementState> MovementsById { get; }
    }
}
