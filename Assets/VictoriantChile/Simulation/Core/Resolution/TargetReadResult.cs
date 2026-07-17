using System;
using System.Collections.Generic;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public sealed class TargetReadResult
    {
        private TargetReadResult(bool success, string target, int valueS, TargetValueSource source, IEnumerable<StateDiagnostic> diagnostics)
        {
            Success = success;
            Target = target ?? string.Empty;
            ValueS = valueS;
            Source = source;
            Diagnostics = Array.AsReadOnly(new List<StateDiagnostic>(diagnostics ?? new StateDiagnostic[0]).ToArray());
        }

        public bool Success { get; }

        public string Target { get; }

        public int ValueS { get; }

        public TargetValueSource Source { get; }

        public IReadOnlyList<StateDiagnostic> Diagnostics { get; }

        public static TargetReadResult Succeeded(string target, int valueS, TargetValueSource source)
        {
            return new TargetReadResult(true, target, valueS, source, new StateDiagnostic[0]);
        }

        public static TargetReadResult Failed(string target, StateDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            return new TargetReadResult(false, target, 0, TargetValueSource.DynamicState, new[] { diagnostic });
        }
    }
}
