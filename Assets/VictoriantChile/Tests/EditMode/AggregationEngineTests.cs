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

            for (int i = 0; i < fixture.expected_metric_contributions.Length; i++)
            {
                ExpectedMetricContributions metric = fixture.expected_metric_contributions[i];
                TargetCausalSnapshot target = FindTarget(snapshot, metric.target);
                Dictionary<string, long> actual = ContributionsByCause(target);
                for (int j = 0; j < metric.contributions.Length; j++)
                {
                    ExpectedContribution expected = metric.contributions[j];
                    string cause = expected.cause;
                    Assert.That(actual.ContainsKey(cause), Is.True, cause);
                    Assert.That(actual[cause], Is.EqualTo(expected.deltaS), cause);
                    Assert.That(CountColon(cause), Is.EqualTo(1), cause);
                }
            }
        }

        [Test]
        public void AggregationCauseRefsArePrecompiledAndExact()
        {
            AggregationRuntimePlan plan = LoadRealPack().AggregationRuntimePlan;

            Assert.That(plan.GetReversionCause(TargetPath.Parse("internals.economy.growth")).CanonicalKey, Is.EqualTo("SYSTEM:REVERSION.internals.economy.growth"));
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

        private static Dictionary<string, long> ContributionsByCause(TargetCausalSnapshot target)
        {
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.Ordinal);
            for (int i = 0; i < target.Contributions.Count; i++)
            {
                result.Add(target.Contributions[i].Cause.CanonicalKey, target.Contributions[i].DeltaS);
            }

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
            public AggregationExecutionEndToEndCase end_to_end_case;
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
