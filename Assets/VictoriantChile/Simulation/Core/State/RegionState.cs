using System;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class RegionState
    {
        public RegionState(string regionId, int supportS, int tensionS, int organizationS, int rivalPresenceS)
        {
            if (string.IsNullOrEmpty(regionId))
            {
                throw new ArgumentException("Region ID cannot be null or empty.", nameof(regionId));
            }

            RegionId = regionId;
            SupportS = supportS;
            TensionS = tensionS;
            OrganizationS = organizationS;
            RivalPresenceS = rivalPresenceS;
        }

        public string RegionId { get; }

        public int SupportS { get; }

        public int TensionS { get; }

        public int OrganizationS { get; }

        public int RivalPresenceS { get; }
    }
}
