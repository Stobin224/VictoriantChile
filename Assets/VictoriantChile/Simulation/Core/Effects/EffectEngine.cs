using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Effects
{
    public sealed class EffectEngine
    {
        private const string SupportedCloutNormalizeGroup = "igs.clout_sum_100";

        public GameState RegisterEffect(GameState current, EffectRuntimeCatalog runtimeCatalog, EffectInstance instance)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (runtimeCatalog == null)
            {
                throw new ArgumentNullException(nameof(runtimeCatalog));
            }

            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            EffectTemplateRuntime template = RequireTemplate(runtimeCatalog, instance.TemplateId);
            List<EffectInstance> registry = TrimExpiredForRegistration(current.ActiveEffects, current.Tick);
            if (ContainsInstanceId(registry, instance.Id))
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.DuplicateInstanceId,
                    "Effect instance IDs must be globally unique within the active registry.",
                    instance.Id);
            }

            List<EffectInstance> sameKey = FindByStackKey(registry, instance.StackKey);
            ValidateStackKeyInvariant(sameKey, instance);

            switch (instance.StackMode)
            {
                case EffectStackMode.Stack:
                    registry.Add(instance);
                    break;

                case EffectStackMode.Replace:
                    RemoveByStackKey(registry, instance.StackKey);
                    registry.Add(instance);
                    break;

                case EffectStackMode.Refresh:
                    ApplyRefresh(registry, sameKey, instance);
                    break;

                case EffectStackMode.Max:
                    ApplyMax(registry, sameKey, instance, runtimeCatalog);
                    break;

                case EffectStackMode.StackLimitN:
                    ApplyStackLimit(registry, sameKey, instance);
                    break;

                default:
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.InvalidInstance,
                        "Unsupported effect stack mode.",
                        instance.StackMode.ToString());
            }

            return new GameState(
                current.RngSeed,
                current.ContentMetadata,
                current.Metrics,
                current.Internals,
                current.Regions,
                current.InterestGroups,
                current.Movements,
                registry,
                current.Tick);
        }

        private static List<EffectInstance> TrimExpiredForRegistration(IReadOnlyList<EffectInstance> current, int tick)
        {
            List<EffectInstance> kept = new List<EffectInstance>(current.Count);
            for (int i = 0; i < current.Count; i++)
            {
                EffectInstance instance = current[i];
                if (instance.EndTickExclusive.HasValue && instance.EndTickExclusive.Value <= tick)
                {
                    continue;
                }

                kept.Add(instance);
            }

            return kept;
        }

        private static bool ContainsInstanceId(IReadOnlyList<EffectInstance> instances, string instanceId)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (string.Equals(instances[i].Id, instanceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public GameState RemoveExpiredEffects(GameState current, int tick)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (tick < 0)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidTickRange,
                    "Explicit effect-engine ticks cannot be negative.");
            }

            bool removed = false;
            List<EffectInstance> kept = new List<EffectInstance>();
            for (int i = 0; i < current.ActiveEffects.Count; i++)
            {
                EffectInstance instance = current.ActiveEffects[i];
                if (instance.EndTickExclusive.HasValue && instance.EndTickExclusive.Value <= tick)
                {
                    removed = true;
                    continue;
                }

                kept.Add(instance);
            }

            if (!removed)
            {
                return current;
            }

            return new GameState(
                current.RngSeed,
                current.ContentMetadata,
                current.Metrics,
                current.Internals,
                current.Regions,
                current.InterestGroups,
                current.Movements,
                kept,
                current.Tick);
        }

        public GameState ApplyStartInstantModifiers(GameState current, EffectRuntimeCatalog runtimeCatalog, TargetConfigCatalog targetConfigs, int tick, TickCausalBuffer causalBuffer)
        {
            return ApplyPhase(current, runtimeCatalog, targetConfigs, tick, causalBuffer, false);
        }

        public GameState ApplyPerTickModifiers(GameState current, EffectRuntimeCatalog runtimeCatalog, TargetConfigCatalog targetConfigs, int tick, TickCausalBuffer causalBuffer)
        {
            return ApplyPhase(current, runtimeCatalog, targetConfigs, tick, causalBuffer, true);
        }

        private static GameState ApplyPhase(GameState current, EffectRuntimeCatalog runtimeCatalog, TargetConfigCatalog targetConfigs, int tick, TickCausalBuffer causalBuffer, bool perTickPhase)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (runtimeCatalog == null)
            {
                throw new ArgumentNullException(nameof(runtimeCatalog));
            }

            if (targetConfigs == null)
            {
                throw new ArgumentNullException(nameof(targetConfigs));
            }

            if (tick < 0)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidTickRange,
                    "Explicit effect-engine ticks cannot be negative.");
            }

            VisibleTargetCatalog visibleCatalog = VisibleTargetCatalog.CreateCanonicalFromState(current);
            List<PlannedModifier> plannedModifiers = CollectPhaseModifiers(current, runtimeCatalog, tick, perTickPhase);
            List<EffectInstance> updatedRegistry = BuildUpdatedRegistry(current.ActiveEffects, plannedModifiers, perTickPhase);
            if (plannedModifiers.Count == 0)
            {
                if (RegistryEqual(current.ActiveEffects, updatedRegistry))
                {
                    return current;
                }

                return new GameState(
                    current.RngSeed,
                    current.ContentMetadata,
                    current.Metrics,
                    current.Internals,
                    current.Regions,
                    current.InterestGroups,
                    current.Movements,
                    updatedRegistry,
                    current.Tick);
            }

            Dictionary<TargetPath, List<PlannedModifier>> byTarget = GroupByTarget(plannedModifiers);
            TargetPlanResult plan = BuildTargetPlan(current, targetConfigs, byTarget, visibleCatalog, causalBuffer);
            GameState next = RebuildState(current, updatedRegistry, plan.FinalValues);
            IReadOnlyList<StateDiagnostic> invariantDiagnostics = new GameStateInvariantValidator().Validate(next, targetConfigs);
            if (invariantDiagnostics.Count > 0)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.InvalidInstance,
                    "Effect application produced a state that violates GameState invariants.",
                    invariantDiagnostics[0].Code + ":" + invariantDiagnostics[0].Target);
            }

            if (plan.VisibleContributions.Count > 0)
            {
                if (causalBuffer == null)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.VisibleTargetRequiresLedger,
                        "Visible effect mutations require a non-null causal ledger buffer.");
                }

                try
                {
                    causalBuffer.RecordContributionsBatch(plan.VisibleContributions);
                }
                catch (CausalLedgerException exception) when (exception.Code == CausalLedgerErrorCodes.ContributionBeforeBaseline
                    || exception.Code == CausalLedgerErrorCodes.TargetNotTracked)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.MissingVisibleBaseline,
                        "Visible effect mutations require tracked causal baselines before application.",
                        exception.Target,
                        exception);
                }
            }

            return next;
        }

        private static List<PlannedModifier> CollectPhaseModifiers(GameState current, EffectRuntimeCatalog runtimeCatalog, int tick, bool perTickPhase)
        {
            List<PlannedModifier> result = new List<PlannedModifier>();
            for (int i = 0; i < current.ActiveEffects.Count; i++)
            {
                EffectInstance instance = current.ActiveEffects[i];
                if (instance.EndTickExclusive.HasValue && tick >= instance.EndTickExclusive.Value)
                {
                    continue;
                }

                EffectTemplateRuntime template = RequireTemplate(runtimeCatalog, instance.TemplateId);
                bool phaseEligible = perTickPhase
                    ? instance.StartTick <= tick
                    : instance.StartTick == tick && !instance.StartInstantApplied;
                if (!phaseEligible)
                {
                    continue;
                }

                for (int modifierIndex = 0; modifierIndex < template.Modifiers.Count; modifierIndex++)
                {
                    EffectModifierRuntime modifier = template.Modifiers[modifierIndex];
                    if (modifier.IsPerTick != perTickPhase)
                    {
                        continue;
                    }

                    result.Add(new PlannedModifier(instance, modifier, modifierIndex));
                }
            }

            return result;
        }

        private static List<EffectInstance> BuildUpdatedRegistry(IReadOnlyList<EffectInstance> current, IReadOnlyList<PlannedModifier> modifiers, bool perTickPhase)
        {
            List<EffectInstance> updated = new List<EffectInstance>(current);
            if (perTickPhase)
            {
                return updated;
            }

            HashSet<string> toMark = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < modifiers.Count; i++)
            {
                toMark.Add(modifiers[i].Instance.Id);
            }

            if (toMark.Count == 0)
            {
                return updated;
            }

            for (int i = 0; i < updated.Count; i++)
            {
                if (toMark.Contains(updated[i].Id))
                {
                    updated[i] = updated[i].MarkStartInstantApplied();
                }
            }

            return updated;
        }

        private static TargetPlanResult BuildTargetPlan(
            GameState current,
            TargetConfigCatalog targetConfigs,
            Dictionary<TargetPath, List<PlannedModifier>> byTarget,
            VisibleTargetCatalog visibleCatalog,
            TickCausalBuffer causalBuffer)
        {
            Dictionary<TargetPath, int> finalValues = new Dictionary<TargetPath, int>();
            List<CausalTargetContribution> visibleContributions = new List<CausalTargetContribution>();
            bool cloutTouched = false;
            HashSet<TargetPath> visibleTouched = new HashSet<TargetPath>();

            List<TargetPath> orderedTargets = new List<TargetPath>(byTarget.Keys);
            orderedTargets.Sort();
            for (int i = 0; i < orderedTargets.Count; i++)
            {
                TargetPath target = orderedTargets[i];
                List<PlannedModifier> modifiers = byTarget[target];
                if (IsStaticRegionalResource(target))
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.ReadOnlyTarget,
                        "Effects cannot mutate static regional resource targets.",
                        target.ToString());
                }

                if (!TryReadDynamic(current, target, out int initialValueS))
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.TargetNotFound,
                        "Effect target does not exist in the current GameState.",
                        target.ToString());
                }

                if (!targetConfigs.TryResolve(target, out TargetConfig config))
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.TargetConfigNotFound,
                        "Effect target does not resolve to a TargetConfig.",
                        target.ToString());
                }

                if (config.NormalizeGroup != null && !string.Equals(config.NormalizeGroup, SupportedCloutNormalizeGroup, StringComparison.Ordinal))
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.UnsupportedNormalizeGroup,
                        "The effect engine only supports the igs.clout_sum_100 normalize group in v1.",
                        target.ToString());
                }

                bool visible = visibleCatalog.IsVisible(target);
                if (visible)
                {
                    visibleTouched.Add(target);
                }

                TargetMutationPlan targetPlan = BuildSingleTargetPlan(target, initialValueS, config, modifiers, visible);
                if (targetPlan.FinalValueS != initialValueS)
                {
                    finalValues[target] = targetPlan.FinalValueS;
                }

                for (int j = 0; j < targetPlan.VisibleContributions.Count; j++)
                {
                    visibleContributions.Add(targetPlan.VisibleContributions[j]);
                }

                cloutTouched = cloutTouched || IsInterestGroupClout(target);
            }

            if (visibleTouched.Count > 0)
            {
                if (causalBuffer == null)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.VisibleTargetRequiresLedger,
                        "Visible effect mutations require a non-null causal ledger buffer.");
                }

                foreach (TargetPath target in visibleTouched)
                {
                    RequireTrackedVisibleBaseline(causalBuffer, target);
                }
            }

            if (cloutTouched)
            {
                if (causalBuffer == null)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.VisibleTargetRequiresLedger,
                        "IG clout normalization requires a non-null causal ledger buffer.");
                }

                Dictionary<string, int> rawCloutById = new Dictionary<string, int>(StringComparer.Ordinal);
                List<InterestGroupCloutValue> beforeValues = new List<InterestGroupCloutValue>(current.InterestGroups.Count);
                List<InterestGroupCloutValue> afterRawValues = new List<InterestGroupCloutValue>(current.InterestGroups.Count);
                for (int i = 0; i < current.InterestGroups.Count; i++)
                {
                    InterestGroupState group = current.InterestGroups[i];
                    TargetPath cloutTarget = InitialTargetRegistry.InterestGroupClout(group.InterestGroupId);
                    RequireTrackedVisibleBaseline(causalBuffer, cloutTarget);
                    beforeValues.Add(new InterestGroupCloutValue(group.InterestGroupId, group.CloutS));
                    int rawValue = group.CloutS;
                    if (finalValues.TryGetValue(cloutTarget, out int plannedValue))
                    {
                        rawValue = plannedValue;
                    }

                    rawCloutById.Add(group.InterestGroupId, rawValue);
                    afterRawValues.Add(new InterestGroupCloutValue(group.InterestGroupId, rawValue));
                }

                IReadOnlyList<InterestGroupCloutValue> normalized;
                try
                {
                    normalized = CloutNormalizer.Normalize(afterRawValues);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is OverflowException || exception is InvalidOperationException)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.ArithmeticOverflow,
                        "IG clout normalization failed during effect application.",
                        null,
                        exception);
                }

                IReadOnlyList<CausalTargetContribution> normalizationContributions = InterestGroupCloutNormalizationLedger.BuildContributions(afterRawValues, normalized);
                for (int i = 0; i < normalized.Count; i++)
                {
                    InterestGroupCloutValue normalizedValue = normalized[i];
                    TargetPath target = InitialTargetRegistry.InterestGroupClout(normalizedValue.InterestGroupId);
                    int previousRaw = rawCloutById[normalizedValue.InterestGroupId];
                    int original = current.InterestGroupsById[normalizedValue.InterestGroupId].CloutS;
                    if (normalizedValue.CloutS != original)
                    {
                        finalValues[target] = normalizedValue.CloutS;
                    }
                    else
                    {
                        finalValues.Remove(target);
                    }
                }

                for (int i = 0; i < normalizationContributions.Count; i++)
                {
                    visibleContributions.Add(normalizationContributions[i]);
                }
            }

            return new TargetPlanResult(finalValues, visibleContributions);
        }

        private static void RequireTrackedVisibleBaseline(TickCausalBuffer causalBuffer, TargetPath target)
        {
            try
            {
                causalBuffer.RequireTrackedTarget(target);
            }
            catch (CausalLedgerException exception) when (exception.Code == CausalLedgerErrorCodes.TargetNotTracked)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.MissingVisibleBaseline,
                    "Visible effect mutations require tracked causal baselines before application.",
                    target.ToString(),
                    exception);
            }
        }

        private static TargetMutationPlan BuildSingleTargetPlan(
            TargetPath target,
            int initialValueS,
            TargetConfig config,
            IReadOnlyList<PlannedModifier> modifiers,
            bool visible)
        {
            try
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    if (!config.Allows(modifiers[i].Modifier.Operation))
                    {
                        throw new EffectEngineException(
                            EffectEngineErrorCodes.TargetOperationNotAllowed,
                            "Effect modifier operation is not allowed by the target configuration.",
                            target.ToString());
                    }
                }

                List<PlannedModifier> ordered = new List<PlannedModifier>(modifiers);
                ordered.Sort(CompareModifiers);
                List<PlannedModifier> setModifiers = new List<PlannedModifier>();
                List<PlannedModifier> addModifiers = new List<PlannedModifier>();
                List<PlannedModifier> mulModifiers = new List<PlannedModifier>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (ordered[i].Modifier.Operation == TargetOperation.Set)
                    {
                        setModifiers.Add(ordered[i]);
                    }
                    else if (ordered[i].Modifier.Operation == TargetOperation.Add)
                    {
                        addModifiers.Add(ordered[i]);
                    }
                    else if (ordered[i].Modifier.Operation == TargetOperation.Multiply)
                    {
                        mulModifiers.Add(ordered[i]);
                    }
                }

                List<CausalTargetContribution> contributions = new List<CausalTargetContribution>();
                long currentValue = initialValueS;
                EffectiveClamp clamp = new EffectiveClamp(config.MinS, config.MaxS);
                if (setModifiers.Count > 0)
                {
                    PlannedModifier winner = setModifiers[0];
                    currentValue = winner.Modifier.ValueS;
                    long setDelta = checked(currentValue - initialValueS);
                    if (visible && setDelta != 0)
                    {
                        contributions.Add(new CausalTargetContribution(target, winner.Instance.ModifierCause, setDelta));
                    }

                    clamp = clamp.Intersect(winner.Modifier.Clamp, target);
                }
                else
                {
                    for (int i = 0; i < addModifiers.Count; i++)
                    {
                        PlannedModifier modifier = addModifiers[i];
                        currentValue = checked(currentValue + modifier.Modifier.ValueS);
                        if (visible && modifier.Modifier.ValueS != 0)
                        {
                            contributions.Add(new CausalTargetContribution(target, modifier.Instance.ModifierCause, modifier.Modifier.ValueS));
                        }

                        clamp = clamp.Intersect(modifier.Modifier.Clamp, target);
                    }

                    for (int i = 0; i < mulModifiers.Count; i++)
                    {
                        PlannedModifier modifier = mulModifiers[i];
                        long numerator = checked(currentValue * modifier.Modifier.ValueS);
                        long truncated = numerator / FixedMath.MultiplierBaseS;
                        long rounded = FixedMath.RoundDivide(numerator, FixedMath.MultiplierBaseS);
                        long modifierDelta = checked(truncated - currentValue);
                        long roundingDelta = checked(rounded - truncated);
                        if (visible && modifierDelta != 0)
                        {
                            contributions.Add(new CausalTargetContribution(target, modifier.Instance.ModifierCause, modifierDelta));
                        }

                        if (visible && roundingDelta != 0)
                        {
                            contributions.Add(new CausalTargetContribution(target, CauseRef.SystemRounding, roundingDelta));
                        }

                        currentValue = rounded;
                        clamp = clamp.Intersect(modifier.Modifier.Clamp, target);
                    }
                }

                long clampedValue = currentValue;
                if (currentValue < clamp.MinS)
                {
                    clampedValue = clamp.MinS;
                }
                else if (currentValue > clamp.MaxS)
                {
                    clampedValue = clamp.MaxS;
                }

                long clampDelta = checked(clampedValue - currentValue);
                if (visible && clampDelta != 0)
                {
                    contributions.Add(new CausalTargetContribution(target, CauseRef.SystemClamp, clampDelta));
                }

                int finalValue;
                try
                {
                    finalValue = checked((int)clampedValue);
                }
                catch (OverflowException exception)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.ArithmeticOverflow,
                        "Effect application produced a final value outside the supported integer range.",
                        target.ToString(),
                        exception);
                }

                if (target.Namespace == "movements" && target[2] == "direction" && finalValue != -1 && finalValue != 1)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.InvalidDirection,
                        "Movement direction effects must resolve to exactly -1 or 1.",
                        target.ToString());
                }

                return new TargetMutationPlan(finalValue, contributions);
            }
            catch (OverflowException exception)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.ArithmeticOverflow,
                    "Effect application overflowed fixed-point arithmetic.",
                    target.ToString(),
                    exception);
            }
        }

        private static GameState RebuildState(GameState current, IReadOnlyList<EffectInstance> activeEffects, IReadOnlyDictionary<TargetPath, int> finalValues)
        {
            if (finalValues.Count == 0 && RegistryEqual(current.ActiveEffects, activeEffects))
            {
                return current;
            }

            List<MetricState> metrics = new List<MetricState>(current.Metrics.Count);
            for (int i = 0; i < current.Metrics.Count; i++)
            {
                MetricState metric = current.Metrics[i];
                TargetPath path = TargetPath.Parse("metrics." + metric.MetricId);
                metrics.Add(new MetricState(metric.MetricId, finalValues.TryGetValue(path, out int updated) ? updated : metric.ValueS));
            }

            List<InternalDomainState> internals = new List<InternalDomainState>(current.Internals.Count);
            for (int i = 0; i < current.Internals.Count; i++)
            {
                InternalDomainState domain = current.Internals[i];
                List<InternalValueState> components = new List<InternalValueState>(domain.Components.Count);
                for (int j = 0; j < domain.Components.Count; j++)
                {
                    InternalValueState component = domain.Components[j];
                    TargetPath path = TargetPath.Parse("internals." + domain.Domain + "." + component.ComponentId);
                    components.Add(new InternalValueState(component.ComponentId, finalValues.TryGetValue(path, out int updated) ? updated : component.ValueS));
                }

                internals.Add(new InternalDomainState(domain.Domain, components));
            }

            List<RegionState> regions = new List<RegionState>(current.Regions.Count);
            for (int i = 0; i < current.Regions.Count; i++)
            {
                RegionState region = current.Regions[i];
                regions.Add(new RegionState(
                    region.RegionId,
                    ResolveFinal(finalValues, InitialTargetRegistry.RegionSupport(region.RegionId), region.SupportS),
                    ResolveFinal(finalValues, InitialTargetRegistry.RegionTension(region.RegionId), region.TensionS),
                    ResolveFinal(finalValues, InitialTargetRegistry.RegionOrganization(region.RegionId), region.OrganizationS),
                    ResolveFinal(finalValues, InitialTargetRegistry.RegionRivalPresence(region.RegionId), region.RivalPresenceS)));
            }

            List<InterestGroupState> interestGroups = new List<InterestGroupState>(current.InterestGroups.Count);
            for (int i = 0; i < current.InterestGroups.Count; i++)
            {
                InterestGroupState group = current.InterestGroups[i];
                interestGroups.Add(new InterestGroupState(
                    group.InterestGroupId,
                    ResolveFinal(finalValues, InitialTargetRegistry.InterestGroupClout(group.InterestGroupId), group.CloutS),
                    ResolveFinal(finalValues, InitialTargetRegistry.InterestGroupApproval(group.InterestGroupId), group.ApprovalS)));
            }

            List<MovementState> movements = new List<MovementState>(current.Movements.Count);
            for (int i = 0; i < current.Movements.Count; i++)
            {
                MovementState movement = current.Movements[i];
                movements.Add(new MovementState(
                    movement.MovementId,
                    ResolveFinal(finalValues, InitialTargetRegistry.MovementIntensity(movement.MovementId), movement.IntensityS),
                    ResolveFinal(finalValues, InitialTargetRegistry.MovementDirection(movement.MovementId), movement.Direction)));
            }

            return new GameState(
                current.RngSeed,
                current.ContentMetadata,
                metrics,
                internals,
                regions,
                interestGroups,
                movements,
                activeEffects,
                current.Tick);
        }

        private static int ResolveFinal(IReadOnlyDictionary<TargetPath, int> finalValues, TargetPath target, int existing)
        {
            return finalValues.TryGetValue(target, out int updated) ? updated : existing;
        }

        private static bool RegistryEqual(IReadOnlyList<EffectInstance> left, IReadOnlyList<EffectInstance> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i])
                    || left[i].StartInstantApplied != right[i].StartInstantApplied
                    || left[i].EndTickExclusive != right[i].EndTickExclusive
                    || left[i].Priority != right[i].Priority
                    || left[i].StackMode != right[i].StackMode
                    || left[i].StackLimitN != right[i].StackLimitN
                    || left[i].StartTick != right[i].StartTick
                    || !string.Equals(left[i].StackKey, right[i].StackKey, StringComparison.Ordinal)
                    || !string.Equals(left[i].TemplateId, right[i].TemplateId, StringComparison.Ordinal)
                    || !Equals(left[i].Origin, right[i].Origin))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ApplyRefresh(List<EffectInstance> registry, List<EffectInstance> sameKey, EffectInstance incoming)
        {
            if (sameKey.Count == 0)
            {
                registry.Add(incoming);
                return;
            }

            if (sameKey.Count != 1)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.RefreshConflict,
                    "REFRESH requires exactly zero or one existing instance for the same stack key.",
                    incoming.StackKey);
            }

            EffectInstance existing = sameKey[0];
            if (!string.Equals(existing.TemplateId, incoming.TemplateId, StringComparison.Ordinal)
                || !Equals(existing.Origin, incoming.Origin)
                || existing.Priority != incoming.Priority)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.RefreshConflict,
                    "REFRESH cannot silently change template, origin or priority.",
                    incoming.StackKey);
            }

            ReplaceById(registry, existing.Id, existing.RefreshExpiration(incoming.EndTickExclusive));
        }

        private static void ApplyMax(List<EffectInstance> registry, List<EffectInstance> sameKey, EffectInstance incoming, EffectRuntimeCatalog runtimeCatalog)
        {
            List<EffectInstance> candidates = new List<EffectInstance>(sameKey.Count + 1);
            for (int i = 0; i < sameKey.Count; i++)
            {
                candidates.Add(sameKey[i]);
            }

            candidates.Add(incoming);
            if (candidates.Count == 0)
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.MaxConflict,
                    "MAX stacking requires at least one candidate instance.");
            }

            MaxComparable comparable = null;
            EffectInstance winner = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                EffectInstance candidate = candidates[i];
                EffectTemplateRuntime template = RequireTemplate(runtimeCatalog, candidate.TemplateId);
                if (template.Modifiers.Count != 1)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.MaxConflict,
                        "MAX v1 requires templates with exactly one modifier.",
                        candidate.TemplateId);
                }

                EffectModifierRuntime modifier = template.Modifiers[0];
                if (modifier.Operation == TargetOperation.Set)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.MaxSetUnsupported,
                        "MAX stacking does not support SET modifiers in v1.",
                        candidate.TemplateId);
                }

                if (modifier.Operation != TargetOperation.Add && modifier.Operation != TargetOperation.Multiply)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.MaxConflict,
                        "MAX stacking supports only ADD and MUL modifiers in v1.",
                        candidate.TemplateId);
                }

                MaxComparable current = new MaxComparable(candidate, modifier);
                if (comparable == null)
                {
                    comparable = current;
                    winner = candidate;
                    continue;
                }

                if (!current.IsCompatibleWith(comparable))
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.MaxConflict,
                        "MAX stacking requires same target and same operation across all candidates.",
                        candidate.StackKey);
                }

                if (current.CompareStrengthTo(comparable) > 0)
                {
                    comparable = current;
                    winner = candidate;
                }
            }

            RemoveByStackKey(registry, incoming.StackKey);
            registry.Add(winner);
        }

        private static void ApplyStackLimit(List<EffectInstance> registry, List<EffectInstance> sameKey, EffectInstance incoming)
        {
            registry.Add(incoming);
            List<EffectInstance> candidates = FindByStackKey(registry, incoming.StackKey);
            candidates.Sort(CompareEvictionOrder);
            while (candidates.Count > incoming.StackLimitN.Value)
            {
                EffectInstance removed = candidates[0];
                candidates.RemoveAt(0);
                RemoveById(registry, removed.Id);
            }
        }

        private static void ValidateStackKeyInvariant(IReadOnlyList<EffectInstance> sameKey, EffectInstance incoming)
        {
            for (int i = 0; i < sameKey.Count; i++)
            {
                EffectInstance existing = sameKey[i];
                if (existing.StackMode != incoming.StackMode)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.StackModeConflict,
                        "All active instances sharing a stack key must use the same stack mode.",
                        incoming.StackKey);
                }

                if (existing.StackMode == EffectStackMode.StackLimitN && existing.StackLimitN != incoming.StackLimitN)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.StackLimitConflict,
                        "STACK_LIMIT_N instances sharing a stack key must declare the same stack limit.",
                        incoming.StackKey);
                }
            }
        }

        private static EffectTemplateRuntime RequireTemplate(EffectRuntimeCatalog runtimeCatalog, string templateId)
        {
            if (!runtimeCatalog.TemplatesById.TryGetValue(templateId, out EffectTemplateRuntime template))
            {
                throw new EffectEngineException(
                    EffectEngineErrorCodes.MissingTemplate,
                    "Effect instance template ID was not found in the runtime effect catalog.",
                    templateId);
            }

            return template;
        }

        private static List<EffectInstance> FindByStackKey(IReadOnlyList<EffectInstance> registry, string stackKey)
        {
            List<EffectInstance> values = new List<EffectInstance>();
            for (int i = 0; i < registry.Count; i++)
            {
                if (string.Equals(registry[i].StackKey, stackKey, StringComparison.Ordinal))
                {
                    values.Add(registry[i]);
                }
            }

            return values;
        }

        private static void RemoveByStackKey(List<EffectInstance> registry, string stackKey)
        {
            for (int i = registry.Count - 1; i >= 0; i--)
            {
                if (string.Equals(registry[i].StackKey, stackKey, StringComparison.Ordinal))
                {
                    registry.RemoveAt(i);
                }
            }
        }

        private static void ReplaceById(List<EffectInstance> registry, string id, EffectInstance updated)
        {
            for (int i = 0; i < registry.Count; i++)
            {
                if (string.Equals(registry[i].Id, id, StringComparison.Ordinal))
                {
                    registry[i] = updated;
                    return;
                }
            }

            throw new EffectEngineException(
                EffectEngineErrorCodes.InvalidInstance,
                "Internal effect registry replacement could not find the expected instance ID.",
                id);
        }

        private static void RemoveById(List<EffectInstance> registry, string id)
        {
            for (int i = registry.Count - 1; i >= 0; i--)
            {
                if (string.Equals(registry[i].Id, id, StringComparison.Ordinal))
                {
                    registry.RemoveAt(i);
                    return;
                }
            }
        }

        private static Dictionary<TargetPath, List<PlannedModifier>> GroupByTarget(IReadOnlyList<PlannedModifier> modifiers)
        {
            Dictionary<TargetPath, List<PlannedModifier>> result = new Dictionary<TargetPath, List<PlannedModifier>>();
            for (int i = 0; i < modifiers.Count; i++)
            {
                PlannedModifier modifier = modifiers[i];
                if (!result.TryGetValue(modifier.Modifier.Target, out List<PlannedModifier> targetModifiers))
                {
                    targetModifiers = new List<PlannedModifier>();
                    result.Add(modifier.Modifier.Target, targetModifiers);
                }

                targetModifiers.Add(modifier);
            }

            return result;
        }

        private static bool IsInterestGroupClout(TargetPath target)
        {
            return target.Namespace == "igs" && target.SegmentCount == 3 && target[2] == "clout";
        }

        private static bool IsStaticRegionalResource(TargetPath target)
        {
            if (!target.IsValid || target.Namespace != "regions" || target.SegmentCount != 3)
            {
                return false;
            }

            string field = target[2];
            return field == "admin_capS"
                || field == "industry_capS"
                || field == "extractive_capS"
                || field == "social_capS"
                || field == "populationS";
        }

        private static bool TryReadDynamic(GameState state, TargetPath target, out int value)
        {
            value = 0;
            if (target.Namespace == "metrics" && state.MetricsById.TryGetValue(target[1], out MetricState metric))
            {
                value = metric.ValueS;
                return true;
            }

            if (target.Namespace == "internals"
                && state.InternalsByDomain.TryGetValue(target[1], out InternalDomainState domain)
                && domain.ComponentsById.TryGetValue(target[2], out InternalValueState component))
            {
                value = component.ValueS;
                return true;
            }

            if (target.Namespace == "regions" && state.RegionsById.TryGetValue(target[1], out RegionState region))
            {
                if (target[2] == "support") { value = region.SupportS; return true; }
                if (target[2] == "tension") { value = region.TensionS; return true; }
                if (target[2] == "organization") { value = region.OrganizationS; return true; }
                if (target[2] == "rival_presence") { value = region.RivalPresenceS; return true; }
            }

            if (target.Namespace == "igs" && state.InterestGroupsById.TryGetValue(target[1], out InterestGroupState interestGroup))
            {
                if (target[2] == "clout") { value = interestGroup.CloutS; return true; }
                if (target[2] == "approval") { value = interestGroup.ApprovalS; return true; }
            }

            if (target.Namespace == "movements" && state.MovementsById.TryGetValue(target[1], out MovementState movement))
            {
                if (target[2] == "intensity") { value = movement.IntensityS; return true; }
                if (target[2] == "direction") { value = movement.Direction; return true; }
            }

            return false;
        }

        private static int CompareModifiers(PlannedModifier left, PlannedModifier right)
        {
            int priorityCompare = right.Instance.Priority.CompareTo(left.Instance.Priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            int instanceCompare = string.Compare(left.Instance.Id, right.Instance.Id, StringComparison.Ordinal);
            if (instanceCompare != 0)
            {
                return instanceCompare;
            }

            return left.ModifierIndex.CompareTo(right.ModifierIndex);
        }

        private static int CompareEvictionOrder(EffectInstance left, EffectInstance right)
        {
            int tickCompare = left.StartTick.CompareTo(right.StartTick);
            if (tickCompare != 0)
            {
                return tickCompare;
            }

            return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        }

        private sealed class PlannedModifier
        {
            public PlannedModifier(EffectInstance instance, EffectModifierRuntime modifier, int modifierIndex)
            {
                Instance = instance;
                Modifier = modifier;
                ModifierIndex = modifierIndex;
            }

            public EffectInstance Instance { get; }

            public EffectModifierRuntime Modifier { get; }

            public int ModifierIndex { get; }
        }

        private sealed class TargetMutationPlan
        {
            public TargetMutationPlan(int finalValueS, IEnumerable<CausalTargetContribution> visibleContributions)
            {
                FinalValueS = finalValueS;
                VisibleContributions = Array.AsReadOnly(new List<CausalTargetContribution>(visibleContributions).ToArray());
            }

            public int FinalValueS { get; }

            public IReadOnlyList<CausalTargetContribution> VisibleContributions { get; }
        }

        private sealed class TargetPlanResult
        {
            public TargetPlanResult(
                IDictionary<TargetPath, int> finalValues,
                IEnumerable<CausalTargetContribution> visibleContributions)
            {
                FinalValues = new Dictionary<TargetPath, int>(finalValues);
                VisibleContributions = Array.AsReadOnly(new List<CausalTargetContribution>(visibleContributions).ToArray());
            }

            public IReadOnlyDictionary<TargetPath, int> FinalValues { get; }

            public IReadOnlyList<CausalTargetContribution> VisibleContributions { get; }
        }

        private sealed class EffectiveClamp
        {
            public EffectiveClamp(int minS, int maxS)
            {
                MinS = minS;
                MaxS = maxS;
            }

            public int MinS { get; }

            public int MaxS { get; }

            public EffectiveClamp Intersect(EffectClampRuntime localClamp, TargetPath target)
            {
                if (localClamp == null)
                {
                    return this;
                }

                int nextMin = MinS;
                int nextMax = MaxS;
                if (localClamp.MinS.HasValue && localClamp.MinS.Value > nextMin)
                {
                    nextMin = localClamp.MinS.Value;
                }

                if (localClamp.MaxS.HasValue && localClamp.MaxS.Value < nextMax)
                {
                    nextMax = localClamp.MaxS.Value;
                }

                if (nextMin > nextMax)
                {
                    throw new EffectEngineException(
                        EffectEngineErrorCodes.InvalidClampRange,
                        "Effect-local clamps cannot produce an empty target range.",
                        target.ToString());
                }

                return new EffectiveClamp(nextMin, nextMax);
            }
        }

        private sealed class MaxComparable
        {
            public MaxComparable(EffectInstance instance, EffectModifierRuntime modifier)
            {
                Instance = instance;
                Modifier = modifier;
            }

            public EffectInstance Instance { get; }

            public EffectModifierRuntime Modifier { get; }

            public bool IsCompatibleWith(MaxComparable other)
            {
                return Modifier.Target == other.Modifier.Target && Modifier.Operation == other.Modifier.Operation;
            }

            public int CompareStrengthTo(MaxComparable other)
            {
                ulong myStrength = Strength();
                ulong otherStrength = other.Strength();
                int magnitudeCompare = myStrength.CompareTo(otherStrength);
                if (magnitudeCompare != 0)
                {
                    return magnitudeCompare;
                }

                int priorityCompare = Instance.Priority.CompareTo(other.Instance.Priority);
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                return -string.Compare(Instance.Id, other.Instance.Id, StringComparison.Ordinal);
            }

            private ulong Strength()
            {
                if (Modifier.Operation == TargetOperation.Add)
                {
                    return AbsMagnitude(Modifier.ValueS);
                }

                return AbsMagnitude(checked((long)Modifier.ValueS - FixedMath.HundredS));
            }

            private static ulong AbsMagnitude(long value)
            {
                if (value >= 0)
                {
                    return (ulong)value;
                }

                return value == long.MinValue ? ((ulong)long.MaxValue) + 1UL : (ulong)(-value);
            }
        }
    }
}
