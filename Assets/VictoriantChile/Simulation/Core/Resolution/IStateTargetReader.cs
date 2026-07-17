using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Resolution
{
    public interface IStateTargetReader
    {
        TargetReadResult Read(TargetPath target);
    }
}
