using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using VictoriantChile.Content.Models;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Content.State
{
    public sealed class GameStateFactory
    {
        public const string CloutNormalizeGroup = "igs.clout_sum_100";

        private static readonly Regex CanonicalHashPattern = new Regex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        public StateInitializationResult CreateInitialState(ContentPack pack, int rngSeed)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            List<StateInitializationDiagnostic> diagnostics = new List<StateInitializationDiagnostic>();
            GameStateContentMetadata metadata = BuildMetadata(pack.Manifest, diagnostics);
            List<MetricState> metrics = BuildMetrics(pack, diagnostics);
            List<InternalDomainState> internals = BuildInternals(pack, diagnostics);
            List<RegionState> regions = BuildRegions(pack, diagnostics);
            List<InterestGroupState> interestGroups = BuildInterestGroups(pack, diagnostics);
            List<MovementState> movements = BuildMovements(pack, diagnostics);

            if (diagnostics.Count > 0)
            {
                return StateInitializationResult.Failed(diagnostics);
            }

            try
            {
                return StateInitializationResult.Succeeded(new GameState(
                    rngSeed,
                    metadata,
                    metrics,
                    internals,
                    regions,
                    interestGroups,
                    movements));
            }
            catch (ArgumentException)
            {
                diagnostics.Add(new StateInitializationDiagnostic(
                    StateInitializationDiagnosticCode.IncompatibleStateInvariant,
                    "$.state",
                    "State invariant failed while constructing the initial state."));
            }
            catch (OverflowException)
            {
                diagnostics.Add(new StateInitializationDiagnostic(
                    StateInitializationDiagnosticCode.IncompatibleStateInvariant,
                    "$.state",
                    "State arithmetic overflow while constructing the initial state."));
            }

            return StateInitializationResult.Failed(diagnostics);
        }

        private static GameStateContentMetadata BuildMetadata(ContentManifest manifest, List<StateInitializationDiagnostic> diagnostics)
        {
            if (manifest.ContentPackVersion <= 0)
            {
                diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.InvalidMetadata, "$.manifest.content_pack_version", "Content pack version must be positive."));
            }

            if (manifest.ContentSchemaVersion <= 0)
            {
                diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.InvalidMetadata, "$.manifest.content_schema_version", "Content schema version must be positive."));
            }

            if (manifest.MinGameSchemaVersion <= 0)
            {
                diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.InvalidMetadata, "$.manifest.min_game_schema_version", "Minimum game schema version must be positive."));
            }

            if (string.IsNullOrEmpty(manifest.DefaultLanguage))
            {
                diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.InvalidMetadata, "$.manifest.default_language", "Default language is required."));
            }

            List<ContentFileIdentity> files = new List<ContentFileIdentity>();
            foreach (KeyValuePair<string, string> file in manifest.Files)
            {
                if (string.IsNullOrEmpty(file.Key) || string.IsNullOrEmpty(file.Value) || !CanonicalHashPattern.IsMatch(file.Value))
                {
                    diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.InvalidMetadata, "$.manifest.files", "Manifest file identities must include a relative path and canonical hash."));
                    continue;
                }

                files.Add(new ContentFileIdentity(file.Key, file.Value));
            }

            if (files.Count == 0)
            {
                diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.InvalidMetadata, "$.manifest.files", "At least one manifest file identity is required."));
            }

            if (diagnostics.Count > 0)
            {
                return null;
            }

            return new GameStateContentMetadata(
                manifest.ContentPackVersion,
                manifest.ContentSchemaVersion,
                manifest.MinGameSchemaVersion,
                manifest.DefaultLanguage,
                files);
        }

        private static List<MetricState> BuildMetrics(ContentPack pack, List<StateInitializationDiagnostic> diagnostics)
        {
            List<MetricState> metrics = new List<MetricState>();
            foreach (TargetPath path in InitialTargetRegistry.Metrics)
            {
                int value = ResolveDefault(pack, path, diagnostics);
                metrics.Add(new MetricState(path[1], value));
            }

            return metrics;
        }

        private static List<InternalDomainState> BuildInternals(ContentPack pack, List<StateInitializationDiagnostic> diagnostics)
        {
            Dictionary<string, List<InternalValueState>> byDomain = new Dictionary<string, List<InternalValueState>>(StringComparer.Ordinal);
            foreach (TargetPath path in InitialTargetRegistry.Internals)
            {
                int value = ResolveDefault(pack, path, diagnostics);
                string domain = path[1];
                if (!byDomain.TryGetValue(domain, out List<InternalValueState> components))
                {
                    components = new List<InternalValueState>();
                    byDomain.Add(domain, components);
                }

                components.Add(new InternalValueState(path[2], value));
            }

            List<string> domains = new List<string>(byDomain.Keys);
            domains.Sort(StringComparer.Ordinal);
            List<InternalDomainState> result = new List<InternalDomainState>();
            for (int i = 0; i < domains.Count; i++)
            {
                result.Add(new InternalDomainState(domains[i], byDomain[domains[i]]));
            }

            return result;
        }

        private static List<RegionState> BuildRegions(ContentPack pack, List<StateInitializationDiagnostic> diagnostics)
        {
            List<RegionDefinition> definitions = SortById(pack.Regions, region => region.Id);
            List<RegionState> regions = new List<RegionState>();
            for (int i = 0; i < definitions.Count; i++)
            {
                string id = definitions[i].Id;
                regions.Add(new RegionState(
                    id,
                    ResolveDefault(pack, InitialTargetRegistry.RegionSupport(id), diagnostics),
                    ResolveDefault(pack, InitialTargetRegistry.RegionTension(id), diagnostics),
                    ResolveDefault(pack, InitialTargetRegistry.RegionOrganization(id), diagnostics),
                    ResolveDefault(pack, InitialTargetRegistry.RegionRivalPresence(id), diagnostics)));
            }

            return regions;
        }

        private static List<InterestGroupState> BuildInterestGroups(ContentPack pack, List<StateInitializationDiagnostic> diagnostics)
        {
            List<InterestGroupDefinition> definitions = SortById(pack.InterestGroups, ig => ig.Id);
            List<InterestGroupCloutValue> rawClout = new List<InterestGroupCloutValue>();
            Dictionary<string, int> approvals = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < definitions.Count; i++)
            {
                string id = definitions[i].Id;
                TargetPath cloutPath = InitialTargetRegistry.InterestGroupClout(id);
                TargetConfig cloutConfig = ResolveConfig(pack, cloutPath, diagnostics);
                int cloutDefault = ResolveDefault(cloutPath, cloutConfig, diagnostics);
                if (cloutConfig != null && !string.Equals(cloutConfig.NormalizeGroup, CloutNormalizeGroup, StringComparison.Ordinal))
                {
                    diagnostics.Add(new StateInitializationDiagnostic(
                        StateInitializationDiagnosticCode.InvalidNormalizeGroup,
                        cloutPath.ToString(),
                        "IG clout requires normalize_group igs.clout_sum_100."));
                }

                rawClout.Add(new InterestGroupCloutValue(id, cloutDefault));
                approvals.Add(id, ResolveDefault(pack, InitialTargetRegistry.InterestGroupApproval(id), diagnostics));
            }

            IReadOnlyList<InterestGroupCloutValue> normalized = null;
            try
            {
                normalized = CloutNormalizer.Normalize(rawClout);
            }
            catch (ArgumentException)
            {
                diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.CloutNormalizationFailed, "igs.*.clout", "IG clout normalization failed."));
            }
            catch (OverflowException)
            {
                diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.CloutNormalizationFailed, "igs.*.clout", "IG clout normalization overflowed."));
            }

            List<InterestGroupState> result = new List<InterestGroupState>();
            if (normalized != null)
            {
                for (int i = 0; i < normalized.Count; i++)
                {
                    InterestGroupCloutValue clout = normalized[i];
                    result.Add(new InterestGroupState(clout.InterestGroupId, clout.CloutS, approvals[clout.InterestGroupId]));
                }
            }

            return result;
        }

        private static List<MovementState> BuildMovements(ContentPack pack, List<StateInitializationDiagnostic> diagnostics)
        {
            List<MovementDefinition> definitions = SortById(pack.Movements, movement => movement.Id);
            List<MovementState> movements = new List<MovementState>();
            for (int i = 0; i < definitions.Count; i++)
            {
                string id = definitions[i].Id;
                movements.Add(new MovementState(
                    id,
                    ResolveDefault(pack, InitialTargetRegistry.MovementIntensity(id), diagnostics),
                    ResolveDefault(pack, InitialTargetRegistry.MovementDirection(id), diagnostics)));
            }

            return movements;
        }

        private static int ResolveDefault(ContentPack pack, TargetPath path, List<StateInitializationDiagnostic> diagnostics)
        {
            return ResolveDefault(path, ResolveConfig(pack, path, diagnostics), diagnostics);
        }

        private static TargetConfig ResolveConfig(ContentPack pack, TargetPath path, List<StateInitializationDiagnostic> diagnostics)
        {
            if (!pack.TargetConfigCatalog.TryResolve(path, out TargetConfig config))
            {
                diagnostics.Add(new StateInitializationDiagnostic(
                    StateInitializationDiagnosticCode.MissingTargetConfig,
                    path.ToString(),
                    "No TargetConfig matches required initial state target."));
            }

            return config;
        }

        private static int ResolveDefault(TargetPath path, TargetConfig config, List<StateInitializationDiagnostic> diagnostics)
        {
            if (config == null)
            {
                return 0;
            }

            if (config.DefaultS < config.MinS || config.DefaultS > config.MaxS)
            {
                diagnostics.Add(new StateInitializationDiagnostic(
                    StateInitializationDiagnosticCode.DefaultOutOfRange,
                    path.ToString(),
                    "TargetConfig default is outside its configured range."));
            }

            return config.DefaultS;
        }

        private static List<T> SortById<T>(IEnumerable<T> values, Func<T, string> keySelector)
        {
            List<T> result = new List<T>(values);
            result.Sort((left, right) => string.Compare(keySelector(left), keySelector(right), StringComparison.Ordinal));
            return result;
        }
    }
}
