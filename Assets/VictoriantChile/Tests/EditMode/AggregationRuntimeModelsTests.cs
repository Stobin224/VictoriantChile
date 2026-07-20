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
using VictoriantChile.Simulation.Core.Aggregation;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class AggregationRuntimeModelsTests
    {
        private static readonly string[] PrimaryMetrics =
        {
            "metrics.economy",
            "metrics.security",
            "metrics.social_tension",
            "metrics.public_agenda",
            "metrics.information_quality",
            "metrics.governability",
            "metrics.legislative_capacity",
            "metrics.party_organization",
            "metrics.internal_cohesion"
        };

        private static readonly string[][] ComponentTargets =
        {
            new[] { "internals.economy.growth", "internals.economy.unemployment", "internals.economy.inflation", "internals.economy.fiscal_stability" },
            new[] { "internals.security.police_capacity", "internals.security.crime_rate", "internals.security.violent_crime", "internals.security.organized_crime" },
            new[] { "internals.tension.cost_of_living", "internals.tension.polarization", "internals.tension.protest_activity", "internals.tension.institutional_trust" },
            new[] { "internals.agenda.media_heat", "internals.agenda.policy_conflict", "internals.agenda.movement_salience" },
            new[] { "internals.info.intel_capacity", "internals.info.media_noise", "internals.info.institutional_access" },
            new[] { "internals.gov.bureaucracy_capacity", "internals.gov.budget_flexibility", "internals.gov.execution_focus", "internals.gov.legal_friction" },
            new[] { "internals.leg.coalition_strength", "internals.leg.party_discipline", "internals.leg.opposition_obstruction", "internals.leg.senate_inertia" },
            new[] { "internals.party.field_ops", "internals.party.funding", "internals.party.cadre_quality", "internals.party.internal_scandal" },
            new[] { "internals.cohesion.factionalism", "internals.cohesion.leadership_unity", "internals.cohesion.discipline_culture", "internals.cohesion.ambition_rivalries" },
            new[] { "internals.legitimacy.performance", "internals.legitimacy.integrity", "internals.legitimacy.scandal_pressure", "internals.legitimacy.social_tension_load" }
        };

        private static readonly int[][] ComponentWeights =
        {
            new[] { 350000, -250000, -250000, 150000 },
            new[] { 350000, -250000, -250000, -150000 },
            new[] { 350000, 250000, 250000, -150000 },
            new[] { 400000, 300000, 300000 },
            new[] { 450000, -350000, 200000 },
            new[] { 350000, 250000, 200000, -200000 },
            new[] { 350000, 350000, -200000, -100000 },
            new[] { 300000, 250000, 250000, -200000 },
            new[] { -350000, 300000, 200000, -150000 },
            new[] { 350000, 250000, -200000, -200000 }
        };

        [Test]
        public void RealPackExposesExactRuntimeShape()
        {
            AggregationRuntimePlan plan = LoadRealPack().AggregationRuntimePlan;

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.Scale, Is.EqualTo(100));
            Assert.That(plan.MidS, Is.EqualTo(5000));
            Assert.That(AggregationRuntimePlan.PpmDenominator, Is.EqualTo(1000000));
            Assert.That(plan.Rounding, Is.EqualTo(AggregationRoundingModeRuntime.HalfAwayFromZero));
            Assert.That(plan.InternalReversion.Groups.Count, Is.EqualTo(10));
            Assert.That(plan.InternalReversion.SkipTargets.Count, Is.EqualTo(2));
            Assert.That(plan.DerivedInternals.Rules.Count, Is.EqualTo(2));
            Assert.That(plan.PrimaryMetrics.Metrics.Count, Is.EqualTo(9));
            Assert.That(plan.Legitimacy.Metrics.Count, Is.EqualTo(1));
            Assert.That(plan.Legitimacy.Metrics[0].Metric, Is.EqualTo(TargetPath.Parse("metrics.legitimacy")));
        }

        [Test]
        public void RuntimeApiHasCanonicalPassNames()
        {
            AggregationRuntimePlan plan = BuildPack(BuildCanonicalConfig()).AggregationRuntimePlan;

            Assert.That(plan.InternalReversion, Is.TypeOf<AggregationReversionPassRuntime>());
            Assert.That(plan.DerivedInternals, Is.TypeOf<AggregationDerivedPassRuntime>());
            Assert.That(plan.PrimaryMetrics, Is.TypeOf<AggregationMetricsPassRuntime>());
            Assert.That(plan.Legitimacy, Is.TypeOf<AggregationMetricsPassRuntime>());
            Assert.That(typeof(AggregationRuntimePlan).GetProperty("CausePrefix"), Is.Null);
            Assert.That(typeof(AggregationCauseMaterializer).GetMethod("ResolveBaseFromCauseRef"), Is.Null);
        }

        [Test]
        public void RealPackPreservesCanonicalReversionDerivedAndMetricOrder()
        {
            AggregationRuntimePlan plan = LoadRealPack().AggregationRuntimePlan;
            string[] groups =
            {
                "internals.economy.*",
                "internals.security.*",
                "internals.tension.*",
                "internals.agenda.*",
                "internals.info.*",
                "internals.gov.*",
                "internals.leg.*",
                "internals.party.*",
                "internals.cohesion.*",
                "internals.legitimacy.*"
            };

            for (int i = 0; i < groups.Length; i++)
            {
                Assert.That(plan.InternalReversion.Groups[i].Pattern.ToString(), Is.EqualTo(groups[i]));
            }

            Assert.That(plan.InternalReversion.SkipTargets[0].ToString(), Is.EqualTo("internals.legitimacy.performance"));
            Assert.That(plan.InternalReversion.SkipTargets[1].ToString(), Is.EqualTo("internals.legitimacy.social_tension_load"));
            Assert.That(plan.DerivedInternals.Rules[0].Target.ToString(), Is.EqualTo("internals.legitimacy.performance"));
            Assert.That(plan.DerivedInternals.Rules[0].Expression.Kind, Is.EqualTo(AggregationExpressionKindRuntime.Avg));
            Assert.That(plan.DerivedInternals.Rules[0].Expression.Targets[0].ToString(), Is.EqualTo("metrics.economy"));
            Assert.That(plan.DerivedInternals.Rules[0].Expression.Targets[1].ToString(), Is.EqualTo("metrics.security"));
            Assert.That(plan.DerivedInternals.Rules[0].Expression.Targets[2].ToString(), Is.EqualTo("metrics.governability"));
            Assert.That(plan.DerivedInternals.Rules[1].Target.ToString(), Is.EqualTo("internals.legitimacy.social_tension_load"));
            Assert.That(plan.DerivedInternals.Rules[1].Expression.Kind, Is.EqualTo(AggregationExpressionKindRuntime.Copy));
            Assert.That(plan.DerivedInternals.Rules[1].Expression.Target.Value.ToString(), Is.EqualTo("metrics.social_tension"));

            for (int i = 0; i < PrimaryMetrics.Length; i++)
            {
                Assert.That(plan.PrimaryMetrics.Metrics[i].Metric.ToString(), Is.EqualTo(PrimaryMetrics[i]));
            }
        }

        [Test]
        public void RealPackPreservesExactComponentOrderForAllTenMetrics()
        {
            AggregationRuntimePlan plan = LoadRealPack().AggregationRuntimePlan;
            AggregationMetricRuntime[] metrics = AllMetrics(plan);

            for (int m = 0; m < metrics.Length; m++)
            {
                Assert.That(metrics[m].Components.Count, Is.EqualTo(ComponentTargets[m].Length), metrics[m].Metric.ToString());
                for (int c = 0; c < ComponentTargets[m].Length; c++)
                {
                    Assert.That(metrics[m].Components[c].Target.ToString(), Is.EqualTo(ComponentTargets[m][c]));
                    Assert.That(metrics[m].Components[c].WeightPpm, Is.EqualTo(ComponentWeights[m][c]));
                }
            }
        }

        [Test]
        public void LookupIsExactUnionOfPrimaryAndLegitimacyMetrics()
        {
            AggregationRuntimePlan plan = LoadRealPack().AggregationRuntimePlan;
            Assert.That(plan.MetricsByTarget, Is.InstanceOf<ReadOnlyDictionary<TargetPath, AggregationMetricRuntime>>());
            Assert.That(plan.MetricsByTarget.Count, Is.EqualTo(10));

            foreach (AggregationMetricRuntime metric in AllMetrics(plan))
            {
                Assert.That(plan.TryGetMetric(metric.Metric, out AggregationMetricRuntime found), Is.True);
                Assert.That(ReferenceEquals(found, metric), Is.True, metric.Metric.ToString());
                Assert.That(plan.MetricsByTarget[metric.Metric], Is.SameAs(metric));
            }

            Assert.That(plan.TryGetMetric(TargetPath.Parse("internals.economy.growth"), out AggregationMetricRuntime absent), Is.False);
            Assert.That(absent, Is.Null);
            Assert.That(plan.TryGetMetric(default, out absent), Is.False);
            Assert.That(absent, Is.Null);
            Assert.That(() => ((IDictionary<TargetPath, AggregationMetricRuntime>)plan.MetricsByTarget)
                .Add(TargetPath.Parse("metrics.other"), plan.PrimaryMetrics.Metrics[0]), Throws.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void DispatchAAndBIgnorePhysicalPassOrderButPreserveProductiveContent()
        {
            AggregationConfig configA = BuildCanonicalConfig();
            AggregationPass[] passes = CopyPasses(configA);
            AggregationConfig configB = new AggregationConfig(
                1,
                100,
                5000,
                ContentRoundingMode.HalfAwayFromZero,
                new[] { passes[2], passes[3], passes[0], passes[1] });

            AggregationRuntimePlan planA = BuildPack(configA).AggregationRuntimePlan;
            AggregationRuntimePlan planB = BuildPack(configB).AggregationRuntimePlan;

            AssertPlansEquivalent(planA, planB);
            Assert.That(SerializePlan(planB), Is.EqualTo(SerializePlan(planA)));
        }

        [Test]
        public void SourceMutationResistanceCoversAllMutableInputs()
        {
            MutableSources sources = BuildMutableSources();
            AggregationRuntimePlan plan = BuildPack(sources.Config).AggregationRuntimePlan;
            string before = SerializePlan(plan);

            sources.Groups.Clear();
            sources.Groups.Add(new AggregationReversionGroup(TargetPattern.Parse("internals.changed.*"), 1, 1));
            sources.SkipTargets[0] = TargetPath.Parse("internals.changed.skip");
            sources.PrimaryMetrics.Clear();
            sources.PrimaryMetrics.Add(Metric("metrics.changed", 0));
            sources.LegitimacyMetrics[0] = Metric("metrics.changed_legitimacy", 0);
            sources.EconomyComponents[0] = new WeightedTargetComponent(TargetPath.Parse("internals.changed.component"), 1000000);
            sources.ExpressionTargets[0] = TargetPath.Parse("metrics.changed_source");
            sources.Rules.Clear();
            sources.Rules.Add(new DerivedAggregationRule(
                TargetPath.Parse("internals.changed.derived"),
                TargetOperation.Set,
                new AggregationExpression(AggregationExpressionKind.Copy, TargetPath.Parse("metrics.economy"), Array.Empty<TargetPath>())));
            sources.Passes.Clear();

            Assert.That(SerializePlan(plan), Is.EqualTo(before));
            Assert.That(plan.PrimaryMetrics.Metrics.Count, Is.EqualTo(9));
            Assert.That(plan.PrimaryMetrics.Metrics[0].Metric.ToString(), Is.EqualTo("metrics.economy"));
            Assert.That(plan.PrimaryMetrics.Metrics[0].Components[0].Target.ToString(), Is.EqualTo("internals.economy.growth"));
        }

        [Test]
        public void RuntimeCollectionsAreReadOnlyAtEveryLevel()
        {
            AggregationRuntimePlan plan = BuildPack(BuildCanonicalConfig()).AggregationRuntimePlan;

            Assert.That(plan.InternalReversion.Groups, Is.InstanceOf<ReadOnlyCollection<AggregationReversionGroupRuntime>>());
            Assert.That(plan.InternalReversion.SkipTargets, Is.InstanceOf<ReadOnlyCollection<TargetPath>>());
            Assert.That(plan.DerivedInternals.Rules, Is.InstanceOf<ReadOnlyCollection<DerivedAggregationRuleRuntime>>());
            Assert.That(plan.DerivedInternals.Rules[0].Expression.Targets, Is.InstanceOf<ReadOnlyCollection<TargetPath>>());
            Assert.That(plan.PrimaryMetrics.Metrics, Is.InstanceOf<ReadOnlyCollection<AggregationMetricRuntime>>());
            Assert.That(plan.Legitimacy.Metrics, Is.InstanceOf<ReadOnlyCollection<AggregationMetricRuntime>>());

            Assert.That(() => ((IList)plan.InternalReversion.Groups).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IList)plan.InternalReversion.SkipTargets).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IList)plan.DerivedInternals.Rules).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IList)plan.DerivedInternals.Rules[0].Expression.Targets).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IList)plan.PrimaryMetrics.Metrics).Clear(), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => ((IList)plan.Legitimacy.Metrics).Clear(), Throws.InstanceOf<NotSupportedException>());

            foreach (AggregationMetricRuntime metric in AllMetrics(plan))
            {
                Assert.That(metric.Components, Is.InstanceOf<ReadOnlyCollection<WeightedTargetComponentRuntime>>());
                Assert.That(() => ((IList)metric.Components).Clear(), Throws.InstanceOf<NotSupportedException>());
            }
        }

        [Test]
        public void ForwardPathUsesClosedPrecompiledCauseRefs()
        {
            AggregationRuntimePlan plan = LoadRealPack().AggregationRuntimePlan;
            AggregationMetricRuntime economy = plan.PrimaryMetrics.Metrics[0];
            WeightedTargetComponentRuntime growth = economy.Components[0];
            DerivedAggregationRuleRuntime performance = plan.DerivedInternals.Rules[0];

            Assert.That(plan.InternalReversion.CauseBase, Is.EqualTo(AggregationCauseBase.Reversion));
            Assert.That(plan.InternalReversion.MaterializeCause(TargetPath.Parse("internals.economy.growth")).CanonicalKey,
                Is.EqualTo("SYSTEM:REVERSION.internals.economy.growth"));
            Assert.That(performance.Cause.CanonicalKey, Is.EqualTo("SYSTEM:DERIVED.internals.legitimacy.performance"));
            Assert.That(economy.BaseCause.CanonicalKey, Is.EqualTo("SYSTEM:AGG.metrics.economy"));
            Assert.That(growth.Cause.CanonicalKey, Is.EqualTo("SYSTEM:AGG.metrics.economy.internals.economy.growth"));

            Assert.That(plan.GetReversionCause(TargetPath.Parse("internals.economy.growth")).CanonicalKey,
                Is.EqualTo("SYSTEM:REVERSION.internals.economy.growth"));
            Assert.That(plan.GetDerivedCause(performance.Target), Is.SameAs(performance.Cause));
            Assert.That(plan.GetMetricCause(economy.Metric), Is.SameAs(economy.BaseCause));
            Assert.That(plan.GetComponentCause(economy.Metric, growth.Target), Is.SameAs(growth.Cause));
        }

        [TestCaseSource(nameof(AllMetricCauseCases))]
        public void RealPackPrecompilesMetricAndComponentCauses(int metricIndex, string metricPath, string componentPath, string expectedCause)
        {
            AggregationRuntimePlan plan = LoadRealPack().AggregationRuntimePlan;
            AggregationMetricRuntime metric = AllMetrics(plan)[metricIndex];

            Assert.That(metric.Metric.ToString(), Is.EqualTo(metricPath));
            if (componentPath == null)
            {
                Assert.That(metric.BaseCause.CanonicalKey, Is.EqualTo(expectedCause));
                Assert.That(plan.GetMetricCause(metric.Metric), Is.SameAs(metric.BaseCause));
                return;
            }

            bool found = false;
            for (int i = 0; i < metric.Components.Count; i++)
            {
                WeightedTargetComponentRuntime component = metric.Components[i];
                if (component.Target.ToString() == componentPath)
                {
                    Assert.That(component.Cause.CanonicalKey, Is.EqualTo(expectedCause));
                    Assert.That(plan.GetComponentCause(metric.Metric, component.Target), Is.SameAs(component.Cause));
                    found = true;
                }
            }

            Assert.That(found, Is.True, componentPath);
        }

        [Test]
        public void AggregationCauseBaseIsClosedAndNoCanonicalCauseHasSecondColon()
        {
            AggregationRuntimePlan plan = LoadRealPack().AggregationRuntimePlan;

            Assert.That(Enum.GetNames(typeof(AggregationCauseBase)).Length, Is.EqualTo(3));
            Assert.That(Enum.IsDefined(typeof(AggregationCauseBase), AggregationCauseBase.Reversion), Is.True);
            Assert.That(Enum.IsDefined(typeof(AggregationCauseBase), AggregationCauseBase.Derived), Is.True);
            Assert.That(Enum.IsDefined(typeof(AggregationCauseBase), AggregationCauseBase.Aggregation), Is.True);

            AssertOneColon(plan.InternalReversion.MaterializeCause(TargetPath.Parse("internals.economy.growth")));
            foreach (DerivedAggregationRuleRuntime rule in plan.DerivedInternals.Rules)
            {
                AssertOneColon(rule.Cause);
            }

            foreach (AggregationMetricRuntime metric in AllMetrics(plan))
            {
                AssertOneColon(metric.BaseCause);
                foreach (WeightedTargetComponentRuntime component in metric.Components)
                {
                    AssertOneColon(component.Cause);
                }
            }
        }

        [Test]
        public void MaterializerRejectsInvalidTargetsAndUnknownBase()
        {
            Assert.That(() => AggregationCauseMaterializer.Materialize(AggregationCauseBase.Reversion, default), Throws.ArgumentException);
            Assert.That(() => AggregationCauseMaterializer.Materialize(AggregationCauseBase.Derived, default), Throws.ArgumentException);
            Assert.That(() => AggregationCauseMaterializer.Materialize(AggregationCauseBase.Aggregation, default), Throws.ArgumentException);
            Assert.That(() => AggregationCauseMaterializer.MaterializeAggregationComponent(default, TargetPath.Parse("internals.test.base")), Throws.ArgumentException);
            Assert.That(() => AggregationCauseMaterializer.MaterializeAggregationComponent(TargetPath.Parse("metrics.test"), default), Throws.ArgumentException);
            Assert.That(() => AggregationCauseMaterializer.Materialize((AggregationCauseBase)99, TargetPath.Parse("metrics.test")), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [TestCase(101, 5000, ContentRoundingMode.HalfAwayFromZero, TestName = "ProgrammaticScaleMustBeExact")]
        [TestCase(100, 4999, ContentRoundingMode.HalfAwayFromZero, TestName = "ProgrammaticMidpointMustBeExact")]
        [TestCase(100, 5000, (ContentRoundingMode)99, TestName = "ProgrammaticRoundingMustBeDefined")]
        public void ProgrammaticConfigRejectsInvalidConstants(int scale, int midS, ContentRoundingMode rounding)
        {
            Assert.That(() => BuildPack(BuildCanonicalConfig(scale: scale, midS: midS, rounding: rounding)), Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void ProgrammaticConfigRejectsReversionMidpointMismatch()
        {
            AggregationPass[] passes = CopyPasses(BuildCanonicalConfig());
            passes[0] = ReversionPass(midS: 4999);

            Assert.That(() => BuildPack(BuildCanonicalConfig(passes: passes)), Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void ProgrammaticConfigRejectsPrimaryCountLegitimacyShapeAndDuplicates()
        {
            AggregationPass[] primaryTooShort = CopyPasses(BuildCanonicalConfig());
            primaryTooShort[1] = MetricsPass(new[] { Metric("metrics.economy", 0) });
            Assert.That(() => BuildPack(BuildCanonicalConfig(passes: primaryTooShort)), Throws.InstanceOf<InvalidOperationException>());

            AggregationPass[] badLegitimacy = CopyPasses(BuildCanonicalConfig());
            badLegitimacy[3] = MetricsPass(new[] { Metric("metrics.not_legitimacy", 0) });
            Assert.That(() => BuildPack(BuildCanonicalConfig(passes: badLegitimacy)), Throws.InstanceOf<InvalidOperationException>());

            AggregationPass[] duplicateMetric = CopyPasses(BuildCanonicalConfig());
            AggregationMetric[] metrics = PrimaryMetricArray();
            metrics[1] = Metric("metrics.economy", 1);
            duplicateMetric[1] = MetricsPass(metrics);
            Assert.That(() => BuildPack(BuildCanonicalConfig(passes: duplicateMetric)), Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void RuntimeModelsRejectInvalidCollectionsAndEnums()
        {
            Assert.That(() => new AggregationReversionPassRuntime(
                new[] { new AggregationReversionGroupRuntime(TargetPattern.Parse("internals.test.*"), 1, 1) },
                new[] { default(TargetPath) }), Throws.ArgumentException);
            Assert.That(() => new AggregationReversionPassRuntime(
                new AggregationReversionGroupRuntime[] { null },
                Array.Empty<TargetPath>()), Throws.ArgumentException);
            Assert.That(() => new AggregationDerivedPassRuntime(new DerivedAggregationRuleRuntime[] { null }), Throws.ArgumentException);
            Assert.That(() => new AggregationMetricsPassRuntime(new AggregationMetricRuntime[] { null }), Throws.ArgumentException);
            Assert.That(() => new AggregationMetricRuntime(
                default,
                1,
                1,
                0,
                new[] { new WeightedTargetComponentRuntime(TargetPath.Parse("internals.test.base"), 1000000) }), Throws.ArgumentException);
            Assert.That(() => new WeightedTargetComponentRuntime(default, 1000000), Throws.ArgumentException);
            Assert.That(() => new DerivedAggregationRuleRuntime(
                TargetPath.Parse("internals.test.derived"),
                (TargetOperation)99,
                new AggregationExpressionRuntime(AggregationExpressionKindRuntime.Copy, TargetPath.Parse("metrics.economy"), Array.Empty<TargetPath>())), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new AggregationExpressionRuntime((AggregationExpressionKindRuntime)99, null, null), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RuntimeModelsRejectExpressionShapesAndRanges()
        {
            Assert.That(() => new AggregationExpressionRuntime(
                AggregationExpressionKindRuntime.Copy,
                TargetPath.Parse("metrics.economy"),
                new[] { TargetPath.Parse("metrics.security") }), Throws.ArgumentException);
            Assert.That(() => new AggregationExpressionRuntime(
                AggregationExpressionKindRuntime.Avg,
                TargetPath.Parse("metrics.economy"),
                new[] { TargetPath.Parse("metrics.security") }), Throws.ArgumentException);
            Assert.That(() => new AggregationExpressionRuntime(
                AggregationExpressionKindRuntime.Avg,
                null,
                Array.Empty<TargetPath>()), Throws.ArgumentException);
            Assert.That(() => new AggregationExpressionRuntime(
                AggregationExpressionKindRuntime.Avg,
                null,
                new[] { default(TargetPath) }), Throws.ArgumentException);
            Assert.That(() => new DerivedAggregationRuleRuntime(
                TargetPath.Parse("internals.test.derived"),
                TargetOperation.Add,
                new AggregationExpressionRuntime(AggregationExpressionKindRuntime.Copy, TargetPath.Parse("metrics.economy"), Array.Empty<TargetPath>())), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new AggregationReversionGroupRuntime(TargetPattern.Parse("internals.test.*"), 1, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new AggregationMetricRuntime(
                TargetPath.Parse("metrics.test"),
                1,
                AggregationRuntimePlan.PpmDenominator + 1,
                0,
                new[] { new WeightedTargetComponentRuntime(TargetPath.Parse("internals.test.base"), 1000000) }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new AggregationMetricRuntime(
                TargetPath.Parse("metrics.test"),
                1,
                1,
                -1,
                new[] { new WeightedTargetComponentRuntime(TargetPath.Parse("internals.test.base"), 1000000) }), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RuntimeModelsRejectDuplicateComponentsAndMetrics()
        {
            Assert.That(() => new AggregationMetricRuntime(
                TargetPath.Parse("metrics.test"),
                1,
                1,
                0,
                new[]
                {
                    new WeightedTargetComponentRuntime(TargetPath.Parse("internals.test.base"), 500000),
                    new WeightedTargetComponentRuntime(TargetPath.Parse("internals.test.base"), 500000)
                }), Throws.ArgumentException);

            AggregationMetricRuntime metric = RuntimeMetric("metrics.test", "internals.test.base");
            Assert.That(() => new AggregationMetricsPassRuntime(new[] { metric, metric }), Throws.ArgumentException);
        }

        [TestCase("\"scale\": 100", "\"scale\": 101", ContentDiagnosticCode.InvalidRange, TestName = "LoaderScaleMustBeExact")]
        [TestCase("\"midS\": 5000", "\"midS\": 4999", ContentDiagnosticCode.InvalidRange, TestName = "LoaderMidpointMustBeExact")]
        [TestCase("\"rounding\": \"HALF_AWAY_FROM_ZERO\"", "\"rounding\": \"BANKERS\"", ContentDiagnosticCode.InvalidEnum, TestName = "LoaderRoundingMustBeExact")]
        [TestCase("\"midS\": 5000", "\"midS\": 4999", ContentDiagnosticCode.InvalidRange, TestName = "LoaderReversionMidpointMustBeExact")]
        public void LoaderInvalidConstantsFailWithNullPackAndStableDiagnostic(string oldText, string newText, ContentDiagnosticCode code)
        {
            Dictionary<string, byte[]> files = FixtureFromReal();
            files["rules/aggregation_config.json"] = Bytes(Text(files["rules/aggregation_config.json"]).Replace(oldText, newText));

            AssertFailure(LoadInMemory(RebuildManifest(files)), code);
        }

        [TestCase("AGG")]
        [TestCase("SYSTEM:AGG:EXTRA")]
        [TestCase("EVENT:AGG")]
        [TestCase("SYSTEM:")]
        [TestCase("SYSTEM:UNKNOWN")]
        [TestCase("system:AGG")]
        [TestCase("SYSTEM:AGG.")]
        public void LoaderInvalidPrefixesFailWithNullPackAndStableDiagnostic(string prefix)
        {
            Dictionary<string, byte[]> files = FixtureFromReal();
            string aggJson = Text(files["rules/aggregation_config.json"]).Replace("\"cause_prefix\": \"SYSTEM:AGG\"", "\"cause_prefix\": \"" + prefix + "\"");
            files["rules/aggregation_config.json"] = Bytes(aggJson);

            AssertFailure(LoadInMemory(RebuildManifest(files)), ContentDiagnosticCode.AggregationInvalidPrefix);
        }

        [Test]
        public void LoaderCorrectPrefixOnWrongPassTypeFailsWithNullPack()
        {
            Dictionary<string, byte[]> files = FixtureFromReal();
            string aggJson = ReplaceDerivedPrefix(Text(files["rules/aggregation_config.json"]), "SYSTEM:REVERSION");
            files["rules/aggregation_config.json"] = Bytes(aggJson);

            AssertFailure(LoadInMemory(RebuildManifest(files)), ContentDiagnosticCode.AggregationInvalidPrefix);
        }

        [TestCase("INTERNAL_REVERSION", false, ContentDiagnosticCode.AggregationMissingRequiredPassType, TestName = "LoaderMissingReversionPassFails")]
        [TestCase("INTERNAL_REVERSION", true, ContentDiagnosticCode.AggregationMissingRequiredPassType, TestName = "LoaderDuplicateReversionPassFails")]
        [TestCase("DERIVED_INTERNALS", false, ContentDiagnosticCode.AggregationMissingRequiredPassType, TestName = "LoaderMissingDerivedPassFails")]
        [TestCase("DERIVED_INTERNALS", true, ContentDiagnosticCode.AggregationMissingRequiredPassType, TestName = "LoaderDuplicateDerivedPassFails")]
        [TestCase("METRIC_AGGREGATION", true, ContentDiagnosticCode.AggregationPassFieldConflict, TestName = "LoaderDuplicateMetricPassFailsBeforeOverwrite")]
        public void LoaderPassCardinalityFailuresAreClosed(string passType, bool duplicate, ContentDiagnosticCode code)
        {
            Dictionary<string, byte[]> files = FixtureFromReal();
            string json = Text(files["rules/aggregation_config.json"]);
            files["rules/aggregation_config.json"] = Bytes(duplicate ? DuplicatePass(json, passType) : RemovePassByType(json, passType));

            AssertFailure(LoadInMemory(RebuildManifest(files)), code);
        }

        [Test]
        public void LoaderSingleMetricPassFailsWithNullPack()
        {
            Dictionary<string, byte[]> files = FixtureFromReal();
            files["rules/aggregation_config.json"] = Bytes(RemoveSecondMetricPass(Text(files["rules/aggregation_config.json"])));

            AssertFailure(LoadInMemory(RebuildManifest(files)), ContentDiagnosticCode.AggregationMissingRequiredPassType);
        }

        [Test]
        public void LoaderSplitBrainDuplicatePassMetricFailsBeforePlanExposure()
        {
            Dictionary<string, byte[]> files = FixtureFromReal();
            string json = Text(files["rules/aggregation_config.json"]);
            string duplicatePrimary = ",{\"type\":\"METRIC_AGGREGATION\",\"cause_prefix\":\"SYSTEM:AGG\",\"log_components\":true,\"weights_abs_sum_ppm_required\":1000000,\"metrics\":[{\"metric\":\"metrics.economy\",\"half_life_weeks\":8,\"alpha_ppm\":82996,\"cap_per_weekS\":200,\"components\":[{\"target\":\"internals.economy.growth\",\"weight_ppm\":1000000}]}]}";
            files["rules/aggregation_config.json"] = Bytes(json.Insert(json.LastIndexOf("\n  ]", StringComparison.Ordinal), duplicatePrimary));

            AssertFailure(LoadInMemory(RebuildManifest(files)), ContentDiagnosticCode.AggregationPassFieldConflict);
        }

        [Test]
        public void LoaderDuplicateTargetsAndShapeFailuresAreClosed()
        {
            Dictionary<string, byte[]> duplicateMetric = FixtureFromReal();
            duplicateMetric["rules/aggregation_config.json"] = Bytes(Text(duplicateMetric["rules/aggregation_config.json"]).Replace("\"metric\": \"metrics.legitimacy\"", "\"metric\": \"metrics.economy\""));
            AssertFailure(LoadInMemory(RebuildManifest(duplicateMetric)), ContentDiagnosticCode.AggregationPassFieldConflict);

            Dictionary<string, byte[]> duplicateDerived = FixtureFromReal();
            string derivedJson = Text(duplicateDerived["rules/aggregation_config.json"]);
            int derivedType = derivedJson.IndexOf("\"type\": \"DERIVED_INTERNALS\"", StringComparison.Ordinal);
            int rulesKey = derivedJson.IndexOf("\"rules\"", derivedType, StringComparison.Ordinal);
            int rulesClose = FindArrayEnd(derivedJson, derivedJson.IndexOf('[', rulesKey));
            derivedJson = derivedJson.Insert(rulesClose, ",{\"target\":\"internals.legitimacy.performance\",\"op\":\"SET\",\"expr\":{\"kind\":\"COPY\",\"target\":\"metrics.economy\"}}");
            duplicateDerived["rules/aggregation_config.json"] = Bytes(derivedJson);
            AssertFailure(LoadInMemory(RebuildManifest(duplicateDerived)), ContentDiagnosticCode.AggregationPassFieldConflict);

            Dictionary<string, byte[]> duplicatePattern = FixtureFromReal();
            string patternJson = Text(duplicatePattern["rules/aggregation_config.json"]);
            string firstGroup = "{ \"pattern\": \"internals.economy.*\", \"half_life_weeks\": 26, \"alpha_ppm\": 26307 }";
            patternJson = patternJson.Replace(firstGroup, firstGroup + "," + firstGroup);
            duplicatePattern["rules/aggregation_config.json"] = Bytes(patternJson);
            AssertFailure(LoadInMemory(RebuildManifest(duplicatePattern)), ContentDiagnosticCode.AggregationPassFieldConflict);
        }

        [Test]
        public void AssemblyReferencesUseActualAsmdefGuids()
        {
            string assets = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Assets", "VictoriantChile"));
            string coreGuid = ReadGuid(Path.Combine(assets, "Simulation", "Core", "VictoriantChile.Simulation.Core.asmdef.meta"));
            string contentGuid = ReadGuid(Path.Combine(assets, "Content", "VictoriantChile.Content.asmdef.meta"));
            string contentAsmdef = File.ReadAllText(Path.Combine(assets, "Content", "VictoriantChile.Content.asmdef"));
            string testsAsmdef = File.ReadAllText(Path.Combine(assets, "Tests", "EditMode", "VictoriantChile.Simulation.Tests.EditMode.asmdef"));

            CollectionAssert.Contains(ReadAsmdefReferences(contentAsmdef), "GUID:" + coreGuid);
            CollectionAssert.Contains(ReadAsmdefReferences(testsAsmdef), "GUID:" + coreGuid);
            CollectionAssert.Contains(ReadAsmdefReferences(testsAsmdef), "GUID:" + contentGuid);
        }

        [Test]
        public void CoreAggregationModuleDoesNotReferenceContentOrExecutionSystems()
        {
            string aggDir = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Assets", "VictoriantChile", "Simulation", "Core", "Aggregation"));
            foreach (string file in Directory.GetFiles(aggDir, "*.cs"))
            {
                string content = File.ReadAllText(file);
                Assert.That(content, Does.Not.Contain("VictoriantChile.Content."));
                Assert.That(content, Does.Not.Contain("GameState"));
                Assert.That(content, Does.Not.Contain("CausalLedger"));
                Assert.That(content, Does.Not.Contain("Scheduler"));
            }
        }

        private static ContentPack LoadRealPack()
        {
            ContentLoadResult result = new ContentPackLoader().Load(new DirectoryContentFileSource(ContentRoot()));
            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            return result.Pack;
        }

        private static ContentPack BuildPack(AggregationConfig config)
        {
            ContentManifest manifest = new ContentManifest("test", 1, 1, 1, "es", new[] { "es" }, new Dictionary<string, string>());
            return new ContentPack(
                manifest,
                Array.Empty<TargetConfig>(),
                Array.Empty<RegionDefinition>(),
                Array.Empty<InterestGroupDefinition>(),
                Array.Empty<MovementDefinition>(),
                null,
                config,
                null,
                Array.Empty<EffectTemplate>(),
                Array.Empty<EventTemplate>(),
                Array.Empty<ReformTemplate>());
        }

        private static AggregationConfig BuildCanonicalConfig(
            int scale = 100,
            int midS = 5000,
            ContentRoundingMode rounding = ContentRoundingMode.HalfAwayFromZero,
            AggregationPass[] passes = null)
        {
            return new AggregationConfig(
                1,
                scale,
                midS,
                rounding,
                passes ?? new[]
                {
                    ReversionPass(),
                    MetricsPass(PrimaryMetricArray()),
                    DerivedPass(),
                    MetricsPass(new[] { LegitimacyMetric() })
                });
        }

        private static MutableSources BuildMutableSources()
        {
            MutableSources sources = new MutableSources();
            sources.Groups = new List<AggregationReversionGroup>
            {
                new AggregationReversionGroup(TargetPattern.Parse("internals.economy.*"), 26, 26307),
                new AggregationReversionGroup(TargetPattern.Parse("internals.security.*"), 20, 34064)
            };
            sources.SkipTargets = new[]
            {
                TargetPath.Parse("internals.legitimacy.performance"),
                TargetPath.Parse("internals.legitimacy.social_tension_load")
            };
            sources.EconomyComponents = new[]
            {
                new WeightedTargetComponent(TargetPath.Parse("internals.economy.growth"), 350000),
                new WeightedTargetComponent(TargetPath.Parse("internals.economy.unemployment"), -250000),
                new WeightedTargetComponent(TargetPath.Parse("internals.economy.inflation"), -250000),
                new WeightedTargetComponent(TargetPath.Parse("internals.economy.fiscal_stability"), 150000)
            };
            sources.PrimaryMetrics = new List<AggregationMetric>(PrimaryMetricArray());
            sources.PrimaryMetrics[0] = new AggregationMetric(TargetPath.Parse("metrics.economy"), 8, 82996, 200, sources.EconomyComponents);
            sources.LegitimacyMetrics = new[] { LegitimacyMetric() };
            sources.ExpressionTargets = new[]
            {
                TargetPath.Parse("metrics.economy"),
                TargetPath.Parse("metrics.security"),
                TargetPath.Parse("metrics.governability")
            };
            sources.Rules = new List<DerivedAggregationRule>
            {
                new DerivedAggregationRule(
                    TargetPath.Parse("internals.legitimacy.performance"),
                    TargetOperation.Set,
                    new AggregationExpression(AggregationExpressionKind.Avg, null, sources.ExpressionTargets)),
                new DerivedAggregationRule(
                    TargetPath.Parse("internals.legitimacy.social_tension_load"),
                    TargetOperation.Set,
                    new AggregationExpression(AggregationExpressionKind.Copy, TargetPath.Parse("metrics.social_tension"), Array.Empty<TargetPath>()))
            };
            sources.Passes = new List<AggregationPass>
            {
                new AggregationPass(AggregationPassType.InternalReversion, "SYSTEM:REVERSION", 5000, sources.Groups, sources.SkipTargets, null, null, null, null),
                new AggregationPass(AggregationPassType.MetricAggregation, "SYSTEM:AGG", null, null, null, true, 1000000, sources.PrimaryMetrics, null),
                new AggregationPass(AggregationPassType.DerivedInternals, "SYSTEM:DERIVED", null, null, null, null, null, null, sources.Rules),
                new AggregationPass(AggregationPassType.MetricAggregation, "SYSTEM:AGG", null, null, null, true, 1000000, sources.LegitimacyMetrics, null)
            };
            sources.Config = new AggregationConfig(1, 100, 5000, ContentRoundingMode.HalfAwayFromZero, sources.Passes);
            return sources;
        }

        private static AggregationPass ReversionPass(int midS = 5000)
        {
            return new AggregationPass(
                AggregationPassType.InternalReversion,
                "SYSTEM:REVERSION",
                midS,
                new[]
                {
                    new AggregationReversionGroup(TargetPattern.Parse("internals.economy.*"), 26, 26307),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.security.*"), 20, 34064),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.tension.*"), 12, 56126),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.agenda.*"), 6, 109101),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.info.*"), 20, 34064),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.gov.*"), 20, 34064),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.leg.*"), 20, 34064),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.party.*"), 20, 34064),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.cohesion.*"), 20, 34064),
                    new AggregationReversionGroup(TargetPattern.Parse("internals.legitimacy.*"), 20, 34064)
                },
                new[]
                {
                    TargetPath.Parse("internals.legitimacy.performance"),
                    TargetPath.Parse("internals.legitimacy.social_tension_load")
                },
                null,
                null,
                null,
                null);
        }

        private static AggregationPass MetricsPass(IEnumerable<AggregationMetric> metrics)
        {
            return new AggregationPass(
                AggregationPassType.MetricAggregation,
                "SYSTEM:AGG",
                null,
                null,
                null,
                true,
                1000000,
                metrics,
                null);
        }

        private static AggregationPass DerivedPass()
        {
            return new AggregationPass(
                AggregationPassType.DerivedInternals,
                "SYSTEM:DERIVED",
                null,
                null,
                null,
                null,
                null,
                null,
                new[]
                {
                    new DerivedAggregationRule(
                        TargetPath.Parse("internals.legitimacy.performance"),
                        TargetOperation.Set,
                        new AggregationExpression(
                            AggregationExpressionKind.Avg,
                            null,
                            new[]
                            {
                                TargetPath.Parse("metrics.economy"),
                                TargetPath.Parse("metrics.security"),
                                TargetPath.Parse("metrics.governability")
                            })),
                    new DerivedAggregationRule(
                        TargetPath.Parse("internals.legitimacy.social_tension_load"),
                        TargetOperation.Set,
                        new AggregationExpression(
                            AggregationExpressionKind.Copy,
                            TargetPath.Parse("metrics.social_tension"),
                            Array.Empty<TargetPath>()))
                });
        }

        private static AggregationMetric[] PrimaryMetricArray()
        {
            AggregationMetric[] metrics = new AggregationMetric[PrimaryMetrics.Length];
            for (int i = 0; i < PrimaryMetrics.Length; i++)
            {
                metrics[i] = Metric(PrimaryMetrics[i], i);
            }

            return metrics;
        }

        private static AggregationMetric LegitimacyMetric()
        {
            return Metric("metrics.legitimacy", 9);
        }

        private static AggregationMetric Metric(string metric, int componentIndex)
        {
            WeightedTargetComponent[] components = new WeightedTargetComponent[ComponentTargets[componentIndex].Length];
            for (int i = 0; i < components.Length; i++)
            {
                components[i] = new WeightedTargetComponent(TargetPath.Parse(ComponentTargets[componentIndex][i]), ComponentWeights[componentIndex][i]);
            }

            return new AggregationMetric(TargetPath.Parse(metric), 8, 82996, 200, components);
        }

        private static AggregationMetricRuntime RuntimeMetric(string metric, string component)
        {
            return new AggregationMetricRuntime(
                TargetPath.Parse(metric),
                1,
                1,
                0,
                new[] { new WeightedTargetComponentRuntime(TargetPath.Parse(component), 1000000) });
        }

        private static AggregationMetricRuntime[] AllMetrics(AggregationRuntimePlan plan)
        {
            AggregationMetricRuntime[] metrics = new AggregationMetricRuntime[10];
            for (int i = 0; i < plan.PrimaryMetrics.Metrics.Count; i++)
            {
                metrics[i] = plan.PrimaryMetrics.Metrics[i];
            }

            metrics[9] = plan.Legitimacy.Metrics[0];
            return metrics;
        }

        private static IEnumerable AllMetricCauseCases()
        {
            for (int i = 0; i < PrimaryMetrics.Length; i++)
            {
                yield return new TestCaseData(i, PrimaryMetrics[i], null, "SYSTEM:AGG." + PrimaryMetrics[i])
                    .SetName("MetricCause_" + PrimaryMetrics[i]);
                for (int j = 0; j < ComponentTargets[i].Length; j++)
                {
                    yield return new TestCaseData(i, PrimaryMetrics[i], ComponentTargets[i][j], "SYSTEM:AGG." + PrimaryMetrics[i] + "." + ComponentTargets[i][j])
                        .SetName("ComponentCause_" + PrimaryMetrics[i] + "_" + ComponentTargets[i][j]);
                }
            }

            yield return new TestCaseData(9, "metrics.legitimacy", null, "SYSTEM:AGG.metrics.legitimacy")
                .SetName("MetricCause_metrics.legitimacy");
            for (int j = 0; j < ComponentTargets[9].Length; j++)
            {
                yield return new TestCaseData(9, "metrics.legitimacy", ComponentTargets[9][j], "SYSTEM:AGG.metrics.legitimacy." + ComponentTargets[9][j])
                    .SetName("ComponentCause_metrics.legitimacy_" + ComponentTargets[9][j]);
            }
        }

        private static AggregationPass[] CopyPasses(AggregationConfig config)
        {
            AggregationPass[] result = new AggregationPass[config.Passes.Count];
            for (int i = 0; i < config.Passes.Count; i++)
            {
                result[i] = config.Passes[i];
            }

            return result;
        }

        private static void AssertPlansEquivalent(AggregationRuntimePlan left, AggregationRuntimePlan right)
        {
            Assert.That(right.Scale, Is.EqualTo(left.Scale));
            Assert.That(right.MidS, Is.EqualTo(left.MidS));
            Assert.That(right.Rounding, Is.EqualTo(left.Rounding));
            Assert.That(right.MetricsByTarget.Count, Is.EqualTo(left.MetricsByTarget.Count));
            Assert.That(SerializePlan(right), Is.EqualTo(SerializePlan(left)));
        }

        private static string SerializePlan(AggregationRuntimePlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(plan.Scale).Append('|').Append(plan.MidS).Append('|').Append(plan.Rounding).Append('\n');
            foreach (AggregationReversionGroupRuntime group in plan.InternalReversion.Groups)
            {
                sb.Append("R:").Append(group.Pattern).Append('|').Append(group.HalfLifeWeeks).Append('|').Append(group.AlphaPpm).Append('\n');
            }

            foreach (TargetPath skip in plan.InternalReversion.SkipTargets)
            {
                sb.Append("S:").Append(skip).Append('\n');
            }

            foreach (DerivedAggregationRuleRuntime rule in plan.DerivedInternals.Rules)
            {
                sb.Append("D:").Append(rule.Target).Append('|').Append(rule.Operation).Append('|').Append(rule.Cause.CanonicalKey).Append('|').Append(rule.Expression.Kind);
                if (rule.Expression.Target.HasValue)
                {
                    sb.Append('|').Append(rule.Expression.Target.Value);
                }

                for (int i = 0; i < rule.Expression.Targets.Count; i++)
                {
                    sb.Append('|').Append(rule.Expression.Targets[i]);
                }

                sb.Append('\n');
            }

            foreach (AggregationMetricRuntime metric in AllMetrics(plan))
            {
                sb.Append("M:").Append(metric.Metric).Append('|').Append(metric.HalfLifeWeeks).Append('|').Append(metric.AlphaPpm).Append('|').Append(metric.CapPerWeekS).Append('|').Append(metric.BaseCause.CanonicalKey).Append('\n');
                foreach (WeightedTargetComponentRuntime component in metric.Components)
                {
                    sb.Append("C:").Append(component.Target).Append('|').Append(component.WeightPpm).Append('|').Append(component.Cause.CanonicalKey).Append('\n');
                }
            }

            return sb.ToString();
        }

        private static void AssertOneColon(CauseRef cause)
        {
            int count = 0;
            for (int i = 0; i < cause.CanonicalKey.Length; i++)
            {
                if (cause.CanonicalKey[i] == ':')
                {
                    count++;
                }
            }

            Assert.That(count, Is.EqualTo(1), cause.CanonicalKey);
        }

        private static ContentLoadResult LoadInMemory(Dictionary<string, byte[]> files)
        {
            return new ContentPackLoader().Load(new InMemoryContentFileSource(files));
        }

        private static void AssertFailure(ContentLoadResult result, ContentDiagnosticCode code)
        {
            Assert.That(result.IsSuccess, Is.False, Diagnostics(result));
            Assert.That(result.Pack, Is.Null);
            Assert.That(ContainsCode(result, code), Is.True, Diagnostics(result));
        }

        private static bool ContainsCode(ContentLoadResult result, ContentDiagnosticCode code)
        {
            foreach (ContentDiagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Diagnostics(ContentLoadResult result)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ContentDiagnostic diagnostic in result.Diagnostics)
            {
                sb.Append(diagnostic.ToString()).Append('\n');
            }

            return sb.ToString();
        }

        private static string ContentRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Assets", "StreamingAssets", "content"));
        }

        private static Dictionary<string, byte[]> FixtureFromReal()
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

            return files;
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

        private static string RemovePassByType(string json, string passType)
        {
            string search = "\"type\": \"" + passType + "\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0)
            {
                Assert.Fail("Pass type not found: " + passType);
            }

            int start = json.LastIndexOf("\n    {", idx, StringComparison.Ordinal);
            int end = FindPassEnd(json, start);
            string before = json.Substring(0, start);
            string after = json.Substring(end);
            if (after.StartsWith(",", StringComparison.Ordinal))
            {
                after = after.Substring(1);
            }
            else if (before.EndsWith(",", StringComparison.Ordinal))
            {
                before = before.Substring(0, before.Length - 1);
            }

            return before + after;
        }

        private static string DuplicatePass(string json, string passType)
        {
            string search = "\"type\": \"" + passType + "\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0)
            {
                Assert.Fail("Pass type not found: " + passType);
            }

            int start = json.LastIndexOf("\n    {", idx, StringComparison.Ordinal);
            int end = FindPassEnd(json, start);
            string snippet = json.Substring(start, end - start);
            return json.Insert(json.LastIndexOf("\n  ]", StringComparison.Ordinal), "," + snippet);
        }

        private static string RemoveSecondMetricPass(string json)
        {
            int first = json.IndexOf("\"type\": \"METRIC_AGGREGATION\"", StringComparison.Ordinal);
            int second = json.IndexOf("\"type\": \"METRIC_AGGREGATION\"", first + 1, StringComparison.Ordinal);
            int start = json.LastIndexOf("\n    {", second, StringComparison.Ordinal);
            int end = FindPassEnd(json, start);
            string before = json.Substring(0, start);
            string after = json.Substring(end);
            if (before.EndsWith(",", StringComparison.Ordinal))
            {
                before = before.Substring(0, before.Length - 1);
            }

            return before + after;
        }

        private static int FindPassEnd(string json, int objectStart)
        {
            int depth = 0;
            bool inString = false;
            for (int i = objectStart; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inString = true;
                    }
                    else if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return i + 1;
                        }
                    }
                }
            }

            Assert.Fail("Object end not found.");
            return -1;
        }

        private static int FindArrayEnd(string json, int arrayStart)
        {
            int depth = 0;
            bool inString = false;
            for (int i = arrayStart; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inString = true;
                    }
                    else if (c == '[')
                    {
                        depth++;
                    }
                    else if (c == ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return i;
                        }
                    }
                }
            }

            Assert.Fail("Array end not found.");
            return -1;
        }

        private static string ReplaceDerivedPrefix(string json, string replacement)
        {
            int typeIndex = json.IndexOf("\"type\": \"DERIVED_INTERNALS\"", StringComparison.Ordinal);
            int prefixIndex = json.IndexOf("\"cause_prefix\"", typeIndex, StringComparison.Ordinal);
            int valueStart = json.IndexOf('"', json.IndexOf(':', prefixIndex) + 1) + 1;
            int valueEnd = json.IndexOf('"', valueStart);
            return json.Substring(0, valueStart) + replacement + json.Substring(valueEnd);
        }

        private static string[] ReadAsmdefReferences(string json)
        {
            int key = json.IndexOf("\"references\"", StringComparison.Ordinal);
            int start = json.IndexOf('[', key);
            int end = json.IndexOf(']', start);
            string body = json.Substring(start + 1, end - start - 1);
            List<string> refs = new List<string>();
            string[] parts = body.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string value = parts[i].Trim().Trim('"');
                if (value.Length > 0)
                {
                    refs.Add(value);
                }
            }

            return refs.ToArray();
        }

        private static string ReadGuid(string metaPath)
        {
            string[] lines = File.ReadAllLines(metaPath);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("guid: ", StringComparison.Ordinal))
                {
                    return lines[i].Substring("guid: ".Length).Trim();
                }
            }

            Assert.Fail("GUID not found: " + metaPath);
            return string.Empty;
        }

        private static string Text(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private sealed class MutableSources
        {
            public List<AggregationReversionGroup> Groups;
            public TargetPath[] SkipTargets;
            public List<AggregationMetric> PrimaryMetrics;
            public AggregationMetric[] LegitimacyMetrics;
            public WeightedTargetComponent[] EconomyComponents;
            public TargetPath[] ExpressionTargets;
            public List<DerivedAggregationRule> Rules;
            public List<AggregationPass> Passes;
            public AggregationConfig Config;
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
