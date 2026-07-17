using System;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class InterestGroupState
    {
        public InterestGroupState(string interestGroupId, int cloutS, int approvalS)
        {
            if (string.IsNullOrEmpty(interestGroupId))
            {
                throw new ArgumentException("Interest group ID cannot be null or empty.", nameof(interestGroupId));
            }

            InterestGroupId = interestGroupId;
            CloutS = cloutS;
            ApprovalS = approvalS;
        }

        public string InterestGroupId { get; }

        public int CloutS { get; }

        public int ApprovalS { get; }
    }
}
