using System;
using VictoriantChile.Content.Models;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Content.State
{
    public sealed class ContentPackStaticTargetSource : IReadOnlyStaticTargetSource
    {
        private readonly ContentPack _pack;

        public ContentPackStaticTargetSource(ContentPack pack)
        {
            _pack = pack ?? throw new ArgumentNullException(nameof(pack));
        }

        public TargetReadResult TryReadStatic(TargetPath target)
        {
            string targetText = target.IsValid ? target.ToString() : "<invalid>";
            if (!target.IsValid || target.Namespace != "regions" || target.SegmentCount != 3)
            {
                return Fail(targetText, "target.invalid_path", "Static target path is invalid.");
            }

            if (!_pack.RegionsById.TryGetValue(target[1], out RegionDefinition region))
            {
                return Fail(targetText, "target.static_not_found", "Region static resource was not found.");
            }

            string field = target[2];
            if (field == "admin_capS")
            {
                return TargetReadResult.Succeeded(targetText, region.AdminCapS, TargetValueSource.StaticContent);
            }

            if (field == "industry_capS")
            {
                return TargetReadResult.Succeeded(targetText, region.IndustryCapS, TargetValueSource.StaticContent);
            }

            if (field == "extractive_capS")
            {
                return TargetReadResult.Succeeded(targetText, region.ExtractiveCapS, TargetValueSource.StaticContent);
            }

            if (field == "social_capS")
            {
                return TargetReadResult.Succeeded(targetText, region.SocialCapS, TargetValueSource.StaticContent);
            }

            if (field == "populationS")
            {
                return TargetReadResult.Succeeded(targetText, region.PopulationS, TargetValueSource.StaticContent);
            }

            return Fail(targetText, "target.static_not_found", "Static regional field is not supported.");
        }

        private static TargetReadResult Fail(string target, string code, string message)
        {
            return TargetReadResult.Failed(target, new StateDiagnostic(code, target, message));
        }
    }
}
