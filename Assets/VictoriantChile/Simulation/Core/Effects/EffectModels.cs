using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Effects
{
    public enum EffectStackMode
    {
        Stack = 1,
        Replace = 2,
        Refresh = 3,
        Max = 4,
        StackLimitN = 5
    }

    public static class EffectEngineErrorCodes
    {
        public const string InvalidInstance = "effect.invalid_instance";
        public const string InvalidOrigin = "effect.invalid_origin";
        public const string MissingTemplate = "effect.template_not_found";
        public const string DuplicateInstanceId = "effect.duplicate_instance_id";
        public const string InvalidStackKey = "effect.invalid_stack_key";
        public const string InvalidStackLimit = "effect.invalid_stack_limit";
        public const string InvalidTickRange = "effect.invalid_tick_range";
        public const string StackModeConflict = "effect.stack_mode_conflict";
        public const string StackLimitConflict = "effect.stack_limit_conflict";
        public const string RefreshConflict = "effect.refresh_conflict";
        public const string MaxConflict = "effect.max_conflict";
        public const string MaxSetUnsupported = "effect.max_set_unsupported";
        public const string ReadOnlyTarget = "effect.target_read_only";
        public const string TargetNotFound = "effect.target_not_found";
        public const string TargetConfigNotFound = "effect.target_config_not_found";
        public const string TargetOperationNotAllowed = "effect.target_operation_not_allowed";
        public const string UnsupportedNormalizeGroup = "effect.unsupported_normalize_group";
        public const string VisibleTargetRequiresLedger = "effect.visible_target_requires_ledger";
        public const string MissingVisibleBaseline = "effect.visible_target_missing_baseline";
        public const string InvalidClampRange = "effect.invalid_clamp_range";
        public const string ArithmeticOverflow = "effect.arithmetic_overflow";
        public const string InvalidDirection = "effect.invalid_direction";
    }

    public sealed class EffectEngineException : InvalidOperationException
    {
        public EffectEngineException(string code, string message, string detail = null, Exception innerException = null)
            : base(message, innerException)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("Effect engine error code cannot be null or empty.", nameof(code));
            }

            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Effect engine error message cannot be null or empty.", nameof(message));
            }

            Code = code;
            Detail = detail;
        }

        public string Code { get; }

        public string Detail { get; }
    }

    public sealed class EffectInstance : IEquatable<EffectInstance>, IComparable<EffectInstance>
    {
        public EffectInstance(
            string id,
            string templateId,
            CauseRef origin,
            int startTick,
            int? endTickExclusive,
            string stackKey,
            EffectStackMode stackMode,
            int? stackLimitN,
            int priority,
            bool startInstantApplied = false)
        {
            ValidateIdentifier(id, nameof(id));
            ValidateIdentifier(templateId, nameof(templateId));
            ValidateOrigin(origin);
            ValidateStackKey(stackKey);
            ValidateTickRange(startTick, endTickExclusive);
            ValidateStackLimit(stackMode, stackLimitN);

            Id = id;
            TemplateId = templateId;
            Origin = origin;
            StartTick = startTick;
            EndTickExclusive = endTickExclusive;
            StackKey = stackKey;
            StackMode = stackMode;
            StackLimitN = stackLimitN;
            Priority = priority;
            StartInstantApplied = startInstantApplied;
        }

        public string Id { get; }

        public string TemplateId { get; }

        public CauseRef Origin { get; }

        public int StartTick { get; }

        public int? EndTickExclusive { get; }

        public string StackKey { get; }

        public EffectStackMode StackMode { get; }

        public int? StackLimitN { get; }

        public int Priority { get; }

        public bool StartInstantApplied { get; }

        public CauseRef ModifierCause => new CauseRef(CauseCategory.Modifier, TemplateId, Origin);

        public EffectInstance MarkStartInstantApplied()
        {
            if (StartInstantApplied)
            {
                return this;
            }

            return new EffectInstance(
                Id,
                TemplateId,
                Origin,
                StartTick,
                EndTickExclusive,
                StackKey,
                StackMode,
                StackLimitN,
                Priority,
                true);
        }

        public EffectInstance RefreshExpiration(int? refreshedEndTickExclusive)
        {
            ValidateTickRange(StartTick, refreshedEndTickExclusive);
            int? effectiveEnd = ChooseLatestEnd(EndTickExclusive, refreshedEndTickExclusive);
            if (effectiveEnd == EndTickExclusive)
            {
                return this;
            }

            return new EffectInstance(
                Id,
                TemplateId,
                Origin,
                StartTick,
                effectiveEnd,
                StackKey,
                StackMode,
                StackLimitN,
                Priority,
                StartInstantApplied);
        }

        public bool Equals(EffectInstance other)
        {
            return other != null && string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as EffectInstance);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Id);
        }

        public int CompareTo(EffectInstance other)
        {
            if (other == null)
            {
                return 1;
            }

            return string.Compare(Id, other.Id, StringComparison.Ordinal);
        }

        private static void ValidateIdentifier(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidInstance,
                    "Effect instance identifiers must be non-empty ASCII strings.",
                    name);
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidInstance,
                    "Effect instance identifiers cannot contain leading or trailing whitespace.",
                    name);
            }

            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current > 127 || char.IsWhiteSpace(current) || char.IsControl(current))
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.InvalidInstance,
                        "Effect instance identifiers must use printable non-whitespace ASCII only.",
                        name);
                }
            }
        }

        private static void ValidateOrigin(CauseRef origin)
        {
            if (origin == null)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidOrigin,
                    "Effect instances require a non-null origin cause.");
            }

            if (origin.Parent != null
                || (origin.Category != CauseCategory.Decision
                    && origin.Category != CauseCategory.Event
                    && origin.Category != CauseCategory.Reform
                    && origin.Category != CauseCategory.Movement))
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidOrigin,
                    "Effect instance origins must be DECISION, EVENT, REFORM or MOVEMENT causes without parents.");
            }
        }

        private static void ValidateStackKey(string stackKey)
        {
            if (string.IsNullOrEmpty(stackKey))
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidStackKey,
                    "Effect stack keys must be non-empty printable ASCII strings.");
            }

            if (!string.Equals(stackKey, stackKey.Trim(), StringComparison.Ordinal))
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidStackKey,
                    "Effect stack keys cannot contain leading or trailing whitespace.");
            }

            for (int i = 0; i < stackKey.Length; i++)
            {
                char current = stackKey[i];
                if (current > 127 || char.IsWhiteSpace(current) || char.IsControl(current))
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.InvalidStackKey,
                        "Effect stack keys must use printable non-whitespace ASCII only.");
                }
            }
        }

        private static void ValidateTickRange(int startTick, int? endTickExclusive)
        {
            if (startTick < 0)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidTickRange,
                    "Effect instance start tick cannot be negative.");
            }

            if (endTickExclusive.HasValue && endTickExclusive.Value <= startTick)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidTickRange,
                    "Effect instance end tick must be strictly greater than start tick when present.");
            }
        }

        private static void ValidateStackLimit(EffectStackMode stackMode, int? stackLimitN)
        {
            if (stackMode == EffectStackMode.StackLimitN)
            {
                if (!stackLimitN.HasValue || stackLimitN.Value <= 0)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.InvalidStackLimit,
                        "STACK_LIMIT_N requires a positive stack limit.");
                }

                return;
            }

            if (stackLimitN.HasValue)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidStackLimit,
                    "Only STACK_LIMIT_N instances may declare StackLimitN.");
            }
        }

        private static int? ChooseLatestEnd(int? existingEnd, int? refreshedEnd)
        {
            if (!existingEnd.HasValue || !refreshedEnd.HasValue)
            {
                return existingEnd.HasValue ? existingEnd : refreshedEnd;
            }

            return refreshedEnd.Value > existingEnd.Value ? refreshedEnd : existingEnd;
        }
    }

    public sealed class EffectClampRuntime
    {
        public EffectClampRuntime(int? minS, int? maxS)
        {
            MinS = minS;
            MaxS = maxS;
        }

        public int? MinS { get; }

        public int? MaxS { get; }
    }

    public sealed class EffectModifierRuntime
    {
        public EffectModifierRuntime(TargetPath target, TargetOperation operation, int valueS, bool isPerTick, EffectClampRuntime clamp)
        {
            if (!target.IsValid)
            {
                throw new ArgumentException("Runtime effect modifier targets must be valid concrete TargetPath values.", nameof(target));
            }

            Target = target;
            Operation = operation;
            ValueS = valueS;
            IsPerTick = isPerTick;
            Clamp = clamp;
        }

        public TargetPath Target { get; }

        public TargetOperation Operation { get; }

        public int ValueS { get; }

        public bool IsPerTick { get; }

        public EffectClampRuntime Clamp { get; }
    }

    public sealed class EffectTemplateRuntime
    {
        public EffectTemplateRuntime(string id, IEnumerable<EffectModifierRuntime> modifiers)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Runtime effect template IDs must be non-empty.", nameof(id));
            }

            Id = id;
            Modifiers = Array.AsReadOnly(Snapshot(modifiers, nameof(modifiers)));
        }

        public string Id { get; }

        public IReadOnlyList<EffectModifierRuntime> Modifiers { get; }

        private static T[] Snapshot<T>(IEnumerable<T> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            return new List<T>(values).ToArray();
        }
    }

    public sealed class EffectRuntimeCatalog
    {
        public EffectRuntimeCatalog(IEnumerable<EffectTemplateRuntime> templates)
        {
            if (templates == null)
            {
                throw new ArgumentNullException(nameof(templates));
            }

            List<EffectTemplateRuntime> snapshot = new List<EffectTemplateRuntime>();
            Dictionary<string, EffectTemplateRuntime> byId = new Dictionary<string, EffectTemplateRuntime>(StringComparer.Ordinal);
            foreach (EffectTemplateRuntime template in templates)
            {
                if (template == null)
                {
                    throw new ArgumentNullException(nameof(templates), "Runtime effect catalogs cannot contain null templates.");
                }

                snapshot.Add(template);
                byId.Add(template.Id, template);
            }

            snapshot.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
            Templates = Array.AsReadOnly(snapshot.ToArray());
            TemplatesById = new ReadOnlyDictionary<string, EffectTemplateRuntime>(byId);
        }

        public IReadOnlyList<EffectTemplateRuntime> Templates { get; }

        public IReadOnlyDictionary<string, EffectTemplateRuntime> TemplatesById { get; }
    }
}
