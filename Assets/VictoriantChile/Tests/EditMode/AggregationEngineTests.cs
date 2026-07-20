using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VictoriantChile.Content.Loading;
using VictoriantChile.Content.Models;
using VictoriantChile.Content.State;
using VictoriantChile.Simulation.Core.Aggregation;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Effects;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.Scheduling;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class AggregationEngineTests
    {
        [Test]
        public void BindingExpandsReversionOnceInGroupThenRegistryOrder()
        {
            ContentPack pack = LoadRealPack();
            AggregationEngine engine = new AggregationEngine(pack.AggregationRuntimePlan, pack.TargetConfigCatalog);

            Assert.That(engine.ReversionTargets, Is.InstanceOf<ReadOnlyCollection<AggregationTargetBinding>>());
            Assert.That(engine.ReversionTargets.Count, Is.EqualTo(36));
            Assert.That(engine.ReversionTargets[0].Target.ToString(), Is.EqualTo("internals.economy.growth"));
            Assert.That(engine.ReversionTargets[3].Target.ToString(), Is.EqualTo("internals.economy.fiscal_stability"));
            Assert.That(engine.ReversionTargets[4].Target.ToString(), Is.EqualTo("internals.security.police_capacity"));
            Assert.That(engine.ReversionTargets[34].Target.ToString(), Is.EqualTo("internals.legitimacy.integrity"));
            Assert.That(engine.ReversionTargets[35].Target.ToString(), Is.EqualTo("internals.legitimacy.scandal_pressure"));
            Assert.That(ContainsBinding(engine.ReversionTargets, "internals.legitimacy.performance"), Is.False);
            Assert.That(ContainsBinding(engine.ReversionTargets, "internals.legitimacy.social_tension_load"), Is.False);
            Assert.That(engine.PrimaryMetricBindings.Count, Is.EqualTo(9));
            Assert.That(engine.LegitimacyMetricBindings.Count, Is.EqualTo(1));
            Assert.That(engine.PrimaryMetricBindings, Is.InstanceOf<ReadOnlyCollection<AggregationMetricBinding>>());
        }

        [Test]
        public void BindingRejectsPatternNoMatchOverlapSkipUncoveredAndMissingConfig()
        {
            ContentPack pack = LoadRealPack();
            AggregationRuntimePlan plan = pack.AggregationRuntimePlan;

            List<AggregationReversionGroupRuntime> noMatchGroups = CopyGroups(plan);
            noMatchGroups[0] = Group("internals.missing.*");
            AssertAggregationError(
                () => new AggregationEngine(WithReversion(plan, noMatchGroups, plan.InternalReversion.SkipTargets), pack.TargetConfigCatalog),
                AggregationExecutionErrorCodes.ReversionPatternNoMatch);

            List<AggregationReversionGroupRuntime> overlapGroups = CopyGroups(plan);
            overlapGroups[0] = Group("internals.*.*");
            AssertAggregationError(
                () => new AggregationEngine(WithReversion(plan, overlapGroups, plan.InternalReversion.SkipTargets), pack.TargetConfigCatalog),
                AggregationExecutionErrorCodes.ReversionOverlap);

            AssertAggregationError(
                () => new AggregationEngine(WithReversion(plan, plan.InternalReversion.Groups, new[] { TargetPath.Parse("internals.missing.target") }), pack.TargetConfigCatalog),
                AggregationExecutionErrorCodes.ReversionSkipUnmatched);

            List<AggregationReversionGroupRuntime> uncovered = CopyGroups(plan);
            uncovered[0] = Group("internals.economy.growth");
            AssertAggregationError(
                () => new AggregationEngine(WithReversion(plan, uncovered, plan.InternalReversion.SkipTargets), pack.TargetConfigCatalog),
                AggregationExecutionErrorCodes.ReversionUncoveredTarget);

            List<TargetConfig> configs = new List<TargetConfig>();
            for (int i = 0; i < pack.TargetConfigs.Count; i++)
            {
                if (pack.TargetConfigs[i].Pattern.ToString() != "internals.*.*")
                {
                    configs.Add(pack.TargetConfigs[i]);
                }
            }

            AssertAggregationError(
                () => new AggregationEngine(plan, new TargetConfigCatalog(configs)),
                AggregationExecutionErrorCodes.TargetConfigMissing);
        }

        [Test]
        public void ReversionBindingRejectsTargetConfigWithoutSet()
        {
            ContentPack pack = LoadRealPack();
            TargetConfigCatalog configs = WithExactTargetConfig(pack, "internals.economy.growth", TargetOperation.Add);

            AssertAggregationError(
                () => new AggregationEngine(pack.AggregationRuntimePlan, configs),
                AggregationExecutionErrorCodes.TargetConfigMissing);
        }

        [Test]
        public void ReversionVectorCasesExecuteAgainstProductiveEngine()
        {
            ContentPack pack = LoadRealPack();
            AggregationExecutionVectorFile vectors = ReadVectors();

            for (int i = 0; i < vectors.reversion_cases.Length; i++)
            {
                ReversionCase item = vectors.reversion_cases[i];
                AggregationRuntimePlan plan = WithReversionAlpha(pack.AggregationRuntimePlan, "internals.economy.*", item.alpha_ppm);
                AggregationEngine engine = new AggregationEngine(plan, pack.TargetConfigCatalog);
                GameState state = Set(CreateState(pack, 42), pack, "internals.economy.growth", item.currentS);

                GameState final = engine.RevertInternals(state);

                Assert.That(ReadInternal(final, "internals.economy.growth"), Is.EqualTo(item.expected.finalS), item.id);
                Assert.That(ReadInternal(state, "internals.economy.growth"), Is.EqualTo(item.currentS), item.id);
            }
        }

        [Test]
        public void DerivedVectorCasesExecuteAgainstProductiveEngine()
        {
            ContentPack pack = LoadRealPack();
            AggregationEngine engine = new AggregationEngine(pack.AggregationRuntimePlan, pack.TargetConfigCatalog);
            AggregationExecutionVectorFile vectors = ReadVectors();

            for (int i = 0; i < vectors.derived_cases.Length; i++)
            {
                DerivedCase item = vectors.derived_cases[i];
                GameState state = CreateState(pack, 42);
                if (item.kind == "AVG")
                {
                    state = Set(state, pack, "metrics.economy", item.inputs[0]);
                    state = Set(state, pack, "metrics.security", item.inputs[1]);
                    state = Set(state, pack, "metrics.governability", item.inputs[2]);
                    GameState final = engine.DeriveInternals(state);
                    Assert.That(ReadInternal(final, "internals.legitimacy.performance"), Is.EqualTo(item.expected), item.id);
                    Assert.That(ReadInternal(state, "internals.legitimacy.performance"), Is.EqualTo(5000), item.id);
                }
                else if (item.kind == "COPY")
                {
                    state = Set(state, pack, "metrics.social_tension", item.input);
                    GameState final = engine.DeriveInternals(state);
                    Assert.That(ReadInternal(final, "internals.legitimacy.social_tension_load"), Is.EqualTo(item.expected), item.id);
                    Assert.That(ReadInternal(state, "internals.legitimacy.social_tension_load"), Is.EqualTo(5000), item.id);
                }
                else
                {
                    Assert.Fail("Unknown derived vector kind: " + item.kind);
                }
            }
        }

        [Test]
        public void MetricVectorCasesExecuteAgainstProductiveEngineWithExactCausality()
        {
            ContentPack pack = LoadRealPack();
            AggregationExecutionVectorFile vectors = ReadVectors();

            for (int i = 0; i < vectors.metric_cases.Length; i++)
            {
                MetricCase item = vectors.metric_cases[i];
                AggregationRuntimePlan plan = WithMetricCase(pack.AggregationRuntimePlan, item);
                AggregationEngine engine = new AggregationEngine(plan, pack.TargetConfigCatalog);
                GameState state = StateForMetricCase(pack, item, item.components.Length);
                TickCausalBuffer buffer = CreateBuffer(state);

                GameState final = ExecuteMetricPass(engine, state, buffer, item.metric);
                TickCausalSnapshot snapshot = Close(buffer, final);

                Assert.That(ReadMetric(final, item.metric), Is.EqualTo(item.expected.finalS), item.id);
                AssertContributionsExact(snapshot, item.metric, item.expected.contributions, item.id);

                for (int prefix = 0; prefix < item.expected.F_values.Length; prefix++)
                {
                    TickCausalBuffer prefixBuffer;
                    GameState prefixState = StateForMetricCase(pack, item, prefix);
                    prefixBuffer = CreateBuffer(prefixState);
                    GameState prefixFinal = ExecuteMetricPass(engine, prefixState, prefixBuffer, item.metric);
                    Assert.That(ReadMetric(prefixFinal, item.metric), Is.EqualTo(item.expected.F_values[prefix]), item.id + " F(V" + prefix + ")");
                }
            }
        }

        [Test]
        public void EngineMatchesExecutionVectorEndToEndAndKeepsInternalsOutOfLedger()
        {
            ContentPack pack = LoadRealPack();
            AggregationExecutionVectorFile vectors = ReadVectors();
            AggregationExecutionEndToEndCase fixture = vectors.end_to_end_case;
            GameState state = CreateState(pack, 42);
            for (int i = 0; i < fixture.initial_metric_values.Length; i++)
            {
                state = Set(state, pack, fixture.initial_metric_values[i].target, fixture.initial_metric_values[i].valueS);
            }

            for (int i = 0; i < fixture.initial_internal_overrides.Length; i++)
            {
                state = Set(state, pack, fixture.initial_internal_overrides[i].target, fixture.initial_internal_overrides[i].valueS);
            }

            TickCausalBuffer buffer = CreateBuffer(state);
            AggregationEngine engine = new AggregationEngine(pack.AggregationRuntimePlan, pack.TargetConfigCatalog);
            GameState afterReversion = engine.RevertInternals(state);
            GameState afterDerived = engine.DeriveInternals(afterReversion);
            GameState afterPrimary = engine.AggregatePrimaryMetrics(afterDerived, buffer);
            GameState final = engine.AggregateLegitimacy(afterPrimary, buffer);
            TickCausalSnapshot snapshot = Close(buffer, final);

            for (int i = 0; i < fixture.expected_final_metric_values.Length; i++)
            {
                TargetValue expected = fixture.expected_final_metric_values[i];
                Assert.That(ReadMetric(final, expected.target), Is.EqualTo(expected.valueS), expected.target);
            }

            for (int i = 0; i < fixture.expected_final_internal_overrides.Length; i++)
            {
                TargetValue expected = fixture.expected_final_internal_overrides[i];
                Assert.That(ReadInternal(final, expected.target), Is.EqualTo(expected.valueS), expected.target);
            }

            foreach (TargetCausalSnapshot target in snapshot.AuditedTargets)
            {
                Assert.That(target.Target.Namespace, Is.Not.EqualTo("internals"));
            }

            Dictionary<string, ExpectedContribution[]> expectedContributions = new Dictionary<string, ExpectedContribution[]>(StringComparer.Ordinal);
            for (int i = 0; i < fixture.expected_metric_contributions.Length; i++)
            {
                ExpectedMetricContributions metric = fixture.expected_metric_contributions[i];
                expectedContributions.Add(metric.target, metric.contributions);
            }

            for (int i = 0; i < InitialTargetRegistry.Metrics.Count; i++)
            {
                string targetPath = InitialTargetRegistry.Metrics[i].ToString();
                TargetCausalSnapshot target = FindTarget(snapshot, targetPath);
                if (expectedContributions.TryGetValue(targetPath, out ExpectedContribution[] expected))
                {
                    AssertContributionsExact(target, expected, targetPath);
                }
                else
                {
                    Assert.That(target.Contributions, Is.Empty, targetPath);
                }
            }
        }

        [Test]
        public void AggregationCauseRefsArePrecompiledAndExact()
        {
            ContentPack pack = LoadRealPack();
            AggregationRuntimePlan plan = pack.AggregationRuntimePlan;
            AggregationEngine engine = new AggregationEngine(plan, pack.TargetConfigCatalog);

            CauseRef reversion = engine.GetReversionCause(TargetPath.Parse("internals.economy.growth"));
            Assert.That(reversion.CanonicalKey, Is.EqualTo("SYSTEM:REVERSION.internals.economy.growth"));
            Assert.That(engine.TryGetReversionCause(TargetPath.Parse("internals.economy.growth"), out CauseRef reversionAgain), Is.True);
            Assert.That(reversionAgain, Is.SameAs(reversion));
            Assert.That(FindReversionBinding(engine, "internals.economy.growth").Cause, Is.SameAs(reversion));
            Assert.That(engine.TryGetReversionCause(TargetPath.Parse("internals.legitimacy.performance"), out CauseRef skipped), Is.False);
            Assert.That(skipped, Is.Null);
            Assert.That(engine.TryGetReversionCause(TargetPath.Parse("internals.legitimacy.social_tension_load"), out skipped), Is.False);
            Assert.That(skipped, Is.Null);
            Assert.That(engine.TryGetReversionCause(TargetPath.Parse("internals.unknown.value"), out skipped), Is.False);
            Assert.That(skipped, Is.Null);
            Assert.That(engine.TryGetReversionCause(default, out skipped), Is.False);
            Assert.That(skipped, Is.Null);
            Assert.That(() => engine.GetReversionCause(TargetPath.Parse("internals.legitimacy.performance")), Throws.InstanceOf<KeyNotFoundException>());
            Assert.That(() => engine.GetReversionCause(TargetPath.Parse("internals.unknown.value")), Throws.InstanceOf<KeyNotFoundException>());
            Assert.That(() => engine.GetReversionCause(default), Throws.ArgumentException);
            Assert.That(plan.GetDerivedCause(TargetPath.Parse("internals.legitimacy.performance")).CanonicalKey, Is.EqualTo("SYSTEM:DERIVED.internals.legitimacy.performance"));
            Assert.That(plan.GetMetricCause(TargetPath.Parse("metrics.economy")).CanonicalKey, Is.EqualTo("SYSTEM:AGG.metrics.economy"));
            Assert.That(plan.GetComponentCause(TargetPath.Parse("metrics.economy"), TargetPath.Parse("internals.economy.growth")).CanonicalKey, Is.EqualTo("SYSTEM:AGG.metrics.economy.internals.economy.growth"));
            Assert.That(plan.GetMetricCause(TargetPath.Parse("metrics.economy")), Is.SameAs(plan.PrimaryMetrics.Metrics[0].BaseCause));
            Assert.That(plan.GetComponentCause(TargetPath.Parse("metrics.economy"), TargetPath.Parse("internals.economy.growth")), Is.SameAs(plan.PrimaryMetrics.Metrics[0].Components[0].Cause));
        }

        [Test]
        public void SchedulerRequiresRuntimePlan()
        {
            ContentPack pack = LoadRealPack();

            Assert.That(() => new SchedulerEngine(
                    new EffectEngine(),
                    pack.EffectRuntimeCatalog,
                    pack.TargetConfigCatalog,
                    null,
                    Ids(pack.Regions, item => item.Id),
                    Ids(pack.InterestGroups, item => item.Id),
                    Ids(pack.Movements, item => item.Id),
                    Array.Empty<KeyValuePair<string, IScheduledActionHandler>>()),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("aggregationRuntimePlan"));

            foreach (var constructor in typeof(SchedulerEngine).GetConstructors())
            {
                Assert.That(Array.Exists(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(AggregationRuntimePlan)), Is.True);
            }
        }

        private static bool ContainsBinding(IReadOnlyList<AggregationTargetBinding> bindings, string target)
        {
            TargetPath path = TargetPath.Parse(target);
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Target == path)
                {
                    return true;
                }
            }

            return false;
        }

        private static AggregationRuntimePlan WithReversion(
            AggregationRuntimePlan plan,
            IEnumerable<AggregationReversionGroupRuntime> groups,
            IEnumerable<TargetPath> skips)
        {
            return new AggregationRuntimePlan(
                plan.Scale,
                plan.MidS,
                plan.Rounding,
                new AggregationReversionPassRuntime(new List<AggregationReversionGroupRuntime>(groups), new List<TargetPath>(skips)),
                plan.DerivedInternals,
                plan.PrimaryMetrics,
                plan.Legitimacy);
        }

        private static List<AggregationReversionGroupRuntime> CopyGroups(AggregationRuntimePlan plan)
        {
            return new List<AggregationReversionGroupRuntime>(plan.InternalReversion.Groups);
        }

        private static AggregationReversionGroupRuntime Group(string pattern)
        {
            return new AggregationReversionGroupRuntime(TargetPattern.Parse(pattern), 1, 1);
        }

        private static TargetConfigCatalog WithExactTargetConfig(ContentPack pack, string target, params TargetOperation[] operations)
        {
            List<TargetConfig> configs = new List<TargetConfig>(pack.TargetConfigs);
            configs.Add(new TargetConfig(TargetPattern.Parse(target), 100, 0, 10000, 5000, operations));
            return new TargetConfigCatalog(configs);
        }

        private static AggregationRuntimePlan WithReversionAlpha(AggregationRuntimePlan plan, string pattern, int alphaPpm)
        {
            List<AggregationReversionGroupRuntime> groups = CopyGroups(plan);
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Pattern.ToString() == pattern)
                {
                    groups[i] = new AggregationReversionGroupRuntime(groups[i].Pattern, groups[i].HalfLifeWeeks, alphaPpm);
                    return WithReversion(plan, groups, plan.InternalReversion.SkipTargets);
                }
            }

            Assert.Fail("Reversion pattern not found: " + pattern);
            return null;
        }

        private static AggregationRuntimePlan WithMetricCase(AggregationRuntimePlan plan, MetricCase item)
        {
            TargetPath metricPath = TargetPath.Parse(item.metric);
            AggregationMetricRuntime replacement = new AggregationMetricRuntime(
                metricPath,
                1,
                item.alpha_ppm,
                item.cap_per_weekS,
                Components(metricPath, item.components));

            List<AggregationMetricRuntime> primary = new List<AggregationMetricRuntime>(plan.PrimaryMetrics.Metrics);
            List<AggregationMetricRuntime> legitimacy = new List<AggregationMetricRuntime>(plan.Legitimacy.Metrics);
            bool replaced = ReplaceMetric(primary, metricPath, replacement) || ReplaceMetric(legitimacy, metricPath, replacement);
            Assert.That(replaced, Is.True, item.metric);

            return new AggregationRuntimePlan(
                plan.Scale,
                plan.MidS,
                plan.Rounding,
                plan.InternalReversion,
                plan.DerivedInternals,
                new AggregationMetricsPassRuntime(primary),
                new AggregationMetricsPassRuntime(legitimacy));
        }

        private static bool ReplaceMetric(List<AggregationMetricRuntime> metrics, TargetPath metricPath, AggregationMetricRuntime replacement)
        {
            for (int i = 0; i < metrics.Count; i++)
            {
                if (metrics[i].Metric == metricPath)
                {
                    metrics[i] = replacement;
                    return true;
                }
            }

            return false;
        }

        private static WeightedTargetComponentRuntime[] Components(TargetPath metric, MetricComponent[] components)
        {
            WeightedTargetComponentRuntime[] result = new WeightedTargetComponentRuntime[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                result[i] = new WeightedTargetComponentRuntime(metric, TargetPath.Parse(components[i].target), components[i].weight_ppm);
            }

            return result;
        }

        private static GameState StateForMetricCase(ContentPack pack, MetricCase item, int prefixCount)
        {
            GameState state = Set(CreateState(pack, 42), pack, item.metric, item.current_metricS);
            for (int i = 0; i < item.components.Length; i++)
            {
                int valueS = i < prefixCount ? item.components[i].componentS : AggregationRuntimePlan.RequiredMidS;
                state = Set(state, pack, item.components[i].target, valueS);
            }

            return state;
        }

        private static GameState ExecuteMetricPass(AggregationEngine engine, GameState state, TickCausalBuffer buffer, string metric)
        {
            return metric == "metrics.legitimacy"
                ? engine.AggregateLegitimacy(state, buffer)
                : engine.AggregatePrimaryMetrics(state, buffer);
        }

        private static AggregationTargetBinding FindReversionBinding(AggregationEngine engine, string target)
        {
            TargetPath path = TargetPath.Parse(target);
            for (int i = 0; i < engine.ReversionTargets.Count; i++)
            {
                if (engine.ReversionTargets[i].Target == path)
                {
                    return engine.ReversionTargets[i];
                }
            }

            Assert.Fail("Missing reversion binding: " + target);
            return null;
        }

        private static TickCausalBuffer CreateBuffer(GameState state)
        {
            TickCausalBuffer buffer = new TickCausalBuffer(state.Tick, VisibleTargetCatalog.CreateCanonicalFromState(state));
            for (int i = 0; i < InitialTargetRegistry.Metrics.Count; i++)
            {
                TargetPath target = InitialTargetRegistry.Metrics[i];
                buffer.TrackTarget(target, ReadMetric(state, target.ToString()));
            }

            return buffer;
        }

        private static TickCausalSnapshot Close(TickCausalBuffer buffer, GameState state)
        {
            for (int i = 0; i < InitialTargetRegistry.Metrics.Count; i++)
            {
                TargetPath target = InitialTargetRegistry.Metrics[i];
                buffer.CloseTarget(target, ReadMetric(state, target.ToString()));
            }

            return buffer.Seal();
        }

        private static GameState Set(GameState state, ContentPack pack, string target, int valueS)
        {
            StateMutationResult result = new GameStateMutator().Apply(
                state,
                new TargetMutation(TargetPath.Parse(target), TargetOperation.Set, valueS),
                pack.TargetConfigCatalog);
            Assert.That(result.Success, Is.True, target);
            return result.State;
        }

        private static int ReadMetric(GameState state, string target)
        {
            TargetPath path = TargetPath.Parse(target);
            return state.MetricsById[path[1]].ValueS;
        }

        private static int ReadInternal(GameState state, string target)
        {
            TargetPath path = TargetPath.Parse(target);
            return state.InternalsByDomain[path[1]].ComponentsById[path[2]].ValueS;
        }

        private static TargetCausalSnapshot FindTarget(TickCausalSnapshot snapshot, string target)
        {
            TargetPath path = TargetPath.Parse(target);
            for (int i = 0; i < snapshot.AuditedTargets.Count; i++)
            {
                if (snapshot.AuditedTargets[i].Target == path)
                {
                    return snapshot.AuditedTargets[i];
                }
            }

            Assert.Fail("Missing causal target: " + target);
            return null;
        }

        private static void AssertContributionsExact(TickCausalSnapshot snapshot, string target, ExpectedContribution[] expected, string label)
        {
            AssertContributionsExact(FindTarget(snapshot, target), expected, label);
        }

        private static void AssertContributionsExact(TargetCausalSnapshot target, ExpectedContribution[] expected, string label)
        {
            ExpectedContribution[] sortedExpected = SortedExpected(expected);
            Assert.That(target.Contributions.Count, Is.EqualTo(sortedExpected.Length), label);
            long sum = 0;
            for (int i = 0; i < sortedExpected.Length; i++)
            {
                string actualCause = target.Contributions[i].Cause.CanonicalKey;
                Assert.That(actualCause, Is.EqualTo(sortedExpected[i].cause), label + " cause " + i);
                Assert.That(target.Contributions[i].DeltaS, Is.EqualTo(sortedExpected[i].deltaS), label + " delta " + i);
                Assert.That(CountColon(actualCause), Is.EqualTo(1), actualCause);
                sum += target.Contributions[i].DeltaS;
            }

            Assert.That(sum, Is.EqualTo(target.DeltaTotalS), label + " telescopic");
        }

        private static ExpectedContribution[] SortedExpected(ExpectedContribution[] expected)
        {
            ExpectedContribution[] result = new ExpectedContribution[expected.Length];
            Array.Copy(expected, result, expected.Length);
            Array.Sort(result, (left, right) => string.Compare(left.cause, right.cause, StringComparison.Ordinal));
            return result;
        }

        private static void AssertAggregationError(TestDelegate action, string code)
        {
            AggregationExecutionException exception = Assert.Throws<AggregationExecutionException>(action);
            Assert.That(exception.Code, Is.EqualTo(code));
        }

        private static int CountColon(string value)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == ':')
                {
                    count++;
                }
            }

            return count;
        }

        private static List<string> Ids<T>(IReadOnlyList<T> values, Func<T, string> selector)
        {
            List<string> result = new List<string>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                result.Add(selector(values[i]));
            }

            return result;
        }

        private static AggregationExecutionVectorFile ReadVectors()
        {
            return JsonUtility.FromJson<AggregationExecutionVectorFile>(File.ReadAllText(Path.Combine(ProjectRoot(), "tests", "aggregation", "aggregation_execution_v1_vectors.json")));
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

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", ".."));
        }

        private static string ContentRoot()
        {
            return Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "content");
        }

        [Serializable]
        private sealed class AggregationExecutionVectorFile
        {
            public ReversionCase[] reversion_cases;
            public DerivedCase[] derived_cases;
            public MetricCase[] metric_cases;
            public AggregationExecutionEndToEndCase end_to_end_case;
        }

        [Serializable]
        private sealed class ReversionCase
        {
            public string id;
            public int currentS;
            public int alpha_ppm;
            public ReversionExpected expected;
        }

        [Serializable]
        private sealed class ReversionExpected
        {
            public int distanceS;
            public int rounded_deltaS;
            public int finalS;
        }

        [Serializable]
        private sealed class DerivedCase
        {
            public string id;
            public string kind;
            public int[] inputs;
            public int input;
            public int expected;
        }

        [Serializable]
        private sealed class MetricCase
        {
            public string id;
            public string metric;
            public int current_metricS;
            public int alpha_ppm;
            public int cap_per_weekS;
            public MetricComponent[] components;
            public MetricExpected expected;
        }

        [Serializable]
        private sealed class MetricComponent
        {
            public string target;
            public int componentS;
            public int weight_ppm;
        }

        [Serializable]
        private sealed class MetricExpected
        {
            public int weighted_offsetS;
            public int targetS;
            public int elastic_deltaS;
            public int capped_deltaS;
            public int pre_finalS;
            public int finalS;
            public int delta_totalS;
            public int[] F_values;
            public ExpectedContribution[] contributions;
        }

        [Serializable]
        private sealed class AggregationExecutionEndToEndCase
        {
            public string id;
            public TargetValue[] initial_metric_values;
            public TargetValue[] initial_internal_overrides;
            public TargetValue[] expected_final_metric_values;
            public TargetValue[] expected_final_internal_overrides;
            public ExpectedMetricContributions[] expected_metric_contributions;
        }

        [Serializable]
        private sealed class TargetValue
        {
            public string target;
            public int valueS;
        }

        [Serializable]
        private sealed class ExpectedMetricContributions
        {
            public string target;
            public ExpectedContribution[] contributions;
        }

        [Serializable]
        private sealed class ExpectedContribution
        {
            public string cause;
            public long deltaS;
        }
    }
}
