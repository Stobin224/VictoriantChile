using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using VictoriantChile.Content.Loading;
using VictoriantChile.Content.Models;
using VictoriantChile.Content.State;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class GameStateFactoryTests
    {
        [Test]
        public void RealContentPackCreatesCanonicalInitialState()
        {
            GameState state = CreateRealState(12345);

            Assert.That(state.StateSchemaVersion, Is.EqualTo(2));
            Assert.That(state.Tick, Is.EqualTo(0));
            Assert.That(state.RngSeed, Is.EqualTo(12345));
            Assert.That(state.Metrics.Count, Is.EqualTo(10));
            Assert.That(TotalInternalComponents(state), Is.EqualTo(38));
            Assert.That(state.Regions.Count, Is.EqualTo(16));
            Assert.That(state.InterestGroups.Count, Is.EqualTo(9));
            Assert.That(state.Movements.Count, Is.EqualTo(9));
            Assert.That(state.ActiveEffects, Is.Empty);
            AssertAllMetrics(state, 5000);
            AssertAllRegions(state, 5000);
            AssertAllInternals(state, 5000);
            AssertAllApprovals(state, 0);
            AssertAllMovements(state, 0, 1);
            Assert.That(SumClout(state), Is.EqualTo(10000));
            Assert.That(state.InterestGroupsById["ig_ambiental_regionalista"].CloutS, Is.EqualTo(1112));
            Assert.That(CountClout(state, 1111), Is.EqualTo(8));
        }

        [Test]
        public void MetadataCapturesContentIdentityWithoutPhysicalLocation()
        {
            ContentPack pack = LoadRealPack();
            GameState state = CreateState(pack, 7);

            Assert.That(state.ContentMetadata.ContentPackVersion, Is.EqualTo(3));
            Assert.That(state.ContentMetadata.ContentSchemaVersion, Is.EqualTo(1));
            Assert.That(state.ContentMetadata.MinimumGameSchemaVersion, Is.EqualTo(1));
            Assert.That(state.ContentMetadata.DefaultLanguage, Is.EqualTo("es"));
            Assert.That(state.ContentMetadata.Files.Count, Is.EqualTo(10));
            for (int i = 0; i < state.ContentMetadata.Files.Count; i++)
            {
                ContentFileIdentity identity = state.ContentMetadata.Files[i];
                Assert.That(pack.Manifest.Files[identity.RelativePath], Is.EqualTo(identity.CanonicalHash));
                Assert.That(identity.RelativePath, Does.Not.Contain(":"));
                Assert.That(identity.RelativePath, Does.Not.Contain("\\"));
                Assert.That(identity.CanonicalHash, Does.Match("^sha256:[0-9a-f]{64}$"));
            }
        }

        [Test]
        public void RegionStateDoesNotCopyStaticRegionResources()
        {
            string[] forbidden =
            {
                "AdminCapS",
                "IndustryCapS",
                "ExtractiveCapS",
                "SocialCapS",
                "PopulationS"
            };

            for (int i = 0; i < forbidden.Length; i++)
            {
                Assert.That(typeof(RegionState).GetProperty(forbidden[i]), Is.Null);
            }
        }

        [Test]
        public void FactoryIsDeterministicAndCultureIndependent()
        {
            ContentPack pack = LoadRealPack();
            CultureInfo oldCulture = CultureInfo.CurrentCulture;
            CultureInfo oldUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
                string first = Snapshot(CreateState(pack, 11));
                string second = Snapshot(CreateState(pack, 11));
                string differentSeed = Snapshot(CreateState(pack, 12));

                Assert.That(first, Is.EqualTo(second));
                Assert.That(differentSeed, Is.Not.EqualTo(first));
                Assert.That(differentSeed.Replace("seed=12", "seed=11"), Is.EqualTo(first));
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCulture;
                CultureInfo.CurrentUICulture = oldUiCulture;
            }
        }

        [Test]
        public void PublicCollectionsAreReadOnlySnapshots()
        {
            List<MetricState> metrics = new List<MetricState>
            {
                new MetricState("zeta", 1),
                new MetricState("alpha", 2)
            };
            List<InternalValueState> components = new List<InternalValueState>
            {
                new InternalValueState("component", 3)
            };
            List<InternalDomainState> internals = new List<InternalDomainState>
            {
                new InternalDomainState("domain", components)
            };
            List<ContentFileIdentity> files = new List<ContentFileIdentity>
            {
                new ContentFileIdentity("a.json", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
            };
            GameState state = new GameState(
                1,
                new GameStateContentMetadata(1, 1, 1, "es", files),
                metrics,
                internals,
                new[] { new RegionState("region", 4, 5, 6, 7) },
                new[] { new InterestGroupState("ig", 8, 9) },
                new[] { new MovementState("mov", 10, 1) });

            metrics.Add(new MetricState("beta", 99));
            components.Add(new InternalValueState("other", 99));
            internals.Add(new InternalDomainState("other", new[] { new InternalValueState("component", 99) }));
            files.Add(new ContentFileIdentity("b.json", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));

            Assert.That(state.Metrics.Count, Is.EqualTo(2));
            Assert.That(state.Metrics[0].MetricId, Is.EqualTo("alpha"));
            Assert.That(state.Internals.Count, Is.EqualTo(1));
            Assert.That(state.Internals[0].Components.Count, Is.EqualTo(1));
            Assert.That(state.ContentMetadata.Files.Count, Is.EqualTo(1));
            Assert.That(state.ActiveEffects.Count, Is.EqualTo(0));

            AssertReadOnlyList(state.Metrics, new MetricState("gamma", 3));
            AssertReadOnlyDictionary(state.MetricsById, "gamma", new MetricState("gamma", 3));
            AssertReadOnlyList(state.Internals, new InternalDomainState("gamma", new[] { new InternalValueState("component", 1) }));
            AssertReadOnlyDictionary(state.InternalsByDomain, "gamma", new InternalDomainState("gamma", new[] { new InternalValueState("component", 1) }));
            AssertReadOnlyList(state.Internals[0].Components, new InternalValueState("gamma", 3));
            AssertReadOnlyDictionary(state.Internals[0].ComponentsById, "gamma", new InternalValueState("gamma", 3));
            AssertReadOnlyList(state.Regions, new RegionState("gamma", 1, 1, 1, 1));
            AssertReadOnlyDictionary(state.RegionsById, "gamma", new RegionState("gamma", 1, 1, 1, 1));
            AssertReadOnlyList(state.InterestGroups, new InterestGroupState("gamma", 1, 1));
            AssertReadOnlyDictionary(state.InterestGroupsById, "gamma", new InterestGroupState("gamma", 1, 1));
            AssertReadOnlyList(state.Movements, new MovementState("gamma", 1, 1));
            AssertReadOnlyDictionary(state.MovementsById, "gamma", new MovementState("gamma", 1, 1));
            AssertReadOnlyList(state.ActiveEffects, new VictoriantChile.Simulation.Core.Effects.EffectInstance(
                "gamma",
                "eff_gamma",
                new VictoriantChile.Simulation.Core.Causality.CauseRef(VictoriantChile.Simulation.Core.Causality.CauseCategory.Decision, "decision_gamma"),
                0,
                1,
                "gamma.k",
                VictoriantChile.Simulation.Core.Effects.EffectStackMode.Stack,
                null,
                0));
            AssertReadOnlyList(state.ContentMetadata.Files, new ContentFileIdentity("gamma.json", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"));
        }

        [Test]
        public void InitialTargetRegistryIsClosedCanonicalAndResolvable()
        {
            ContentPack pack = LoadRealPack();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            Assert.That(InitialTargetRegistry.Metrics.Count, Is.EqualTo(10));
            Assert.That(InitialTargetRegistry.Internals.Count, Is.EqualTo(38));
            AssertReadOnlyList(InitialTargetRegistry.Metrics, TargetPath.Parse("metrics.extra"));
            AssertReadOnlyList(InitialTargetRegistry.Internals, TargetPath.Parse("internals.extra.value"));

            for (int i = 0; i < InitialTargetRegistry.Metrics.Count; i++)
            {
                TargetPath path = InitialTargetRegistry.Metrics[i];
                Assert.That(TargetPath.Parse(path.ToString()), Is.EqualTo(path));
                Assert.That(seen.Add(path.ToString()), Is.True, path.ToString());
                Assert.That(pack.TargetConfigCatalog.TryResolve(path, out TargetConfig config), Is.True, path.ToString());
                Assert.That(config.DefaultS, Is.EqualTo(5000), path.ToString());
            }

            for (int i = 0; i < InitialTargetRegistry.Internals.Count; i++)
            {
                TargetPath path = InitialTargetRegistry.Internals[i];
                Assert.That(TargetPath.Parse(path.ToString()), Is.EqualTo(path));
                Assert.That(seen.Add(path.ToString()), Is.True, path.ToString());
                Assert.That(pack.TargetConfigCatalog.TryResolve(path, out TargetConfig config), Is.True, path.ToString());
                Assert.That(config.DefaultS, Is.EqualTo(5000), path.ToString());
            }
        }

        [Test]
        public void StateInitializationResultEnforcesInvariantsAndReadOnlyDiagnostics()
        {
            GameState state = CreateRealState(1);
            StateInitializationResult success = StateInitializationResult.Succeeded(state);
            Assert.That(success.Success, Is.True);
            Assert.That(success.State, Is.SameAs(state));
            Assert.That(success.Diagnostics, Is.Empty);
            AssertReadOnlyList(success.Diagnostics, new StateInitializationDiagnostic(StateInitializationDiagnosticCode.InvalidMetadata, "$.x", "x"));

            StateInitializationDiagnostic diagnostic = new StateInitializationDiagnostic(StateInitializationDiagnosticCode.InvalidMetadata, "$.x", "x");
            List<StateInitializationDiagnostic> diagnostics = new List<StateInitializationDiagnostic> { diagnostic };
            StateInitializationResult failed = StateInitializationResult.Failed(diagnostics);
            diagnostics.Add(new StateInitializationDiagnostic(StateInitializationDiagnosticCode.MissingTargetConfig, "$.y", "y"));

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.State, Is.Null);
            Assert.That(failed.Diagnostics.Count, Is.EqualTo(1));
            AssertReadOnlyList(failed.Diagnostics, diagnostic);
            Assert.Throws<ArgumentException>(() => StateInitializationResult.Failed(new StateInitializationDiagnostic[0]));
        }

        [Test]
        public void MissingRequiredTargetConfigFailsClosed()
        {
            ContentPack pack = WithTargetConfigs(LoadRealPack(), RemovePattern(LoadRealPack().TargetConfigs, "internals.*.*"));

            StateInitializationResult result = new GameStateFactory().CreateInitialState(pack, 1);

            AssertFailure(result, StateInitializationDiagnosticCode.MissingTargetConfig);
        }

        [Test]
        public void IncorrectCloutNormalizeGroupFailsClosed()
        {
            ContentPack realPack = LoadRealPack();
            ContentPack pack = WithTargetConfigs(realPack, ReplaceCloutConfig(realPack.TargetConfigs, defaultS: 1111, normalizeGroup: "igs.bad_sum"));

            StateInitializationResult result = new GameStateFactory().CreateInitialState(pack, 1);

            AssertFailure(result, StateInitializationDiagnosticCode.InvalidNormalizeGroup);
        }

        [Test]
        public void MissingCloutNormalizeGroupFailsClosed()
        {
            ContentPack realPack = LoadRealPack();
            ContentPack pack = WithTargetConfigs(realPack, ReplaceCloutConfig(realPack.TargetConfigs, defaultS: 1111, normalizeGroup: null));

            StateInitializationResult result = new GameStateFactory().CreateInitialState(pack, 1);

            AssertFailure(result, StateInitializationDiagnosticCode.InvalidNormalizeGroup);
        }

        [Test]
        public void ZeroTotalCloutFailsClosed()
        {
            ContentPack realPack = LoadRealPack();
            ContentPack pack = WithTargetConfigs(realPack, ReplaceCloutConfig(realPack.TargetConfigs, defaultS: 0, normalizeGroup: GameStateFactory.CloutNormalizeGroup));

            StateInitializationResult result = new GameStateFactory().CreateInitialState(pack, 1);

            AssertFailure(result, StateInitializationDiagnosticCode.CloutNormalizationFailed);
        }

        [Test]
        public void InvalidMetadataFailsClosed()
        {
            ContentPack realPack = LoadRealPack();
            ContentManifest manifest = new ContentManifest("pack", 0, 1, 1, "es", new[] { "es" }, new KeyValuePair<string, string>[0]);
            ContentPack pack = new ContentPack(manifest, realPack.TargetConfigs, realPack.Regions, realPack.InterestGroups, realPack.Movements);

            StateInitializationResult result = new GameStateFactory().CreateInitialState(pack, 1);

            AssertFailure(result, StateInitializationDiagnosticCode.InvalidMetadata);
        }

        [Test]
        public void DuplicateStateIdsAreRejectedByStateConstructors()
        {
            GameStateContentMetadata metadata = new GameStateContentMetadata(
                1,
                1,
                1,
                "es",
                new[] { new ContentFileIdentity("a.json", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") });

            Assert.Throws<ArgumentException>(() => new GameState(
                1,
                metadata,
                new[] { new MetricState("metric", 1), new MetricState("metric", 2) },
                new[] { new InternalDomainState("domain", new[] { new InternalValueState("component", 1) }) },
                new[] { new RegionState("region", 1, 1, 1, 1) },
                new[] { new InterestGroupState("ig", 1, 1) },
                new[] { new MovementState("mov", 1, 1) }));

            Assert.Throws<ArgumentException>(() => new InternalDomainState(
                "domain",
                new[] { new InternalValueState("component", 1), new InternalValueState("component", 2) }));
        }

        [Test]
        public void DiagnosticsAreDeterministicAndNoPartialStateEscapes()
        {
            ContentPack realPack = LoadRealPack();
            ContentPack pack = WithTargetConfigs(realPack, ReplaceCloutConfig(RemovePattern(realPack.TargetConfigs, "internals.*.*"), defaultS: 0, normalizeGroup: "igs.bad_sum"));

            StateInitializationResult first = new GameStateFactory().CreateInitialState(pack, 1);
            StateInitializationResult second = new GameStateFactory().CreateInitialState(pack, 1);

            Assert.That(first.Success, Is.False);
            Assert.That(first.State, Is.Null);
            Assert.That(DiagnosticSnapshot(first), Is.EqualTo(DiagnosticSnapshot(second)));
            Assert.That(first.Diagnostics.Count, Is.GreaterThan(1));
        }

        private static GameState CreateRealState(int seed)
        {
            return CreateState(LoadRealPack(), seed);
        }

        private static GameState CreateState(ContentPack pack, int seed)
        {
            StateInitializationResult result = new GameStateFactory().CreateInitialState(pack, seed);
            Assert.That(result.Success, Is.True, DiagnosticSnapshot(result));
            Assert.That(result.State, Is.Not.Null);
            return result.State;
        }

        private static ContentPack LoadRealPack()
        {
            ContentLoadResult result = new ContentPackLoader().Load(new DirectoryContentFileSource(ContentRoot()));
            Assert.That(result.IsSuccess, Is.True);
            return result.Pack;
        }

        private static string ContentRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Assets", "StreamingAssets", "content"));
        }

        private static ContentPack WithTargetConfigs(ContentPack pack, IEnumerable<TargetConfig> targetConfigs)
        {
            return new ContentPack(pack.Manifest, targetConfigs, pack.Regions, pack.InterestGroups, pack.Movements);
        }

        private static List<TargetConfig> RemovePattern(IEnumerable<TargetConfig> configs, string pattern)
        {
            List<TargetConfig> result = new List<TargetConfig>();
            foreach (TargetConfig config in configs)
            {
                if (config.Pattern.ToString() != pattern)
                {
                    result.Add(config);
                }
            }

            return result;
        }

        private static List<TargetConfig> ReplaceCloutConfig(IEnumerable<TargetConfig> configs, int defaultS, string normalizeGroup)
        {
            List<TargetConfig> result = new List<TargetConfig>();
            foreach (TargetConfig config in configs)
            {
                if (config.Pattern.ToString() == "igs.*.clout")
                {
                    result.Add(new TargetConfig(config.Pattern, config.Scale, config.MinS, config.MaxS, defaultS, config.AllowedOperations, normalizeGroup));
                }
                else
                {
                    result.Add(config);
                }
            }

            return result;
        }

        private static void AssertFailure(StateInitializationResult result, StateInitializationDiagnosticCode code)
        {
            Assert.That(result.Success, Is.False, DiagnosticSnapshot(result));
            Assert.That(result.State, Is.Null);
            Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(ContainsCode(result, code), Is.True, DiagnosticSnapshot(result));
            AssertReadOnlyList(result.Diagnostics, new StateInitializationDiagnostic(code, "$.extra", "extra"));
        }

        private static bool ContainsCode(StateInitializationResult result, StateInitializationDiagnosticCode code)
        {
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                if (result.Diagnostics[i].Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        private static int TotalInternalComponents(GameState state)
        {
            int total = 0;
            for (int i = 0; i < state.Internals.Count; i++)
            {
                total += state.Internals[i].Components.Count;
            }

            return total;
        }

        private static int SumClout(GameState state)
        {
            int total = 0;
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                total += state.InterestGroups[i].CloutS;
            }

            return total;
        }

        private static int CountClout(GameState state, int value)
        {
            int total = 0;
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                if (state.InterestGroups[i].CloutS == value)
                {
                    total++;
                }
            }

            return total;
        }

        private static void AssertAllMetrics(GameState state, int expected)
        {
            for (int i = 0; i < state.Metrics.Count; i++)
            {
                Assert.That(state.Metrics[i].ValueS, Is.EqualTo(expected), state.Metrics[i].MetricId);
            }
        }

        private static void AssertAllRegions(GameState state, int expected)
        {
            for (int i = 0; i < state.Regions.Count; i++)
            {
                RegionState region = state.Regions[i];
                Assert.That(region.SupportS, Is.EqualTo(expected), region.RegionId);
                Assert.That(region.TensionS, Is.EqualTo(expected), region.RegionId);
                Assert.That(region.OrganizationS, Is.EqualTo(expected), region.RegionId);
                Assert.That(region.RivalPresenceS, Is.EqualTo(expected), region.RegionId);
            }
        }

        private static void AssertAllInternals(GameState state, int expected)
        {
            for (int i = 0; i < state.Internals.Count; i++)
            {
                for (int j = 0; j < state.Internals[i].Components.Count; j++)
                {
                    Assert.That(state.Internals[i].Components[j].ValueS, Is.EqualTo(expected), state.Internals[i].Domain + "." + state.Internals[i].Components[j].ComponentId);
                }
            }
        }

        private static void AssertAllApprovals(GameState state, int expected)
        {
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                Assert.That(state.InterestGroups[i].ApprovalS, Is.EqualTo(expected), state.InterestGroups[i].InterestGroupId);
            }
        }

        private static void AssertAllMovements(GameState state, int intensity, int direction)
        {
            for (int i = 0; i < state.Movements.Count; i++)
            {
                Assert.That(state.Movements[i].IntensityS, Is.EqualTo(intensity), state.Movements[i].MovementId);
                Assert.That(state.Movements[i].Direction, Is.EqualTo(direction), state.Movements[i].MovementId);
            }
        }

        private static string Snapshot(GameState state)
        {
            List<string> parts = new List<string>
            {
                "schema=" + state.StateSchemaVersion,
                "tick=" + state.Tick,
                "seed=" + state.RngSeed,
                "files=" + state.ContentMetadata.Files.Count
            };

            for (int i = 0; i < state.Metrics.Count; i++)
            {
                parts.Add("metric:" + state.Metrics[i].MetricId + "=" + state.Metrics[i].ValueS);
            }

            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                parts.Add("ig:" + state.InterestGroups[i].InterestGroupId + "=" + state.InterestGroups[i].CloutS + "/" + state.InterestGroups[i].ApprovalS);
            }

            return string.Join("|", parts.ToArray());
        }

        private static string DiagnosticSnapshot(StateInitializationResult result)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                parts.Add(result.Diagnostics[i].ToString());
            }

            return string.Join("\n", parts.ToArray());
        }

        private static void AssertReadOnlyList<T>(IReadOnlyList<T> values, T sample)
        {
            IList<T> list = values as IList<T>;
            Assert.That(list, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => list.Add(sample));
            Assert.Throws<NotSupportedException>(() => list.Remove(sample));
            Assert.Throws<NotSupportedException>(() => list.Clear());
            if (list.Count > 0)
            {
                Assert.Throws<NotSupportedException>(() => list[0] = sample);
            }
        }

        private static void AssertReadOnlyDictionary<T>(IReadOnlyDictionary<string, T> values, string key, T sample)
        {
            IDictionary<string, T> dictionary = values as IDictionary<string, T>;
            Assert.That(dictionary, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => dictionary.Add(key, sample));
            Assert.Throws<NotSupportedException>(() => dictionary.Remove(key));
            Assert.Throws<NotSupportedException>(() => dictionary.Clear());
            if (dictionary.Count > 0)
            {
                string existingKey = null;
                foreach (string existing in dictionary.Keys)
                {
                    existingKey = existing;
                    break;
                }

                Assert.Throws<NotSupportedException>(() => dictionary[existingKey] = sample);
            }
        }
    }
}
