using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VictoriantChile.Content.Diagnostics;
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

namespace VictoriantChile.Simulation.Runner
{
    public sealed class ScenarioRunner
    {
        public const int ResultSchemaVersion = 1;

        public ScenarioRunnerResult Run(byte[] scenarioBytes, string contentRoot)
        {
            if (contentRoot == null)
            {
                throw new ArgumentNullException(nameof(contentRoot));
            }

            return Run(scenarioBytes, new DirectoryContentFileSource(contentRoot));
        }

        internal ScenarioRunnerResult Run(byte[] scenarioBytes, IContentFileSource contentSource)
        {
            if (scenarioBytes == null)
            {
                throw new ArgumentNullException(nameof(scenarioBytes));
            }

            if (contentSource == null)
            {
                throw new ArgumentNullException(nameof(contentSource));
            }

            ScenarioParseResult parse = new ScenarioParser().Parse(scenarioBytes);
            if (!parse.Success)
            {
                return Failed(0, 0, 0, new CommandExecutionResult[0], parse.Diagnostics);
            }

            ScenarioDefinition scenario = parse.Scenario;
            ContentLoadResult content = new ContentPackLoader().Load(contentSource);
            if (!content.IsSuccess)
            {
                return Failed(scenario.ScenarioSchemaVersion, scenario.Seed, scenario.Commands.Count, new CommandExecutionResult[0], ConvertContentDiagnostics(content.Diagnostics));
            }

            StateInitializationResult initial = new GameStateFactory().CreateInitialState(content.Pack, scenario.Seed);
            if (!initial.Success)
            {
                return Failed(scenario.ScenarioSchemaVersion, scenario.Seed, scenario.Commands.Count, new CommandExecutionResult[0], ConvertInitializationDiagnostics(initial.Diagnostics));
            }

            GameState state = initial.State;
            IReadOnlyList<StateDiagnostic> initialInvariant = new GameStateInvariantValidator().Validate(state, content.Pack.TargetConfigCatalog);
            if (initialInvariant.Count > 0)
            {
                return Failed(scenario.ScenarioSchemaVersion, scenario.Seed, scenario.Commands.Count, new CommandExecutionResult[0], initialInvariant);
            }

            List<CommandExecutionResult> commandResults = new List<CommandExecutionResult>();
            for (int i = 0; i < scenario.Commands.Count; i++)
            {
                ScenarioCommand command = scenario.Commands[i];
                if (command.Type == ScenarioCommandType.Read)
                {
                    CommandExecutionResult read = ExecuteRead(i, command, state, content.Pack);
                    commandResults.Add(read);
                    if (read.Status != "passed")
                    {
                        return Failed(scenario.ScenarioSchemaVersion, scenario.Seed, scenario.Commands.Count, commandResults, read.Diagnostics);
                    }
                }
                else if (command.Type == ScenarioCommandType.Mutate)
                {
                    CommandExecutionResult mutate = ExecuteMutate(i, command, state, content.Pack.TargetConfigCatalog, out GameState next);
                    commandResults.Add(mutate);
                    if (mutate.Status != "passed")
                    {
                        return Failed(scenario.ScenarioSchemaVersion, scenario.Seed, scenario.Commands.Count, commandResults, mutate.Diagnostics);
                    }

                    state = next;
                }
                else
                {
                    CommandExecutionResult advance = ExecuteAdvance(i, command, state, content.Pack, out GameState next);
                    commandResults.Add(advance);
                    if (advance.Status != "passed")
                    {
                        return Failed(scenario.ScenarioSchemaVersion, scenario.Seed, scenario.Commands.Count, commandResults, advance.Diagnostics);
                    }

                    state = next;
                }
            }

            IReadOnlyList<StateDiagnostic> finalInvariant = new GameStateInvariantValidator().Validate(state, content.Pack.TargetConfigCatalog);
            if (finalInvariant.Count > 0)
            {
                return Failed(scenario.ScenarioSchemaVersion, scenario.Seed, scenario.Commands.Count, commandResults, finalInvariant);
            }

            JObject stateJson = new CanonicalGameStateSerializer().ToJObject(state);
            string stateHash = new GameStateHasher().ComputeHash(state);
            return new ScenarioRunnerResult("passed", scenario.ScenarioSchemaVersion, scenario.Seed, scenario.Commands.Count, commandResults, stateHash, stateJson, new StateDiagnostic[0]);
        }

