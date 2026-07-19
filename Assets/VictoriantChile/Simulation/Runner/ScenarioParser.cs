using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Runner
{
    public sealed class ScenarioParseResult
    {
        public ScenarioParseResult(ScenarioDefinition scenario, IEnumerable<StateDiagnostic> diagnostics)
        {
            Scenario = scenario;
            Diagnostics = Array.AsReadOnly(new List<StateDiagnostic>(diagnostics ?? new StateDiagnostic[0]).ToArray());
        }

        public bool Success => Scenario != null && Diagnostics.Count == 0;

        public ScenarioDefinition Scenario { get; }

        public IReadOnlyList<StateDiagnostic> Diagnostics { get; }
    }

    public sealed class ScenarioParser
    {
        private static readonly Regex CommandIdPattern = new Regex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

        public ScenarioParseResult Parse(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            List<StateDiagnostic> diagnostics = new List<StateDiagnostic>();
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Fail("scenario.invalid_utf8", "$", "Scenario must be valid UTF-8.");
            }

            JObject root;
            try
            {
                JsonTextReader reader = new JsonTextReader(new StringReader(text))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double
                };
                JsonLoadSettings settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    CommentHandling = CommentHandling.Load
                };
                JToken token = JToken.ReadFrom(reader, settings);
                if (reader.Read())
                {
                    diagnostics.Add(new StateDiagnostic("scenario.trailing_content", "$", "Scenario JSON has trailing content."));
                }

                if (ContainsComment(token))
                {
                    diagnostics.Add(new StateDiagnostic("scenario.comment", "$", "Scenario JSON comments are not allowed."));
                }

                root = token as JObject;
                if (root == null)
                {
                    diagnostics.Add(new StateDiagnostic("scenario.invalid_root", "$", "Scenario root must be an object."));
                    return new ScenarioParseResult(null, diagnostics);
                }
            }
            catch (JsonReaderException)
            {
                return Fail("scenario.json_malformed", "$", "Scenario JSON is malformed.");
            }

            RequireOnly(root, "$", diagnostics, "scenario_schema_version", "seed", "commands");
            int schema = ReadRequiredInt(root, "scenario_schema_version", "$.scenario_schema_version", diagnostics);
            int seed = ReadRequiredInt(root, "seed", "$.seed", diagnostics);
            if (schema != 1)
            {
                diagnostics.Add(new StateDiagnostic("scenario.unsupported_schema", "$.scenario_schema_version", "Scenario schema version must be exactly 1."));
            }

            JArray commandsArray = root["commands"] as JArray;
            if (commandsArray == null)
            {
                diagnostics.Add(new StateDiagnostic("scenario.invalid_type", "$.commands", "commands must be an array."));
            }
            else if (commandsArray.Count == 0)
            {
                diagnostics.Add(new StateDiagnostic("scenario.invalid_value", "$.commands", "commands cannot be empty."));
            }

            List<ScenarioCommand> commands = new List<ScenarioCommand>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (commandsArray != null)
            {
                for (int i = 0; i < commandsArray.Count; i++)
                {
                    JObject command = commandsArray[i] as JObject;
                    string path = "$.commands[" + i + "]";
                    if (command == null)
                    {
                        diagnostics.Add(new StateDiagnostic("scenario.invalid_type", path, "Command must be an object."));
                        continue;
                    }

                    ParseCommand(command, path, ids, commands, diagnostics);
                }
            }

            return diagnostics.Count == 0
                ? new ScenarioParseResult(new ScenarioDefinition(schema, seed, commands), diagnostics)
                : new ScenarioParseResult(null, diagnostics);
        }

        private static void ParseCommand(JObject command, string path, HashSet<string> ids, List<ScenarioCommand> commands, List<StateDiagnostic> diagnostics)
        {
            string typeText = ReadRequiredString(command, "type", path + ".type", diagnostics);
            bool isRead = typeText == "READ";
            bool isMutate = typeText == "MUTATE";
            bool isAdvance = typeText == "ADVANCE";
            if (!isRead && !isMutate && !isAdvance)
            {
                diagnostics.Add(new StateDiagnostic("scenario.invalid_value", path + ".type", "Command type must be READ, MUTATE, or ADVANCE."));
            }

            if (isRead)
            {
                RequireOnly(command, path, diagnostics, "id", "type", "target");
            }
            else if (isMutate)
            {
                RequireOnly(command, path, diagnostics, "id", "type", "target", "op", "value_s");
            }
            else if (isAdvance)
            {
                RequireOnly(command, path, diagnostics, "id", "type", "weeks");
            }

            string id = ReadRequiredString(command, "id", path + ".id", diagnostics);
            if (!string.IsNullOrEmpty(id))
            {
                if (!CommandIdPattern.IsMatch(id))
                {
                    diagnostics.Add(new StateDiagnostic("scenario.invalid_value", path + ".id", "Command ID must be ASCII lowercase snake_case."));
                }

                if (!ids.Add(id))
                {
                    diagnostics.Add(new StateDiagnostic("scenario.duplicate_id", path + ".id", "Command ID must be unique."));
                }
            }

            TargetPath target = default;
            if (isRead || isMutate)
            {
                string targetText = ReadRequiredString(command, "target", path + ".target", diagnostics);
                if (!string.IsNullOrEmpty(targetText) && !TargetPath.TryParse(targetText, out target))
                {
                    diagnostics.Add(new StateDiagnostic("target.invalid_path", path + ".target", "Target must be a concrete canonical TargetPath."));
                }
            }

            TargetOperation operation = TargetOperation.Add;
            int valueS = 0;
            int weeks = 0;
            if (isMutate)
            {
                string opText = ReadRequiredString(command, "op", path + ".op", diagnostics);
                if (opText == "ADD")
                {
                    operation = TargetOperation.Add;
                }
                else if (opText == "MUL")
                {
                    operation = TargetOperation.Multiply;
                }
                else if (opText == "SET")
                {
                    operation = TargetOperation.Set;
                }
                else
                {
                    diagnostics.Add(new StateDiagnostic("scenario.invalid_value", path + ".op", "Operation must be ADD, MUL, or SET."));
                }

                valueS = ReadRequiredInt(command, "value_s", path + ".value_s", diagnostics);
            }
            else if (isRead)
            {
                if (command["op"] != null || command["value_s"] != null)
                {
                    diagnostics.Add(new StateDiagnostic("scenario.unknown_property", path, "READ commands cannot include op or value_s."));
                }
            }
            else if (isAdvance)
            {
                weeks = ReadRequiredInt(command, "weeks", path + ".weeks", diagnostics);
                if (weeks != 1 && weeks != 4 && weeks != 12)
                {
                    diagnostics.Add(new StateDiagnostic("scenario.invalid_value", path + ".weeks", "ADVANCE weeks must be exactly 1, 4, or 12."));
                }
            }

            if (diagnostics.Count == 0 || target.IsValid || isAdvance)
            {
                commands.Add(new ScenarioCommand(
                    id,
                    isRead ? ScenarioCommandType.Read : (isMutate ? ScenarioCommandType.Mutate : ScenarioCommandType.Advance),
                    target,
                    operation,
                    valueS,
                    weeks));
            }
        }

        private static bool ContainsComment(JToken token)
        {
            if (token.Type == JTokenType.Comment)
            {
                return true;
            }

            foreach (JToken child in token.Children())
            {
                if (ContainsComment(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ReadRequiredInt(JObject obj, string name, string path, List<StateDiagnostic> diagnostics)
        {
            JToken token = obj[name];
            if (token == null)
            {
                diagnostics.Add(new StateDiagnostic("scenario.missing_property", path, "Required integer property is missing."));
                return 0;
            }

            if (token.Type != JTokenType.Integer)
            {
                diagnostics.Add(new StateDiagnostic("scenario.invalid_type", path, "Value must be an integer."));
                return 0;
            }

            long value = token.Value<long>();
            if (value < int.MinValue || value > int.MaxValue)
            {
                diagnostics.Add(new StateDiagnostic("scenario.invalid_value", path, "Integer value is outside Int32 range."));
                return 0;
            }

            return (int)value;
        }

        private static string ReadRequiredString(JObject obj, string name, string path, List<StateDiagnostic> diagnostics)
        {
            JToken token = obj[name];
            if (token == null)
            {
                diagnostics.Add(new StateDiagnostic("scenario.missing_property", path, "Required string property is missing."));
                return string.Empty;
            }

            if (token.Type != JTokenType.String)
            {
                diagnostics.Add(new StateDiagnostic("scenario.invalid_type", path, "Value must be a string."));
                return string.Empty;
            }

            string value = token.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                diagnostics.Add(new StateDiagnostic("scenario.invalid_value", path, "String value cannot be empty."));
            }

            return value;
        }

        private static void RequireOnly(JObject obj, string path, List<StateDiagnostic> diagnostics, params string[] allowed)
        {
            HashSet<string> allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (JProperty property in obj.Properties())
            {
                if (!allowedSet.Contains(property.Name))
                {
                    diagnostics.Add(new StateDiagnostic("scenario.unknown_property", path + "." + property.Name, "Unknown property is not allowed."));
                }
            }
        }

        private static ScenarioParseResult Fail(string code, string target, string message)
        {
            return new ScenarioParseResult(null, new[] { new StateDiagnostic(code, target, message) });
        }
    }
}
