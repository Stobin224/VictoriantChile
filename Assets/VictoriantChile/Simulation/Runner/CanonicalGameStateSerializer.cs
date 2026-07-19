using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Effects;
using VictoriantChile.Simulation.Core.Numerics;
using VictoriantChile.Simulation.Core.Scheduling;
using VictoriantChile.Simulation.Core.State;

namespace VictoriantChile.Simulation.Runner
{
    public sealed class CanonicalGameStateSerializer
    {
        public JObject ToJObject(GameState state)
        {
            JObject root = new JObject
            {
                ["state_schema_version"] = state.StateSchemaVersion,
                ["tick"] = state.Tick,
                ["rng_seed"] = state.RngSeed,
                ["rng"] = BuildRng(state.RngState),
                ["blocking_decision"] = state.BlockingDecision == null ? JValue.CreateNull() : BuildBlockingDecision(state.BlockingDecision),
                ["content"] = BuildContent(state),
                ["metrics"] = BuildMetrics(state),
                ["internals"] = BuildInternals(state),
                ["regions"] = BuildRegions(state),
                ["interest_groups"] = BuildInterestGroups(state),
                ["movements"] = BuildMovements(state),
                ["active_effects"] = BuildActiveEffects(state),
                ["scheduled_actions"] = BuildScheduledActions(state)
            };
            return root;
        }

        public string ToCompactJson(GameState state)
        {
            return Write(ToJObject(state), Formatting.None);
        }

        public string ToPrettyJson(JToken token)
        {
            return Write(token, Formatting.Indented) + "\n";
        }

        private static JObject BuildContent(GameState state)
        {
            JArray files = new JArray();
            for (int i = 0; i < state.ContentMetadata.Files.Count; i++)
            {
                ContentFileIdentity file = state.ContentMetadata.Files[i];
                files.Add(new JObject
                {
                    ["path"] = file.RelativePath,
                    ["hash"] = file.CanonicalHash
                });
            }

            return new JObject
            {
                ["content_pack_version"] = state.ContentMetadata.ContentPackVersion,
                ["content_schema_version"] = state.ContentMetadata.ContentSchemaVersion,
                ["min_game_schema_version"] = state.ContentMetadata.MinimumGameSchemaVersion,
                ["default_language"] = state.ContentMetadata.DefaultLanguage,
                ["files"] = files
            };
        }

        private static JArray BuildMetrics(GameState state)
        {
            JArray values = new JArray();
            for (int i = 0; i < state.Metrics.Count; i++)
            {
                MetricState metric = state.Metrics[i];
                values.Add(new JObject
                {
                    ["id"] = metric.MetricId,
                    ["value_s"] = metric.ValueS
                });
            }

            return values;
        }

        private static JArray BuildInternals(GameState state)
        {
            JArray domains = new JArray();
            for (int i = 0; i < state.Internals.Count; i++)
            {
                InternalDomainState domain = state.Internals[i];
                JArray components = new JArray();
                for (int j = 0; j < domain.Components.Count; j++)
                {
                    InternalValueState component = domain.Components[j];
                    components.Add(new JObject
                    {
                        ["id"] = component.ComponentId,
                        ["value_s"] = component.ValueS
                    });
                }

                domains.Add(new JObject
                {
                    ["domain"] = domain.Domain,
                    ["components"] = components
                });
            }

            return domains;
        }

        private static JArray BuildRegions(GameState state)
        {
            JArray values = new JArray();
            for (int i = 0; i < state.Regions.Count; i++)
            {
                RegionState region = state.Regions[i];
                values.Add(new JObject
                {
                    ["id"] = region.RegionId,
                    ["support_s"] = region.SupportS,
                    ["tension_s"] = region.TensionS,
                    ["organization_s"] = region.OrganizationS,
                    ["rival_presence_s"] = region.RivalPresenceS
                });
            }

            return values;
        }

        private static JArray BuildInterestGroups(GameState state)
        {
            JArray values = new JArray();
            for (int i = 0; i < state.InterestGroups.Count; i++)
            {
                InterestGroupState group = state.InterestGroups[i];
                values.Add(new JObject
                {
                    ["id"] = group.InterestGroupId,
                    ["clout_s"] = group.CloutS,
                    ["approval_s"] = group.ApprovalS
                });
            }

            return values;
        }