        public JObject ToResultJson(ScenarioRunnerResult result)
        {
            JObject root = new JObject
            {
                ["result_schema_version"] = ResultSchemaVersion,
                ["status"] = result.Status,
                ["scenario_schema_version"] = result.ScenarioSchemaVersion,
                ["seed"] = result.Seed,
                ["command_count"] = result.CommandCount,
                ["commands"] = BuildCommands(result.Commands),
                ["state_hash"] = result.StateHash == null ? JValue.CreateNull() : new JValue(result.StateHash),
                ["state"] = result.State == null ? JValue.CreateNull() : result.State,
                ["diagnostics"] = BuildDiagnostics(result.Diagnostics)
            };
            return root;
        }

        public string ToPrettyJson(ScenarioRunnerResult result)
        {
            return new CanonicalGameStateSerializer().ToPrettyJson(ToResultJson(result));
        }

        private static CommandExecutionResult ExecuteRead(int index, ScenarioCommand command, GameState state, Content.Models.ContentPack pack)
        {
            StateTargetReader reader = new StateTargetReader(state, new ContentPackStaticTargetSource(pack));
            TargetReadResult read = reader.Read(command.Target);
            return new CommandExecutionResult
            {
                Index = index,
                Id = command.Id,
                Type = "read",
                Status = read.Success ? "passed" : "failed",
                Target = command.Target.ToString(),
                Source = read.Success ? SourceToJson(read.Source) : null,
                ValueS = read.Success ? read.ValueS : (int?)null,
                Operation = null,
                BeforeS = null,
                RequestedS = null,
                AfterS = null,
                Clamped = false,
                NormalizeGroup = null,
                Diagnostics = read.Diagnostics
            };
        }

        private static CommandExecutionResult ExecuteMutate(int index, ScenarioCommand command, GameState state, TargetConfigCatalog configs, out GameState next)
        {
            TargetMutation mutation = new TargetMutation(command.Target, command.Operation, command.ValueS);
            StateMutationResult result = new GameStateMutator().Apply(state, mutation, configs);
            next = result.State;
            return new CommandExecutionResult
            {
                Index = index,
                Id = command.Id,
                Type = "mutate",
                Status = result.Success ? "passed" : "failed",
                Target = command.Target.ToString(),
                Source = "dynamic_state",
                ValueS = null,
                Operation = OperationToJson(command.Operation),
                BeforeS = result.Success ? result.BeforeS : (int?)null,
                RequestedS = command.ValueS,
                AfterS = result.Success ? result.AfterS : (int?)null,
                Clamped = result.Success && result.Clamped,
                NormalizeGroup = result.NormalizeGroup,
                Diagnostics = result.Diagnostics,
                TickStateHashes = Array.Empty<string>(),
                CausalTicks = Array.Empty<TickCausalSnapshot>()
            };
        }

        private static CommandExecutionResult ExecuteAdvance(int index, ScenarioCommand command, GameState state, ContentPack pack, out GameState next)
        {
            SchedulerEngine scheduler = CreateScheduler(pack);
            try
            {
                List<string> hashes = new List<string>();
                List<TickCausalSnapshot> ticks = new List<TickCausalSnapshot>();
                GameState working = state;
                BlockingDecision blockingDecision = state.BlockingDecision;
                for (int i = 0; i < command.Weeks; i++)
                {
                    TickAdvanceResult tick = scheduler.AdvanceOneTick(working);
                    working = tick.FinalState;
                    blockingDecision = tick.BlockingDecision ?? working.BlockingDecision;
                    if (tick.TickSnapshot == null)
                    {
                        break;
                    }

                    ticks.Add(tick.TickSnapshot);
                    hashes.Add(new GameStateHasher().ComputeHash(working));
                    if (blockingDecision != null)
                    {
                        break;
                    }
                }

                next = working;
                return new CommandExecutionResult
                {
                    Index = index,
                    Id = command.Id,
                    Type = "advance",
                    Status = "passed",
                    Target = "scheduler.advance",
                    Source = "dynamic_state",
                    ValueS = null,
                    Operation = null,
                    BeforeS = null,
                    RequestedS = null,
                    AfterS = null,
                    Clamped = false,
                    NormalizeGroup = null,
                    Diagnostics = Array.Empty<StateDiagnostic>(),
                    WeeksRequested = command.Weeks,
                    TicksCompleted = ticks.Count,
                    BlockingDecision = blockingDecision,
                    TickStateHashes = hashes,
                    CausalTicks = ticks
                };
            }
            catch (SchedulerException exception)
            {
                next = null;
                return new CommandExecutionResult
                {
                    Index = index,
                    Id = command.Id,
                    Type = "advance",
                    Status = "failed",
                    Target = "scheduler.advance",
                    Source = "dynamic_state",
                    Diagnostics = new[] { new StateDiagnostic(exception.Code, exception.Detail ?? "scheduler.advance", exception.Message) },
                    WeeksRequested = command.Weeks,
                    TickStateHashes = Array.Empty<string>(),
                    CausalTicks = Array.Empty<TickCausalSnapshot>()
                };
            }
            catch (AggregationExecutionException exception)
            {
                next = null;
                return new CommandExecutionResult
                {
                    Index = index,
                    Id = command.Id,
                    Type = "advance",
                    Status = "failed",
                    Target = "scheduler.advance",
                    Source = "dynamic_state",
                    Diagnostics = new[] { new StateDiagnostic(exception.Code, exception.Target, exception.Message) },
                    WeeksRequested = command.Weeks,
                    TickStateHashes = Array.Empty<string>(),
                    CausalTicks = Array.Empty<TickCausalSnapshot>()
                };
            }
        }

