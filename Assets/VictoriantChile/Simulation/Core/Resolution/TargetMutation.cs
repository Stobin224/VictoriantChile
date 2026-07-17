using System;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public sealed class TargetMutation
    {
        public TargetMutation(TargetPath target, TargetOperation operation, int valueS)
        {
            if (!target.IsValid)
            {
                throw new ArgumentException("Mutation target must be a valid concrete TargetPath.", nameof(target));
            }

            Target = target;
            Operation = operation;
            ValueS = valueS;
        }

        public TargetPath Target { get; }

        public TargetOperation Operation { get; }

        public int ValueS { get; }
    }
}
