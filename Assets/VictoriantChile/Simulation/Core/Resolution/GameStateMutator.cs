using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public sealed class GameStateMutator
    {
        public StateMutationResult Apply(GameState current, TargetMutation mutation, TargetConfigCatalog configs)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (mutation == null)
            {
                throw new ArgumentNullException(nameof(mutation));
            }

            if (configs == null)
            {
                throw new ArgumentNullException(nameof(configs));
            }

            TargetPath target = mutation.Target;
            if (IsStaticRegionalResource(target))
            {
                return Fail(mutation, "target.read_only", "Static regional resources are read-only.");
            }

            if (!TryReadDynamic(current, target, out int beforeS))
            {
                return Fail(mutation, "target.not_found", "Dynamic target was not found.");
            }

            if (!configs.TryResolve(target, out TargetConfig config))
            {
                return Fail(mutation, "target.config_not_found", "No TargetConfig matches target.");
            }

            if (!config.Allows(mutation.Operation))
            {
                return Fail(mutation, "target.operation_not_allowed", "Operation is not allowed by TargetConfig.");
            }

            if (config.NormalizeGroup != null && config.NormalizeGroup != "igs.clout_sum_100")
            {
                return Fail(mutation, "target.unsupported_normalize_group", "Normalize group is not supported.");
            }

            long requestedLong;
            try
            {
                requestedLong = CalculateRequested(beforeS, mutation);
            }
            catch (OverflowException)
            {
                return Fail(mutation, "math.overflow", "Mutation arithmetic overflowed.");
            }

            bool clamped = requestedLong < config.MinS || requestedLong > config.MaxS;
            int requestedClamped = ClampToInt(requestedLong, config.MinS, config.MaxS);

            if (target.Namespace == "movements" && target[2] == "direction" && requestedClamped != -1 && requestedClamped != 1)
            {
                return Fail(mutation, "target.invalid_direction", "Movement direction must be exactly -1 or 1.");
            }

            GameState next;
            try
            {
                next = target.Namespace == "igs" && target[2] == "clout"
                    ? ApplyCloutMutation(current, target[1], requestedClamped)
                    : ApplyScalarMutation(current, target, requestedClamped);
            }
            catch (ArgumentException)
            {
                return Fail(mutation, "state.invariant_violation", "State snapshot could not be rebuilt.");
            }
            catch (OverflowException)
            {
                return Fail(mutation, "math.overflow", "State rebuild overflowed.");
            }

            IReadOnlyList<StateDiagnostic> invariantDiagnostics = new GameStateInvariantValidator().Validate(next, configs);
            if (invariantDiagnostics.Count > 0)
            {
                return StateMutationResult.Failed(mutation, invariantDiagnostics);
            }

            int afterS = ReadDynamicOrZero(next, target);
            return StateMutationResult.Succeeded(next, mutation, beforeS, mutation.ValueS, afterS, clamped, config.NormalizeGroup);
        }

        private static StateMutationResult Fail(TargetMutation mutation, string code, string message)
        {
            return StateMutationResult.Failed(mutation, new StateDiagnostic(code, mutation.Target.ToString(), message));
        }

        private static long CalculateRequested(int beforeS, TargetMutation mutation)
        {
            if (mutation.Operation == TargetOperation.Add)
            {
                return checked((long)beforeS + mutation.ValueS);
            }

            if (mutation.Operation == TargetOperation.Multiply)
            {
                long product = checked((long)beforeS * mutation.ValueS);
                return FixedMath.RoundDivide(product, FixedMath.MultiplierBaseS);
            }

            return mutation.ValueS;
        }

        private static int ClampToInt(long value, int minS, int maxS)
        {
            if (value < minS)
            {
                return minS;
            }

            if (value > maxS)
            {
                return maxS;
            }

            return checked((int)value);
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

            if (target.Namespace == "igs" && state.InterestGroupsById.TryGetValue(target[1], out InterestGroupState ig))
            {
                if (target[2] == "clout") { value = ig.CloutS; return true; }
                if (target[2] == "approval") { value = ig.ApprovalS; return true; }
            }

            if (target.Namespace == "movements" && state.MovementsById.TryGetValue(target[1], out MovementState movement))
            {
                if (target[2] == "intensity") { value = movement.IntensityS; return true; }
                if (target[2] == "direction") { value = movement.Direction; return true; }
            }

            return false;
        }

        private static int ReadDynamicOrZero(GameState state, TargetPath target)
        {
            TryReadDynamic(state, target, out int value);
            return value;
        }

        private static GameState ApplyScalarMutation(GameState state, TargetPath target, int valueS)
        {
            return new GameState(
                state.RngSeed,
                state.ContentMetadata,
                BuildMetrics(state, target, valueS),
                BuildInternals(state, target, valueS),
                BuildRegions(state, target, valueS),
                BuildInterestGroups(state, target, valueS),
                BuildMovements(state, target, valueS));
        }

        private static GameState ApplyCloutMutation(GameState state, string interestGroupId, int valueS)
        {
            List<InterestGroupCloutValue> raw = new List<InterestGroupCloutValue>();
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState ig = state.InterestGroups[i];
                raw.Add(new InterestGroupCloutValue(ig.InterestGroupId, ig.InterestGroupId == interestGroupId ? valueS : ig.CloutS));
            }

            IReadOnlyList<InterestGroupCloutValue> normalized = CloutNormalizer.Normalize(raw);
            Dictionary<string, int> cloutById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < normalized.Count; i++)
            {
                cloutById.Add(normalized[i].InterestGroupId, normalized[i].CloutS);
            }

            List<InterestGroupState> groups = new List<InterestGroupState>();
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState ig = state.InterestGroups[i];
                groups.Add(new InterestGroupState(ig.InterestGroupId, cloutById[ig.InterestGroupId], ig.ApprovalS));
            }

            return new GameState(
                state.RngSeed,
                state.ContentMetadata,
                state.Metrics,
                state.Internals,
                state.Regions,
                groups,
                state.Movements);
        }

        private static List<MetricState> BuildMetrics(GameState state, TargetPath target, int valueS)
        {
            List<MetricState> values = new List<MetricState>();
            for (int i = 0; i < state.Metrics.Count; i++)
            {
                MetricState metric = state.Metrics[i];
                values.Add(new MetricState(metric.MetricId, target.Namespace == "metrics" && target[1] == metric.MetricId ? valueS : metric.ValueS));
            }

            return values;
        }

        private static List<InternalDomainState> BuildInternals(GameState state, TargetPath target, int valueS)
        {
            List<InternalDomainState> values = new List<InternalDomainState>();
            for (int i = 0; i < state.Internals.Count; i++)
            {
                InternalDomainState domain = state.Internals[i];
                List<InternalValueState> components = new List<InternalValueState>();
                for (int j = 0; j < domain.Components.Count; j++)
                {
                    InternalValueState component = domain.Components[j];
                    bool match = target.Namespace == "internals" && target[1] == domain.Domain && target[2] == component.ComponentId;
                    components.Add(new InternalValueState(component.ComponentId, match ? valueS : component.ValueS));
                }

                values.Add(new InternalDomainState(domain.Domain, components));
            }

            return values;
        }

        private static List<RegionState> BuildRegions(GameState state, TargetPath target, int valueS)
        {
            List<RegionState> values = new List<RegionState>();
            for (int i = 0; i < state.Regions.Count; i++)
            {
                RegionState region = state.Regions[i];
                bool match = target.Namespace == "regions" && target[1] == region.RegionId;
                values.Add(new RegionState(
                    region.RegionId,
                    match && target[2] == "support" ? valueS : region.SupportS,
                    match && target[2] == "tension" ? valueS : region.TensionS,
                    match && target[2] == "organization" ? valueS : region.OrganizationS,
                    match && target[2] == "rival_presence" ? valueS : region.RivalPresenceS));
            }

            return values;
        }

        private static List<MovementState> BuildMovements(GameState state, TargetPath target, int valueS)
        {
            List<MovementState> values = new List<MovementState>();
            for (int i = 0; i < state.Movements.Count; i++)
            {
                MovementState movement = state.Movements[i];
                bool match = target.Namespace == "movements" && target[1] == movement.MovementId;
                values.Add(new MovementState(
                    movement.MovementId,
                    match && target[2] == "intensity" ? valueS : movement.IntensityS,
                    match && target[2] == "direction" ? valueS : movement.Direction));
            }

            return values;
        }

        private static List<InterestGroupState> BuildInterestGroups(GameState state, TargetPath target, int valueS)
        {
            List<InterestGroupState> values = new List<InterestGroupState>();
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState interestGroup = state.InterestGroups[i];
                bool match = target.Namespace == "igs" && target[1] == interestGroup.InterestGroupId;
                values.Add(new InterestGroupState(
                    interestGroup.InterestGroupId,
                    interestGroup.CloutS,
                    match && target[2] == "approval" ? valueS : interestGroup.ApprovalS));
            }

            return values;
        }
    }
}
