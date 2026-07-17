using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public sealed class StateMutationResult
    {
        private StateMutationResult(
            bool success,
            GameState state,
            TargetPath target,
            TargetOperation operation,
            int beforeS,
            int requestedS,
            int afterS,
            bool clamped,
            string normalizeGroup,
            IEnumerable<StateDiagnostic> diagnostics)
        {
            Success = success;
            State = state;
            Target = target;
            Operation = operation;
            BeforeS = beforeS;
            RequestedS = requestedS;
            AfterS = afterS;
            Clamped = clamped;
            NormalizeGroup = normalizeGroup;
            Diagnostics = Array.AsReadOnly(new List<StateDiagnostic>(diagnostics ?? new StateDiagnostic[0]).ToArray());
        }

        public bool Success { get; }

        public GameState State { get; }

        public TargetPath Target { get; }

        public TargetOperation Operation { get; }

        public int BeforeS { get; }

        public int RequestedS { get; }

        public int AfterS { get; }

        public bool Clamped { get; }

        public string NormalizeGroup { get; }

        public IReadOnlyList<StateDiagnostic> Diagnostics { get; }

        public static StateMutationResult Succeeded(
            GameState state,
            TargetMutation mutation,
            int beforeS,
            int requestedS,
            int afterS,
            bool clamped,
            string normalizeGroup)
        {
            return new StateMutationResult(true, state, mutation.Target, mutation.Operation, beforeS, requestedS, afterS, clamped, normalizeGroup, new StateDiagnostic[0]);
        }

        public static StateMutationResult Failed(TargetMutation mutation, StateDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            return new StateMutationResult(false, null, mutation.Target, mutation.Operation, 0, mutation.ValueS, 0, false, null, new[] { diagnostic });
        }

        public static StateMutationResult Failed(TargetMutation mutation, IEnumerable<StateDiagnostic> diagnostics)
        {
            return new StateMutationResult(false, null, mutation.Target, mutation.Operation, 0, mutation.ValueS, 0, false, null, diagnostics);
        }
    }
}
