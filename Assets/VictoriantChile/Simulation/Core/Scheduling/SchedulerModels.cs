using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Effects;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.State;

namespace VictoriantChile.Simulation.Core.Scheduling
{
    public enum SimulationTickPhase
    {
        IncrementTick = 1,
        ExpireEffects = 2,
        ExecuteScheduledActions = 3,
        ApplyStartInstantModifiers = 4,
        ApplyPerTickModifiers = 5,
        RevertInternals = 6,
        DeriveInternals = 7,
        AggregateNationalMetrics = 8,
        DriftNationalToRegions = 9,
        PullRegionsToInternals = 10,
        UpdateMovements = 11,
        AdvanceReforms = 12,
        ResolveEventsAndCrises = 13,
        ApplyFinalClampsAndNormalizations = 14,
        CloseCausalReport = 15,
        DetectAndPublishBlockingDecision = 16
    }

    public static class SchedulerErrorCodes
    {
        public const string InvalidAction = "scheduler.invalid_action";
        public const string DuplicateActionId = "scheduler.duplicate_action_id";
        public const string InvalidActionTick = "scheduler.invalid_action_tick";
        public const string InvalidActionPayload = "scheduler.invalid_action_payload";
        public const string InvalidActionSource = "scheduler.invalid_action_source";
        public const string UnknownActionHandler = "scheduler.unknown_action_handler";
        public const string LateAction = "scheduler.late_action";
        public const string InvalidAdvance = "scheduler.invalid_advance";
        public const string AlreadyBlocked = "scheduler.already_blocked";
        public const string InvalidBlockingDecision = "scheduler.invalid_blocking_decision";
        public const string VisibleDirectMutationUnsupported = "scheduler.visible_direct_mutation_unsupported";
        public const string HiddenMutationFailed = "scheduler.hidden_mutation_failed";
    }

    public sealed class SchedulerException : InvalidOperationException
    {
        public SchedulerException(string code, string message, string detail = null, Exception innerException = null)
            : base(message, innerException)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Detail = detail;
        }

        public string Code { get; }

