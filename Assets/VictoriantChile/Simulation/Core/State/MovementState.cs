using System;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class MovementState
    {
        public MovementState(string movementId, int intensityS, int direction)
        {
            if (string.IsNullOrEmpty(movementId))
            {
                throw new ArgumentException("Movement ID cannot be null or empty.", nameof(movementId));
            }

            MovementId = movementId;
            IntensityS = intensityS;
            Direction = direction;
        }

        public string MovementId { get; }

        public int IntensityS { get; }

        public int Direction { get; }
    }
}
