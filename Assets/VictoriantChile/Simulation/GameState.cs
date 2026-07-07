using System;
using System.Collections.Generic;

namespace VictoriantChile.Simulation
{
    /// <summary>
    /// Raíz del estado dinámico de una campaña, definida por CON-SIM-001.
    /// No contiene datos estáticos del Content Pack.
    /// </summary>
    [Serializable]
    public sealed class GameState
    {
        public const int CurrentSaveVersion = 1;

        private readonly Dictionary<string, int> _metrics;
        private readonly Dictionary<string, int> _internals;
        private readonly Dictionary<string, RegionState> _regions;
        private readonly Dictionary<string, InterestGroupState> _interestGroups;
        private readonly Dictionary<string, MovementState> _movements;

        private GameState(GameStateMeta meta)
        {
            Meta = meta ?? throw new ArgumentNullException(nameof(meta));
            _metrics = NewOrdinalDictionary<int>();
            _internals = NewOrdinalDictionary<int>();
            _regions = NewOrdinalDictionary<RegionState>();
            _interestGroups = NewOrdinalDictionary<InterestGroupState>();
            _movements = NewOrdinalDictionary<MovementState>();
        }

        public GameStateMeta Meta { get; }

        public IReadOnlyDictionary<string, int> Metrics => _metrics;

        public IReadOnlyDictionary<string, int> Internals => _internals;

        public IReadOnlyDictionary<string, RegionState> Regions => _regions;

        public IReadOnlyDictionary<string, InterestGroupState> InterestGroups => _interestGroups;

        public IReadOnlyDictionary<string, MovementState> Movements => _movements;

        public static GameState CreateNew(int rngSeed)
        {
            return new GameState(new GameStateMeta(CurrentSaveVersion, rngSeed, tick: 0));
        }

        private static Dictionary<string, TValue> NewOrdinalDictionary<TValue>()
        {
            return new Dictionary<string, TValue>(StringComparer.Ordinal);
        }
    }

    [Serializable]
    public sealed class GameStateMeta
    {
        public GameStateMeta(int saveVersion, int rngSeed, int tick)
        {
            if (saveVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(saveVersion));
            }

            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            SaveVersion = saveVersion;
            RngSeed = rngSeed;
            Tick = tick;
        }

        public int SaveVersion { get; }

        public int RngSeed { get; }

        public int Tick { get; }
    }

    [Serializable]
    public sealed class RegionState
    {
        private readonly Dictionary<string, int> _metrics =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, int> Metrics => _metrics;
    }

    [Serializable]
    public sealed class InterestGroupState
    {
        public InterestGroupState(int cloutS, int approvalS)
        {
            CloutS = FixedPoint.Clamp(cloutS, 0, FixedPoint.OneHundred);
            ApprovalS = FixedPoint.Clamp(
                approvalS,
                -FixedPoint.OneHundred,
                FixedPoint.OneHundred);
        }

        public int CloutS { get; }

        public int ApprovalS { get; }
    }

    [Serializable]
    public sealed class MovementState
    {
        public MovementState(int intensityS, int direction)
        {
            if (direction != -1 && direction != 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(direction),
                    "La dirección debe ser -1 (anti) o 1 (pro).");
            }

            IntensityS = FixedPoint.Clamp(intensityS, 0, FixedPoint.OneHundred);
            Direction = direction;
        }

        public int IntensityS { get; }

        public int Direction { get; }
    }
}
