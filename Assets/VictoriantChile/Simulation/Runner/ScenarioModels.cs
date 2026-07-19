using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.Scheduling;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Runner
{
    public enum ScenarioCommandType
    {
        Read,
        Mutate,
        Advance
    }

    public sealed class ScenarioDefinition
    {
        public ScenarioDefinition(int schemaVersion, int seed, IEnumerable<ScenarioCommand> commands)
        {
            ScenarioSchemaVersion = schemaVersion;
            Seed = seed;
            Commands = Array.AsReadOnly(new List<ScenarioCommand>(commands ?? throw new ArgumentNullException(nameof(commands))).ToArray());
        }

        public int ScenarioSchemaVersion { get; }

        public int Seed { get; }

        public IReadOnlyList<ScenarioCommand> Commands { get; }
    }

    public sealed class ScenarioCommand
    {
        public ScenarioCommand(string id, ScenarioCommandType type, TargetPath target, TargetOperation operation, int valueS, int weeks = 0)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Type = type;
            Target = target;
            Operation = operation;
            ValueS = valueS;
            Weeks = weeks;
        }

        public string Id { get; }

        public ScenarioCommandType Type { get; }

        public TargetPath Target { get; }

        public TargetOperation Operation { get; }

        public int ValueS { get; }

        public int Weeks { get; }
    }

    public sealed class ScenarioRunnerResult
    {
        public ScenarioRunnerResult(
            string status,
            int scenarioSchemaVersion,
            int seed,
            int commandCount,
            IEnumerable<CommandExecutionResult> commands,
            string stateHash,
            Newtonsoft.Json.Linq.JObject state,
            IEnumerable<StateDiagnostic> diagnostics)
        {
            Status = status ?? throw new ArgumentNullException(nameof(status));
            ScenarioSchemaVersion = scenarioSchemaVersion;
            Seed = seed;
            CommandCount = commandCount;
            Commands = Array.AsReadOnly(new List<CommandExecutionResult>(commands ?? new CommandExecutionResult[0]).ToArray());
            StateHash = stateHash;
            State = state;
            Diagnostics = Array.AsReadOnly(new List<StateDiagnostic>(diagnostics ?? new StateDiagnostic[0]).ToArray());
        }

        public string Status { get; }

        public int ScenarioSchemaVersion { get; }

        public int Seed { get; }

        public int CommandCount { get; }

        public IReadOnlyList<CommandExecutionResult> Commands { get; }

        public string StateHash { get; }

        public Newtonsoft.Json.Linq.JObject State { get; }

        public IReadOnlyList<StateDiagnostic> Diagnostics { get; }
    }

    public sealed class CommandExecutionResult
    {
        public int Index { get; set; }
        public string Id { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Target { get; set; }
        public string Source { get; set; }
        public int? ValueS { get; set; }
        public string Operation { get; set; }
        public int? BeforeS { get; set; }
        public int? RequestedS { get; set; }
        public int? AfterS { get; set; }
        public bool Clamped { get; set; }
        public string NormalizeGroup { get; set; }
        public IReadOnlyList<StateDiagnostic> Diagnostics { get; set; }
        public int? WeeksRequested { get; set; }
        public int? TicksCompleted { get; set; }
        public BlockingDecision BlockingDecision { get; set; }
        public IReadOnlyList<string> TickStateHashes { get; set; }
        public IReadOnlyList<TickCausalSnapshot> CausalTicks { get; set; }
    }
}
