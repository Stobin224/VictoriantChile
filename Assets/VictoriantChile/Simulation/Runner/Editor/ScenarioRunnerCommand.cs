using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VictoriantChile.Simulation.Runner.Editor
{
    public static class ScenarioRunnerCommand
    {
        public static void Run()
        {
            int exitCode = 3;
            string jsonOutput = null;
            try
            {
                Dictionary<string, string> args = ParseArgs(Environment.GetCommandLineArgs());
                bool hasScenario = args.TryGetValue("--scenario", out string scenarioPath);
                bool hasContentRoot = args.TryGetValue("--content-root", out string contentRoot);
                bool hasJsonOutput = args.TryGetValue("--json-output", out jsonOutput);
                if (!hasScenario || !hasContentRoot || !hasJsonOutput)
                {
                    jsonOutput = jsonOutput ?? Path.Combine(Path.GetTempPath(), "VictoriantChile", "ScenarioRunner", "missing-args.json");
                    WriteFailure(jsonOutput, "runner.invalid_args", "Scenario, content-root, and json-output are required.");
                    exitCode = 2;
                    return;
                }

                byte[] scenarioBytes = File.ReadAllBytes(Path.GetFullPath(scenarioPath));
                ScenarioRunner runner = new ScenarioRunner();
                ScenarioRunnerResult result = runner.Run(scenarioBytes, Path.GetFullPath(contentRoot));
                WriteJson(jsonOutput, runner.ToPrettyJson(result));
                exitCode = result.Status == "passed" ? 0 : 2;
                Debug.Log("ScenarioRunner completed with status: " + result.Status);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (string.IsNullOrEmpty(jsonOutput))
                {
                    jsonOutput = Path.Combine(Path.GetTempPath(), "VictoriantChile", "ScenarioRunner", "unexpected.json");
                }

                WriteFailure(jsonOutput, "runner.unexpected", "Unexpected runner infrastructure error.");
                exitCode = 3;
            }
            finally
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static Dictionary<string, string> ParseArgs(string[] rawArgs)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < rawArgs.Length; i++)
            {
                string arg = rawArgs[i];
                if ((arg == "--scenario" || arg == "--content-root" || arg == "--json-output") && i + 1 < rawArgs.Length)
                {
                    result[arg] = rawArgs[i + 1];
                    i++;
                }
            }

            return result;
        }

        private static void WriteFailure(string path, string code, string message)
        {
            JObject root = new JObject
            {
                ["result_schema_version"] = ScenarioRunner.ResultSchemaVersion,
                ["status"] = "failed",
                ["scenario_schema_version"] = 0,
                ["seed"] = 0,
                ["command_count"] = 0,
                ["commands"] = new JArray(),
                ["state_hash"] = JValue.CreateNull(),
                ["state"] = JValue.CreateNull(),
                ["diagnostics"] = new JArray
                {
                    new JObject
                    {
                        ["code"] = code,
                        ["target"] = "$",
                        ["message"] = message
                    }
                }
            };
            WriteJson(path, CanonicalGameStateSerializer.Write(root, Newtonsoft.Json.Formatting.Indented) + "\n");
        }

        private static void WriteJson(string path, string json)
        {
            string fullPath = Path.GetFullPath(path);
            string parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            string tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json.Replace("\r\n", "\n"));
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, null);
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
