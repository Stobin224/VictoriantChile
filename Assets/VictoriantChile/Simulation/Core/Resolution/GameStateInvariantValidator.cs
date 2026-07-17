using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public sealed class GameStateInvariantValidator
    {
        public IReadOnlyList<StateDiagnostic> Validate(GameState state, TargetConfigCatalog configs)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (configs == null)
            {
                throw new ArgumentNullException(nameof(configs));
            }

            List<StateDiagnostic> diagnostics = new List<StateDiagnostic>();
            if (state.StateSchemaVersion != GameState.CurrentStateSchemaVersion)
            {
                diagnostics.Add(new StateDiagnostic("state.invariant_violation", "$.state_schema_version", "Unsupported state schema version."));
            }

            if (state.Tick < 0)
            {
                diagnostics.Add(new StateDiagnostic("state.invariant_violation", "$.tick", "Tick cannot be negative."));
            }

            ValidateMetadata(state, diagnostics);
            ValidateMetrics(state, configs, diagnostics);
            ValidateInternals(state, configs, diagnostics);
            ValidateRegions(state, configs, diagnostics);
            ValidateInterestGroups(state, configs, diagnostics);
            ValidateMovements(state, configs, diagnostics);
            return Array.AsReadOnly(diagnostics.ToArray());
        }

        private static void ValidateMetadata(GameState state, List<StateDiagnostic> diagnostics)
        {
            if (state.ContentMetadata == null
                || state.ContentMetadata.ContentPackVersion <= 0
                || state.ContentMetadata.ContentSchemaVersion <= 0
                || state.ContentMetadata.MinimumGameSchemaVersion <= 0
                || string.IsNullOrEmpty(state.ContentMetadata.DefaultLanguage)
                || state.ContentMetadata.Files.Count == 0)
            {
                diagnostics.Add(new StateDiagnostic("state.invariant_violation", "$.content", "Content metadata is incomplete."));
            }
        }

        private static void ValidateMetrics(GameState state, TargetConfigCatalog configs, List<StateDiagnostic> diagnostics)
        {
            if (state.Metrics.Count != InitialTargetRegistry.Metrics.Count)
            {
                diagnostics.Add(new StateDiagnostic("state.invariant_violation", "$.metrics", "Metric set has an unexpected size."));
            }

            for (int i = 0; i < InitialTargetRegistry.Metrics.Count; i++)
            {
                TargetPath path = InitialTargetRegistry.Metrics[i];
                if (!state.MetricsById.TryGetValue(path[1], out MetricState metric))
                {
                    diagnostics.Add(new StateDiagnostic("target.not_found", path.ToString(), "Required metric is missing."));
                    continue;
                }

                ValidateRange(path, metric.ValueS, configs, diagnostics);
            }
        }

        private static void ValidateInternals(GameState state, TargetConfigCatalog configs, List<StateDiagnostic> diagnostics)
        {
            int total = 0;
            for (int i = 0; i < state.Internals.Count; i++)
            {
                total += state.Internals[i].Components.Count;
            }

            if (total != InitialTargetRegistry.Internals.Count)
            {
                diagnostics.Add(new StateDiagnostic("state.invariant_violation", "$.internals", "Internal component set has an unexpected size."));
            }

            for (int i = 0; i < InitialTargetRegistry.Internals.Count; i++)
            {
                TargetPath path = InitialTargetRegistry.Internals[i];
                if (!state.InternalsByDomain.TryGetValue(path[1], out InternalDomainState domain)
                    || !domain.ComponentsById.TryGetValue(path[2], out InternalValueState component))
                {
                    diagnostics.Add(new StateDiagnostic("target.not_found", path.ToString(), "Required internal component is missing."));
                    continue;
                }

                ValidateRange(path, component.ValueS, configs, diagnostics);
            }
        }

        private static void ValidateRegions(GameState state, TargetConfigCatalog configs, List<StateDiagnostic> diagnostics)
        {
            for (int i = 0; i < state.Regions.Count; i++)
            {
                RegionState region = state.Regions[i];
                ValidateRange(InitialTargetRegistry.RegionSupport(region.RegionId), region.SupportS, configs, diagnostics);
                ValidateRange(InitialTargetRegistry.RegionTension(region.RegionId), region.TensionS, configs, diagnostics);
                ValidateRange(InitialTargetRegistry.RegionOrganization(region.RegionId), region.OrganizationS, configs, diagnostics);
                ValidateRange(InitialTargetRegistry.RegionRivalPresence(region.RegionId), region.RivalPresenceS, configs, diagnostics);
            }
        }

        private static void ValidateInterestGroups(GameState state, TargetConfigCatalog configs, List<StateDiagnostic> diagnostics)
        {
            int cloutTotal = 0;
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState ig = state.InterestGroups[i];
                ValidateRange(InitialTargetRegistry.InterestGroupClout(ig.InterestGroupId), ig.CloutS, configs, diagnostics);
                ValidateRange(InitialTargetRegistry.InterestGroupApproval(ig.InterestGroupId), ig.ApprovalS, configs, diagnostics);
                cloutTotal = checked(cloutTotal + ig.CloutS);
            }

            if (cloutTotal != FixedMath.HundredS)
            {
                diagnostics.Add(new StateDiagnostic("state.invariant_violation", "igs.*.clout", "IG clout must sum exactly 10000."));
            }
        }

        private static void ValidateMovements(GameState state, TargetConfigCatalog configs, List<StateDiagnostic> diagnostics)
        {
            for (int i = 0; i < state.Movements.Count; i++)
            {
                MovementState movement = state.Movements[i];
                ValidateRange(InitialTargetRegistry.MovementIntensity(movement.MovementId), movement.IntensityS, configs, diagnostics);
                TargetPath direction = InitialTargetRegistry.MovementDirection(movement.MovementId);
                ValidateRange(direction, movement.Direction, configs, diagnostics);
                if (movement.Direction != -1 && movement.Direction != 1)
                {
                    diagnostics.Add(new StateDiagnostic("target.invalid_direction", direction.ToString(), "Movement direction must be exactly -1 or 1."));
                }
            }
        }

        private static void ValidateRange(TargetPath path, int valueS, TargetConfigCatalog configs, List<StateDiagnostic> diagnostics)
        {
            if (!configs.TryResolve(path, out TargetConfig config))
            {
                diagnostics.Add(new StateDiagnostic("target.config_not_found", path.ToString(), "No TargetConfig matches target."));
                return;
            }

            if (valueS < config.MinS || valueS > config.MaxS)
            {
                diagnostics.Add(new StateDiagnostic("state.invariant_violation", path.ToString(), "Target value is outside TargetConfig range."));
            }
        }
    }
}