        private static ScenarioRunnerResult Failed(
            int scenarioSchemaVersion,
            int seed,
            int commandCount,
            IEnumerable<CommandExecutionResult> commands,
            IEnumerable<StateDiagnostic> diagnostics)
        {
            return new ScenarioRunnerResult("failed", scenarioSchemaVersion, seed, commandCount, commands, null, null, diagnostics);
        }

        private static IReadOnlyList<StateDiagnostic> ConvertContentDiagnostics(IEnumerable<ContentDiagnostic> diagnostics)
        {
            List<StateDiagnostic> result = new List<StateDiagnostic>();
            foreach (ContentDiagnostic diagnostic in diagnostics)
            {
                result.Add(new StateDiagnostic("content." + diagnostic.Code, diagnostic.RelativeFile + diagnostic.JsonPath, diagnostic.Message));
            }

            return result;
        }

        private static IReadOnlyList<StateDiagnostic> ConvertInitializationDiagnostics(IEnumerable<StateInitializationDiagnostic> diagnostics)
        {
            List<StateDiagnostic> result = new List<StateDiagnostic>();
            foreach (StateInitializationDiagnostic diagnostic in diagnostics)
            {
                result.Add(new StateDiagnostic("state." + diagnostic.Code, diagnostic.Target, diagnostic.Message));
            }

            return result;
        }

        private static JArray BuildCommands(IEnumerable<CommandExecutionResult> commands)
        {
            JArray values = new JArray();
            foreach (CommandExecutionResult command in commands)
            {
                values.Add(new JObject
                {
                    ["index"] = command.Index,
                    ["id"] = command.Id,
                    ["type"] = command.Type,
                    ["status"] = command.Status,
                    ["target"] = command.Target,
                    ["source"] = command.Source == null ? JValue.CreateNull() : new JValue(command.Source),
                    ["value_s"] = command.ValueS.HasValue ? new JValue(command.ValueS.Value) : JValue.CreateNull(),
                    ["operation"] = command.Operation == null ? JValue.CreateNull() : new JValue(command.Operation),
                    ["before_s"] = command.BeforeS.HasValue ? new JValue(command.BeforeS.Value) : JValue.CreateNull(),
                    ["requested_s"] = command.RequestedS.HasValue ? new JValue(command.RequestedS.Value) : JValue.CreateNull(),
                    ["after_s"] = command.AfterS.HasValue ? new JValue(command.AfterS.Value) : JValue.CreateNull(),
                    ["clamped"] = command.Clamped,
                    ["normalize_group"] = command.NormalizeGroup == null ? JValue.CreateNull() : new JValue(command.NormalizeGroup),
                    ["diagnostics"] = BuildDiagnostics(command.Diagnostics)
                });

                JObject item = (JObject)values[values.Count - 1];
                if (command.WeeksRequested.HasValue)
                {
                    item["weeks_requested"] = command.WeeksRequested.Value;
                    item["ticks_completed"] = command.TicksCompleted.GetValueOrDefault();
                    item["blocking_decision"] = command.BlockingDecision == null ? JValue.CreateNull() : BuildBlockingDecision(command.BlockingDecision);
                    item["tick_state_hashes"] = BuildTickStateHashes(command.TickStateHashes);
                    item["causal_ticks"] = BuildCausalTicks(command.CausalTicks);
                }
            }

            return values;
        }

        private static JArray BuildTickStateHashes(IReadOnlyList<string> hashes)
        {
            JArray values = new JArray();
            if (hashes == null)
            {
                return values;
            }

            for (int i = 0; i < hashes.Count; i++)
            {
                values.Add(hashes[i]);
            }

            return values;
        }