        public string Detail { get; }
    }

    public sealed class ScheduledActionPayloadEntry
    {
        public ScheduledActionPayloadEntry(string key, string value)
        {
            ValidateKey(key);
            if (value == null)
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidActionPayload, "Scheduled action payload values cannot be null.", key);
            }

            Key = key;
            Value = value;
        }

        public string Key { get; }

        public string Value { get; }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidActionPayload, "Scheduled action payload keys cannot be null or empty.");
            }

            if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidActionPayload, "Scheduled action payload keys cannot contain leading or trailing whitespace.", key);
            }
        }
    }

    public sealed class ScheduledActionPayload
    {
        public static readonly ScheduledActionPayload Empty = new ScheduledActionPayload(Array.Empty<KeyValuePair<string, string>>());

        public ScheduledActionPayload(IEnumerable<KeyValuePair<string, string>> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            List<ScheduledActionPayloadEntry> snapshot = new List<ScheduledActionPayloadEntry>();
            Dictionary<string, string> byKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> item in entries)
            {
                ScheduledActionPayloadEntry entry = new ScheduledActionPayloadEntry(item.Key, item.Value);
                snapshot.Add(entry);
                byKey.Add(entry.Key, entry.Value);
            }

            snapshot.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
            Entries = Array.AsReadOnly(snapshot.ToArray());
            EntriesByKey = new ReadOnlyDictionary<string, string>(byKey);
        }

        public IReadOnlyList<ScheduledActionPayloadEntry> Entries { get; }

        public IReadOnlyDictionary<string, string> EntriesByKey { get; }

        public string GetRequired(string key)
        {
            if (!EntriesByKey.TryGetValue(key, out string value))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidActionPayload, "Scheduled action payload is missing a required key.", key);
            }

            return value;
        }

        public int GetRequiredInt32(string key)
        {
            string value = GetRequired(key);
            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidActionPayload, "Scheduled action payload value is not a valid Int32.", key);
            }

            return parsed;
        }
    }

    public sealed class ScheduledAction : IEquatable<ScheduledAction>
    {
        public ScheduledAction(string id, int runTick, int priority, string type, ScheduledActionPayload payload, CauseRef source)
        {
            ValidateIdentifier(id, nameof(id));
            ValidateIdentifier(type, nameof(type));
            if (runTick < 0)
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidActionTick, "Scheduled action run ticks cannot be negative.", id);
            }

            if (source == null)
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidActionSource, "Scheduled actions require a non-null source cause.", id);
            }

            if (source.Category == CauseCategory.Modifier)
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidActionSource, "Scheduled action sources cannot be modifier causes.", id);
            }

            Id = id;
            RunTick = runTick;
            Priority = priority;
            Type = type;
            Payload = payload ?? ScheduledActionPayload.Empty;
            Source = source;
        }

        public string Id { get; }

        public int RunTick { get; }

        public int Priority { get; }

        public string Type { get; }

        public ScheduledActionPayload Payload { get; }

        public CauseRef Source { get; }

        public bool Equals(ScheduledAction other)
        {
            return other != null && string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ScheduledAction);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Id);
        }

        public static int CompareQueueOrder(ScheduledAction left, ScheduledAction right)
        {
            int tickCompare = left.RunTick.CompareTo(right.RunTick);
            if (tickCompare != 0)
            {
                return tickCompare;
            }

            int priorityCompare = right.Priority.CompareTo(left.Priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        }

        public static int CompareExecutionOrder(ScheduledAction left, ScheduledAction right)
        {
            int priorityCompare = right.Priority.CompareTo(left.Priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        }

        private static void ValidateIdentifier(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidAction, "Scheduled action identifiers cannot be null or empty.", name);
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidAction, "Scheduled action identifiers cannot contain leading or trailing whitespace.", name);
            }
        }
    }

    public sealed class ScheduledActionMutation
    {
        public ScheduledActionMutation(TargetMutation mutation, CauseRef cause)
        {
            Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
            Cause = cause ?? throw new ArgumentNullException(nameof(cause));
        }

        public TargetMutation Mutation { get; }

        public CauseRef Cause { get; }
    }

    public sealed class BlockingDecision
    {
        public BlockingDecision(string id, string type, CauseRef source, int createdTick, ScheduledActionPayload payload)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidBlockingDecision, "Blocking decision IDs cannot be null or empty.");
            }

            if (string.IsNullOrEmpty(type))
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidBlockingDecision, "Blocking decision types cannot be null or empty.", id);
            }

            if (source == null)
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidBlockingDecision, "Blocking decisions require a non-null source cause.", id);
            }

            if (createdTick < 0)
            {
                throw new SchedulerException(SchedulerErrorCodes.InvalidBlockingDecision, "Blocking decision ticks cannot be negative.", id);
            }

            Id = id;
            Type = type;
            Source = source;
            CreatedTick = createdTick;
            Payload = payload ?? ScheduledActionPayload.Empty;
        }

        public string Id { get; }

        public string Type { get; }

        public CauseRef Source { get; }

        public int CreatedTick { get; }

        public ScheduledActionPayload Payload { get; }
    }

    public interface IScheduledActionHandler
    {
        ScheduledActionExecutionResult Execute(ScheduledActionExecutionContext context, ScheduledAction action);
    }

    public sealed class ScheduledActionExecutionContext
    {
        private Pcg32State _rngState;

        public ScheduledActionExecutionContext(GameState currentState, int tick, Pcg32State rngState)
        {
            CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Tick cannot be negative.");
            }

            Tick = tick;
            _rngState = rngState ?? throw new ArgumentNullException(nameof(rngState));
        }

        public GameState CurrentState { get; }

        public int Tick { get; }

        public Pcg32State RngState => _rngState;

        public uint NextUInt32()
        {
            uint result = _rngState.NextUInt32(out Pcg32State nextState);
            _rngState = nextState;
            return result;
        }

        public uint NextUInt32(uint exclusiveUpperBound)
        {
            uint result = _rngState.NextBoundedUInt32(exclusiveUpperBound, out Pcg32State nextState);
            _rngState = nextState;
            return result;
        }

        public Pcg32KeyedDraw DeriveKeyedDraw(long seed, string system, string template, ulong slot)
        {
            return _rngState.DeriveKeyedDraw(seed, checked((ulong)Tick), system, template, slot);
        }
    }

    public sealed class ScheduledActionExecutionResult
    {
        public static readonly ScheduledActionExecutionResult Empty = new ScheduledActionExecutionResult(
            Array.Empty<ScheduledActionMutation>(),
            Array.Empty<EffectInstance>(),
            Array.Empty<ScheduledAction>(),
            Array.Empty<string>(),
            null);

        public ScheduledActionExecutionResult(
            IEnumerable<ScheduledActionMutation> mutations,
            IEnumerable<EffectInstance> effectRegistrations,
            IEnumerable<ScheduledAction> scheduledActions,
            IEnumerable<string> cancelActionIds,
            BlockingDecision blockingDecision)
        {
            Mutations = Array.AsReadOnly(new List<ScheduledActionMutation>(mutations ?? Array.Empty<ScheduledActionMutation>()).ToArray());
            EffectRegistrations = Array.AsReadOnly(new List<EffectInstance>(effectRegistrations ?? Array.Empty<EffectInstance>()).ToArray());
            ScheduledActions = Array.AsReadOnly(new List<ScheduledAction>(scheduledActions ?? Array.Empty<ScheduledAction>()).ToArray());
            CancelActionIds = Array.AsReadOnly(new List<string>(cancelActionIds ?? Array.Empty<string>()).ToArray());
            BlockingDecision = blockingDecision;
        }

        public IReadOnlyList<ScheduledActionMutation> Mutations { get; }

        public IReadOnlyList<EffectInstance> EffectRegistrations { get; }

        public IReadOnlyList<ScheduledAction> ScheduledActions { get; }

        public IReadOnlyList<string> CancelActionIds { get; }

        public BlockingDecision BlockingDecision { get; }
    }

    public sealed class TickAdvanceResult
    {
        public TickAdvanceResult(
            GameState finalState,
            Causality.TickCausalSnapshot tickSnapshot,
            IEnumerable<string> stateHashes,
            BlockingDecision blockingDecision,
            IEnumerable<SimulationTickPhase> phases)
        {
            FinalState = finalState ?? throw new ArgumentNullException(nameof(finalState));
            TickSnapshot = tickSnapshot;
            StateHashes = Array.AsReadOnly(new List<string>(stateHashes ?? Array.Empty<string>()).ToArray());
            BlockingDecision = blockingDecision;
            Phases = Array.AsReadOnly(new List<SimulationTickPhase>(phases ?? Array.Empty<SimulationTickPhase>()).ToArray());
        }

        public GameState FinalState { get; }

        public Causality.TickCausalSnapshot TickSnapshot { get; }

        public IReadOnlyList<string> StateHashes { get; }

        public BlockingDecision BlockingDecision { get; }

        public IReadOnlyList<SimulationTickPhase> Phases { get; }
    }

    public sealed class AdvanceWeeksResult
    {
        public AdvanceWeeksResult(
            int requestedWeeks,
            GameState finalState,
            IEnumerable<Causality.TickCausalSnapshot> tickSnapshots,
            IEnumerable<string> tickStateHashes,
            BlockingDecision blockingDecision)
        {
            RequestedWeeks = requestedWeeks;
            FinalState = finalState ?? throw new ArgumentNullException(nameof(finalState));
            TickSnapshots = Array.AsReadOnly(new List<Causality.TickCausalSnapshot>(tickSnapshots ?? Array.Empty<Causality.TickCausalSnapshot>()).ToArray());
            TickStateHashes = Array.AsReadOnly(new List<string>(tickStateHashes ?? Array.Empty<string>()).ToArray());
            BlockingDecision = blockingDecision;
            CompletedTicks = TickSnapshots.Count;
            PeriodSnapshot = CompletedTicks == 0 ? null : CausalPeriodAccumulator.Accumulate(TickSnapshots);
        }

        public int RequestedWeeks { get; }

        public int CompletedTicks { get; }

        public GameState FinalState { get; }

        public IReadOnlyList<Causality.TickCausalSnapshot> TickSnapshots { get; }

        public IReadOnlyList<string> TickStateHashes { get; }

        public BlockingDecision BlockingDecision { get; }

        public Causality.PeriodCausalSnapshot PeriodSnapshot { get; }
    }
}
