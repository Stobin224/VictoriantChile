using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using VictoriantChile.Content.Loading;
using VictoriantChile.Content.Models;
using VictoriantChile.Content.State;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;
using VictoriantChile.Simulation.Runner;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class StateResolutionScenarioRunnerTests
    {
        private const string SmokeStateHash = "sha256:51168b952197ecad4ca3454ae81917ce65ccee4ec806195a05528ca0864f86b6";

        [TestCase("metrics.legitimacy", 5000)]
        [TestCase("internals.economy.growth", 5000)]
        [TestCase("regions.metropolitana.support", 5000)]
        [TestCase("regions.metropolitana.tension", 5000)]
        [TestCase("regions.metropolitana.organization", 5000)]
        [TestCase("regions.metropolitana.rival_presence", 5000)]
        [TestCase("igs.ig_sindicatos_trabajo.clout", 1111)]
        [TestCase("igs.ig_sindicatos_trabajo.approval", 0)]
        [TestCase("movements.mov_seguridad_mano_dura.intensity", 0)]
        [TestCase("movements.mov_seguridad_mano_dura.direction", 1)]
        public void ReaderResolvesDynamicTargets(string target, int expected)
        {
            TargetReadResult result = Reader().Read(TargetPath.Parse(target));

            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            Assert.That(result.ValueS, Is.EqualTo(expected));
            Assert.That(result.Source, Is.EqualTo(TargetValueSource.DynamicState));
        }

        [TestCase("regions.metropolitana.admin_capS")]
        [TestCase("regions.metropolitana.industry_capS")]
        [TestCase("regions.metropolitana.extractive_capS")]
        [TestCase("regions.metropolitana.social_capS")]
        [TestCase("regions.metropolitana.populationS")]
        public void ReaderResolvesStaticRegionalResources(string target)
        {
            TargetReadResult result = Reader().Read(TargetPath.Parse(target));

            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            Assert.That(result.ValueS, Is.EqualTo(5000));
            Assert.That(result.Source, Is.EqualTo(TargetValueSource.StaticContent));
        }

        [Test]
        public void ReaderRejectsMissingIdsAndDoesNotExposeMutationApi()
        {
            IStateTargetReader reader = Reader();
            TargetReadResult missingRegion = reader.Read(TargetPath.Parse("regions.missing.support"));
            TargetReadResult missingInternal = reader.Read(TargetPath.Parse("internals.economy.missing"));
            TargetReadResult macrozone = reader.Read(TargetPath.Parse("regions.metropolitana.macrozone"));
            TargetReadResult weight = reader.Read(TargetPath.Parse("regions.metropolitana.weight_ppm"));

            Assert.That(missingRegion.Success, Is.False);
            Assert.That(missingRegion.Diagnostics[0].Code, Is.EqualTo("target.not_found").Or.EqualTo("target.static_not_found"));
            Assert.That(missingInternal.Success, Is.False);
            Assert.That(missingInternal.Diagnostics[0].Code, Is.EqualTo("target.not_found"));
            Assert.That(macrozone.Success, Is.False);
            Assert.That(weight.Success, Is.False);
            Assert.That(TargetPath.TryParse("metrics.*", out TargetPath wildcard), Is.False);
            Assert.That(typeof(IStateTargetReader).GetMethod("Read"), Is.Not.Null);
            Assert.That(typeof(IStateTargetReader).GetMethods().Length, Is.EqualTo(1));
            Assert.That(typeof(IReadOnlyStaticTargetSource).GetMethods().Length, Is.EqualTo(1));
            MethodInfo[] mutatorMethods = typeof(GameStateMutator).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            Assert.That(mutatorMethods.Length, Is.EqualTo(1));
            Assert.That(mutatorMethods[0].Name, Is.EqualTo("Apply"));
        }

        [Test]
        public void MutatorAppliesAddMulSetAndClampsFunctionally()
        {
            ContentPack pack = LoadRealPack();
            GameState original = CreateState(pack, 10);
            GameStateMutator mutator = new GameStateMutator();

            StateMutationResult add = mutator.Apply(original, Mutation("metrics.legitimacy", TargetOperation.Add, 6000), pack.TargetConfigCatalog);
            StateMutationResult mul = mutator.Apply(add.State, Mutation("metrics.economy", TargetOperation.Multiply, 11000), pack.TargetConfigCatalog);
            StateMutationResult set = mutator.Apply(mul.State, Mutation("movements.mov_seguridad_mano_dura.direction", TargetOperation.Set, -1), pack.TargetConfigCatalog);

            Assert.That(add.Success, Is.True, Diagnostics(add.Diagnostics));
            Assert.That(add.BeforeS, Is.EqualTo(5000));
            Assert.That(add.AfterS, Is.EqualTo(10000));
            Assert.That(add.Clamped, Is.True);
            Assert.That(mul.Success, Is.True, Diagnostics(mul.Diagnostics));
            Assert.That(mul.AfterS, Is.EqualTo(5500));
            Assert.That(set.Success, Is.True, Diagnostics(set.Diagnostics));
            Assert.That(set.AfterS, Is.EqualTo(-1));
            Assert.That(original.MetricsById["legitimacy"].ValueS, Is.EqualTo(5000));
            Assert.That(original.MovementsById["mov_seguridad_mano_dura"].Direction, Is.EqualTo(1));
            Assert.That(set.State, Is.Not.SameAs(original));
        }

        [Test]
        public void AddSetAndDirectionClampsCoverBothBoundsAndLargeOperands()
        {
            ContentPack pack = LoadRealPack();
            GameState state = CreateState(pack, 10);
            GameStateMutator mutator = new GameStateMutator();

            StateMutationResult addMin = mutator.Apply(state, Mutation("metrics.legitimacy", TargetOperation.Add, -6000), pack.TargetConfigCatalog);
            StateMutationResult addMax = mutator.Apply(state, Mutation("metrics.legitimacy", TargetOperation.Add, int.MaxValue), pack.TargetConfigCatalog);
            StateMutationResult addMinLarge = mutator.Apply(state, Mutation("igs.ig_sindicatos_trabajo.approval", TargetOperation.Add, int.MinValue), pack.TargetConfigCatalog);
            StateMutationResult setMin = mutator.Apply(state, Mutation("metrics.legitimacy", TargetOperation.Set, -1), pack.TargetConfigCatalog);
            StateMutationResult setMax = mutator.Apply(state, Mutation("metrics.legitimacy", TargetOperation.Set, 10001), pack.TargetConfigCatalog);
            StateMutationResult directionMax = mutator.Apply(state, Mutation("movements.mov_seguridad_mano_dura.direction", TargetOperation.Set, 2), pack.TargetConfigCatalog);
            StateMutationResult directionMin = mutator.Apply(state, Mutation("movements.mov_seguridad_mano_dura.direction", TargetOperation.Set, -2), pack.TargetConfigCatalog);

            Assert.That(addMin.AfterS, Is.EqualTo(0));
            Assert.That(addMin.Clamped, Is.True);
            Assert.That(addMax.AfterS, Is.EqualTo(10000));
            Assert.That(addMax.Clamped, Is.True);
            Assert.That(addMinLarge.AfterS, Is.EqualTo(-10000));
            Assert.That(addMinLarge.Clamped, Is.True);
            Assert.That(setMin.AfterS, Is.EqualTo(0));
            Assert.That(setMax.AfterS, Is.EqualTo(10000));
            Assert.That(directionMax.Success, Is.True, Diagnostics(directionMax.Diagnostics));
            Assert.That(directionMax.AfterS, Is.EqualTo(1));
            Assert.That(directionMax.Clamped, Is.True);
            Assert.That(directionMin.Success, Is.True, Diagnostics(directionMin.Diagnostics));
            Assert.That(directionMin.AfterS, Is.EqualTo(-1));
            Assert.That(directionMin.Clamped, Is.True);
            Assert.That(state.MetricsById["legitimacy"].ValueS, Is.EqualTo(5000));
        }

        [Test]
        public void MultiplyCoversIdentityRoundingNegativeFactorAndClamp()
        {
            ContentPack pack = LoadRealPack();
            GameState state = CreateState(pack, 10);
            GameStateMutator mutator = new GameStateMutator();

            StateMutationResult identity = mutator.Apply(state, Mutation("metrics.legitimacy", TargetOperation.Multiply, 10000), pack.TargetConfigCatalog);
            StateMutationResult positiveTie = mutator.Apply(state, Mutation("metrics.legitimacy", TargetOperation.Multiply, 10001), pack.TargetConfigCatalog);
            StateMutationResult negativeFactor = mutator.Apply(state, Mutation("metrics.legitimacy", TargetOperation.Multiply, -10000), pack.TargetConfigCatalog);
            StateMutationResult approvalSet = mutator.Apply(state, Mutation("igs.ig_sindicatos_trabajo.approval", TargetOperation.Set, -5000), pack.TargetConfigCatalog);
            StateMutationResult negativeTie = mutator.Apply(approvalSet.State, Mutation("igs.ig_sindicatos_trabajo.approval", TargetOperation.Multiply, 10001), pack.TargetConfigCatalog);

            Assert.That(identity.AfterS, Is.EqualTo(5000));
            Assert.That(identity.Clamped, Is.False);
            Assert.That(positiveTie.AfterS, Is.EqualTo(5001));
            Assert.That(negativeFactor.AfterS, Is.EqualTo(0));
            Assert.That(negativeFactor.Clamped, Is.True);
            Assert.That(negativeTie.AfterS, Is.EqualTo(-5001));
            Assert.That(negativeTie.Clamped, Is.False);
        }

        [Test]
        public void MutatorRejectsReadOnlyMissingOperationDirectionAndPreservesOriginalOnFailure()
        {
            ContentPack pack = LoadRealPack();
            GameState state = CreateState(pack, 10);
            GameStateMutator mutator = new GameStateMutator();
            string beforeHash = new GameStateHasher().ComputeHash(state);

            AssertFailure(mutator.Apply(state, Mutation("regions.metropolitana.admin_capS", TargetOperation.Set, 1), pack.TargetConfigCatalog), "target.read_only");
            AssertFailure(mutator.Apply(state, Mutation("metrics.missing", TargetOperation.Set, 1), pack.TargetConfigCatalog), "target.not_found");
            AssertFailure(mutator.Apply(state, Mutation("internals.economy.missing", TargetOperation.Set, 1), pack.TargetConfigCatalog), "target.not_found");
            AssertFailure(mutator.Apply(state, Mutation("movements.mov_seguridad_mano_dura.direction", TargetOperation.Add, 1), pack.TargetConfigCatalog), "target.operation_not_allowed");
            AssertFailure(mutator.Apply(state, Mutation("movements.mov_seguridad_mano_dura.direction", TargetOperation.Set, 0), pack.TargetConfigCatalog), "target.invalid_direction");
            StateMutationResult largeMultiply = mutator.Apply(state, Mutation("metrics.legitimacy", TargetOperation.Multiply, int.MaxValue), pack.TargetConfigCatalog);
            Assert.That(largeMultiply.Success, Is.True, Diagnostics(largeMultiply.Diagnostics));
            Assert.That(largeMultiply.AfterS, Is.EqualTo(10000));
            Assert.That(largeMultiply.Clamped, Is.True);
            Assert.That(state.MetricsById["legitimacy"].ValueS, Is.EqualTo(5000));
            Assert.That(new GameStateHasher().ComputeHash(state), Is.EqualTo(beforeHash));
        }

        [Test]
        public void CloutMutationRenormalizesAtomically()
        {
            ContentPack pack = LoadRealPack();
            GameState state = CreateState(pack, 10);
            StateMutationResult result = new GameStateMutator().Apply(
                state,
                Mutation("igs.ig_sindicatos_trabajo.clout", TargetOperation.Add, 1000),
                pack.TargetConfigCatalog);

            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            Assert.That(result.NormalizeGroup, Is.EqualTo("igs.clout_sum_100"));
            Assert.That(result.AfterS, Is.EqualTo(1920));
            Assert.That(SumClout(result.State), Is.EqualTo(10000));
            Assert.That(state.InterestGroupsById["ig_sindicatos_trabajo"].CloutS, Is.EqualTo(1111));
        }

        [Test]
        public void UnsupportedNormalizeGroupFailsWithoutState()
        {
            ContentPack realPack = LoadRealPack();
            List<TargetConfig> configs = new List<TargetConfig>();
            for (int i = 0; i < realPack.TargetConfigs.Count; i++)
            {
                TargetConfig config = realPack.TargetConfigs[i];
                configs.Add(config.Pattern.ToString() == "igs.*.clout"
                    ? new TargetConfig(config.Pattern, config.Scale, config.MinS, config.MaxS, config.DefaultS, config.AllowedOperations, "igs.bad")
                    : config);
            }

            TargetConfigCatalog catalog = new TargetConfigCatalog(configs);
            StateMutationResult result = new GameStateMutator().Apply(
                CreateState(realPack, 10),
                Mutation("igs.ig_sindicatos_trabajo.clout", TargetOperation.Add, 1),
                catalog);

            AssertFailure(result, "target.unsupported_normalize_group");
        }

        [Test]
        public void PostMutationInvariantFailurePreventsPublishingSnapshot()
        {
            ContentPack realPack = LoadRealPack();
            List<TargetConfig> configs = new List<TargetConfig>();
            for (int i = 0; i < realPack.TargetConfigs.Count; i++)
            {
                if (realPack.TargetConfigs[i].Pattern.ToString() != "internals.*.*")
                {
                    configs.Add(realPack.TargetConfigs[i]);
                }
            }

            GameState state = CreateState(realPack, 10);
            string beforeHash = new GameStateHasher().ComputeHash(state);
            StateMutationResult result = new GameStateMutator().Apply(
                state,
                Mutation("metrics.legitimacy", TargetOperation.Add, 1),
                new TargetConfigCatalog(configs));

            Assert.That(result.Success, Is.False);
            Assert.That(result.State, Is.Null);
            Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(new GameStateHasher().ComputeHash(state), Is.EqualTo(beforeHash));
        }

        [Test]
        public void InvariantValidatorDetectsRangeCloutAndDirectionFailures()
        {
            ContentPack pack = LoadRealPack();
            GameState valid = CreateState(pack, 10);
            Assert.That(new GameStateInvariantValidator().Validate(valid, pack.TargetConfigCatalog), Is.Empty);

            GameState badMetric = new GameState(valid.RngSeed, valid.ContentMetadata, ReplaceMetric(valid, "legitimacy", 10001), valid.Internals, valid.Regions, valid.InterestGroups, valid.Movements, valid.ActiveEffects, valid.Tick);
            GameState badClout = new GameState(valid.RngSeed, valid.ContentMetadata, valid.Metrics, valid.Internals, valid.Regions, ReplaceClout(valid, "ig_ambiental_regionalista", 1), valid.Movements, valid.ActiveEffects, valid.Tick);
            GameState badDirection = new GameState(valid.RngSeed, valid.ContentMetadata, valid.Metrics, valid.Internals, valid.Regions, valid.InterestGroups, ReplaceDirection(valid, "mov_seguridad_mano_dura", 0), valid.ActiveEffects, valid.Tick);

            Assert.That(HasCode(new GameStateInvariantValidator().Validate(badMetric, pack.TargetConfigCatalog), "state.invariant_violation"), Is.True);
            Assert.That(HasCode(new GameStateInvariantValidator().Validate(badClout, pack.TargetConfigCatalog), "state.invariant_violation"), Is.True);
            Assert.That(HasCode(new GameStateInvariantValidator().Validate(badDirection, pack.TargetConfigCatalog), "target.invalid_direction"), Is.True);
        }

        [Test]
        public void CanonicalSerializerAndHasherHaveStableGoldenOutput()
        {
            GameState state = CreateState(LoadRealPack(), 424242);
            string json = new CanonicalGameStateSerializer().ToCompactJson(state);
            string hash = new GameStateHasher().ComputeHash(state);

            Assert.That(
                json,
                Does.StartWith(
                    "{\"state_schema_version\":3,\"tick\":0,\"rng_seed\":424242,\"rng\":{\"algorithm\":\"pcg32-xsh-rr\",\"contract_version\":\"pcg32-v1\","));
            Assert.That(hash, Does.Match("^sha256:[0-9a-f]{64}$"));
            Assert.That(hash, Is.EqualTo(new GameStateHasher().ComputeHash(CreateState(LoadRealPack(), 424242))));
            Assert.That(hash, Is.Not.EqualTo(new GameStateHasher().ComputeHash(CreateState(LoadRealPack(), 424243))));
        }

        [Test]
        public void ScenarioRunnerSmokeMatchesGoldenHashAndCommandShape()
        {
            ScenarioRunnerResult result = RunScenario("smoke_v1.json");

            Assert.That(result.Status, Is.EqualTo("passed"), Diagnostics(result.Diagnostics));
            Assert.That(result.StateHash, Is.EqualTo(SmokeStateHash));
            Assert.That(result.Commands.Count, Is.EqualTo(7));
            Assert.That(result.Commands[1].Source, Is.EqualTo("static_content"));
            Assert.That(result.Commands[2].Clamped, Is.True);
            Assert.That(result.Commands[5].NormalizeGroup, Is.EqualTo("igs.clout_sum_100"));
            Assert.That(result.Diagnostics, Is.Empty);
        }

        [Test]
        public void ScenarioRunnerAdvanceCommandReportsWeeksHashesAndCausalTicksDeterministically()
        {
            byte[] scenario = Encoding.UTF8.GetBytes(
                "{\n"
                + "  \"scenario_schema_version\": 1,\n"
                + "  \"seed\": 424242,\n"
                + "  \"commands\": [\n"
                + "    {\n"
                + "      \"id\": \"advance_four\",\n"
                + "      \"type\": \"ADVANCE\",\n"
                + "      \"weeks\": 4\n"
                + "    }\n"
                + "  ]\n"
                + "}\n");

            ScenarioRunnerResult first = new ScenarioRunner().Run(scenario, ContentRoot());
            ScenarioRunnerResult second = new ScenarioRunner().Run(scenario, ContentRoot());

            Assert.That(first.Status, Is.EqualTo("passed"), Diagnostics(first.Diagnostics));
            Assert.That(first.CommandCount, Is.EqualTo(1));
            Assert.That(first.Commands.Count, Is.EqualTo(1));
            Assert.That(first.Commands[0].Type, Is.EqualTo("advance"));
            Assert.That(first.Commands[0].WeeksRequested, Is.EqualTo(4));
            Assert.That(first.Commands[0].TicksCompleted, Is.EqualTo(4));
            Assert.That(first.Commands[0].BlockingDecision, Is.Null);
            Assert.That(first.Commands[0].TickStateHashes.Count, Is.EqualTo(4));
            Assert.That(first.Commands[0].CausalTicks.Count, Is.EqualTo(4));
            string stateJson = ScenarioState(first).ToString();
            Assert.That(stateJson, Does.Contain("\"tick\": 4").Or.Contain("\"tick\":4"));
            Assert.That(stateJson, Does.Contain("\"state_schema_version\": 3").Or.Contain("\"state_schema_version\":3"));
            Assert.That(first.StateHash, Is.EqualTo(second.StateHash));
            Assert.That(first.Commands[0].TickStateHashes, Is.EqualTo(second.Commands[0].TickStateHashes));
            Assert.That(first.Commands[0].CausalTicks.Count, Is.EqualTo(second.Commands[0].CausalTicks.Count));
            for (int i = 0; i < first.Commands[0].CausalTicks.Count; i++)
            {
                Assert.That(first.Commands[0].CausalTicks[i].Tick, Is.EqualTo(i + 1));
                Assert.That(first.Commands[0].CausalTicks[i].AuditedTargets.Count, Is.GreaterThan(0));
            }
        }

        [TestCase("negative_static_mutation.json", "target.read_only")]
        [TestCase("negative_direction_zero.json", "target.invalid_direction")]
        [TestCase("negative_operation_not_allowed.json", "target.operation_not_allowed")]
        [TestCase("negative_target_missing.json", "target.not_found")]
        [TestCase("negative_internal_missing.json", "target.not_found")]
        [TestCase("negative_schema_future.json", "scenario.unsupported_schema")]
        [TestCase("negative_bool_integer.json", "scenario.invalid_type")]
        [TestCase("negative_duplicate_property.json", "scenario.json_malformed")]
        [TestCase("negative_malformed.json", "scenario.json_malformed")]
        public void ScenarioRunnerFailuresAreClosed(string fixture, string expectedCode)
        {
            ScenarioRunnerResult result = RunScenario(fixture);

            Assert.That(result.Status, Is.EqualTo("failed"));
            Assert.That(ScenarioState(result), Is.Null);
            Assert.That(result.StateHash, Is.Null);
            Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(HasCode(result.Diagnostics, expectedCode), Is.True, Diagnostics(result.Diagnostics));
        }

        private static IStateTargetReader Reader()
        {
            ContentPack pack = LoadRealPack();
            return new StateTargetReader(CreateState(pack, 10), new ContentPackStaticTargetSource(pack));
        }

        private static ContentPack LoadRealPack()
        {
            ContentLoadResult result = new ContentPackLoader().Load(new DirectoryContentFileSource(ContentRoot()));
            Assert.That(result.IsSuccess, Is.True);
            return result.Pack;
        }

        private static GameState CreateState(ContentPack pack, int seed)
        {
            StateInitializationResult result = new GameStateFactory().CreateInitialState(pack, seed);
            Assert.That(result.Success, Is.True);
            return result.State;
        }

        private static ScenarioRunnerResult RunScenario(string fixture)
        {
            string scenarioPath = Path.Combine(ProjectRoot(), "tests", "scenarios", fixture);
            byte[] bytes = File.ReadAllBytes(scenarioPath);
            return new ScenarioRunner().Run(bytes, ContentRoot());
        }

        private static object ScenarioState(ScenarioRunnerResult result)
        {
            return typeof(ScenarioRunnerResult).GetProperty("State").GetValue(result, null);
        }

        private static TargetMutation Mutation(string target, TargetOperation operation, int valueS)
        {
            return new TargetMutation(TargetPath.Parse(target), operation, valueS);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", ".."));
        }

        private static string ContentRoot()
        {
            return Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "content");
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

        private static void AssertFailure(StateMutationResult result, string code)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.State, Is.Null);
            Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(HasCode(result.Diagnostics, code), Is.True, Diagnostics(result.Diagnostics));
        }

        private static bool HasCode(IEnumerable<StateDiagnostic> diagnostics, string code)
        {
            foreach (StateDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Diagnostics(IEnumerable<StateDiagnostic> diagnostics)
        {
            List<string> lines = new List<string>();
            foreach (StateDiagnostic diagnostic in diagnostics)
            {
                lines.Add(diagnostic.ToString());
            }

            return string.Join("\n", lines.ToArray());
        }

        private static List<MetricState> ReplaceMetric(GameState state, string metricId, int valueS)
        {
            List<MetricState> result = new List<MetricState>();
            for (int i = 0; i < state.Metrics.Count; i++)
            {
                MetricState metric = state.Metrics[i];
                result.Add(new MetricState(metric.MetricId, metric.MetricId == metricId ? valueS : metric.ValueS));
            }

            return result;
        }

        private static List<InterestGroupState> ReplaceClout(GameState state, string igId, int valueS)
        {
            List<InterestGroupState> result = new List<InterestGroupState>();
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState ig = state.InterestGroups[i];
                result.Add(new InterestGroupState(ig.InterestGroupId, ig.InterestGroupId == igId ? valueS : ig.CloutS, ig.ApprovalS));
            }

            return result;
        }

        private static List<MovementState> ReplaceDirection(GameState state, string movementId, int direction)
        {
            List<MovementState> result = new List<MovementState>();
            for (int i = 0; i < state.Movements.Count; i++)
            {
                MovementState movement = state.Movements[i];
                result.Add(new MovementState(movement.MovementId, movement.IntensityS, movement.MovementId == movementId ? direction : movement.Direction));
            }

            return result;
        }
    }
}
