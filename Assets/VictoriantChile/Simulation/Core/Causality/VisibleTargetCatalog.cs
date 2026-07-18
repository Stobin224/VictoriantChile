using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Causality
{
    public sealed class VisibleTargetCatalog
    {
        private readonly HashSet<TargetPath> _visibleTargets;

        private VisibleTargetCatalog(IEnumerable<TargetPath> orderedTargets)
        {
            List<TargetPath> snapshot = new List<TargetPath>();
            _visibleTargets = new HashSet<TargetPath>();
            foreach (TargetPath target in orderedTargets)
            {
                if (!_visibleTargets.Add(target))
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidTargetOrder,
                        target.ToString(),
                        null,
                        "Visible target catalogs cannot contain duplicate targets.");
                }

                snapshot.Add(target);
            }

            Targets = Array.AsReadOnly(snapshot.ToArray());
        }

        public IReadOnlyList<TargetPath> Targets { get; }

        public static VisibleTargetCatalog CreateForMvp(
            GameState state,
            IEnumerable<string> orderedRegionIds,
            IEnumerable<string> orderedInterestGroupIds,
            IEnumerable<string> orderedMovementIds)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            IReadOnlyList<string> regions = SnapshotIds(orderedRegionIds, "orderedRegionIds");
            IReadOnlyList<string> interestGroups = SnapshotIds(orderedInterestGroupIds, "orderedInterestGroupIds");
            IReadOnlyList<string> movements = SnapshotIds(orderedMovementIds, "orderedMovementIds");
            ValidateExactCoverage(state.RegionsById.Keys, regions, "regions");
            ValidateExactCoverage(state.InterestGroupsById.Keys, interestGroups, "interestGroups");
            ValidateExactCoverage(state.MovementsById.Keys, movements, "movements");

            List<TargetPath> targets = new List<TargetPath>();
            for (int i = 0; i < InitialTargetRegistry.Metrics.Count; i++)
            {
                TargetPath target = InitialTargetRegistry.Metrics[i];
                EnsureStateContainsTarget(state, target);
                targets.Add(target);
            }

            for (int i = 0; i < regions.Count; i++)
            {
                string id = regions[i];
                AddVisibleTarget(state, targets, InitialTargetRegistry.RegionSupport(id));
                AddVisibleTarget(state, targets, InitialTargetRegistry.RegionTension(id));
                AddVisibleTarget(state, targets, InitialTargetRegistry.RegionOrganization(id));
                AddVisibleTarget(state, targets, InitialTargetRegistry.RegionRivalPresence(id));
            }

            for (int i = 0; i < interestGroups.Count; i++)
            {
                string id = interestGroups[i];
                AddVisibleTarget(state, targets, InitialTargetRegistry.InterestGroupClout(id));
                AddVisibleTarget(state, targets, InitialTargetRegistry.InterestGroupApproval(id));
            }

            for (int i = 0; i < movements.Count; i++)
            {
                string id = movements[i];
                AddVisibleTarget(state, targets, InitialTargetRegistry.MovementIntensity(id));
                AddVisibleTarget(state, targets, InitialTargetRegistry.MovementDirection(id));
            }

            return new VisibleTargetCatalog(targets);
        }

        public static VisibleTargetCatalog CreateCanonicalFromState(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return CreateForMvp(
                state,
                SnapshotAndSortIds(state.RegionsById.Keys, "regions"),
                SnapshotAndSortIds(state.InterestGroupsById.Keys, "interestGroups"),
                SnapshotAndSortIds(state.MovementsById.Keys, "movements"));
        }

        public bool IsVisible(TargetPath target)
        {
            return target.IsValid && _visibleTargets.Contains(target);
        }

        public void RequireVisible(TargetPath target)
        {
            if (!IsVisible(target))
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.NonVisibleTarget,
                    target.IsValid ? target.ToString() : "<invalid>",
                    null,
                    "Target is not part of the visible MVP causal surface.");
            }
        }

        private static void AddVisibleTarget(GameState state, List<TargetPath> targets, TargetPath target)
        {
            EnsureStateContainsTarget(state, target);
            targets.Add(target);
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
                if (string.IsNullOrEmpty(value))
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidTargetOrder,
                        null,
                        null,
                        "Visible target ID lists cannot contain null or empty values.");
                }

                if (!seen.Add(value))
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidTargetOrder,
                        value,
                        null,
                        "Visible target ID lists cannot contain duplicates.");
                }

                snapshot.Add(value);
            }

            return Array.AsReadOnly(snapshot.ToArray());
        }

        private static void ValidateExactCoverage(IEnumerable<string> stateIds, IReadOnlyList<string> orderedIds, string family)
        {
            HashSet<string> expected = new HashSet<string>(stateIds, StringComparer.Ordinal);
            if (expected.Count != orderedIds.Count)
            {
                throw new CausalLedgerException(
                    CausalLedgerErrorCodes.InvalidTargetOrder,
                    family,
                    null,
                    "Visible target catalog IDs must cover the exact state set without omissions or extras.");
            }

            for (int i = 0; i < orderedIds.Count; i++)
            {
                if (!expected.Remove(orderedIds[i]))
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidTargetOrder,
                        orderedIds[i],
                        null,
                        "Visible target catalog contains an ID that does not exist in the current GameState.");
                }
            }

            if (expected.Count > 0)
            {
                foreach (string missing in expected)
                {
                    throw new CausalLedgerException(
                        CausalLedgerErrorCodes.InvalidTargetOrder,
                        missing,
                        null,
                        "Visible target catalog omitted a GameState ID.");
                }
            }
        }

        private static List<string> ProjectIds<T>(IReadOnlyList<T> values, Func<T, string> selector)
        {
            List<string> result = new List<string>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                result.Add(selector(values[i]));
            }

            return result;
        }

        private static IReadOnlyList<string> SnapshotAndSortIds(IEnumerable<string> values, string family)
        {
            List<string> snapshot = new List<string>(SnapshotIds(values, family));
            snapshot.Sort(StringComparer.Ordinal);
            return Array.AsReadOnly(snapshot.ToArray());
        }

        private static void EnsureStateContainsTarget(GameState state, TargetPath target)
        {
            if (target.Namespace == "metrics")
            {
                if (state.MetricsById.ContainsKey(target[1]))
                {
                    return;
                }
            }
            else if (target.Namespace == "regions")
            {
                if (state.RegionsById.ContainsKey(target[1])
                    && (target[2] == "support" || target[2] == "tension" || target[2] == "organization" || target[2] == "rival_presence"))
                {
                    return;
                }
            }
            else if (target.Namespace == "igs")
            {
                if (state.InterestGroupsById.ContainsKey(target[1])
                    && (target[2] == "clout" || target[2] == "approval"))
                {
                    return;
                }
            }
            else if (target.Namespace == "movements")
            {
                if (state.MovementsById.ContainsKey(target[1])
                    && (target[2] == "intensity" || target[2] == "direction"))
                {
                    return;
                }
            }

            throw new CausalLedgerException(
                CausalLedgerErrorCodes.NonVisibleTarget,
                target.ToString(),
                null,
                "Visible target catalog cannot include a target that is missing from the current GameState.");
        }
    }
}
