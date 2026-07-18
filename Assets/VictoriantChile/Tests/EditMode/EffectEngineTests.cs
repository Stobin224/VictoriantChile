using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using VictoriantChile.Content.Loading;
using VictoriantChile.Content.Models;
using VictoriantChile.Content.State;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Effects;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;
using VictoriantChile.Simulation.Runner;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class EffectEngineTests
    {
        private const string GoldenHash = "sha256:b7d2b922d3c63bb50f62101b86869c44e2fa9eb1a57181582f843d0926e14cfe";

        [Test]
        public void EffectInstanceValidatesOriginAndBuildsModifierCause()
        {
            CauseRef decision = Decision("decision_route_a");
            EffectInstance instance = new EffectInstance(
                "inst_legit",
                "eff_legit",
                decision,
                0,
                3,
                "legit.main",
                EffectStackMode.Stack,
                null,
                5);

            Assert.That(instance.ModifierCause, Is.EqualTo(new CauseRef(CauseCategory.Modifier, "eff_legit", decision)));
            Assert.That(instance.StartInstantApplied, Is.False);
            Assert.Throws<EffectEngineException>(() => new EffectInstance("x", "eff", CauseRef.SystemClamp, 0, 1, "k", EffectStackMode.Stack, null, 0));
            Assert.Throws<EffectEngineException>(() => new EffectInstance("x", "eff", decision, -1, 1, "k", EffectStackMode.Stack, null, 0));
            Assert.Throws<EffectEngineException>(() => new EffectInstance("x", "eff", decision, 1, 1, "k", EffectStackMode.Stack, null, 0));
            Assert.Throws<EffectEngineException>(() => new EffectInstance("x", "eff", decision, 0, 2, " ", EffectStackMode.Stack, null, 0));
            Assert.Throws<EffectEngineException>(() => new EffectInstance("x", "eff", decision, 0, 2, "k", EffectStackMode.StackLimitN, null, 0));
        }

        [Test]
        public void RegisterEffectSupportsStackReplaceRefreshMaxAndStackLimit()
        {
            ContentPack pack = CreatePack(
                Effect("eff_add_100", Modifier("metrics.legitimacy", TargetOperation.Add, 100, false)),
                Effect("eff_add_300", Modifier("metrics.legitimacy", TargetOperation.Add, 300, false)),
                Effect("eff_mul_11000", Modifier("metrics.legitimacy", TargetOperation.Multiply, 11000, true)));
            EffectEngine engine = new EffectEngine();
            GameState stackState = CreateState(pack, 1);
            stackState = engine.RegisterEffect(stackState, pack.EffectRuntimeCatalog, Instance("inst_a", "eff_add_100", Decision("decision_a"), 0, 2, "legit.stack", EffectStackMode.Stack, null, 1));
            stackState = engine.RegisterEffect(stackState, pack.EffectRuntimeCatalog, Instance("inst_b", "eff_add_100", Event("event_b"), 0, 2, "legit.stack", EffectStackMode.Stack, null, 1));
            Assert.That(stackState.ActiveEffects.Count, Is.EqualTo(2));

            GameState replaceState = CreateState(pack, 1);
            replaceState = engine.RegisterEffect(replaceState, pack.EffectRuntimeCatalog, Instance("inst_replace_a", "eff_add_100", Decision("decision_replace_a"), 0, 2, "legit.replace", EffectStackMode.Replace, null, 1));
            replaceState = engine.RegisterEffect(replaceState, pack.EffectRuntimeCatalog, Instance("inst_replace_b", "eff_add_300", Decision("decision_replace_b"), 0, 4, "legit.replace", EffectStackMode.Replace, null, 2));
            Assert.That(replaceState.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(replaceState.ActiveEffects[0].Id, Is.EqualTo("inst_replace_b"));

            GameState refreshState = CreateState(pack, 1);
            refreshState = engine.RegisterEffect(refreshState, pack.EffectRuntimeCatalog, Instance("inst_refresh", "eff_add_300", Decision("decision_refresh"), 0, 4, "legit.refresh", EffectStackMode.Refresh, null, 2));
            refreshState = engine.RegisterEffect(refreshState, pack.EffectRuntimeCatalog, Instance("inst_refresh_new", "eff_add_300", Decision("decision_refresh"), 0, 6, "legit.refresh", EffectStackMode.Refresh, null, 2));
            Assert.That(refreshState.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(refreshState.ActiveEffects[0].Id, Is.EqualTo("inst_refresh"));
            Assert.That(refreshState.ActiveEffects[0].EndTickExclusive, Is.EqualTo(6));

            GameState maxState = CreateState(pack, 1);
            maxState = engine.RegisterEffect(maxState, pack.EffectRuntimeCatalog, Instance("inst_max_low", "eff_add_100", Decision("decision_max_low"), 0, 5, "legit.max", EffectStackMode.Max, null, 1));
            maxState = engine.RegisterEffect(maxState, pack.EffectRuntimeCatalog, Instance("inst_max_high", "eff_add_300", Decision("decision_max_high"), 0, 5, "legit.max", EffectStackMode.Max, null, 1));
            Assert.That(maxState.ActiveEffectsById.ContainsKey("inst_max_low"), Is.False);
            Assert.That(maxState.ActiveEffectsById.ContainsKey("inst_max_high"), Is.True);

            GameState limitState = CreateState(pack, 1);
            limitState = engine.RegisterEffect(limitState, pack.EffectRuntimeCatalog, Instance("inst_limit_a", "eff_mul_11000", Decision("decision_limit"), 1, 4, "legit.limit", EffectStackMode.StackLimitN, 2, 1));
            limitState = engine.RegisterEffect(limitState, pack.EffectRuntimeCatalog, Instance("inst_limit_b", "eff_mul_11000", Decision("decision_limit"), 2, 4, "legit.limit", EffectStackMode.StackLimitN, 2, 1));
            limitState = engine.RegisterEffect(limitState, pack.EffectRuntimeCatalog, Instance("inst_limit_c", "eff_mul_11000", Decision("decision_limit"), 3, 4, "legit.limit", EffectStackMode.StackLimitN, 2, 1));
            Assert.That(limitState.ActiveEffectsById.ContainsKey("inst_limit_a"), Is.False);
            Assert.That(limitState.ActiveEffectsById.ContainsKey("inst_limit_b"), Is.True);
            Assert.That(limitState.ActiveEffectsById.ContainsKey("inst_limit_c"), Is.True);
        }

        [Test]
        public void RegisterEffectIgnoresExpiredInstancesAtCurrentTickForDuplicateIdsAndStacking()
        {
            ContentPack pack = CreatePack(
                Effect("eff_add_100", Modifier("metrics.legitimacy", TargetOperation.Add, 100, false)),
                Effect("eff_add_300", Modifier("metrics.legitimacy", TargetOperation.Add, 300, false)));
            EffectEngine engine = new EffectEngine();
            GameState state = new GameState(
                1,
                CreateState(pack, 1).ContentMetadata,
                CreateState(pack, 1).Metrics,
                CreateState(pack, 1).Internals,
                CreateState(pack, 1).Regions,
                CreateState(pack, 1).InterestGroups,
                CreateState(pack, 1).Movements,
                new[]
                {
                    Instance("inst_expired", "eff_add_300", Decision("decision_old"), 1, 5, "shared.k", EffectStackMode.Refresh, null, 2),
                    Instance("inst_max_old", "eff_add_300", Decision("decision_old"), 1, 5, "max.k", EffectStackMode.Max, null, 2),
                },
                5);

            GameState refreshed = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_expired", "eff_add_100", Decision("decision_new"), 5, 8, "shared.k", EffectStackMode.Refresh, null, 1));
            Assert.That(refreshed.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(refreshed.ActiveEffectsById.ContainsKey("inst_expired"), Is.True);
            Assert.That(refreshed.ActiveEffectsById["inst_expired"].TemplateId, Is.EqualTo("eff_add_100"));
            Assert.That(refreshed.ActiveEffectsById["inst_expired"].EndTickExclusive, Is.EqualTo(8));

            GameState maxed = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_max_new", "eff_add_100", Decision("decision_new"), 5, 8, "max.k", EffectStackMode.Max, null, 1));
            Assert.That(maxed.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(maxed.ActiveEffectsById.ContainsKey("inst_max_new"), Is.True);
        }

        [Test]
        public void RegisterEffectFailsClosedForDuplicateIdsRefreshIdentityShiftAndMixedModes()
        {
            ContentPack pack = CreatePack(
                Effect("eff_a", Modifier("metrics.legitimacy", TargetOperation.Add, 100, false)),
                Effect("eff_b", Modifier("metrics.legitimacy", TargetOperation.Add, 200, false)),
                Effect("eff_set", Modifier("metrics.legitimacy", TargetOperation.Set, 8000, false)));
            EffectEngine engine = new EffectEngine();
            GameState state = CreateState(pack, 1);

            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_a", "eff_a", Decision("decision_a"), 0, 2, "stack.k", EffectStackMode.Stack, null, 1));
            Assert.Throws<EffectEngineException>(() => engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_a", "eff_a", Decision("decision_b"), 0, 2, "other.k", EffectStackMode.Stack, null, 1)));
            Assert.Throws<EffectEngineException>(() => engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_refresh", "eff_b", Decision("decision_a"), 0, 3, "stack.k", EffectStackMode.Refresh, null, 1)));
            Assert.Throws<EffectEngineException>(() => engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_mode", "eff_a", Decision("decision_a"), 0, 3, "stack.k", EffectStackMode.Replace, null, 1)));
            Assert.Throws<EffectEngineException>(() => engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_set", "eff_set", Decision("decision_set"), 0, 2, "max.k", EffectStackMode.Max, null, 1)));
        }

        [Test]
        public void StartInstantVisibleMutationRequiresLedgerAndAppliesExactlyOnce()
        {
            ContentPack pack = CreatePack(Effect("eff_start", Modifier("metrics.legitimacy", TargetOperation.Add, 2000, false)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_start", "eff_start", Decision("decision_start"), 0, 3, "start.k", EffectStackMode.Stack, null, 5));

            Assert.Throws<EffectEngineException>(() => engine.ApplyStartInstantModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, null));

            TickCausalBuffer buffer = BufferFor(state, TargetPath.Parse("metrics.legitimacy"));
            GameState next = engine.ApplyStartInstantModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer);
            Assert.That(next.MetricsById["legitimacy"].ValueS, Is.EqualTo(7000));
            Assert.That(next.ActiveEffectsById["inst_start"].StartInstantApplied, Is.True);

            buffer.CloseTarget(TargetPath.Parse("metrics.legitimacy"), 7000);
            TickCausalSnapshot snapshot = buffer.Seal();
            Assert.That(snapshot.ChangedTargets.Count, Is.EqualTo(1));
            Assert.That(snapshot.ChangedTargets[0].Contributions.Count, Is.EqualTo(1));
            Assert.That(snapshot.ChangedTargets[0].Contributions[0].DeltaS, Is.EqualTo(2000));
            Assert.That(snapshot.ChangedTargets[0].Contributions[0].Cause, Is.EqualTo(new CauseRef(CauseCategory.Modifier, "eff_start", Decision("decision_start"))));

            TickCausalBuffer secondBuffer = BufferFor(next, TargetPath.Parse("metrics.legitimacy"));
            GameState second = engine.ApplyStartInstantModifiers(next, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, secondBuffer);
            Assert.That(second, Is.SameAs(next));
        }

        [Test]
        public void PerTickWindowAndExpirationAreExplicitAndDeterministic()
        {
            ContentPack pack = CreatePack(Effect("eff_tick", Modifier("metrics.legitimacy", TargetOperation.Add, 100, true)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_tick", "eff_tick", Decision("decision_tick"), 2, 4, "tick.k", EffectStackMode.Stack, null, 1));

            GameState tick1 = engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 1, BufferFor(state, TargetPath.Parse("metrics.legitimacy")));
            Assert.That(tick1, Is.SameAs(state));

            TickCausalBuffer tick2Buffer = BufferFor(state, TargetPath.Parse("metrics.legitimacy"));
            GameState tick2 = engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 2, tick2Buffer);
            Assert.That(tick2.MetricsById["legitimacy"].ValueS, Is.EqualTo(5100));

            TickCausalBuffer tick3Buffer = BufferFor(tick2, TargetPath.Parse("metrics.legitimacy"));
            GameState tick3 = engine.ApplyPerTickModifiers(tick2, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 3, tick3Buffer);
            Assert.That(tick3.MetricsById["legitimacy"].ValueS, Is.EqualTo(5200));

            GameState trimmed = engine.RemoveExpiredEffects(tick3, 4);
            Assert.That(trimmed.ActiveEffects, Is.Empty);
        }

        [Test]
        public void SetSuppressesAddAndMulAndRecordsClampResidue()
        {
            ContentPack pack = CreatePack(
                Effect("eff_set", Modifier("metrics.social_tension", TargetOperation.Set, 12000, true)),
                Effect("eff_add", Modifier("metrics.social_tension", TargetOperation.Add, -500, true)),
                Effect("eff_mul", Modifier("metrics.social_tension", TargetOperation.Multiply, 9000, true)));
            EffectEngine engine = new EffectEngine();
            GameState state = CreateState(pack, 1);
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_set", "eff_set", Decision("decision_set"), 0, 2, "tension.stack", EffectStackMode.Stack, null, 10));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_add", "eff_add", Event("event_add"), 0, 2, "tension.other", EffectStackMode.Stack, null, 9));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_mul", "eff_mul", Reform("reform_mul"), 0, 2, "tension.mul", EffectStackMode.Stack, null, 8));

            TickCausalBuffer buffer = BufferFor(state, TargetPath.Parse("metrics.social_tension"));
            GameState next = engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer);
            Assert.That(next.MetricsById["social_tension"].ValueS, Is.EqualTo(10000));
            buffer.CloseTarget(TargetPath.Parse("metrics.social_tension"), 10000);
            TickCausalSnapshot snapshot = buffer.Seal();
            Assert.That(snapshot.ChangedTargets[0].Contributions.Count, Is.EqualTo(2));
            Assert.That(FindContribution(snapshot, "metrics.social_tension", "MODIFIER:eff_set|parent=DECISION:decision_set"), Is.EqualTo(7000));
            Assert.That(FindContribution(snapshot, "metrics.social_tension", "SYSTEM:CLAMP"), Is.EqualTo(-2000));
            Assert.That(FindContribution(snapshot, "metrics.social_tension", "MODIFIER:eff_add|parent=EVENT:event_add"), Is.Null);
            Assert.That(FindContribution(snapshot, "metrics.social_tension", "MODIFIER:eff_mul|parent=REFORM:reform_mul"), Is.Null);
        }

        [Test]
        public void AddThenSequentialMulUseStableOrderingAndSeparateRounding()
        {
            ContentPack pack = CreatePack(
                Effect("eff_add_a", Modifier("metrics.legitimacy", TargetOperation.Add, 1, true)),
                Effect("eff_add_b", Modifier("metrics.legitimacy", TargetOperation.Add, 2, true)),
                Effect("eff_mul_a", Modifier("metrics.legitimacy", TargetOperation.Multiply, 5000, true)),
                Effect("eff_mul_b", Modifier("metrics.legitimacy", TargetOperation.Multiply, 10001, true)));
            EffectEngine engine = new EffectEngine();
            GameState state = CreateState(pack, 1);
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_mul_b", "eff_mul_b", Decision("decision_mul_b"), 0, 2, "k4", EffectStackMode.Stack, null, 1));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_add_b", "eff_add_b", Event("event_add_b"), 0, 2, "k2", EffectStackMode.Stack, null, 3));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_add_a", "eff_add_a", Decision("decision_add_a"), 0, 2, "k1", EffectStackMode.Stack, null, 3));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_mul_a", "eff_mul_a", Movement("movement_mul_a"), 0, 2, "k3", EffectStackMode.Stack, null, 2));

            TickCausalBuffer buffer = BufferFor(state, TargetPath.Parse("metrics.legitimacy"));
            GameState next = engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer);
            Assert.That(next.MetricsById["legitimacy"].ValueS, Is.EqualTo(2502));
            buffer.CloseTarget(TargetPath.Parse("metrics.legitimacy"), 2502);
            TickCausalSnapshot snapshot = buffer.Seal();

            Assert.That(FindContribution(snapshot, "metrics.legitimacy", "MODIFIER:eff_add_a|parent=DECISION:decision_add_a"), Is.EqualTo(1));
            Assert.That(FindContribution(snapshot, "metrics.legitimacy", "MODIFIER:eff_add_b|parent=EVENT:event_add_b"), Is.EqualTo(2));
            Assert.That(FindContribution(snapshot, "metrics.legitimacy", "MODIFIER:eff_mul_a|parent=MOVEMENT:movement_mul_a"), Is.EqualTo(-2502));
            Assert.That(FindContribution(snapshot, "metrics.legitimacy", "MODIFIER:eff_mul_b|parent=DECISION:decision_mul_b"), Is.EqualTo(0).Or.Null);
            Assert.That(FindContribution(snapshot, "metrics.legitimacy", "SYSTEM:ROUNDING"), Is.EqualTo(1));
        }

        [Test]
        public void NegativeMultiplyRoundingAndClampAreRecordedSeparately()
        {
            ContentPack pack = CreatePack(Effect("eff_negative_mul", Modifier("igs.ig_sindicatos_trabajo.approval", TargetOperation.Multiply, 10001, true)));
            EffectEngine engine = new EffectEngine();
            GameState start = CreateState(pack, 1);
            start = new GameState(
                start.RngSeed,
                start.ContentMetadata,
                start.Metrics,
                start.Internals,
                start.Regions,
                ReplaceApproval(start, "ig_sindicatos_trabajo", -5000),
                start.Movements,
                start.ActiveEffects,
                start.Tick);
            start = engine.RegisterEffect(start, pack.EffectRuntimeCatalog, Instance("inst_negative", "eff_negative_mul", Decision("decision_negative"), 0, 2, "approval.k", EffectStackMode.Stack, null, 1));

            TickCausalBuffer buffer = BufferFor(start, TargetPath.Parse("igs.ig_sindicatos_trabajo.approval"));
            GameState next = engine.ApplyPerTickModifiers(start, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer);
            Assert.That(next.InterestGroupsById["ig_sindicatos_trabajo"].ApprovalS, Is.EqualTo(-5001));
            buffer.CloseTarget(TargetPath.Parse("igs.ig_sindicatos_trabajo.approval"), -5001);
            TickCausalSnapshot snapshot = buffer.Seal();
            Assert.That(FindContribution(snapshot, "igs.ig_sindicatos_trabajo.approval", "SYSTEM:ROUNDING"), Is.EqualTo(-1));
        }

        [Test]
        public void HiddenTargetsCanMutateWithoutVisibleLedger()
        {
            ContentPack pack = CreatePack(Effect("eff_hidden", Modifier("internals.economy.growth", TargetOperation.Add, 100, true)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_hidden", "eff_hidden", Decision("decision_hidden"), 0, 2, "hidden.k", EffectStackMode.Stack, null, 1));

            GameState next = engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, null);
            Assert.That(next.InternalsByDomain["economy"].ComponentsById["growth"].ValueS, Is.EqualTo(5100));
        }

        [Test]
        public void MissingVisibleBaselineFailsWithoutMutatingStateOrLedger()
        {
            ContentPack pack = CreatePack(Effect("eff_visible", Modifier("metrics.legitimacy", TargetOperation.Add, 100, true)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_visible", "eff_visible", Decision("decision_visible"), 0, 2, "visible.k", EffectStackMode.Stack, null, 1));
            TickCausalBuffer buffer = new TickCausalBuffer(0, VisibleTargetCatalog.CreateCanonicalFromState(state));

            EffectEngineException exception = Assert.Throws<EffectEngineException>(() => engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer));
            Assert.That(exception.Code, Is.EqualTo(EffectEngineErrorCodes.MissingVisibleBaseline));
            Assert.That(state.MetricsById["legitimacy"].ValueS, Is.EqualTo(5000));
        }

        [Test]
        public void MultiTargetVisibleFailureIsAtomicForStateLedgerAndInstantRegistry()
        {
            ContentPack pack = CreatePack(
                Effect(
                    "eff_multi_visible",
                    Modifier("metrics.legitimacy", TargetOperation.Add, 100, false),
                    Modifier("metrics.social_tension", TargetOperation.Add, -50, false)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_multi", "eff_multi_visible", Decision("decision_multi"), 0, 2, "multi.k", EffectStackMode.Stack, null, 1));
            TickCausalBuffer buffer = BufferFor(state, TargetPath.Parse("metrics.legitimacy"));

            EffectEngineException exception = Assert.Throws<EffectEngineException>(() => engine.ApplyStartInstantModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer));
            Assert.That(exception.Code, Is.EqualTo(EffectEngineErrorCodes.MissingVisibleBaseline));
            Assert.That(state.MetricsById["legitimacy"].ValueS, Is.EqualTo(5000));
            Assert.That(state.MetricsById["social_tension"].ValueS, Is.EqualTo(5000));
            Assert.That(state.ActiveEffectsById["inst_multi"].StartInstantApplied, Is.False);
            buffer.CloseTarget(TargetPath.Parse("metrics.legitimacy"), 5000);
            TickCausalSnapshot snapshot = buffer.Seal();
            Assert.That(snapshot.AuditedTargets.Count, Is.EqualTo(1));
            Assert.That(snapshot.AuditedTargets[0].Contributions, Is.Empty);
        }

        [Test]
        public void HiddenAndVisibleBatchFailureLeavesHiddenMutationUnapplied()
        {
            ContentPack pack = CreatePack(
                Effect(
                    "eff_hidden_visible",
                    Modifier("internals.economy.growth", TargetOperation.Add, 100, true),
                    Modifier("metrics.legitimacy", TargetOperation.Add, 200, true)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_hidden_visible", "eff_hidden_visible", Decision("decision_hidden_visible"), 0, 2, "hidden.visible.k", EffectStackMode.Stack, null, 1));
            TickCausalBuffer buffer = new TickCausalBuffer(0, VisibleTargetCatalog.CreateCanonicalFromState(state));

            EffectEngineException exception = Assert.Throws<EffectEngineException>(() => engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer));
            Assert.That(exception.Code, Is.EqualTo(EffectEngineErrorCodes.MissingVisibleBaseline));
            Assert.That(state.InternalsByDomain["economy"].ComponentsById["growth"].ValueS, Is.EqualTo(5000));
            Assert.That(state.MetricsById["legitimacy"].ValueS, Is.EqualTo(5000));
        }

        [Test]
        public void RefreshConflictIsAtomicAndPreservesExistingRegistry()
        {
            ContentPack pack = CreatePack(
                Effect("eff_a", Modifier("metrics.legitimacy", TargetOperation.Add, 100, false)),
                Effect("eff_b", Modifier("metrics.legitimacy", TargetOperation.Add, 200, false)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_refresh", "eff_a", Decision("decision_a"), 0, 2, "refresh.k", EffectStackMode.Refresh, null, 1));

            Assert.Throws<EffectEngineException>(() => engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_refresh_new", "eff_b", Decision("decision_a"), 0, 3, "refresh.k", EffectStackMode.Refresh, null, 1)));
            Assert.That(state.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(state.ActiveEffectsById["inst_refresh"].TemplateId, Is.EqualTo("eff_a"));
            Assert.That(state.ActiveEffectsById["inst_refresh"].EndTickExclusive, Is.EqualTo(2));
        }

        [Test]
        public void SuppressedModifierClampDoesNotAffectWinningSet()
        {
            ContentPack pack = CreatePack(
                Effect("eff_set", Modifier("metrics.legitimacy", TargetOperation.Set, 9000, true)),
                Effect("eff_add_clamp", Modifier("metrics.legitimacy", TargetOperation.Add, 100, true, 0, 1000)));
            EffectEngine engine = new EffectEngine();
            GameState state = CreateState(pack, 1);
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_set", "eff_set", Decision("decision_set"), 0, 2, "set.k", EffectStackMode.Stack, null, 2));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_add", "eff_add_clamp", Decision("decision_add"), 0, 2, "add.k", EffectStackMode.Stack, null, 1));

            TickCausalBuffer buffer = BufferFor(state, TargetPath.Parse("metrics.legitimacy"));
            GameState next = engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer);
            Assert.That(next.MetricsById["legitimacy"].ValueS, Is.EqualTo(9000));
            buffer.CloseTarget(TargetPath.Parse("metrics.legitimacy"), 9000);
            TickCausalSnapshot snapshot = buffer.Seal();
            Assert.That(FindContribution(snapshot, "metrics.legitimacy", "MODIFIER:eff_set|parent=DECISION:decision_set"), Is.EqualTo(4000));
            Assert.That(FindContribution(snapshot, "metrics.legitimacy", "SYSTEM:CLAMP"), Is.Null);
            Assert.That(FindContribution(snapshot, "metrics.legitimacy", "MODIFIER:eff_add_clamp|parent=DECISION:decision_add"), Is.Null);
        }

        [Test]
        public void CloutNormalizationRequiresFullAuditAndRecordsSystemResiduals()
        {
            ContentPack pack = CreatePack(Effect("eff_clout", Modifier("igs.ig_sindicatos_trabajo.clout", TargetOperation.Add, 1000, true)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_clout", "eff_clout", Decision("decision_clout"), 0, 2, "clout.k", EffectStackMode.Stack, null, 1));

            TickCausalBuffer partial = BufferFor(state, TargetPath.Parse("igs.ig_sindicatos_trabajo.clout"));
            EffectEngineException partialException = Assert.Throws<EffectEngineException>(() => engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, partial));
            Assert.That(partialException.Code, Is.EqualTo(EffectEngineErrorCodes.MissingVisibleBaseline));

            TickCausalBuffer full = BufferForAllClout(state);
            GameState next = engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, full);
            Assert.That(SumClout(next), Is.EqualTo(10000));
            for (int i = 0; i < next.InterestGroups.Count; i++)
            {
                InterestGroupState group = next.InterestGroups[i];
                full.CloseTarget(InitialTargetRegistry.InterestGroupClout(group.InterestGroupId), group.CloutS);
            }

            TickCausalSnapshot snapshot = full.Seal();
            Assert.That(FindContribution(snapshot, "igs.ig_sindicatos_trabajo.clout", "MODIFIER:eff_clout|parent=DECISION:decision_clout"), Is.EqualTo(1000));
            Assert.That(FindContribution(snapshot, "igs.ig_sindicatos_trabajo.clout", "SYSTEM:IG_CLOUT_NORMALIZE"), Is.Not.Null);
        }

        [Test]
        public void LocalClampsNarrowGlobalAndEmptyIntersectionFailsClosed()
        {
            ContentPack pack = CreatePack(
                Effect("eff_clamp_local", Modifier("metrics.legitimacy", TargetOperation.Add, 9000, true, 0, 8000)),
                Effect("eff_bad_clamp", Modifier("metrics.legitimacy", TargetOperation.Add, 100, true, 9000, 8000)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_local", "eff_clamp_local", Decision("decision_local"), 0, 2, "local.k", EffectStackMode.Stack, null, 1));
            TickCausalBuffer buffer = BufferFor(state, TargetPath.Parse("metrics.legitimacy"));
            GameState next = engine.ApplyPerTickModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer);
            Assert.That(next.MetricsById["legitimacy"].ValueS, Is.EqualTo(8000));

            GameState bad = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_bad", "eff_bad_clamp", Decision("decision_bad"), 0, 2, "bad.k", EffectStackMode.Stack, null, 1));
            Assert.Throws<EffectEngineException>(() => engine.ApplyPerTickModifiers(bad, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, BufferFor(bad, TargetPath.Parse("metrics.legitimacy"))));
        }

        [Test]
        public void OperationNotAllowedDirectionAndOverflowFailClosed()
        {
            ContentPack pack = CreatePack(
                Effect("eff_direction_add", Modifier("movements.mov_seguridad_mano_dura.direction", TargetOperation.Add, 1, true)),
                Effect("eff_overflow", Modifier("metrics.legitimacy", TargetOperation.Multiply, int.MaxValue, true)));
            EffectEngine engine = new EffectEngine();

            GameState directionState = engine.RegisterEffect(CreateState(pack, 1), pack.EffectRuntimeCatalog, Instance("inst_dir", "eff_direction_add", Decision("decision_dir"), 0, 2, "dir.k", EffectStackMode.Stack, null, 1));
            Assert.Throws<EffectEngineException>(() => engine.ApplyPerTickModifiers(directionState, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, BufferFor(directionState, TargetPath.Parse("movements.mov_seguridad_mano_dura.direction"))));

            GameState overflowState = CreateState(pack, 1);
            overflowState = engine.RegisterEffect(overflowState, pack.EffectRuntimeCatalog, Instance("inst_overflow_a", "eff_overflow", Decision("decision_overflow"), 0, 2, "overflow.a", EffectStackMode.Stack, null, 3));
            overflowState = engine.RegisterEffect(overflowState, pack.EffectRuntimeCatalog, Instance("inst_overflow_b", "eff_overflow", Decision("decision_overflow"), 0, 2, "overflow.b", EffectStackMode.Stack, null, 2));
            overflowState = engine.RegisterEffect(overflowState, pack.EffectRuntimeCatalog, Instance("inst_overflow_c", "eff_overflow", Decision("decision_overflow"), 0, 2, "overflow.c", EffectStackMode.Stack, null, 1));
            Assert.Throws<EffectEngineException>(() => engine.ApplyPerTickModifiers(overflowState, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, BufferFor(overflowState, TargetPath.Parse("metrics.legitimacy"))));
        }

        [Test]
        public void ActiveEffectsAreStoredInCanonicalStateAndHashesAreDeterministic()
        {
            ContentPack pack = CreatePack(Effect("eff_add", Modifier("metrics.legitimacy", TargetOperation.Add, 100, false)));
            EffectEngine engine = new EffectEngine();
            GameState state = engine.RegisterEffect(CreateState(pack, 99), pack.EffectRuntimeCatalog, Instance("inst_hash", "eff_add", Decision("decision_hash"), 0, null, "hash.k", EffectStackMode.Stack, null, 4));

            string jsonA = new CanonicalGameStateSerializer().ToCompactJson(state);
            string jsonB = new CanonicalGameStateSerializer().ToCompactJson(state);
            string hashA = new GameStateHasher().ComputeHash(state);
            string hashB = new GameStateHasher().ComputeHash(state);

            Assert.That(state.StateSchemaVersion, Is.EqualTo(2));
            Assert.That(state.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(jsonA, Is.EqualTo(jsonB));
            Assert.That(hashA, Is.EqualTo(hashB));
            Assert.That(jsonA, Does.Contain("\"active_effects\":[{\"id\":\"inst_hash\""));
        }

        [Test]
        public void GoldenEvidenceIsDeterministicAndMatchesExpectedHash()
        {
            string first = BuildGoldenEvidence();
            string second = BuildGoldenEvidence();
            string expected = File.ReadAllText(Path.Combine(ProjectRoot(), "tests", "effects", "effect_engine_v1.expected.json"), Encoding.UTF8);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo(expected));
            Assert.That(Hash(first), Is.EqualTo(GoldenHash));
        }

        private static string BuildGoldenEvidence()
        {
            ContentPack pack = CreatePack(
                Effect("eff_start_legitimacy", Modifier("metrics.legitimacy", TargetOperation.Add, 2000, false)),
                Effect("eff_tick_mul", Modifier("metrics.legitimacy", TargetOperation.Multiply, 10001, true)),
                Effect("eff_set_tension", Modifier("metrics.social_tension", TargetOperation.Set, 12000, true)),
                Effect("eff_add_tension", Modifier("metrics.social_tension", TargetOperation.Add, -500, true)),
                Effect("eff_clout_up", Modifier("igs.ig_sindicatos_trabajo.clout", TargetOperation.Add, 1000, true)));
            EffectEngine engine = new EffectEngine();
            GameState state = CreateState(pack, 7);
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_start", "eff_start_legitimacy", Decision("decision_start"), 0, 2, "legit.start", EffectStackMode.Stack, null, 5));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_mul", "eff_tick_mul", Event("event_mul"), 0, 2, "legit.tick", EffectStackMode.Stack, null, 2));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_set", "eff_set_tension", Reform("reform_set"), 0, 2, "tension.set", EffectStackMode.Stack, null, 10));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_add", "eff_add_tension", Movement("movement_add"), 0, 2, "tension.add", EffectStackMode.Stack, null, 9));
            state = engine.RegisterEffect(state, pack.EffectRuntimeCatalog, Instance("inst_clout", "eff_clout_up", Decision("decision_clout"), 0, 2, "clout.tick", EffectStackMode.Stack, null, 1));

            TickCausalBuffer buffer = new TickCausalBuffer(0, VisibleTargetCatalog.CreateCanonicalFromState(state));
            buffer.TrackTarget(TargetPath.Parse("metrics.legitimacy"), state.MetricsById["legitimacy"].ValueS);
            buffer.TrackTarget(TargetPath.Parse("metrics.social_tension"), state.MetricsById["social_tension"].ValueS);
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState group = state.InterestGroups[i];
                buffer.TrackTarget(InitialTargetRegistry.InterestGroupClout(group.InterestGroupId), group.CloutS);
            }

            GameState afterStart = engine.ApplyStartInstantModifiers(state, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer);
            GameState finalState = engine.ApplyPerTickModifiers(afterStart, pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, 0, buffer);
            buffer.CloseTarget(TargetPath.Parse("metrics.legitimacy"), finalState.MetricsById["legitimacy"].ValueS);
            buffer.CloseTarget(TargetPath.Parse("metrics.social_tension"), finalState.MetricsById["social_tension"].ValueS);
            for (int i = 0; i < finalState.InterestGroups.Count; i++)
            {
                InterestGroupState group = finalState.InterestGroups[i];
                buffer.CloseTarget(InitialTargetRegistry.InterestGroupClout(group.InterestGroupId), group.CloutS);
            }

            TickCausalSnapshot tick = buffer.Seal();
            CausalTopNProjection projection = CausalTopNProjector.Project(tick);

            StringBuilder builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"state_hash\": \"").Append(new GameStateHasher().ComputeHash(finalState)).Append("\",\n");
            builder.Append("  \"active_effect_ids\": [");
            for (int i = 0; i < finalState.ActiveEffects.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("\"").Append(finalState.ActiveEffects[i].Id).Append("\"");
            }

            builder.Append("],\n");
            builder.Append("  \"changed_targets\": [\n");
            for (int i = 0; i < tick.ChangedTargets.Count; i++)
            {
                TargetCausalSnapshot target = tick.ChangedTargets[i];
                if (i > 0)
                {
                    builder.Append(",\n");
                }

                builder.Append("    {\"target\":\"").Append(target.Target).Append("\",\"delta_s\":").Append(target.DeltaTotalS.ToString(CultureInfo.InvariantCulture)).Append(",\"contributions\":[");
                for (int j = 0; j < target.Contributions.Count; j++)
                {
                    if (j > 0)
                    {
                        builder.Append(",");
                    }

                    builder.Append("{\"cause\":\"").Append(target.Contributions[j].Cause.CanonicalKey).Append("\",\"delta_s\":").Append(target.Contributions[j].DeltaS.ToString(CultureInfo.InvariantCulture)).Append("}");
                }

                builder.Append("]}");
            }

            builder.Append("\n  ],\n");
            builder.Append("  \"top_n\": [\n");
            for (int i = 0; i < projection.Targets.Count; i++)
            {
                ProjectedCausalTarget target = projection.Targets[i];
                if (i > 0)
                {
                    builder.Append(",\n");
                }

                builder.Append("    {\"target\":\"").Append(target.Target).Append("\",\"delta_s\":").Append(target.DeltaTotalS.ToString(CultureInfo.InvariantCulture)).Append(",\"other_delta_s\":").Append(target.OtherDeltaS.ToString(CultureInfo.InvariantCulture)).Append(",\"top_causes\":[");
                for (int j = 0; j < target.TopCauses.Count; j++)
                {
                    if (j > 0)
                    {
                        builder.Append(",");
                    }

                    builder.Append("{\"cause\":\"").Append(target.TopCauses[j].Cause.CanonicalKey).Append("\",\"delta_s\":").Append(target.TopCauses[j].DeltaS.ToString(CultureInfo.InvariantCulture)).Append("}");
                }

                builder.Append("]}");
            }

            builder.Append("\n  ]\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        private static ContentPack CreatePack(params EffectTemplate[] effects)
        {
            ContentPack real = LoadRealPack();
            return new ContentPack(
                real.Manifest,
                real.TargetConfigs,
                real.Regions,
                real.InterestGroups,
                real.Movements,
                real.Localization,
                real.AggregationConfig,
                real.LegislativeConfig,
                effects,
                real.Events,
                real.Reforms);
        }

        private static EffectTemplate Effect(string id, params EffectModifier[] modifiers)
        {
            return new EffectTemplate(id, "effect." + id + ".title", modifiers, new[] { "theme.test" });
        }

        private static EffectModifier Modifier(string target, TargetOperation operation, int valueS, bool isPerTick, int? clampMin = null, int? clampMax = null)
        {
            return new EffectModifier(TargetPath.Parse(target), operation, valueS, isPerTick, clampMin.HasValue || clampMax.HasValue ? new EffectClamp(clampMin, clampMax) : null);
        }

        private static EffectInstance Instance(
            string id,
            string templateId,
            CauseRef origin,
            int startTick,
            int? endTickExclusive,
            string stackKey,
            EffectStackMode stackMode,
            int? stackLimitN,
            int priority)
        {
            return new EffectInstance(id, templateId, origin, startTick, endTickExclusive, stackKey, stackMode, stackLimitN, priority);
        }

        private static CauseRef Decision(string id)
        {
            return new CauseRef(CauseCategory.Decision, id);
        }

        private static CauseRef Event(string id)
        {
            return new CauseRef(CauseCategory.Event, id);
        }

        private static CauseRef Reform(string id)
        {
            return new CauseRef(CauseCategory.Reform, id);
        }

        private static CauseRef Movement(string id)
        {
            return new CauseRef(CauseCategory.Movement, id);
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

        private static TickCausalBuffer BufferFor(GameState state, params TargetPath[] targets)
        {
            TickCausalBuffer buffer = new TickCausalBuffer(0, VisibleTargetCatalog.CreateCanonicalFromState(state));
            for (int i = 0; i < targets.Length; i++)
            {
                buffer.TrackTarget(targets[i], ReadVisibleValue(state, targets[i]));
            }

            return buffer;
        }

        private static TickCausalBuffer BufferForAllClout(GameState state)
        {
            TickCausalBuffer buffer = new TickCausalBuffer(0, VisibleTargetCatalog.CreateCanonicalFromState(state));
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState group = state.InterestGroups[i];
                TargetPath target = InitialTargetRegistry.InterestGroupClout(group.InterestGroupId);
                buffer.TrackTarget(target, group.CloutS);
            }

            return buffer;
        }

        private static int ReadVisibleValue(GameState state, TargetPath target)
        {
            if (target.Namespace == "metrics")
            {
                return state.MetricsById[target[1]].ValueS;
            }

            if (target.Namespace == "igs")
            {
                return target[2] == "clout"
                    ? state.InterestGroupsById[target[1]].CloutS
                    : state.InterestGroupsById[target[1]].ApprovalS;
            }

            if (target.Namespace == "movements")
            {
                return target[2] == "intensity"
                    ? state.MovementsById[target[1]].IntensityS
                    : state.MovementsById[target[1]].Direction;
            }

            return target[2] switch
            {
                "support" => state.RegionsById[target[1]].SupportS,
                "tension" => state.RegionsById[target[1]].TensionS,
                "organization" => state.RegionsById[target[1]].OrganizationS,
                _ => state.RegionsById[target[1]].RivalPresenceS,
            };
        }

        private static long? FindContribution(TickCausalSnapshot snapshot, string target, string causeKey)
        {
            for (int i = 0; i < snapshot.AuditedTargets.Count; i++)
            {
                TargetCausalSnapshot current = snapshot.AuditedTargets[i];
                if (current.Target.ToString() != target)
                {
                    continue;
                }

                for (int j = 0; j < current.Contributions.Count; j++)
                {
                    if (current.Contributions[j].Cause.CanonicalKey == causeKey)
                    {
                        return current.Contributions[j].DeltaS;
                    }
                }

                return null;
            }

            return null;
        }

        private static List<InterestGroupState> ReplaceApproval(GameState state, string interestGroupId, int approvalS)
        {
            List<InterestGroupState> result = new List<InterestGroupState>();
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState group = state.InterestGroups[i];
                result.Add(new InterestGroupState(group.InterestGroupId, group.CloutS, group.InterestGroupId == interestGroupId ? approvalS : group.ApprovalS));
            }

            return result;
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

        private static string Hash(string text)
        {
            using SHA256 sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            StringBuilder builder = new StringBuilder("sha256:", 71);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string ContentRoot()
        {
            return Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "content");
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", ".."));
        }
    }
}