        private static JArray BuildMovements(GameState state)
        {
            JArray values = new JArray();
            for (int i = 0; i < state.Movements.Count; i++)
            {
                MovementState movement = state.Movements[i];
                values.Add(new JObject
                {
                    ["id"] = movement.MovementId,
                    ["intensity_s"] = movement.IntensityS,
                    ["direction"] = movement.Direction
                });
            }

            return values;
        }

        private static JArray BuildActiveEffects(GameState state)
        {
            JArray values = new JArray();
            for (int i = 0; i < state.ActiveEffects.Count; i++)
            {
                EffectInstance instance = state.ActiveEffects[i];
                JObject item = new JObject
                {
                    ["id"] = instance.Id,
                    ["template_id"] = instance.TemplateId,
                    ["origin"] = BuildCause(instance.Origin),
                    ["start_tick"] = instance.StartTick,
                    ["end_tick_exclusive"] = instance.EndTickExclusive.HasValue ? (JToken)instance.EndTickExclusive.Value : JValue.CreateNull(),
                    ["stack_key"] = instance.StackKey,
                    ["stack_mode"] = FormatStackMode(instance.StackMode),
                    ["stack_limit_n"] = instance.StackLimitN.HasValue ? (JToken)instance.StackLimitN.Value : JValue.CreateNull(),
                    ["priority"] = instance.Priority,
                    ["start_instant_applied"] = instance.StartInstantApplied
                };
                values.Add(item);
            }

            return values;
        }

        private static JObject BuildRng(Pcg32State state)
        {
            return new JObject
            {
                ["algorithm"] = Pcg32State.Algorithm,
                ["contract_version"] = Pcg32State.ContractVersion,
                ["state_u64"] = state.StateHex,
                ["stream_u64"] = state.StreamHex,
                ["draw_count_u64"] = state.DrawCountHex
            };
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

        private static JArray BuildScheduledActions(GameState state)
        {
            JArray values = new JArray();
            for (int i = 0; i < state.ScheduledActions.Count; i++)
            {
                ScheduledAction action = state.ScheduledActions[i];
                values.Add(new JObject
                {
                    ["id"] = action.Id,
                    ["run_tick"] = action.RunTick,
                    ["priority"] = action.Priority,
                    ["type"] = action.Type,
                    ["source"] = BuildCause(action.Source),
                    ["payload"] = BuildPayload(action.Payload)
                });
            }

            return values;
        }

        private static JObject BuildPayload(ScheduledActionPayload payload)
        {
            JObject root = new JObject();
            for (int i = 0; i < payload.Entries.Count; i++)
            {
                ScheduledActionPayloadEntry entry = payload.Entries[i];
                root.Add(entry.Key, entry.Value);
            }

            return root;
        }

        private static JObject BuildCause(CauseRef cause)
        {
            JObject item = new JObject
            {
                ["category"] = FormatCauseCategory(cause.Category),
                ["id"] = cause.Id,
                ["parent"] = cause.Parent == null ? JValue.CreateNull() : BuildCause(cause.Parent)
            };
            return item;
        }

        private static string FormatCauseCategory(CauseCategory category)
        {
            switch (category)
            {
                case CauseCategory.Decision: return "DECISION";
                case CauseCategory.Event: return "EVENT";
                case CauseCategory.Reform: return "REFORM";
                case CauseCategory.Movement: return "MOVEMENT";
                case CauseCategory.Modifier: return "MODIFIER";
                case CauseCategory.System: return "SYSTEM";
                default: throw new InvalidOperationException("Unsupported cause category.");
            }
        }

        private static string FormatStackMode(EffectStackMode stackMode)
        {
            switch (stackMode)
            {
                case EffectStackMode.Stack: return "STACK";
                case EffectStackMode.Replace: return "REPLACE";
                case EffectStackMode.Refresh: return "REFRESH";
                case EffectStackMode.Max: return "MAX";
                case EffectStackMode.StackLimitN: return "STACK_LIMIT_N";
                default: throw new InvalidOperationException("Unsupported effect stack mode.");
            }
        }

        public static string Write(JToken token, Formatting formatting)
        {
            StringWriter stringWriter = new StringWriter(CultureInfo.InvariantCulture);
            JsonTextWriter writer = new JsonTextWriter(stringWriter)
            {
                Formatting = formatting,
                Culture = CultureInfo.InvariantCulture,
                StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
            };
            token.WriteTo(writer);
            writer.Flush();
            return stringWriter.ToString();
        }
    }
}
