using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public interface IReadOnlyStaticTargetSource
    {
        TargetReadResult TryReadStatic(TargetPath target);
    }
}
