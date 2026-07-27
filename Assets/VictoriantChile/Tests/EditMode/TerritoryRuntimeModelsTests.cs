using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using NUnit.Framework;
using VictoriantChile.Content.Diagnostics;
using VictoriantChile.Content.Loading;
using VictoriantChile.Content.Models;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Targets;
using VictoriantChile.Simulation.Core.Territory;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class TerritoryRuntimeModelsTests
    {
        private static readonly string[] ExpectedRegions =
        {
            "arica_parinacota",
            "tarapaca",
            "antofagasta",
            "atacama",
            "coquimbo",
            "valparaiso",
            "metropolitana",
            "ohiggins",
            "maule",
            "nuble",
            "biobio",
            "araucania",
            "los_rios",
            "los_lagos",
            "aysen",
            "magallanes"
        };

        private static readonly TerritoryDynamicFieldRuntime[] ExpectedFields =
        {
            TerritoryDynamicFieldRuntime.Support,
            TerritoryDynamicFieldRuntime.Tension,
            TerritoryDynamicFieldRuntime.Organization,
            TerritoryDynamicFieldRuntime.RivalPresence
        };

        private static readonly string[] ExpectedFieldSegments =
        {
            "support",
            "tension",
            "organization",
            "rival_presence"
        };

        private static readonly string[] ExpectedPullIds =
        {
            "support_to_coalition_strength",
            "organization_to_field_ops",
            "tension_to_protest_activity",
            "rival_presence_to_opposition_obstruction",
            "tension_to_movement_salience"
        };

        private static readonly string[] ExpectedPullDestinations =
        {
            "internals.leg.coalition_strength",
            "internals.party.field_ops",
            "internals.tension.protest_activity",
            "internals.leg.opposition_obstruction",
            "internals.agenda.movement_salience"
        };

        private static readonly TerritoryDynamicFieldRuntime[] ExpectedPullSources =
        {
            TerritoryDynamicFieldRuntime.Support,
            TerritoryDynamicFieldRuntime.Organization,
            TerritoryDynamicFieldRuntime.Tension,
            TerritoryDynamicFieldRuntime.RivalPresence,
            TerritoryDynamicFieldRuntime.Tension
        };

        [Test]
        public void RealContentPackCompilesTerritoryRuntimePlan()
        {
            ContentLoadResult result = LoadRealPack();

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            TerritoryRuntimePlan plan = result.Pack.TerritoryRuntimePlan;
            Assert.That(plan, Is.Not.Null);
            AssertConstants(plan);
            AssertRegions(plan);
            AssertDriftBindings(plan);
            AssertPullBindings(plan);
            AssertLookups(plan);
        }

        [Test]
        public void StaticResourcesAreCopiedForEveryRegion()
        {
            TerritoryRuntimePlan plan = LoadRealPack().Pack.TerritoryRuntimePlan;

            for (int i = 0; i < plan.Regions.Count; i++)
            {
                TerritoryRegionRuntime region = plan.Regions[i];
                Assert.That(region.AdminCapS, Is.EqualTo(5000), region.RegionId);
                Assert.That(region.IndustryCapS, Is.EqualTo(5000), region.RegionId);
                Assert.That(region.ExtractiveCapS, Is.EqualTo(5000), region.RegionId);
                Assert.That(region.SocialCapS, Is.EqualTo(5000), region.RegionId);
                Assert.That(region.PopulationS, Is.EqualTo(5000), region.RegionId);
            }
        }

        [Test]
        public void TwoCompilationsProduceSameObservableProjection()
        {
            ContentPack pack = LoadRealPack().Pack;
            TerritoryRuntimePlan first = ContentPack.CompileTerritoryRuntimePlan(pack.Regions, pack.TargetConfigCatalog);
            TerritoryRuntimePlan second = ContentPack.CompileTerritoryRuntimePlan(pack.Regions, pack.TargetConfigCatalog);

            Assert.That(SerializePlan(second), Is.EqualTo(SerializePlan(first)));
        }

        [Test]
        public void ReorderedTargetConfigsDoNotChangePlan()
        {
            ContentPack pack = LoadRealPack().Pack;
            List<TargetConfig> reordered = new List<TargetConfig>(pack.TargetConfigs);
            reordered.Reverse();

            TerritoryRuntimePlan canonical = ContentPack.CompileTerritoryRuntimePlan(pack.Regions, pack.TargetConfigCatalog);
            TerritoryRuntimePlan fromReordered = ContentPack.CompileTerritoryRuntimePlan(pack.Regions, new TargetConfigCatalog(reordered));

            Assert.That(SerializePlan(fromReordered), Is.EqualTo(SerializePlan(canonical)));
            Assert.That(fromReordered.DriftBindings[0].OutputTarget.ToString(), Is.EqualTo("regions.arica_parinacota.support"));
            Assert.That(fromReordered.PullBindings[4].Destination.ToString(), Is.EqualTo("internals.agenda.movement_salience"));
        }

        [Test]
        public void SourceMutationAfterCompilationDoesNotChangePlan()
        {
            ContentPack pack = LoadRealPack().Pack;
            List<RegionDefinition> regions = new List<RegionDefinition>(pack.Regions);
            List<TargetConfig> configs = new List<TargetConfig>(pack.TargetConfigs);
            TerritoryRuntimePlan plan = ContentPack.CompileTerritoryRuntimePlan(regions, new TargetConfigCatalog(configs));
            string before = SerializePlan(plan);

            regions.Clear();
            regions.Add(new RegionDefinition("changed", "Changed", 1, RegionMacrozone.North, 1, 1, 1, 1, 1));
            regions.Reverse();
            regions[0] = new RegionDefinition("other", "Other", 1, RegionMacrozone.North, 2, 2, 2, 2, 2);
            configs.Clear();
            configs.Add(new TargetConfig(TargetPattern.Parse("metrics.*"), 1, 0, 1, 0, new[] { TargetOperation.Add }));

            Assert.That(SerializePlan(plan), Is.EqualTo(before));
            Assert.That(plan.Regions[0].RegionId, Is.EqualTo("arica_parinacota"));
            Assert.That(plan.DriftBindings[0].Cause.CanonicalKey, Is.EqualTo("SYSTEM:REG_DRIFT.regions.arica_parinacota.support"));
        }

        [Test]
        public void RuntimeCollectionsAreActuallyReadOnly()
        {
            TerritoryRuntimePlan plan = LoadRealPack().Pack.TerritoryRuntimePlan;

            Assert.That(plan.Regions, Is.InstanceOf<ReadOnlyCollection<TerritoryRegionRuntime>>());
            Assert.That(plan.DriftBindings, Is.InstanceOf<ReadOnlyCollection<TerritoryDriftBindingRuntime>>());
            Assert.That(plan.PullBindings, Is.InstanceOf<ReadOnlyCollection<TerritoryPullBindingRuntime>>());
            Assert.That(plan.RegionsById, Is.InstanceOf<ReadOnlyDictionary<string, TerritoryRegionRuntime>>());
            Assert.That(plan.DriftBindingsByOutput, Is.InstanceOf<ReadOnlyDictionary<TargetPath, TerritoryDriftBindingRuntime>>());
            Assert.That(plan.PullBindingsById, Is.InstanceOf<ReadOnlyDictionary<string, TerritoryPullBindingRuntime>>());
            Assert.That(plan.PullBindingsByDestination, Is.InstanceOf<ReadOnlyDictionary<TargetPath, TerritoryPullBindingRuntime>>());

            Assert.That(() => ((IList)plan.Regions).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IList)plan.DriftBindings).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IList)plan.PullBindings).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IList)plan.DriftBindings[0].Terms).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IDictionary<string, TerritoryRegionRuntime>)plan.RegionsById).Add("x", plan.Regions[0]), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IDictionary<TargetPath, TerritoryDriftBindingRuntime>)plan.DriftBindingsByOutput).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IDictionary<string, TerritoryPullBindingRuntime>)plan.PullBindingsById).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IDictionary<TargetPath, TerritoryPullBindingRuntime>)plan.PullBindingsByDestination).Clear(), Throws.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void CoreAssemblyDoesNotDependOnContentNewtonsoftOrUnityEngine()
        {
            string assets = AssetsRoot();
            string coreAsmdef = File.ReadAllText(Path.Combine(assets, "Simulation", "Core", "VictoriantChile.Simulation.Core.asmdef"));
            string contentAsmdef = File.ReadAllText(Path.Combine(assets, "Content", "VictoriantChile.Content.asmdef"));
            string testsAsmdef = File.ReadAllText(Path.Combine(assets, "Tests", "EditMode", "VictoriantChile.Simulation.Tests.EditMode.asmdef"));
            string territorySource = File.ReadAllText(Path.Combine(assets, "Simulation", "Core", "Territory", "TerritoryRuntimeModels.cs"));

            Assert.That(coreAsmdef, Does.Contain("\"references\": []"));
            Assert.That(coreAsmdef, Does.Contain("\"noEngineReferences\": true"));
            Assert.That(contentAsmdef, Does.Contain("\"Newtonsoft.Json\""));
            Assert.That(testsAsmdef, Does.Contain("TestAssemblies"));
            Assert.That(territorySource, Does.Not.Contain("VictoriantChile.Content"));
            Assert.That(territorySource, Does.Not.Contain("Newtonsoft"));
            Assert.That(territorySource, Does.Not.Contain("UnityEngine"));
            Assert.That(territorySource, Does.Not.Contain("JObject"));
            Assert.That(territorySource, Does.Not.Contain("JToken"));
            Assert.That(territorySource, Does.Not.Contain("float"));
            Assert.That(territorySource, Does.Not.Contain("double"));
            Assert.That(territorySource, Does.Not.Contain("decimal"));
        }

        [Test]
        public void LoaderMissingRegionFailsClosed()
        {
            ContentLoadResult result = Load(RebuildManifest(WithFile(ValidFixture(), "core/regions.json", RegionJsonWithoutLastAdjustedToOneMillion())));

            AssertFailure(result, ContentDiagnosticCode.TerritoryPlanInvalid, "core/regions.json", "$.regions");
        }

        [Test]
        public void LoaderUnknownRegionFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["core/regions.json"] = Bytes(Text(files["core/regions.json"]).Replace("\"id\": \"arica_parinacota\"", "\"id\": \"unknown_region\""));

            ContentLoadResult result = Load(RebuildManifest(files));

            AssertFailure(result, ContentDiagnosticCode.TerritoryPlanInvalid, "core/regions.json", "$.regions[0].id");
        }

        [Test]
        public void LoaderRegionalOrderMismatchFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            string text = Text(files["core/regions.json"])
                .Replace("\"id\": \"arica_parinacota\"", "\"id\": \"tarapaca_tmp\"")
                .Replace("\"id\": \"tarapaca\"", "\"id\": \"arica_parinacota\"")
                .Replace("\"id\": \"tarapaca_tmp\"", "\"id\": \"tarapaca\"");
            files["core/regions.json"] = Bytes(text);

            ContentLoadResult result = Load(RebuildManifest(files));

            AssertFailure(result, ContentDiagnosticCode.TerritoryPlanInvalid, "core/regions.json", "$.regions[0].id");
        }

        [Test]
        public void LoaderEmptyRegionsFailBeforePartialPack()
        {
            ContentLoadResult result = Load(RebuildManifest(WithFile(ValidFixture(), "core/regions.json", "{\"regions\":[]}")));

            Assert.That(result.IsSuccess, Is.False, Diagnostics(result));
            Assert.That(result.Pack, Is.Null);
            Assert.That(ContainsCode(result, ContentDiagnosticCode.InvalidValue), Is.True, Diagnostics(result));
        }

        [Test]
        public void LoaderDuplicateRegionFailsClosedBeforePartialPack()
        {
            string json = "{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":500000,\"macrozone\":\"CENTER\"},{\"id\":\"metropolitana\",\"name\":\"Metro 2\",\"weight_ppm\":500000,\"macrozone\":\"CENTER\"}]}";

            ContentLoadResult result = Load(RebuildManifest(WithFile(ValidFixture(), "core/regions.json", json)));

            Assert.That(result.IsSuccess, Is.False, Diagnostics(result));
            Assert.That(result.Pack, Is.Null);
            Assert.That(ContainsCode(result, ContentDiagnosticCode.DuplicateId), Is.True, Diagnostics(result));
        }

        [TestCase("\"weight_ppm\": 62500", "\"weight_ppm\": 0", ContentDiagnosticCode.TerritoryPlanInvalid, "$.regions[0].weight_ppm", TestName = "WeightNonPositiveFailsClosed")]
        [TestCase("\"weight_ppm\": 62500", "\"weight_ppm\": 62501", ContentDiagnosticCode.TerritoryPlanInvalid, "$.regions[0].weight_ppm", TestName = "WeightNotContractualFailsClosed")]
        [TestCase("\"populationS\": 5000", "\"populationS\": 10001", ContentDiagnosticCode.InvalidRange, "$.regions[0].populationS", TestName = "StaticResourceOutOfRangeFailsClosed")]
        public void LoaderRegionValueFailuresFailClosed(string oldText, string newText, ContentDiagnosticCode code, string expectedPath)
        {
            Dictionary<string, byte[]> files = ValidFixture();
            string regions = ReplaceFirst(Text(files["core/regions.json"]), oldText, newText);
            if (newText == "\"weight_ppm\": 0")
            {
                regions = ReplaceFirst(regions, "\"weight_ppm\": 62500", "\"weight_ppm\": 125000");
            }
            else if (newText == "\"weight_ppm\": 62501")
            {
                regions = ReplaceFirst(regions, "\"weight_ppm\": 62500", "\"weight_ppm\": 62499");
            }

            files["core/regions.json"] = Bytes(regions);

            ContentLoadResult result = Load(RebuildManifest(files));

            AssertFailure(result, code, "core/regions.json", expectedPath);
        }

        [Test]
        public void LoaderRegionWeightSumMismatchFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["core/regions.json"] = Bytes(Text(files["core/regions.json"]).Replace("\"weight_ppm\": 62500", "\"weight_ppm\": 62499"));

            ContentLoadResult result = Load(RebuildManifest(files));

            AssertFailure(result, ContentDiagnosticCode.RegionWeightTotalMismatch, "core/regions.json", "$.regions");
        }

        [TestCase("\"pattern\": \"regions.*.support\"", "\"pattern\": \"regions.*.missing\"", "$", TestName = "MissingRegionalFieldTargetConfigFailsClosed")]
        [TestCase("\"pattern\": \"regions.*.support\"", "\"pattern\": \"regions.*.support\", \"scale\": 101, \"x\":\"x\"", "$", TestName = "DomainOrScaleIncompatibleFailsClosed")]
        public void LoaderTargetConfigMissingOrIncompatibleFailsClosed(string oldText, string newText, string expectedPath)
        {
            Dictionary<string, byte[]> files = ValidFixture();
            string targetConfig = Text(files["rules/target_config.json"]);
            if (newText.IndexOf("\"x\"", StringComparison.Ordinal) >= 0)
            {
                targetConfig = targetConfig.Replace("\"pattern\": \"regions.*.support\",\n    \"scale\": 100", "\"pattern\": \"regions.*.support\",\n    \"scale\": 101");
            }
            else
            {
                targetConfig = targetConfig.Replace(oldText, newText);
            }

            files["rules/target_config.json"] = Bytes(targetConfig);

            ContentLoadResult result = Load(RebuildManifest(files));

            AssertFailure(result, ContentDiagnosticCode.TerritoryPlanInvalid, "rules/target_config.json", expectedPath);
        }

        [Test]
        public void LoaderSetNotAllowedFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["rules/target_config.json"] = Bytes(Text(files["rules/target_config.json"])
                .Replace("\"pattern\": \"regions.*.support\",\n    \"scale\": 100,\n    \"minS\": 0,\n    \"maxS\": 10000,\n    \"defaultS\": 5000,\n    \"allow_ops\": [\"ADD\", \"MUL\", \"SET\"]",
                    "\"pattern\": \"regions.*.support\",\n    \"scale\": 100,\n    \"minS\": 0,\n    \"maxS\": 10000,\n    \"defaultS\": 5000,\n    \"allow_ops\": [\"ADD\", \"MUL\"]"));

            ContentLoadResult result = Load(RebuildManifest(files));

            AssertFailure(result, ContentDiagnosticCode.TerritoryPlanInvalid, "rules/target_config.json", "$");
        }

        [Test]
        public void ProgrammaticCoreValidationCoversClosedBindingInvariants()
        {
            ContentPack pack = LoadRealPack().Pack;
            TerritoryRuntimePlan canonical = pack.TerritoryRuntimePlan;
            List<TerritoryRegionRuntime> regions = new List<TerritoryRegionRuntime>(canonical.Regions);
            List<TerritoryDriftBindingRuntime> drift = new List<TerritoryDriftBindingRuntime>(canonical.DriftBindings);
            List<TerritoryPullBindingRuntime> pull = new List<TerritoryPullBindingRuntime>(canonical.PullBindings);

            Assert.That(() => new TerritoryRuntimePlan(100, 5000, 1000000, 109101, 200, 6, 206299, 400, 1000000, null, drift, pull), Throws.ArgumentNullException);
            Assert.That(() => new TerritoryRuntimePlan(100, 5000, 1000000, 109101, 200, 6, 206299, 400, 1000000, regions, null, pull), Throws.ArgumentNullException);
            Assert.That(() => new TerritoryRuntimePlan(100, 5000, 1000000, 109101, 200, 6, 206299, 400, 1000000, regions, drift, null), Throws.ArgumentNullException);

            regions[0] = null;
            Assert.That(() => ClonePlan(regions, drift, pull), Throws.ArgumentException);
            regions = new List<TerritoryRegionRuntime>(canonical.Regions);
            drift[0] = null;
            Assert.That(() => ClonePlan(regions, drift, pull), Throws.ArgumentException);
            drift = new List<TerritoryDriftBindingRuntime>(canonical.DriftBindings);
            pull[0] = null;
            Assert.That(() => ClonePlan(regions, drift, pull), Throws.ArgumentException);

            drift = new List<TerritoryDriftBindingRuntime>(canonical.DriftBindings);
            drift[1] = drift[0];
            Assert.That(() => ClonePlan(regions, drift, pull), Throws.ArgumentException, "duplicate drift output and cause");

            drift = new List<TerritoryDriftBindingRuntime>(canonical.DriftBindings);
            drift.RemoveAt(63);
            Assert.That(() => ClonePlan(regions, drift, pull), Throws.ArgumentException, "missing drift binding");

            pull = new List<TerritoryPullBindingRuntime>(canonical.PullBindings);
            pull[1] = pull[0];
            Assert.That(() => ClonePlan(regions, new List<TerritoryDriftBindingRuntime>(canonical.DriftBindings), pull), Throws.ArgumentException, "duplicate pull binding and destination");

            pull = new List<TerritoryPullBindingRuntime>(canonical.PullBindings);
            pull.RemoveAt(4);
            Assert.That(() => ClonePlan(regions, new List<TerritoryDriftBindingRuntime>(canonical.DriftBindings), pull), Throws.ArgumentException, "missing pull binding");

            TargetConfig config = pack.TargetConfigCatalog.Resolve(TargetPath.Parse("internals.leg.coalition_strength"));
            Assert.That(() => new TerritoryPullBindingRuntime("bad_source", (TerritoryDynamicFieldRuntime)99, TargetPath.Parse("internals.leg.coalition_strength"), config, TerritoryCauseMaterializer.MaterializePull(TargetPath.Parse("internals.leg.coalition_strength"))), Throws.InstanceOf<ArgumentOutOfRangeException>());

            List<TargetConfig> noInternalDestination = RemovePattern(pack.TargetConfigs, "internals.*.*");
            Assert.That(() => ContentPack.CompileTerritoryRuntimePlan(pack.Regions, new TargetConfigCatalog(noInternalDestination)), Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void CauseRefsArePrecompiledAndDistinct()
        {
            TerritoryRuntimePlan plan = LoadRealPack().Pack.TerritoryRuntimePlan;
            HashSet<string> driftCauses = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> pullCauses = new HashSet<string>(StringComparer.Ordinal);

            for (int region = 0; region < ExpectedRegions.Length; region++)
            {
                for (int field = 0; field < ExpectedFieldSegments.Length; field++)
                {
                    int index = region * ExpectedFieldSegments.Length + field;
                    string expected = "SYSTEM:REG_DRIFT.regions." + ExpectedRegions[region] + "." + ExpectedFieldSegments[field];
                    Assert.That(plan.DriftBindings[index].Cause.CanonicalKey, Is.EqualTo(expected));
                    Assert.That(plan.DriftBindings[index].Cause.Category, Is.EqualTo(CauseCategory.System));
                    Assert.That(plan.DriftBindings[index].Cause.Parent, Is.Null);
                    Assert.That(driftCauses.Add(plan.DriftBindings[index].Cause.CanonicalKey), Is.True);
                }
            }

            for (int i = 0; i < ExpectedPullDestinations.Length; i++)
            {
                string expected = "SYSTEM:REG_TO_INT." + ExpectedPullDestinations[i];
                Assert.That(plan.PullBindings[i].Cause.CanonicalKey, Is.EqualTo(expected));
                Assert.That(plan.PullBindings[i].Cause.Category, Is.EqualTo(CauseCategory.System));
                Assert.That(plan.PullBindings[i].Cause.Parent, Is.Null);
                Assert.That(pullCauses.Add(plan.PullBindings[i].Cause.CanonicalKey), Is.True);
            }

            Assert.That(driftCauses.Count, Is.EqualTo(64));
            Assert.That(pullCauses.Count, Is.EqualTo(5));
        }

        private static TerritoryRuntimePlan ClonePlan(
            IReadOnlyList<TerritoryRegionRuntime> regions,
            IReadOnlyList<TerritoryDriftBindingRuntime> drift,
            IReadOnlyList<TerritoryPullBindingRuntime> pull)
        {
            return new TerritoryRuntimePlan(100, 5000, 1000000, 109101, 200, 6, 206299, 400, 1000000, regions, drift, pull);
        }

        private static void AssertConstants(TerritoryRuntimePlan plan)
        {
            Assert.That(plan.Scale, Is.EqualTo(100));
            Assert.That(plan.MidS, Is.EqualTo(5000));
            Assert.That(plan.DriftPpmDenominator, Is.EqualTo(1000000));
            Assert.That(plan.DriftAlphaPpmValue, Is.EqualTo(109101));
            Assert.That(plan.DriftCapPerWeekSValue, Is.EqualTo(200));
            Assert.That(plan.DriftHalfLifeWeeksMetadataValue, Is.EqualTo(6));
            Assert.That(plan.PullAlphaPpmValue, Is.EqualTo(206299));
            Assert.That(plan.PullCapPerWeekSValue, Is.EqualTo(400));
            Assert.That(plan.PullWeightedAverageDenominatorValue, Is.EqualTo(1000000));
        }

        private static void AssertRegions(TerritoryRuntimePlan plan)
        {
            Assert.That(plan.Regions.Count, Is.EqualTo(16));
            long sum = 0;
            for (int i = 0; i < ExpectedRegions.Length; i++)
            {
                Assert.That(plan.Regions[i].RegionId, Is.EqualTo(ExpectedRegions[i]));
                Assert.That(plan.Regions[i].WeightPpm, Is.EqualTo(62500));
                sum += plan.Regions[i].WeightPpm;
                Assert.That(plan.TryGetRegion(ExpectedRegions[i], out TerritoryRegionRuntime found), Is.True);
                Assert.That(found, Is.SameAs(plan.Regions[i]));
                Assert.That(plan.GetRegion(ExpectedRegions[i]), Is.SameAs(plan.Regions[i]));
            }

            Assert.That(sum, Is.EqualTo(1000000));
            Assert.That(plan.TryGetRegion("unknown", out TerritoryRegionRuntime absent), Is.False);
            Assert.That(absent, Is.Null);
        }

        private static void AssertDriftBindings(TerritoryRuntimePlan plan)
        {
            Assert.That(plan.DriftBindings.Count, Is.EqualTo(64));
            for (int region = 0; region < ExpectedRegions.Length; region++)
            {
                for (int field = 0; field < ExpectedFields.Length; field++)
                {
                    int index = region * ExpectedFields.Length + field;
                    TerritoryDriftBindingRuntime binding = plan.DriftBindings[index];
                    string target = "regions." + ExpectedRegions[region] + "." + ExpectedFieldSegments[field];
                    Assert.That(binding.RegionId, Is.EqualTo(ExpectedRegions[region]));
                    Assert.That(binding.Field, Is.EqualTo(ExpectedFields[field]));
                    Assert.That(binding.OutputTarget.ToString(), Is.EqualTo(target));
                    Assert.That(binding.OutputConfig.Allows(TargetOperation.Set), Is.True);
                    Assert.That(binding.OutputConfig.Scale, Is.EqualTo(100));
                    Assert.That(binding.OutputConfig.MinS, Is.EqualTo(0));
                    Assert.That(binding.OutputConfig.MaxS, Is.EqualTo(10000));
                    Assert.That(binding.OutputConfig.DefaultS, Is.EqualTo(5000));
                    Assert.That(plan.GetDriftBinding(TargetPath.Parse(target)), Is.SameAs(binding));
                }
            }

            AssertTerms(plan.DriftBindings[0], new[] { "metrics.legitimacy", "metrics.party_organization", "metrics.social_tension" }, new[] { TerritoryDriftTransformRuntime.ValueMinusMid, TerritoryDriftTransformRuntime.ValueMinusMid, TerritoryDriftTransformRuntime.ValueMinusMid }, new[] { 600000, 300000, -400000 });
            AssertTerms(plan.DriftBindings[1], new[] { "metrics.economy", "metrics.security", "metrics.public_agenda" }, new[] { TerritoryDriftTransformRuntime.MidMinusValue, TerritoryDriftTransformRuntime.MidMinusValue, TerritoryDriftTransformRuntime.ValueMinusMid }, new[] { 500000, 400000, 300000 });
            AssertTerms(plan.DriftBindings[2], new[] { "metrics.party_organization" }, new[] { TerritoryDriftTransformRuntime.ValueMinusMid }, new[] { 800000 });
            AssertTerms(plan.DriftBindings[3], new[] { "regions.arica_parinacota.support", "metrics.internal_cohesion" }, new[] { TerritoryDriftTransformRuntime.MidMinusValue, TerritoryDriftTransformRuntime.MidMinusValue }, new[] { 700000, 200000 });
        }

        private static void AssertPullBindings(TerritoryRuntimePlan plan)
        {
            Assert.That(plan.PullBindings.Count, Is.EqualTo(5));
            for (int i = 0; i < ExpectedPullIds.Length; i++)
            {
                TerritoryPullBindingRuntime binding = plan.PullBindings[i];
                Assert.That(binding.BindingId, Is.EqualTo(ExpectedPullIds[i]));
                Assert.That(binding.RegionalSource, Is.EqualTo(ExpectedPullSources[i]));
                Assert.That(binding.Destination.ToString(), Is.EqualTo(ExpectedPullDestinations[i]));
                Assert.That(binding.DestinationConfig.Allows(TargetOperation.Set), Is.True);
                Assert.That(binding.DestinationConfig.Scale, Is.EqualTo(100));
                Assert.That(binding.DestinationConfig.MinS, Is.EqualTo(0));
                Assert.That(binding.DestinationConfig.MaxS, Is.EqualTo(10000));
                Assert.That(binding.DestinationConfig.DefaultS, Is.EqualTo(5000));
            }
        }

        private static void AssertLookups(TerritoryRuntimePlan plan)
        {
            Assert.That(plan.RegionsById.Count, Is.EqualTo(16));
            Assert.That(plan.DriftBindingsByOutput.Count, Is.EqualTo(64));
            Assert.That(plan.PullBindingsById.Count, Is.EqualTo(5));
            Assert.That(plan.PullBindingsByDestination.Count, Is.EqualTo(5));

            for (int i = 0; i < ExpectedPullIds.Length; i++)
            {
                Assert.That(plan.TryGetPullBinding(ExpectedPullIds[i], out TerritoryPullBindingRuntime byId), Is.True);
                Assert.That(byId, Is.SameAs(plan.PullBindings[i]));
                Assert.That(plan.GetPullBinding(ExpectedPullIds[i]), Is.SameAs(plan.PullBindings[i]));
                TargetPath destination = TargetPath.Parse(ExpectedPullDestinations[i]);
                Assert.That(plan.TryGetPullBindingByDestination(destination, out TerritoryPullBindingRuntime byDestination), Is.True);
                Assert.That(byDestination, Is.SameAs(plan.PullBindings[i]));
                Assert.That(plan.GetPullBindingByDestination(destination), Is.SameAs(plan.PullBindings[i]));
            }

            Assert.That(plan.TryGetDriftBinding(default, out TerritoryDriftBindingRuntime absentDrift), Is.False);
            Assert.That(absentDrift, Is.Null);
            Assert.That(plan.TryGetPullBinding("absent", out TerritoryPullBindingRuntime absentPull), Is.False);
            Assert.That(absentPull, Is.Null);
            Assert.That(plan.TryGetPullBindingByDestination(TargetPath.Parse("internals.absent.target"), out absentPull), Is.False);
            Assert.That(absentPull, Is.Null);
        }

        private static void AssertTerms(TerritoryDriftBindingRuntime binding, string[] sources, TerritoryDriftTransformRuntime[] transforms, int[] coefficients)
        {
            Assert.That(binding.Terms.Count, Is.EqualTo(sources.Length), binding.OutputTarget.ToString());
            for (int i = 0; i < sources.Length; i++)
            {
                Assert.That(binding.Terms[i].Source.ToString(), Is.EqualTo(sources[i]));
                Assert.That(binding.Terms[i].Transform, Is.EqualTo(transforms[i]));
                Assert.That(binding.Terms[i].CoefficientPpm, Is.EqualTo(coefficients[i]));
            }
        }

        private static string SerializePlan(TerritoryRuntimePlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(plan.Scale).Append('|').Append(plan.MidS).Append('|').Append(plan.DriftPpmDenominator).Append('|')
                .Append(plan.DriftAlphaPpmValue).Append('|').Append(plan.DriftCapPerWeekSValue).Append('|')
                .Append(plan.PullAlphaPpmValue).Append('|').Append(plan.PullCapPerWeekSValue).Append('\n');
            for (int i = 0; i < plan.Regions.Count; i++)
            {
                TerritoryRegionRuntime region = plan.Regions[i];
                sb.Append("R:").Append(region.RegionId).Append('|').Append(region.WeightPpm).Append('|')
                    .Append(region.AdminCapS).Append('|').Append(region.IndustryCapS).Append('|')
                    .Append(region.ExtractiveCapS).Append('|').Append(region.SocialCapS).Append('|')
                    .Append(region.PopulationS).Append('\n');
            }

            for (int i = 0; i < plan.DriftBindings.Count; i++)
            {
                TerritoryDriftBindingRuntime binding = plan.DriftBindings[i];
                sb.Append("D:").Append(binding.RegionId).Append('|').Append(binding.Field).Append('|')
                    .Append(binding.OutputTarget).Append('|').Append(binding.OutputConfig.Pattern).Append('|')
                    .Append(binding.Cause.CanonicalKey).Append('\n');
                for (int t = 0; t < binding.Terms.Count; t++)
                {
                    sb.Append("T:").Append(binding.Terms[t].Source).Append('|')
                        .Append(binding.Terms[t].Transform).Append('|')
                        .Append(binding.Terms[t].CoefficientPpm).Append('\n');
                }
            }

            for (int i = 0; i < plan.PullBindings.Count; i++)
            {
                TerritoryPullBindingRuntime binding = plan.PullBindings[i];
                sb.Append("P:").Append(binding.BindingId).Append('|').Append(binding.RegionalSource).Append('|')
                    .Append(binding.Destination).Append('|').Append(binding.DestinationConfig.Pattern).Append('|')
                    .Append(binding.Cause.CanonicalKey).Append('\n');
            }

            return sb.ToString();
        }

        private static ContentLoadResult LoadRealPack()
        {
            return new ContentPackLoader().Load(new DirectoryContentFileSource(ContentRoot()));
        }

        private static ContentLoadResult Load(Dictionary<string, byte[]> files)
        {
            return new ContentPackLoader().Load(new InMemoryContentFileSource(files));
        }

        private static void AssertFailure(ContentLoadResult result, ContentDiagnosticCode code, string relativeFile, string jsonPath)
        {
            Assert.That(result.IsSuccess, Is.False, Diagnostics(result));
            Assert.That(result.Pack, Is.Null);
            ContentDiagnostic diagnostic = FirstCode(result, code);
            Assert.That(diagnostic, Is.Not.Null, Diagnostics(result));
            Assert.That(diagnostic.RelativeFile, Is.EqualTo(relativeFile));
            Assert.That(diagnostic.JsonPath, Is.EqualTo(jsonPath));
        }

        private static ContentDiagnostic FirstCode(ContentLoadResult result, ContentDiagnosticCode code)
        {
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                if (result.Diagnostics[i].Code == code)
                {
                    return result.Diagnostics[i];
                }
            }

            return null;
        }

        private static bool ContainsCode(ContentLoadResult result, ContentDiagnosticCode code)
        {
            return FirstCode(result, code) != null;
        }

        private static string Diagnostics(ContentLoadResult result)
        {
            List<string> lines = new List<string>();
            foreach (ContentDiagnostic diagnostic in result.Diagnostics)
            {
                lines.Add(diagnostic.ToString());
            }

            return string.Join("\n", lines.ToArray());
        }

        private static string AssetsRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Assets", "VictoriantChile"));
        }

        private static string ContentRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Assets", "StreamingAssets", "content"));
        }

        private static Dictionary<string, byte[]> ValidFixture()
        {
            string root = ContentRoot();
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string relativePath in new[]
            {
                "core/regions.json",
                "core/igs.json",
                "core/movements.json",
                "rules/target_config.json",
                "rules/aggregation_config.json",
                "rules/legislative_config.json",
                "strings/es.json",
                "templates/effects.json",
                "templates/events.json",
                "templates/reforms.json"
            })
            {
                files.Add(relativePath, File.ReadAllBytes(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))));
            }

            return RebuildManifest(files);
        }

        private static Dictionary<string, byte[]> RebuildManifest(Dictionary<string, byte[]> files)
        {
            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
            List<string> paths = new List<string>();
            foreach (string path in result.Keys)
            {
                if (path != "manifest.json")
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);
            StringBuilder filesJson = new StringBuilder();
            for (int i = 0; i < paths.Count; i++)
            {
                if (i > 0)
                {
                    filesJson.Append(",");
                }

                string path = paths[i];
                filesJson.Append("\"").Append(path).Append("\":\"").Append(ContentHash.ComputeCanonicalSha256(result[path])).Append("\"");
            }

            result["manifest.json"] = Bytes("{\"content_pack_id\":\"test_pack\",\"content_pack_version\":1,\"content_schema_version\":1,\"default_language\":\"es\",\"files\":{"
                + filesJson + "},\"languages\":[\"es\"],\"min_game_schema_version\":1}");
            return result;
        }

        private static Dictionary<string, byte[]> WithFile(Dictionary<string, byte[]> files, string path, string text)
        {
            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
            result[path] = Bytes(text);
            return result;
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

        private static string ReplaceFirst(string text, string oldText, string newText)
        {
            int index = text.IndexOf(oldText, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), oldText);
            return text.Substring(0, index) + newText + text.Substring(index + oldText.Length);
        }

        private static string RegionJsonWithoutLastAdjustedToOneMillion()
        {
            return "{\"regions\":["
                + "{\"id\":\"arica_parinacota\",\"name\":\"Arica y Parinacota\",\"weight_ppm\":125000,\"macrozone\":\"NORTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"tarapaca\",\"name\":\"Tarapaca\",\"weight_ppm\":62500,\"macrozone\":\"NORTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"antofagasta\",\"name\":\"Antofagasta\",\"weight_ppm\":62500,\"macrozone\":\"NORTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"atacama\",\"name\":\"Atacama\",\"weight_ppm\":62500,\"macrozone\":\"NORTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"coquimbo\",\"name\":\"Coquimbo\",\"weight_ppm\":62500,\"macrozone\":\"NORTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"valparaiso\",\"name\":\"Valparaiso\",\"weight_ppm\":62500,\"macrozone\":\"CENTER\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":62500,\"macrozone\":\"CENTER\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"ohiggins\",\"name\":\"Ohiggins\",\"weight_ppm\":62500,\"macrozone\":\"CENTER\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"maule\",\"name\":\"Maule\",\"weight_ppm\":62500,\"macrozone\":\"CENTER\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"nuble\",\"name\":\"Nuble\",\"weight_ppm\":62500,\"macrozone\":\"CENTER\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"biobio\",\"name\":\"Biobio\",\"weight_ppm\":62500,\"macrozone\":\"SOUTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"araucania\",\"name\":\"Araucania\",\"weight_ppm\":62500,\"macrozone\":\"SOUTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"los_rios\",\"name\":\"Los Rios\",\"weight_ppm\":62500,\"macrozone\":\"SOUTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"los_lagos\",\"name\":\"Los Lagos\",\"weight_ppm\":62500,\"macrozone\":\"SOUTH\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000},"
                + "{\"id\":\"aysen\",\"name\":\"Aysen\",\"weight_ppm\":62500,\"macrozone\":\"AUSTRAL\",\"admin_capS\":5000,\"industry_capS\":5000,\"extractive_capS\":5000,\"social_capS\":5000,\"populationS\":5000}"
                + "]}";
        }

        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private static string Text(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        private sealed class InMemoryContentFileSource : IContentFileSource
        {
            private readonly Dictionary<string, byte[]> _files;

            public InMemoryContentFileSource(Dictionary<string, byte[]> files)
            {
                _files = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
            }

            public ContentFileReadResult TryReadAllBytes(string relativePath)
            {
                if (!_files.TryGetValue(relativePath, out byte[] bytes))
                {
                    return ContentFileReadResult.Missing("Missing in-memory file.");
                }

                return ContentFileReadResult.FromBytes(bytes);
            }
        }
    }
}
