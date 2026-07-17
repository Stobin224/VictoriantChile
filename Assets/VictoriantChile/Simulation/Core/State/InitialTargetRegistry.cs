using System.Collections.Generic;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.State
{
    public static class InitialTargetRegistry
    {
        private static readonly TargetPath[] MetricTargets =
        {
            TargetPath.Parse("metrics.legitimacy"),
            TargetPath.Parse("metrics.economy"),
            TargetPath.Parse("metrics.security"),
            TargetPath.Parse("metrics.social_tension"),
            TargetPath.Parse("metrics.public_agenda"),
            TargetPath.Parse("metrics.information_quality"),
            TargetPath.Parse("metrics.party_organization"),
            TargetPath.Parse("metrics.internal_cohesion"),
            TargetPath.Parse("metrics.legislative_capacity"),
            TargetPath.Parse("metrics.governability")
        };

        private static readonly TargetPath[] InternalTargets =
        {
            TargetPath.Parse("internals.economy.growth"),
            TargetPath.Parse("internals.economy.unemployment"),
            TargetPath.Parse("internals.economy.inflation"),
            TargetPath.Parse("internals.economy.fiscal_stability"),
            TargetPath.Parse("internals.security.police_capacity"),
            TargetPath.Parse("internals.security.crime_rate"),
            TargetPath.Parse("internals.security.violent_crime"),
            TargetPath.Parse("internals.security.organized_crime"),
            TargetPath.Parse("internals.tension.cost_of_living"),
            TargetPath.Parse("internals.tension.polarization"),
            TargetPath.Parse("internals.tension.protest_activity"),
            TargetPath.Parse("internals.tension.institutional_trust"),
            TargetPath.Parse("internals.agenda.media_heat"),
            TargetPath.Parse("internals.agenda.policy_conflict"),
            TargetPath.Parse("internals.agenda.movement_salience"),
            TargetPath.Parse("internals.info.intel_capacity"),
            TargetPath.Parse("internals.info.media_noise"),
            TargetPath.Parse("internals.info.institutional_access"),
            TargetPath.Parse("internals.gov.bureaucracy_capacity"),
            TargetPath.Parse("internals.gov.budget_flexibility"),
            TargetPath.Parse("internals.gov.execution_focus"),
            TargetPath.Parse("internals.gov.legal_friction"),
            TargetPath.Parse("internals.leg.coalition_strength"),
            TargetPath.Parse("internals.leg.party_discipline"),
            TargetPath.Parse("internals.leg.opposition_obstruction"),
            TargetPath.Parse("internals.leg.senate_inertia"),
            TargetPath.Parse("internals.party.field_ops"),
            TargetPath.Parse("internals.party.funding"),
            TargetPath.Parse("internals.party.cadre_quality"),
            TargetPath.Parse("internals.party.internal_scandal"),
            TargetPath.Parse("internals.cohesion.factionalism"),
            TargetPath.Parse("internals.cohesion.leadership_unity"),
            TargetPath.Parse("internals.cohesion.discipline_culture"),
            TargetPath.Parse("internals.cohesion.ambition_rivalries"),
            TargetPath.Parse("internals.legitimacy.performance"),
            TargetPath.Parse("internals.legitimacy.integrity"),
            TargetPath.Parse("internals.legitimacy.scandal_pressure"),
            TargetPath.Parse("internals.legitimacy.social_tension_load")
        };

        public static IReadOnlyList<TargetPath> Metrics => System.Array.AsReadOnly(MetricTargets);

        public static IReadOnlyList<TargetPath> Internals => System.Array.AsReadOnly(InternalTargets);

        public static TargetPath RegionSupport(string regionId)
        {
            return TargetPath.Parse("regions." + regionId + ".support");
        }

        public static TargetPath RegionTension(string regionId)
        {
            return TargetPath.Parse("regions." + regionId + ".tension");
        }

        public static TargetPath RegionOrganization(string regionId)
        {
            return TargetPath.Parse("regions." + regionId + ".organization");
        }

        public static TargetPath RegionRivalPresence(string regionId)
        {
            return TargetPath.Parse("regions." + regionId + ".rival_presence");
        }

        public static TargetPath InterestGroupClout(string interestGroupId)
        {
            return TargetPath.Parse("igs." + interestGroupId + ".clout");
        }

        public static TargetPath InterestGroupApproval(string interestGroupId)
        {
            return TargetPath.Parse("igs." + interestGroupId + ".approval");
        }

        public static TargetPath MovementIntensity(string movementId)
        {
            return TargetPath.Parse("movements." + movementId + ".intensity");
        }

        public static TargetPath MovementDirection(string movementId)
        {
            return TargetPath.Parse("movements." + movementId + ".direction");
        }
    }
}