        private static JArray BuildCausalTicks(IReadOnlyList<TickCausalSnapshot> snapshots)
        {
            JArray values = new JArray();
            if (snapshots == null)
            {
                return values;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                TickCausalSnapshot snapshot = snapshots[i];
                JArray targets = new JArray();
                for (int j = 0; j < snapshot.AuditedTargets.Count; j++)
                {
                    TargetCausalSnapshot target = snapshot.AuditedTargets[j];
                    JArray contributions = new JArray();
                    for (int k = 0; k < target.Contributions.Count; k++)
                    {
                        contributions.Add(new JObject
                        {
                            ["cause"] = target.Contributions[k].Cause.CanonicalKey,
                            ["delta_s"] = target.Contributions[k].DeltaS
                        });
                    }

                    targets.Add(new JObject
                    {
                        ["target"] = target.Target.ToString(),
                        ["initial_value_s"] = target.InitialValueS,
                        ["final_value_s"] = target.FinalValueS,
                        ["delta_total_s"] = target.DeltaTotalS,
                        ["contributions"] = contributions
                    });
                }

                values.Add(new JObject
                {
                    ["tick"] = snapshot.Tick,
                    ["audited_targets"] = targets
                });
            }

            return values;
        }

        private static JArray BuildDiagnostics(IEnumerable<StateDiagnostic> diagnostics)
        {
            JArray values = new JArray();
            foreach (StateDiagnostic diagnostic in diagnostics)
            {
                values.Add(new JObject
                {
                    ["code"] = diagnostic.Code,
                    ["target"] = diagnostic.Target,
                    ["message"] = diagnostic.Message
                });
            }

            return values;
        }

        private static string SourceToJson(TargetValueSource source)
        {
            return source == TargetValueSource.StaticContent ? "static_content" : "dynamic_state";
        }

        private static string OperationToJson(TargetOperation operation)
        {
            if (operation == TargetOperation.Add)
            {
                return "add";
            }

            if (operation == TargetOperation.Multiply)
            {
                return "mul";
            }

            return "set";
        }

        private static SchedulerEngine CreateScheduler(ContentPack pack)
        {
            List<string> regionIds = new List<string>(pack.Regions.Count);
            for (int i = 0; i < pack.Regions.Count; i++)
            {
                regionIds.Add(pack.Regions[i].Id);
            }

            List<string> interestGroupIds = new List<string>(pack.InterestGroups.Count);
            for (int i = 0; i < pack.InterestGroups.Count; i++)
            {
                interestGroupIds.Add(pack.InterestGroups[i].Id);
            }

            List<string> movementIds = new List<string>(pack.Movements.Count);
            for (int i = 0; i < pack.Movements.Count; i++)
            {
                movementIds.Add(pack.Movements[i].Id);
            }

            return new SchedulerEngine(
                new EffectEngine(),
                pack.EffectRuntimeCatalog,
                pack.TargetConfigCatalog,
                pack.AggregationRuntimePlan,
                regionIds,
                interestGroupIds,
                movementIds,
                Array.Empty<KeyValuePair<string, IScheduledActionHandler>>());
        }

        private static JObject BuildBlockingDecision(BlockingDecision decision)
        {
            return new JObject
            {
                ["id"] = decision.Id,
                ["type"] = decision.Type,
                ["source"] = BuildCause(decision.Source),
                ["created_tick"] = decision.CreatedTick,
                ["payload"] = BuildPayload(decision.Payload)
            };
        }

        private static JObject BuildCause(CauseRef cause)
        {
            return new JObject
            {
                ["category"] = FormatCauseCategory(cause.Category),
                ["id"] = cause.Id,
                ["parent"] = cause.Parent == null ? JValue.CreateNull() : BuildCause(cause.Parent)
            };
        }

        private static JObject BuildPayload(ScheduledActionPayload payload)
        {
            JObject root = new JObject();
            if (payload == null)
            {
                return root;
            }

            for (int i = 0; i < payload.Entries.Count; i++)
            {
                root[payload.Entries[i].Key] = payload.Entries[i].Value;
            }

            return root;
        }

        private static string FormatCauseCategory(CauseCategory category)
        {
            return category switch
            {
                CauseCategory.Decision => "DECISION",
                CauseCategory.Event => "EVENT",
                CauseCategory.Reform => "REFORM",
                CauseCategory.Movement => "MOVEMENT",
                CauseCategory.Modifier => "MODIFIER",
                CauseCategory.System => "SYSTEM",
                _ => throw new InvalidOperationException("Unsupported cause category.")
            };
        }
    }
}
