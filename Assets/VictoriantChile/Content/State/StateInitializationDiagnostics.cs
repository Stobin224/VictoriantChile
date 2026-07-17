using System;
using System.Collections.Generic;
using VictoriantChile.Simulation.Core.State;

namespace VictoriantChile.Content.State
{
    public enum StateInitializationDiagnosticCode
    {
        MissingTargetConfig,
        DefaultOutOfRange,
        InvalidNormalizeGroup,
        CloutNormalizationFailed,
        DuplicateId,
        InvalidMetadata,
        IncompatibleStateInvariant
    }

    public sealed class StateInitializationDiagnostic
    {
        public StateInitializationDiagnostic(StateInitializationDiagnosticCode code, string target, string message)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new ArgumentException("Target cannot be null or empty.", nameof(target));
            }

            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message cannot be null or empty.", nameof(message));
            }

            Code = code;
            Target = target;
            Message = message;
        }

        public StateInitializationDiagnosticCode Code { get; }

        public string Target { get; }

        public string Message { get; }

        public override string ToString()
        {
            return Code + " " + Target + ": " + Message;
        }
    }

    public sealed class StateInitializationResult
    {
        private StateInitializationResult(GameState state, IEnumerable<StateInitializationDiagnostic> diagnostics)
        {
            State = state;
            Diagnostics = Array.AsReadOnly(SnapshotDiagnostics(diagnostics));
        }

        public bool Success => State != null && Diagnostics.Count == 0;

        public GameState State { get; }

        public IReadOnlyList<StateInitializationDiagnostic> Diagnostics { get; }

        public static StateInitializationResult Succeeded(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return new StateInitializationResult(state, new StateInitializationDiagnostic[0]);
        }

        public static StateInitializationResult Failed(IEnumerable<StateInitializationDiagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            StateInitializationDiagnostic[] snapshot = SnapshotDiagnostics(diagnostics);
            if (snapshot.Length == 0)
            {
                throw new ArgumentException("A failed result must include at least one diagnostic.", nameof(diagnostics));
            }

            return new StateInitializationResult(null, snapshot);
        }

        private static StateInitializationDiagnostic[] SnapshotDiagnostics(IEnumerable<StateInitializationDiagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            List<StateInitializationDiagnostic> snapshot = new List<StateInitializationDiagnostic>();
            foreach (StateInitializationDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic == null)
                {
                    throw new ArgumentNullException(nameof(diagnostics), "Diagnostics cannot contain null values.");
                }

                snapshot.Add(diagnostic);
            }

            return snapshot.ToArray();
        }
    }
}
