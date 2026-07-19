using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Effects;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Scheduling
{
    public sealed class SchedulerEngine
    {
        private readonly EffectEngine _effectEngine;
        private readonly EffectRuntimeCatalog _effectRuntimeCatalog;
        private readonly TargetConfigCatalog _targetConfigs;
        private readonly IReadOnlyDictionary<string, IScheduledActionHandler> _handlers;
        private readonly IReadOnlyList<string> _orderedRegionIds;
        private readonly IReadOnlyList<string> _orderedInterestGroupIds;
        private readonly IReadOnlyList<string> _orderedMovementIds;

        public SchedulerEngine(
            EffectEngine effectEngine,
            EffectRuntimeCatalog effectRuntimeCatalog,
            TargetConfigCatalog targetConfigs,
            IEnumerable<string> orderedRegionIds,
            IEnumerable<string> orderedInterestGroupIds,
            IEnumerable<string> orderedMovementIds,
            IEnumerable<KeyValuePair<string, IScheduledActionHandler>> handlers)
        {
            _effectEngine = effectEngine ?? throw new ArgumentNullException(nameof(effectEngine));
            _effectRuntimeCatalog = effectRuntimeCatalog ?? throw new ArgumentNullException(nameof(effectRuntimeCatalog));
            _targetConfigs = targetConfigs ?? throw new ArgumentNullException(nameof(targetConfigs));
            _orderedRegionIds = SnapshotIds(orderedRegionIds, nameof(orderedRegionIds));
            _orderedInterestGroupIds = SnapshotIds(orderedInterestGroupIds, nameof(orderedInterestGroupIds));
            _orderedMovementIds = SnapshotIds(orderedMovementIds, nameof(orderedMovementIds));
            _handlers = SnapshotHandlers(handlers);
        }

        public GameState ScheduleAction(GameState current, ScheduledAction action)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (current.BlockingDecision != null)
            {
                throw new SchedulerException(SchedulerErrorCodes.AlreadyBlocked, "Blocked states cannot accept new scheduled actions.", current.BlockingDecision.Id);
            }

            ValidateScheduledActionForEnqueue(action, current.Tick, current.ScheduledActionsById);
            List<ScheduledAction> queue = new List<ScheduledAction>(current.ScheduledActions);
            queue.Add(action);
            queue.Sort(ScheduledAction.CompareQueueOrder);
            return Rebuild(current, scheduledActions: queue);
        }

        public GameState CancelScheduledAction(GameState current, string actionId)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (string.IsNullOrEmpty(actionId))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidAction, "Scheduled action IDs cannot be null or empty.", nameof(actionId));
            }

            if (!current.ScheduledActionsById.ContainsKey(actionId))
            {
                return current;
            }

            List<ScheduledAction> queue = new List<ScheduledAction>(current.ScheduledActions.Count - 1);
            for (int i = 0; i < current.ScheduledActions.Count; i++)
            {
                if (!string.Equals(current.ScheduledActions[i].Id, actionId, StringComparison.Ordinal))
                {
                    queue.Add(current.ScheduledActions[i]);
                }
            }

            return Rebuild(current, scheduledActions: queue);
        }

        public GameState ResolveBlockingDecision(GameState current, string blockingDecisionId)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (current.BlockingDecision == null)
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidBlockingDecision, "The current state does not contain a blocking decision.");
            }

            if (!string.Equals(current.BlockingDecision.Id, blockingDecisionId, StringComparison.Ordinal))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidBlockingDecision, "Blocking decision resolution requires the exact persisted decision ID.", blockingDecisionId);
            }

            return Rebuild(current, blockingDecision: null, preserveBlockingDecision: false);
        }

        public TickAdvanceResult AdvanceOneTick(GameState current, Action<SimulationTickPhase> observePhase = null)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (current.BlockingDecision != null)
            {
                return new TickAdvanceResult(current, null, Array.Empty<string>(), current.BlockingDecision, Array.Empty<SimulationTickPhase>());
            }

            List<SimulationTickPhase> phases = new List<SimulationTickPhase>();
            GameState working = IncrementTick(current, phases, observePhase);
            VisibleTargetCatalog visibleTargets = VisibleTargetCatalog.CreateForMvp(working, _orderedRegionIds, _orderedInterestGroupIds, _orderedMovementIds);
            TickCausalBuffer causalBuffer = CreateAuditBuffer(working, visibleTargets);
            List<PendingBlockingDecision> blockers = new List<PendingBlockingDecision>();

            working = ExecutePhase(SimulationTickPhase.ExpireEffects, working, phases, observePhase, state => _effectEngine.RemoveExpiredEffects(state, state.Tick));
            working = ExecuteScheduledActionsPhase(working, phases, observePhase, blockers, causalBuffer, visibleTargets);
            working = ExecutePhase(SimulationTickPhase.ApplyStartInstantModifiers, working, phases, observePhase, state => _effectEngine.ApplyStartInstantModifiers(state, _effectRuntimeCatalog, _targetConfigs, state.Tick, causalBuffer));
            working = ExecutePhase(SimulationTickPhase.ApplyPerTickModifiers, working, phases, observePhase, state => _effectEngine.ApplyPerTickModifiers(state, _effectRuntimeCatalog, _targetConfigs, state.Tick, causalBuffer));

            working = ExecuteNoOpPhase(SimulationTickPhase.RevertInternals, working, phases, observePhase);
            working = ExecuteNoOpPhase(SimulationTickPhase.DeriveInternals, working, phases, observePhase);
            working = ExecuteNoOpPhase(SimulationTickPhase.AggregateNationalMetrics, working, phases, observePhase);
            working = ExecuteNoOpPhase(SimulationTickPhase.DriftNationalToRegions, working, phases, observePhase);
            working = ExecuteNoOpPhase(SimulationTickPhase.PullRegionsToInternals, working, phases, observePhase);
            working = ExecuteNoOpPhase(SimulationTickPhase.UpdateMovements, working, phases, observePhase);
            working = ExecuteNoOpPhase(SimulationTickPhase.AdvanceReforms, working, phases, observePhase);
            working = ExecuteNoOpPhase(SimulationTickPhase.ResolveEventsAndCrises, working, phases, observePhase);
            working = ExecuteNoOpPhase(SimulationTickPhase.ApplyFinalClampsAndNormalizations, working, phases, observePhase);

            Observe(SimulationTickPhase.CloseCausalReport, phases, observePhase);
            TickCausalSnapshot snapshot = CloseAndSeal(working, causalBuffer, visibleTargets);

            Observe(SimulationTickPhase.DetectAndPublishBlockingDecision, phases, observePhase);
            BlockingDecision published = ChooseBlockingDecision(blockers);
            if (published != null)
            {
                working = Rebuild(working, blockingDecision: published);
            }

            return new TickAdvanceResult(working, snapshot, Array.Empty<string>(), published, phases);
        }

        public AdvanceWeeksResult AdvanceWeeks(GameState current, int requestedWeeks, Action<SimulationTickPhase> observePhase = null)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (requestedWeeks != 1 && requestedWeeks != 4 && requestedWeeks != 12)
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidAdvance, "AdvanceWeeks only supports exactly 1, 4, or 12 weeks.", requestedWeeks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (current.BlockingDecision != null)
            {
                return new AdvanceWeeksResult(requestedWeeks, current, Array.Empty<TickCausalSnapshot>(), Array.Empty<string>(), current.BlockingDecision);
            }

            List<TickCausalSnapshot> snapshots = new List<TickCausalSnapshot>();
            List<string> hashes = new List<string>();
            GameState working = current;
            for (int i = 0; i < requestedWeeks; i++)
            {
                TickAdvanceResult tick = AdvanceOneTick(working, observePhase);
                if (tick.TickSnapshot == null)
                {
                    return new AdvanceWeeksResult(requestedWeeks, tick.FinalState, snapshots, hashes, tick.BlockingDecision);
                }

                working = tick.FinalState;
                snapshots.Add(tick.TickSnapshot);
                if (tick.StateHashes.Count > 0)
                {
                    hashes.Add(tick.StateHashes[tick.StateHashes.Count - 1]);
                }

                if (tick.BlockingDecision != null)
                {
                    break;
                }
            }

            return new AdvanceWeeksResult(requestedWeeks, working, snapshots, hashes, working.BlockingDecision);
        }

        private GameState ExecuteScheduledActionsPhase(
            GameState current,
            IList<SimulationTickPhase> phases,
            Action<SimulationTickPhase> observePhase,
            IList<PendingBlockingDecision> blockers,
            TickCausalBuffer causalBuffer,
            VisibleTargetCatalog visibleTargets)
        {
            Observe(SimulationTickPhase.ExecuteScheduledActions, phases, observePhase);
            ValidateNoLateActions(current.ScheduledActions, current.Tick);

            List<ScheduledAction> queue = new List<ScheduledAction>(current.ScheduledActions);
            List<ScheduledAction> due = SelectDueActions(queue, current.Tick);
            GameState working = current;
            Pcg32State workingRng = current.RngState;

            for (int i = 0; i < due.Count; i++)
            {
                ScheduledAction action = due[i];
                if (!ContainsAction(queue, action.Id))
                {
                    continue;
                }

                if (!_handlers.TryGetValue(action.Type, out IScheduledActionHandler handler))
                {
                    throw new SchedulerException(SchedulerErrorCodes.UnknownActionHandler, "No scheduled action handler is registered for this action type.", action.Type);
                }

                ScheduledActionExecutionContext context = new ScheduledActionExecutionContext(working, current.Tick, workingRng);
                ScheduledActionExecutionResult result = handler.Execute(context, action) ?? ScheduledActionExecutionResult.Empty;
                workingRng = context.RngState;

                GameState afterMutations = ApplyScheduledActionMutations(working, result.Mutations, visibleTargets);
                GameState afterEffects = ApplyEffectRegistrations(afterMutations, result.EffectRegistrations);
                List<ScheduledAction> nextQueue = ApplyQueueChanges(queue, current.Tick, action, result.CancelActionIds, result.ScheduledActions);
                if (result.BlockingDecision != null)
                {
                    blockers.Add(new PendingBlockingDecision(result.BlockingDecision, action.Priority, action.Id));
                }

                working = Rebuild(afterEffects, rngState: workingRng, scheduledActions: nextQueue);
                queue = nextQueue;
            }

            return working;
        }

        private GameState ApplyScheduledActionMutations(GameState current, IReadOnlyList<ScheduledActionMutation> mutations, VisibleTargetCatalog visibleTargets)
        {
            if (mutations.Count == 0)
            {
                return current;
            }

            GameState working = current;
            GameStateMutator mutator = new GameStateMutator();
            for (int i = 0; i < mutations.Count; i++)
            {
                ScheduledActionMutation item = mutations[i];
                if (visibleTargets.IsVisible(item.Mutation.Target))
                {
                    throw new SchedulerException(
                        SchedulerErrorCodes.VisibleDirectMutationUnsupported,
                        "PR 13 only permits scheduled direct mutations against hidden targets; visible deltas must flow through the effect engine.",
                        item.Mutation.Target.ToString());
                }

                StateMutationResult result = mutator.Apply(working, item.Mutation, _targetConfigs);
                if (!result.Success || result.State == null)
                {
                    string detail = result.Diagnostics.Count == 0 ? item.Mutation.Target.ToString() : result.Diagnostics[0].Code + ":" + result.Diagnostics[0].Target;
                    throw new SchedulerException(
                        SchedulerErrorCodes.HiddenMutationFailed,
                        "A scheduled hidden-target mutation failed closed.",
                        detail);
                }

                working = Rebuild(result.State, rngState: working.RngState, scheduledActions: working.ScheduledActions, blockingDecision: working.BlockingDecision);
            }

            return working;
        }

        private GameState ApplyEffectRegistrations(GameState current, IReadOnlyList<EffectInstance> registrations)
        {
            GameState working = current;
            for (int i = 0; i < registrations.Count; i++)
            {
                working = _effectEngine.RegisterEffect(working, _effectRuntimeCatalog, registrations[i]);
            }

            return working;
        }

        private static List<ScheduledAction> ApplyQueueChanges(
            IReadOnlyList<ScheduledAction> currentQueue,
            int currentTick,
            ScheduledAction executedAction,
            IReadOnlyList<string> cancelIds,
            IReadOnlyList<ScheduledAction> scheduledActions)
        {
            Dictionary<string, ScheduledAction> byId = new Dictionary<string, ScheduledAction>(StringComparer.Ordinal);
            for (int i = 0; i < currentQueue.Count; i++)
            {
                ScheduledAction item = currentQueue[i];
                if (!string.Equals(item.Id, executedAction.Id, StringComparison.Ordinal))
                {
                    byId.Add(item.Id, item);
                }
            }

            for (int i = 0; i < cancelIds.Count; i++)
            {
                if (!string.IsNullOrEmpty(cancelIds[i]))
                {
                    byId.Remove(cancelIds[i]);
                }
            }

            for (int i = 0; i < scheduledActions.Count; i++)
            {
                ScheduledAction action = scheduledActions[i];
                ValidateScheduledActionForEnqueue(action, currentTick, byId);
                byId.Add(action.Id, action);
            }

            List<ScheduledAction> queue = new List<ScheduledAction>(byId.Values);
            queue.Sort(ScheduledAction.CompareQueueOrder);
            return queue;
        }

        private static void ValidateScheduledActionForEnqueue(ScheduledAction action, int currentTick, IReadOnlyDictionary<string, ScheduledAction> existing)
        {
            if (action.RunTick <= currentTick)
            {
                throw new SchedulerException(
                    SchedulerErrorCodes.InvalidActionTick,
                    "Scheduled actions must target a strictly future tick when they are enqueued.",
                    action.Id);
            }

            if (existing.ContainsKey(action.Id))
            {
                throw new SchedulerException(
                    SchedulerErrorCodes.DuplicateActionId,
                    "Scheduled action IDs must remain unique within the queue.",
                    action.Id);
            }
        }

        private static void ValidateNoLateActions(IReadOnlyList<ScheduledAction> queue, int currentTick)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].RunTick < currentTick)
                {
                    throw new SchedulerException(
                        SchedulerErrorCodes.LateAction,
                        "Scheduled actions that are already overdue fail closed instead of executing late.",
                        queue[i].Id);
                }
            }
        }

        private static List<ScheduledAction> SelectDueActions(IReadOnlyList<ScheduledAction> queue, int currentTick)
        {
            List<ScheduledAction> due = new List<ScheduledAction>();
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].RunTick == currentTick)
                {
                    due.Add(queue[i]);
                }
            }

            due.Sort(ScheduledAction.CompareExecutionOrder);
            return due;
        }

        private static bool ContainsAction(IReadOnlyList<ScheduledAction> queue, string actionId)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (string.Equals(queue[i].Id, actionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static GameState IncrementTick(GameState current, IList<SimulationTickPhase> phases, Action<SimulationTickPhase> observePhase)
        {
            Observe(SimulationTickPhase.IncrementTick, phases, observePhase);
            return Rebuild(current, tick: checked(current.Tick + 1));
        }

        private static GameState ExecutePhase(
            SimulationTickPhase phase,
            GameState current,
            IList<SimulationTickPhase> phases,
            Action<SimulationTickPhase> observePhase,
            Func<GameState, GameState> apply)
        {
            Observe(phase, phases, observePhase);
            return apply(current);
        }

        private static GameState ExecuteNoOpPhase(
            SimulationTickPhase phase,
            GameState current,
            IList<SimulationTickPhase> phases,
            Action<SimulationTickPhase> observePhase)
        {
            Observe(phase, phases, observePhase);
            return current;
        }

        private TickCausalBuffer CreateAuditBuffer(GameState state, VisibleTargetCatalog visibleTargets)
        {
            TickCausalBuffer buffer = new TickCausalBuffer(state.Tick, visibleTargets);
            for (int i = 0; i < visibleTargets.Targets.Count; i++)
            {
                TargetPath target = visibleTargets.Targets[i];
                buffer.TrackTarget(target, ReadVisibleValue(state, target));
            }

            return buffer;
        }

        private static TickCausalSnapshot CloseAndSeal(GameState state, TickCausalBuffer buffer, VisibleTargetCatalog visibleTargets)
        {
            for (int i = 0; i < visibleTargets.Targets.Count; i++)
            {
                TargetPath target = visibleTargets.Targets[i];
                buffer.CloseTarget(target, ReadVisibleValue(state, target));
            }

            return buffer.Seal();
        }

        private static BlockingDecision ChooseBlockingDecision(IReadOnlyList<PendingBlockingDecision> candidates)
        {
            PendingBlockingDecision winner = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                PendingBlockingDecision current = candidates[i];
                if (winner == null
                    || current.Priority > winner.Priority
                    || (current.Priority == winner.Priority
                        && string.Compare(current.ActionId, winner.ActionId, StringComparison.Ordinal) < 0))
                {
                    winner = current;
                }
            }

            return winner?.Decision;
        }

        private static int ReadVisibleValue(GameState state, TargetPath target)
        {
            if (target.Namespace == "metrics")
            {
                return state.MetricsById[target[1]].ValueS;
            }

            if (target.Namespace == "regions")
            {
                RegionState region = state.RegionsById[target[1]];
                if (target[2] == "support") { return region.SupportS; }
                if (target[2] == "tension") { return region.TensionS; }
                if (target[2] == "organization") { return region.OrganizationS; }
                return region.RivalPresenceS;
            }

            if (target.Namespace == "igs")
            {
                InterestGroupState group = state.InterestGroupsById[target[1]];
                return target[2] == "clout" ? group.CloutS : group.ApprovalS;
            }

            MovementState movement = state.MovementsById[target[1]];
            return target[2] == "intensity" ? movement.IntensityS : movement.Direction;
        }

        private static IReadOnlyList<string> SnapshotIds(IEnumerable<string> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            List<string> snapshot = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (string.IsNullOrEmpty(value) || !seen.Add(value))
                {
                    throw new ArgumentException("Scheduler ID lists must contain unique non-empty IDs.", name);
                }

                snapshot.Add(value);
            }

            return Array.AsReadOnly(snapshot.ToArray());
        }

        private static IReadOnlyDictionary<string, IScheduledActionHandler> SnapshotHandlers(IEnumerable<KeyValuePair<string, IScheduledActionHandler>> values)
        {
            Dictionary<string, IScheduledActionHandler> handlers = new Dictionary<string, IScheduledActionHandler>(StringComparer.Ordinal);
            if (values == null)
            {
                return new ReadOnlyDictionary<string, IScheduledActionHandler>(handlers);
            }

            foreach (KeyValuePair<string, IScheduledActionHandler> item in values)
            {
                if (string.IsNullOrEmpty(item.Key) || item.Value == null)
                {
                    throw new ArgumentException("Scheduler handlers require non-empty action types and non-null handler instances.", nameof(values));
                }

                handlers.Add(item.Key, item.Value);
            }

            return new ReadOnlyDictionary<string, IScheduledActionHandler>(handlers);
        }

        private static void Observe(SimulationTickPhase phase, IList<SimulationTickPhase> phases, Action<SimulationTickPhase> observePhase)
        {
            phases.Add(phase);
            observePhase?.Invoke(phase);
        }

        private static GameState Rebuild(
            GameState current,
            IEnumerable<MetricState> metrics = null,
            IEnumerable<InternalDomainState> internals = null,
            IEnumerable<RegionState> regions = null,
            IEnumerable<InterestGroupState> interestGroups = null,
            IEnumerable<MovementState> movements = null,
            IEnumerable<EffectInstance> activeEffects = null,
            int? tick = null,
            Pcg32State rngState = null,
            IEnumerable<ScheduledAction> scheduledActions = null,
            BlockingDecision blockingDecision = null,
            bool preserveBlockingDecision = true)
        {
            return new GameState(
                current.RngSeed,
                current.ContentMetadata,
                metrics ?? current.Metrics,
                internals ?? current.Internals,
                regions ?? current.Regions,
                interestGroups ?? current.InterestGroups,
                movements ?? current.Movements,
                activeEffects ?? current.ActiveEffects,
                tick ?? current.Tick,
                rngState ?? current.RngState,
                scheduledActions ?? current.ScheduledActions,
                preserveBlockingDecision ? (blockingDecision ?? current.BlockingDecision) : blockingDecision);
        }

        private sealed class PendingBlockingDecision
        {
            public PendingBlockingDecision(BlockingDecision decision, int priority, string actionId)
            {
                Decision = decision;
                Priority = priority;
                ActionId = actionId;
            }

            public BlockingDecision Decision { get; }

            public int Priority { get; }

            public string ActionId { get; }
        }
    }

    public static class SchedulerFormatting
    {
        public static string FormatPhase(SimulationTickPhase phase)
        {
            return phase switch
            {
                SimulationTickPhase.IncrementTick => "increment_tick",
                SimulationTickPhase.ExpireEffects => "expire_effects",
                SimulationTickPhase.ExecuteScheduledActions => "execute_scheduled_actions",
                SimulationTickPhase.ApplyStartInstantModifiers => "apply_start_instant_modifiers",
                SimulationTickPhase.ApplyPerTickModifiers => "apply_per_tick_modifiers",
                SimulationTickPhase.RevertInternals => "revert_internals",
                SimulationTickPhase.DeriveInternals => "derive_internals",
                SimulationTickPhase.AggregateNationalMetrics => "aggregate_national_metrics",
                SimulationTickPhase.DriftNationalToRegions => "drift_national_to_regions",
                SimulationTickPhase.PullRegionsToInternals => "pull_regions_to_internals",
                SimulationTickPhase.UpdateMovements => "update_movements",
                SimulationTickPhase.AdvanceReforms => "advance_reforms",
                SimulationTickPhase.ResolveEventsAndCrises => "resolve_events_and_crises",
                SimulationTickPhase.ApplyFinalClampsAndNormalizations => "apply_final_clamps_and_normalizations",
                SimulationTickPhase.CloseCausalReport => "close_causal_report",
                SimulationTickPhase.DetectAndPublishBlockingDecision => "detect_and_publish_blocking_decision",
                _ => throw new InvalidOperationException("Unsupported scheduler phase.")
            };
        }
    }
}
