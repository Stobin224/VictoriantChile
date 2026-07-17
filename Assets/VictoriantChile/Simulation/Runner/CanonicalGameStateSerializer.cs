using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                ["content"] = BuildContent(state),
                ["metrics"] = BuildMetrics(state),
                ["internals"] = BuildInternals(state),
                ["regions"] = BuildRegions(state),
                ["interest_groups"] = BuildInterestGroups(state),
                ["movements"] = BuildMovements(state)
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
