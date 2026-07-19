using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using VictoriantChile.Content.Loading;
using VictoriantChile.Content.Models;
using VictoriantChile.Content.State;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Effects;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.Scheduling;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;
using VictoriantChile.Simulation.Runner;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class SchedulerEngineTests
    {
        private const string GoldenHash = "sha256:faf419ac84726456288e56705d355b505d87bd3e995e34803d7c0b51bec31c85";

        [Test]
        public void ScheduledActionsValidateAndQueueDeterministically()
        {
            SchedulerEngine scheduler = CreateScheduler(CreatePack(), Array.Empty<KeyValuePair<string, IScheduledActionHandler>>());
            GameState state = CreateState(CreatePack(), 7);

            state = scheduler.ScheduleAction(state, Action("c_action", 1, 0, "noop", Decision("decision_c")));
            state = scheduler.ScheduleAction(state, Action("b_action", 2, 0, "noop", Decision("decision_b")));
            state = scheduler.ScheduleAction(state, Action("a_action", 1, 5, "noop", Decision("decision_a")));

            Assert.That(SnapshotIds(state.ScheduledActions), Is.EqualTo(new[] { "a_action", "c_action", "b_action" }));
            Assert.Throws<SchedulerException>(() => scheduler.ScheduleAction(state, Action("a_action", 3, 0, "noop", Decision("decision_dup"))));
            Assert.Throws<SchedulerException>(() => scheduler.ScheduleAction(state, Action("late_action", 0, 0, "noop", Decision("decision_late"))));
        }

        [Test]
        public void TickPhasesMatchFrozenOrderExactly()
        {
            ContentPack pack = CreatePack();
            SchedulerEngine scheduler = CreateScheduler(pack, Array.Empty<KeyValuePair<string, IScheduledActionHandler>>());
            List<string> phases = new List<string>();

            TickAdvanceResult result = scheduler.AdvanceOneTick(CreateState(pack, 1), phase => phases.Add(SchedulerFormatting.FormatPhase(phase)));

            Assert.That(phases, Is.EqualTo(new[]
            {
                "increment_tick",
                "expire_effects",
                "execute_scheduled_actions",
                "apply_start_instant_modifiers",
                "apply_per_tick_modifiers",
                "revert_internals",
                "derive_internals",
                "aggregate_national_metrics",
                "drift_national_to_regions",
                "pull_regions_to_internals",
                "update_movements",
                "advance_reforms",
                "resolve_events_and_crises",
                "apply_final_clamps_and_normalizations",
                "close_causal_report",
                "detect_and_publish_blocking_decision"
            }));
            Assert.That(result.FinalState.Tick, Is.EqualTo(1));
            Assert.That(result.TickSnapshot, Is.Not.Null);
            Assert.That(result.TickSnapshot.AuditedTargets.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DueActionsExecuteByPriorityAndCannotReenterInSameTick()
        {
            ContentPack pack = CreatePack();
            List<string> executionOrder = new List<string>();
            SchedulerEngine scheduler = CreateScheduler(pack, new[]
            {
                Pair("record_then_schedule", new DelegateHandler((context, action) =>
                {
                    executionOrder.Add(action.Id);
                    return new ScheduledActionExecutionResult(
                        Array.Empty<ScheduledActionMutation>(),
                        Array.Empty<EffectInstance>(),
                        new[] { Action("follow_up", context.Tick + 1, 0, "record", action.Source) },
                        Array.Empty<string>(),
                        null);
                })),
                Pair("record", new DelegateHandler((context, action) =>
                {
                    executionOrder.Add(action.Id);
                    return ScheduledActionExecutionResult.Empty;
                }))
            });

            GameState state = CreateState(pack, 9);
            state = scheduler.ScheduleAction(state, Action("low", 1, 1, "record", Decision("decision_low")));
            state = scheduler.ScheduleAction(state, Action("high", 1, 5, "record_then_schedule", Decision("decision_high")));

            TickAdvanceResult tick1 = scheduler.AdvanceOneTick(state);
            Assert.That(executionOrder, Is.EqualTo(new[] { "high", "low" }));
            Assert.That(tick1.FinalState.ScheduledActionsById.ContainsKey("follow_up"), Is.True);
            Assert.That(tick1.FinalState.Tick, Is.EqualTo(1));
            Assert.That(tick1.FinalState.ScheduledActionsById["follow_up"].RunTick, Is.EqualTo(2));

            TickAdvanceResult tick2 = scheduler.AdvanceOneTick(tick1.FinalState);
            Assert.That(executionOrder, Is.EqualTo(new[] { "high", "low", "follow_up" }));
            Assert.That(tick2.FinalState.ScheduledActions, Is.Empty);
        }

        [Test]
        public void SchedulerIntegratesEffectLifecycleDeterministically()
        {
            ContentPack pack = CreatePack(
                Effect("eff_sched", Modifier("metrics.legitimacy", TargetOperation.Add, 1000, false), Modifier("metrics.legitimacy", TargetOperation.Add, 100, true)));
            SchedulerEngine scheduler = CreateScheduler(pack, new[]
            {
                Pair("grant_effect", new DelegateHandler((context, action) => new ScheduledActionExecutionResult(
                    Array.Empty<ScheduledActionMutation>(),
                    new[]
                    {
                        Instance("inst_sched", "eff_sched", action.Source, context.Tick, context.Tick + 3, "sched.k", EffectStackMode.Stack, null, 2)
                    },
                    Array.Empty<ScheduledAction>(),
                    Array.Empty<string>(),
                    null)))
            });

            GameState state = CreateState(pack, 4);
            state = scheduler.ScheduleAction(state, Action("grant", 1, 3, "grant_effect", Decision("decision_grant")));

            TickAdvanceResult tick1 = scheduler.AdvanceOneTick(state);
            Assert.That(tick1.FinalState.MetricsById["legitimacy"].ValueS, Is.EqualTo(6100));
            Assert.That(tick1.FinalState.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(tick1.FinalState.ActiveEffects[0].StartInstantApplied, Is.True);

            TickAdvanceResult tick2 = scheduler.AdvanceOneTick(tick1.FinalState);
            Assert.That(tick2.FinalState.MetricsById["legitimacy"].ValueS, Is.EqualTo(6200));

            TickAdvanceResult tick3 = scheduler.AdvanceOneTick(tick2.FinalState);
            Assert.That(tick3.FinalState.MetricsById["legitimacy"].ValueS, Is.EqualTo(6300));
            Assert.That(tick3.FinalState.ActiveEffects.Count, Is.EqualTo(1));

            TickAdvanceResult tick4 = scheduler.AdvanceOneTick(tick3.FinalState);
            Assert.That(tick4.FinalState.MetricsById["legitimacy"].ValueS, Is.EqualTo(6300));
            Assert.That(tick4.FinalState.ActiveEffects, Is.Empty);
        }

        [Test]
        public void AdvanceWeeksMatchesRepeatedSingleTicksAndGroupedFours()
        {
            ContentPack pack = CreatePack(Effect("eff_tick", Modifier("metrics.legitimacy", TargetOperation.Add, 25, true)));
            SchedulerEngine scheduler = CreateScheduler(pack, Array.Empty<KeyValuePair<string, IScheduledActionHandler>>());
            EffectEngine effectEngine = new EffectEngine();

            GameState baseline = effectEngine.RegisterEffect(
                CreateState(pack, 12),
                pack.EffectRuntimeCatalog,
                Instance("inst_tick", "eff_tick", Decision("decision_tick"), 1, 13, "tick.k", EffectStackMode.Stack, null, 1));

            GameState byTwelveSingles = baseline;
            for (int i = 0; i < 12; i++)
            {
                byTwelveSingles = scheduler.AdvanceOneTick(byTwelveSingles).FinalState;
            }

            GameState byThreeFours = baseline;
            for (int i = 0; i < 3; i++)
            {
                byThreeFours = scheduler.AdvanceWeeks(byThreeFours, 4).FinalState;
            }

            GameState byTwelve = scheduler.AdvanceWeeks(baseline, 12).FinalState;
            string hashSingles = new GameStateHasher().ComputeHash(byTwelveSingles);
            string hashFours = new GameStateHasher().ComputeHash(byThreeFours);
            string hashTwelve = new GameStateHasher().ComputeHash(byTwelve);

            Assert.That(hashSingles, Is.EqualTo(hashFours));
            Assert.That(hashSingles, Is.EqualTo(hashTwelve));
        }

        [Test]
        public void BlockingDeadlineStopsAdvanceAtFirstBlockingTick()
        {
            ContentPack pack = CreatePack();
            SchedulerEngine scheduler = CreateScheduler(pack, new[]
            {
                Pair("deadline_block", new DelegateHandler((context, action) => new ScheduledActionExecutionResult(
                    Array.Empty<ScheduledActionMutation>(),
                    Array.Empty<EffectInstance>(),
                    Array.Empty<ScheduledAction>(),
                    Array.Empty<string>(),
                    new BlockingDecision("block_route_a", "deadline_expired", action.Source, context.Tick, ScheduledActionPayload.Empty))))
            });

            GameState state = CreateState(pack, 3);
            state = scheduler.ScheduleAction(state, Action("deadline", 3, 4, "deadline_block", Event("event_deadline")));

            AdvanceWeeksResult result = scheduler.AdvanceWeeks(state, 12);

            Assert.That(result.CompletedTicks, Is.EqualTo(3));
            Assert.That(result.FinalState.Tick, Is.EqualTo(3));
            Assert.That(result.BlockingDecision, Is.Not.Null);
            Assert.That(result.BlockingDecision.Id, Is.EqualTo("block_route_a"));
            Assert.That(result.TickSnapshots.Count, Is.EqualTo(3));
            Assert.That(result.FinalState.BlockingDecision.Id, Is.EqualTo("block_route_a"));
        }

        [Test]
        public void LateActionsAndUnknownHandlersFailClosed()
        {
            ContentPack pack = CreatePack();
            SchedulerEngine scheduler = CreateScheduler(pack, Array.Empty<KeyValuePair<string, IScheduledActionHandler>>());

            GameState late = new GameState(
                1,
                CreateState(pack, 1).ContentMetadata,
                CreateState(pack, 1).Metrics,
                CreateState(pack, 1).Internals,
                CreateState(pack, 1).Regions,
                CreateState(pack, 1).InterestGroups,
                CreateState(pack, 1).Movements,
                CreateState(pack, 1).ActiveEffects,
                1,
                CreateState(pack, 1).RngState,
                new[] { Action("late", 1, 0, "noop", Decision("decision_late")) },
                null);

            Assert.Throws<SchedulerException>(() => scheduler.AdvanceOneTick(late));

            GameState unknown = scheduler.ScheduleAction(CreateState(pack, 2), Action("unknown", 1, 0, "missing_handler", Decision("decision_unknown")));
            Assert.Throws<SchedulerException>(() => scheduler.AdvanceOneTick(unknown));
        }

        [Test]
        public void VisibleDirectMutationsAreRejectedButHiddenOnesRemainAtomic()
        {
            ContentPack pack = CreatePack();
            SchedulerEngine visibleScheduler = CreateScheduler(pack, new[]
            {
                Pair("visible_mutation", new DelegateHandler((context, action) => new ScheduledActionExecutionResult(
                    new[] { new ScheduledActionMutation(new TargetMutation(TargetPath.Parse("metrics.legitimacy"), TargetOperation.Add, 100), action.Source) },
                    Array.Empty<EffectInstance>(),
                    Array.Empty<ScheduledAction>(),
                    Array.Empty<string>(),
                    null)))
            });

            GameState visibleState = visibleScheduler.ScheduleAction(CreateState(pack, 1), Action("visible", 1, 0, "visible_mutation", Decision("decision_visible")));
            Assert.Throws<SchedulerException>(() => visibleScheduler.AdvanceOneTick(visibleState));

            SchedulerEngine hiddenScheduler = CreateScheduler(pack, new[]
            {
                Pair("hidden_mutation", new DelegateHandler((context, action) => new ScheduledActionExecutionResult(
                    new[] { new ScheduledActionMutation(new TargetMutation(TargetPath.Parse("internals.economy.growth"), TargetOperation.Add, 100), action.Source) },
                    Array.Empty<EffectInstance>(),
                    Array.Empty<ScheduledAction>(),
                    Array.Empty<string>(),
                    null)))
            });

            GameState hiddenState = hiddenScheduler.ScheduleAction(CreateState(pack, 1), Action("hidden", 1, 0, "hidden_mutation", Decision("decision_hidden")));
            TickAdvanceResult result = hiddenScheduler.AdvanceOneTick(hiddenState);
            Assert.That(result.FinalState.InternalsByDomain["economy"].ComponentsById["growth"].ValueS, Is.EqualTo(5100));
            Assert.That(result.TickSnapshot.ChangedTargets.Count, Is.EqualTo(0));
        }

        [Test]
        public void Pcg32ContractVectorsMatchIndependentOracle()
        {
            PcgVectorFile vectors = JsonUtility.FromJson<PcgVectorFile>(File.ReadAllText(Path.Combine(ProjectRoot(), "tests", "scheduler", "pcg32_v1_vectors.json"), Encoding.UTF8));

            foreach (PcgInitializationCase init in vectors.initialization_cases)
            {
                long seed = long.Parse(init.seed_i64, CultureInfo.InvariantCulture);
                Pcg32State state = Pcg32State.CreateFromSeed(seed);
                Assert.That(state.StateHex, Is.EqualTo(init.initial_state_u64));
                Assert.That(state.StreamHex, Is.EqualTo(init.initial_stream_u64));
                Assert.That(state.DrawCountHex, Is.EqualTo("0000000000000000"));

                for (int i = 0; i < init.draws_u32.Length; i++)
                {
                    uint sample = state.NextUInt32(out Pcg32State next);
                    Assert.That(sample, Is.EqualTo(init.draws_u32[i]));
                    state = next;
                }

                Assert.That(state.StateHex, Is.EqualTo(init.final_state_u64));
                Assert.That(state.StreamHex, Is.EqualTo(init.final_stream_u64));
                Assert.That(state.DrawCountHex, Is.EqualTo(init.final_draw_count_u64));
            }

            foreach (PcgKeyedCase keyed in vectors.keyed_cases)
            {
                long seed = long.Parse(keyed.seed_i64, CultureInfo.InvariantCulture);
                ulong tick = ulong.Parse(keyed.tick_u64, CultureInfo.InvariantCulture);
                ulong slot = ulong.Parse(keyed.slot_u64, CultureInfo.InvariantCulture);
                Pcg32State state = Pcg32State.CreateFromSeed(seed);
                Pcg32KeyedDraw drawA = state.DeriveKeyedDraw(seed, tick, keyed.system, keyed.template, slot);
                Pcg32KeyedDraw drawB = state.DeriveKeyedDraw(seed, tick, keyed.system, keyed.template, slot);

                Assert.That(drawA.Sample, Is.EqualTo(keyed.keyed_draw_u32));
                Assert.That(drawB.Sample, Is.EqualTo(drawA.Sample));
                Assert.That(state.DrawCountU64, Is.EqualTo(0UL));
            }
        }

        [Test]
        public void Pcg32BoundedDrawCounterAndFailuresMatchContract()
        {
            Pcg32State state = Pcg32State.CreateFromSeed(424242);
            uint value = state.NextBoundedUInt32(1U, out Pcg32State next);
            Assert.That(value, Is.EqualTo(0U));
            Assert.That(next.DrawCountU64, Is.EqualTo(1UL));

            Assert.Throws<Pcg32Exception>(() => state.NextBoundedUInt32(0U, out _));

            Pcg32State exhausted = new Pcg32State(0UL, 1UL, ulong.MaxValue);
            Assert.Throws<Pcg32Exception>(() => exhausted.NextUInt32(out _));
            Assert.Throws<Pcg32Exception>(() => exhausted.NextBoundedUInt32(2U, out _));

            bool observedRejection = false;
            Pcg32State search = Pcg32State.CreateFromSeed(1);
            for (int i = 0; i < 2048 && !observedRejection; i++)
            {
                uint bounded = search.NextBoundedUInt32(2147483649U, out Pcg32State updated);
                observedRejection = updated.DrawCountU64 > search.DrawCountU64 + 1UL;
                Assert.That(bounded, Is.LessThan(2147483649U));
                search = updated;
            }

            Assert.That(observedRejection, Is.True);
        }

        [Test]
        public void GoldenEvidenceIsDeterministicAndMatchesExpectedHash()
        {
            string first = BuildGoldenEvidence();
            string second = BuildGoldenEvidence();
            string expected = File.ReadAllText(Path.Combine(ProjectRoot(), "tests", "scheduler", "scheduler_v1.expected.json"), Encoding.UTF8);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo(expected));
            Assert.That(Hash(first), Is.EqualTo(GoldenHash));
        }

        private static string BuildGoldenEvidence()
        {
            ContentPack pack = CreatePack(
                Effect("eff_sched", Modifier("metrics.legitimacy", TargetOperation.Add, 1000, false), Modifier("metrics.legitimacy", TargetOperation.Add, 100, true)));
            SchedulerEngine scheduler = CreateScheduler(pack, new[]
            {
                Pair("grant_effect", new DelegateHandler((context, action) => new ScheduledActionExecutionResult(
                    Array.Empty<ScheduledActionMutation>(),
                    new[]
                    {
                        Instance("inst_sched", "eff_sched", action.Source, context.Tick, context.Tick + 3, "sched.k", EffectStackMode.Stack, null, 2)
                    },
                    new[]
                    {
                        Action("deadline", context.Tick + 4, 10, "deadline_block", Event("event_deadline"))
                    },
                    Array.Empty<string>(),
                    null))),
                Pair("deadline_block", new DelegateHandler((context, action) => new ScheduledActionExecutionResult(
                    Array.Empty<ScheduledActionMutation>(),
                    Array.Empty<EffectInstance>(),
                    Array.Empty<ScheduledAction>(),
                    Array.Empty<string>(),
                    new BlockingDecision("block_deadline", "deadline_expired", action.Source, context.Tick, ScheduledActionPayload.Empty))))
            });

            GameState state = CreateState(pack, 424242);
            state = scheduler.ScheduleAction(state, Action("grant", 1, 5, "grant_effect", Decision("decision_grant")));
            AdvanceWeeksResult result = scheduler.AdvanceWeeks(state, 12);
            List<string> executedActions = new List<string>();
            GameState replayState = CreateState(pack, 424242);
            SchedulerEngine replayScheduler = CreateScheduler(pack, new[]
            {
                Pair("grant_effect", new DelegateHandler((context, action) =>
                {
                    executedActions.Add(action.Id);
                    return new ScheduledActionExecutionResult(
                        Array.Empty<ScheduledActionMutation>(),
                        new[]
                        {
                            Instance("inst_sched", "eff_sched", action.Source, context.Tick, context.Tick + 3, "sched.k", EffectStackMode.Stack, null, 2)
                        },
                        new[]
                        {
                            Action("deadline", context.Tick + 4, 10, "deadline_block", Event("event_deadline"))
                        },
                        Array.Empty<string>(),
                        null);
                })),
                Pair("deadline_block", new DelegateHandler((context, action) =>
                {
                    executedActions.Add(action.Id);
                    return new ScheduledActionExecutionResult(
                        Array.Empty<ScheduledActionMutation>(),
                        Array.Empty<EffectInstance>(),
                        Array.Empty<ScheduledAction>(),
                        Array.Empty<string>(),
                        new BlockingDecision("block_deadline", "deadline_expired", action.Source, context.Tick, ScheduledActionPayload.Empty));
                }))
            });
            replayState = replayScheduler.ScheduleAction(replayState, Action("grant", 1, 5, "grant_effect", Decision("decision_grant")));
            AdvanceWeeksResult replayResult = replayScheduler.AdvanceWeeks(replayState, 12);

            StringBuilder builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"requested_weeks\": ").Append(result.RequestedWeeks).Append(",\n");
            builder.Append("  \"completed_ticks\": ").Append(result.CompletedTicks).Append(",\n");
            builder.Append("  \"initial_tick\": ").Append(state.Tick).Append(",\n");
            builder.Append("  \"final_tick\": ").Append(result.FinalState.Tick).Append(",\n");
            builder.Append("  \"initial_rng\": ").Append(SerializeRng(state.RngState)).Append(",\n");
            builder.Append("  \"final_rng\": ").Append(SerializeRng(result.FinalState.RngState)).Append(",\n");
            builder.Append("  \"final_state_hash\": \"").Append(new GameStateHasher().ComputeHash(result.FinalState)).Append("\",\n");
            builder.Append("  \"blocking_decision\": ").Append(result.BlockingDecision == null ? "null" : SerializeBlockingDecision(result.BlockingDecision)).Append(",\n");
            builder.Append("  \"executed_actions\": [");
            for (int i = 0; i < executedActions.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("\"").Append(executedActions[i]).Append("\"");
            }

            builder.Append("],\n");
            builder.Append("  \"tick_hashes\": [");
            for (int i = 0; i < replayResult.TickSnapshots.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                string hash = new GameStateHasher().ComputeHash(StateAtTick(state, scheduler, i + 1));
                builder.Append("\"").Append(hash).Append("\"");
            }

            builder.Append("],\n");
            builder.Append("  \"phases\": [");
            List<string> phases = new List<string>();
            scheduler.AdvanceOneTick(state, phase => phases.Add(SchedulerFormatting.FormatPhase(phase)));
            for (int i = 0; i < phases.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("\"").Append(phases[i]).Append("\"");
            }

            builder.Append("],\n");
            builder.Append("  \"causal_ticks\": [\n");
            for (int i = 0; i < result.TickSnapshots.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",\n");
                }

                TickCausalSnapshot snapshot = result.TickSnapshots[i];
                builder.Append("    {\"tick\":").Append(snapshot.Tick).Append(",\"audited_targets\":[");
                for (int j = 0; j < snapshot.AuditedTargets.Count; j++)
                {
                    if (j > 0)
                    {
                        builder.Append(",");
                    }

                    TargetCausalSnapshot target = snapshot.AuditedTargets[j];
                    builder.Append("{\"target\":\"").Append(target.Target).Append("\",\"initial_value_s\":").Append(target.InitialValueS.ToString(CultureInfo.InvariantCulture)).Append(",\"final_value_s\":").Append(target.FinalValueS.ToString(CultureInfo.InvariantCulture)).Append(",\"delta_total_s\":").Append(target.DeltaTotalS.ToString(CultureInfo.InvariantCulture)).Append(",\"contributions\":[");
                    for (int k = 0; k < target.Contributions.Count; k++)
                    {
                        if (k > 0)
                        {
                            builder.Append(",");
                        }

                        builder.Append("{\"cause\":\"").Append(target.Contributions[k].Cause.CanonicalKey).Append("\",\"delta_s\":").Append(target.Contributions[k].DeltaS.ToString(CultureInfo.InvariantCulture)).Append("}");
                    }

                    builder.Append("]}");
                }

                builder.Append("]}");
            }

            builder.Append("\n  ],\n");
            builder.Append("  \"top_n\": [\n");
            CausalTopNProjection projection = CausalTopNProjector.Project(result.PeriodSnapshot);
            for (int i = 0; i < projection.Targets.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",\n");
                }

                ProjectedCausalTarget target = projection.Targets[i];
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

        public static void ExportGoldenEvidence()
        {
            File.WriteAllText(
                Path.Combine(ExportProjectRoot(), "tests", "scheduler", "scheduler_v1.expected.json"),
                BuildGoldenEvidence(),
                new UTF8Encoding(false));
        }

        private static string SerializeBlockingDecision(BlockingDecision decision)
        {
            return "{\"id\":\"" + decision.Id + "\",\"type\":\"" + decision.Type + "\",\"source\":\"" + decision.Source.CanonicalKey + "\",\"created_tick\":" + decision.CreatedTick.ToString(CultureInfo.InvariantCulture) + "}";
        }

        private static string SerializeRng(Pcg32State state)
        {
            return "{\"algorithm\":\"" + Pcg32State.Algorithm + "\",\"contract_version\":\"" + Pcg32State.ContractVersion + "\",\"state_u64\":\"" + state.StateHex + "\",\"stream_u64\":\"" + state.StreamHex + "\",\"draw_count_u64\":\"" + state.DrawCountHex + "\"}";
        }

        private static GameState StateAtTick(GameState startingState, SchedulerEngine scheduler, int weeks)
        {
            GameState working = startingState;
            for (int i = 0; i < weeks; i++)
            {
                working = scheduler.AdvanceOneTick(working).FinalState;
            }

            return working;
        }

        private static SchedulerEngine CreateScheduler(ContentPack pack, IEnumerable<KeyValuePair<string, IScheduledActionHandler>> handlers)
        {
            List<string> regionIds = new List<string>();
            for (int i = 0; i < pack.Regions.Count; i++)
            {
                regionIds.Add(pack.Regions[i].Id);
            }

            List<string> interestGroupIds = new List<string>();
            for (int i = 0; i < pack.InterestGroups.Count; i++)
            {
                interestGroupIds.Add(pack.InterestGroups[i].Id);
            }

            List<string> movementIds = new List<string>();
            for (int i = 0; i < pack.Movements.Count; i++)
            {
                movementIds.Add(pack.Movements[i].Id);
            }

            return new SchedulerEngine(new EffectEngine(), pack.EffectRuntimeCatalog, pack.TargetConfigCatalog, regionIds, interestGroupIds, movementIds, handlers);
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

        private static EffectModifier Modifier(string target, TargetOperation operation, int valueS, bool isPerTick)
        {
            return new EffectModifier(TargetPath.Parse(target), operation, valueS, isPerTick, null);
        }

        private static ScheduledAction Action(string id, int runTick, int priority, string type, CauseRef source)
        {
            return new ScheduledAction(id, runTick, priority, type, ScheduledActionPayload.Empty, source);
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

        private static KeyValuePair<string, IScheduledActionHandler> Pair(string type, IScheduledActionHandler handler)
        {
            return new KeyValuePair<string, IScheduledActionHandler>(type, handler);
        }

        private static CauseRef Decision(string id)
        {
            return new CauseRef(CauseCategory.Decision, id);
        }

        private static CauseRef Event(string id)
        {
            return new CauseRef(CauseCategory.Event, id);
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

        private static string[] SnapshotIds(IReadOnlyList<ScheduledAction> actions)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < actions.Count; i++)
            {
                ids.Add(actions[i].Id);
            }

            return ids.ToArray();
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

        private static string ExportProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private sealed class DelegateHandler : IScheduledActionHandler
        {
            private readonly Func<ScheduledActionExecutionContext, ScheduledAction, ScheduledActionExecutionResult> _execute;

            public DelegateHandler(Func<ScheduledActionExecutionContext, ScheduledAction, ScheduledActionExecutionResult> execute)
            {
                _execute = execute;
            }

            public ScheduledActionExecutionResult Execute(ScheduledActionExecutionContext context, ScheduledAction action)
            {
                return _execute(context, action);
            }
        }

        [Serializable]
        private sealed class PcgVectorFile
        {
            public PcgInitializationCase[] initialization_cases;
            public PcgKeyedCase[] keyed_cases;
        }

        [Serializable]
        private sealed class PcgInitializationCase
        {
            public string seed_i64;
            public string initial_state_u64;
            public string initial_stream_u64;
            public uint[] draws_u32;
            public string final_state_u64;
            public string final_stream_u64;
            public string final_draw_count_u64;
        }

        [Serializable]
        private sealed class PcgKeyedCase
        {
            public string seed_i64;
            public string tick_u64;
            public string system;
            public string template;
            public string slot_u64;
            public uint keyed_draw_u32;
        }
    }
}
