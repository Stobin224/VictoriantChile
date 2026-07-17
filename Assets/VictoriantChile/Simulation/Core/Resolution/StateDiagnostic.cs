using System;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public sealed class StateDiagnostic
    {
        public StateDiagnostic(string code, string target, string message)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("Diagnostic code cannot be null or empty.", nameof(code));
            }

            if (string.IsNullOrEmpty(target))
            {
                throw new ArgumentException("Diagnostic target cannot be null or empty.", nameof(target));
            }

            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Diagnostic message cannot be null or empty.", nameof(message));
            }

            Code = code;
            Target = target;
            Message = message;
        }

        public string Code { get; }

        public string Target { get; }

        public string Message { get; }

        public override string ToString()
        {
            return Code + " " + Target + ": " + Message;
        }
    }
}
