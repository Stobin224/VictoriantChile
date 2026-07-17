using System;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public sealed class StateTargetReader : IStateTargetReader
    {
        private readonly GameState _state;
        private readonly IReadOnlyStaticTargetSource _staticSource;

        public StateTargetReader(GameState state, IReadOnlyStaticTargetSource staticSource)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _staticSource = staticSource ?? throw new ArgumentNullException(nameof(staticSource));
        }

        public TargetReadResult Read(TargetPath target)
        {
            if (!target.IsValid)
            {
                return Fail(target, "target.invalid_path", "Target path is invalid.");
            }

            if (target.Namespace == "metrics")
            {
                if (_state.MetricsById.TryGetValue(target[1], out MetricState metric))
                {
                    return TargetReadResult.Succeeded(target.ToString(), metric.ValueS, TargetValueSource.DynamicState);
                }

                return Fail(target, "target.not_found", "Metric target was not found.");
            }

            if (target.Namespace == "internals")
            {
                if (_state.InternalsByDomain.TryGetValue(target[1], out InternalDomainState domain)
                    && domain.ComponentsById.TryGetValue(target[2], out InternalValueState component))
                {
                    return TargetReadResult.Succeeded(target.ToString(), component.ValueS, TargetValueSource.DynamicState);
                }

                return Fail(target, "target.not_found", "Internal target was not found.");
            }

            if (target.Namespace == "regions")
            {
                if (_state.RegionsById.TryGetValue(target[1], out RegionState region))
                {
                    string field = target[2];
                    if (field == "support")
                    {
                        return TargetReadResult.Succeeded(target.ToString(), region.SupportS, TargetValueSource.DynamicState);
                    }

                    if (field == "tension")
                    {
                        return TargetReadResult.Succeeded(target.ToString(), region.TensionS, TargetValueSource.DynamicState);
                    }

                    if (field == "organization")
                    {
                        return TargetReadResult.Succeeded(target.ToString(), region.OrganizationS, TargetValueSource.DynamicState);
                    }

                    if (field == "rival_presence")
                    {
                        return TargetReadResult.Succeeded(target.ToString(), region.RivalPresenceS, TargetValueSource.DynamicState);
                    }
                }

                TargetReadResult staticResult = _staticSource.TryReadStatic(target);
                if (staticResult.Success)
                {
                    return staticResult;
                }

                return staticResult.Diagnostics.Count > 0 ? staticResult : Fail(target, "target.not_found", "Region target was not found.");
            }

            if (target.Namespace == "igs")
            {
                if (_state.InterestGroupsById.TryGetValue(target[1], out InterestGroupState ig))
                {
                    if (target[2] == "clout")
                    {
                        return TargetReadResult.Succeeded(target.ToString(), ig.CloutS, TargetValueSource.DynamicState);
                    }

                    if (target[2] == "approval")
                    {
                        return TargetReadResult.Succeeded(target.ToString(), ig.ApprovalS, TargetValueSource.DynamicState);
                    }
                }

                return Fail(target, "target.not_found", "Interest group target was not found.");
            }

            if (target.Namespace == "movements")
            {
                if (_state.MovementsById.TryGetValue(target[1], out MovementState movement))
                {
                    if (target[2] == "intensity")
                    {
                        return TargetReadResult.Succeeded(target.ToString(), movement.IntensityS, TargetValueSource.DynamicState);
                    }

                    if (target[2] == "direction")
                    {
                        return TargetReadResult.Succeeded(target.ToString(), movement.Direction, TargetValueSource.DynamicState);
                    }
                }

                return Fail(target, "target.not_found", "Movement target was not found.");
            }

            return Fail(target, "target.invalid_path", "Unsupported target namespace.");
        }

        private static TargetReadResult Fail(TargetPath target, string code, string message)
        {
            string targetText = target.IsValid ? target.ToString() : "<invalid>";
            return TargetReadResult.Failed(targetText, new StateDiagnostic(code, targetText, message));
        }
    }
}
