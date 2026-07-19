using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Simulation.Core.Effects;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.Scheduling;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class GameState
    {
        public const int CurrentStateSchemaVersion = 3;

        public GameState(
            int rngSeed,
            GameStateContentMetadata contentMetadata,
            IEnumerable<MetricState> metrics,
            IEnumerable<InternalDomainState> internals,
            IEnumerable<RegionState> regions,
            IEnumerable<InterestGroupState> interestGroups,
            IEnumerable<MovementState> movements)
            : this(rngSeed, contentMetadata, metrics, internals, regions, interestGroups, movements, null, 0, null, null, null)
        {
        }

        public GameState(
            int rngSeed,
            GameStateContentMetadata contentMetadata,
            IEnumerable<MetricState> metrics,
            IEnumerable<InternalDomainState> internals,
            IEnumerable<RegionState> regions,
            IEnumerable<InterestGroupState> interestGroups,
            IEnumerable<MovementState> movements,
            IEnumerable<EffectInstance> activeEffects,
            int tick = 0,
            Pcg32State rngState = null,
            IEnumerable<ScheduledAction> scheduledActions = null,
            BlockingDecision blockingDecision = null)
        {
            ContentMetadata = contentMetadata ?? throw new ArgumentNullException(nameof(contentMetadata));
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Tick cannot be negative.");
            }

            Tick = tick;
            RngSeed = rngSeed;
            RngState = rngState ?? Pcg32State.CreateFromSeed(rngSeed);
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
            ActiveEffects = StateCollection.SnapshotSorted(activeEffects ?? Array.Empty<EffectInstance>(), item => item.Id, nameof(activeEffects));
            ActiveEffectsById = StateCollection.MapById(ActiveEffects, item => item.Id);
            ScheduledActions = SnapshotScheduledActions(scheduledActions ?? Array.Empty<ScheduledAction>());
            ScheduledActionsById = MapScheduledActionsById(ScheduledActions);
            BlockingDecision = blockingDecision;
        }

        public int StateSchemaVersion => CurrentStateSchemaVersion;

        public int Tick { get; }

        public int RngSeed { get; }

        public Pcg32State RngState { get; }

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

        public IReadOnlyList<EffectInstance> ActiveEffects { get; }

        public IReadOnlyDictionary<string, EffectInstance> ActiveEffectsById { get; }

        public IReadOnlyList<ScheduledAction> ScheduledActions { get; }

        public IReadOnlyDictionary<string, ScheduledAction> ScheduledActionsById { get; }

        public BlockingDecision BlockingDecision { get; }

        private static IReadOnlyList<ScheduledAction> SnapshotScheduledActions(IEnumerable<ScheduledAction> values)
        {
            List<ScheduledAction> snapshot = new List<ScheduledAction>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScheduledAction value in values)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(values), "Scheduled actions cannot contain null values.");
                }

                if (!seen.Add(value.Id))
                {
                    throw new ArgumentException("Duplicate scheduled action ID: " + value.Id, nameof(values));
                }

                snapshot.Add(value);
            }

            snapshot.Sort(ScheduledAction.CompareQueueOrder);
            return Array.AsReadOnly(snapshot.ToArray());
        }

        private static IReadOnlyDictionary<string, ScheduledAction> MapScheduledActionsById(IReadOnlyList<ScheduledAction> values)
        {
            Dictionary<string, ScheduledAction> result = new Dictionary<string, ScheduledAction>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                result.Add(values[i].Id, values[i]);
            }

            return new ReadOnlyDictionary<string, ScheduledAction>(result);
        }
    }
}
