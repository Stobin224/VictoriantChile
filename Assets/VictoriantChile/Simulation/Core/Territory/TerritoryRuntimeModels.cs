using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Core.Territory
{
    public enum TerritoryDynamicFieldRuntime
    {
        Support = 1,
        Tension = 2,
        Organization = 3,
        RivalPresence = 4
    }

    public enum TerritoryDriftTransformRuntime
    {
        ValueMinusMid,
        MidMinusValue
    }

    public static class TerritoryCauseMaterializer
    {
        private const string DriftPrefix = "REG_DRIFT";
        private const string PullPrefix = "REG_TO_INT";

        public static CauseRef MaterializeDrift(string regionId, TerritoryDynamicFieldRuntime field)
        {
            ValidateRegionId(regionId, nameof(regionId));
            ValidateField(field, nameof(field));
            return new CauseRef(CauseCategory.System, DriftPrefix + ".regions." + regionId + "." + FieldToTargetSegment(field));
        }

        public static CauseRef MaterializePull(TargetPath destination)
        {
            ValidateInternalTarget(destination, nameof(destination));
            return new CauseRef(CauseCategory.System, PullPrefix + "." + destination.ToString());
        }

        public static string FieldToTargetSegment(TerritoryDynamicFieldRuntime field)
        {
            if (field == TerritoryDynamicFieldRuntime.Support) { return "support"; }
            if (field == TerritoryDynamicFieldRuntime.Tension) { return "tension"; }
            if (field == TerritoryDynamicFieldRuntime.Organization) { return "organization"; }
            if (field == TerritoryDynamicFieldRuntime.RivalPresence) { return "rival_presence"; }
            throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown territory dynamic field.");
        }

        internal static bool IsRegionalDynamicTarget(TargetPath target)
        {
            return target.IsValid
                && target.SegmentCount == 3
                && string.Equals(target.Namespace, "regions", StringComparison.Ordinal)
                && IsRegionId(target[1])
                && TryParseFieldSegment(target[2], out _);
        }

        internal static bool IsMetricTarget(TargetPath target)
        {
            return target.IsValid
                && target.SegmentCount == 2
                && string.Equals(target.Namespace, "metrics", StringComparison.Ordinal);
        }

        internal static bool IsInternalTarget(TargetPath target)
        {
            return target.IsValid
                && target.SegmentCount == 3
                && string.Equals(target.Namespace, "internals", StringComparison.Ordinal);
        }

        internal static bool TryParseFieldSegment(string segment, out TerritoryDynamicFieldRuntime field)
        {
            if (string.Equals(segment, "support", StringComparison.Ordinal))
            {
                field = TerritoryDynamicFieldRuntime.Support;
                return true;
            }

            if (string.Equals(segment, "tension", StringComparison.Ordinal))
            {
                field = TerritoryDynamicFieldRuntime.Tension;
                return true;
            }

            if (string.Equals(segment, "organization", StringComparison.Ordinal))
            {
                field = TerritoryDynamicFieldRuntime.Organization;
                return true;
            }

            if (string.Equals(segment, "rival_presence", StringComparison.Ordinal))
            {
                field = TerritoryDynamicFieldRuntime.RivalPresence;
                return true;
            }

            field = default;
            return false;
        }

        private static void ValidateField(TerritoryDynamicFieldRuntime field, string parameterName)
        {
            if (!Enum.IsDefined(typeof(TerritoryDynamicFieldRuntime), field))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Unknown territory dynamic field.");
            }
        }

        private static void ValidateInternalTarget(TargetPath target, string parameterName)
        {
            if (!IsInternalTarget(target))
            {
                throw new ArgumentException("Pull destination must be a valid internals.*.* target.", parameterName);
            }
        }

        private static void ValidateRegionId(string regionId, string parameterName)
        {
            if (!IsRegionId(regionId))
            {
                throw new ArgumentException("Region id must be ASCII lowercase snake_case.", parameterName);
            }
        }

        private static bool IsRegionId(string regionId)
        {
            if (string.IsNullOrEmpty(regionId) || regionId[0] < 'a' || regionId[0] > 'z')
            {
                return false;
            }

            for (int i = 1; i < regionId.Length; i++)
            {
                char c = regionId[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class TerritoryRuntimePlan
    {
        public const int RequiredScale = 100;
        public const int RequiredMidS = 5000;
        public const int PpmDenominator = 1_000_000;
        public const int RequiredRegionCount = 16;
        public const int RequiredRegionWeightPpm = 62500;
        public const int RequiredRegionWeightSumPpm = 1_000_000;
        public const int RequiredDriftBindingCount = 64;
        public const int RequiredPullBindingCount = 5;
        public const int DriftAlphaPpm = 109101;
        public const int DriftCapPerWeekS = 200;
        public const int DriftHalfLifeWeeksMetadata = 6;
        public const int PullAlphaPpm = 206299;
        public const int PullCapPerWeekS = 400;
        public const int PullWeightedAverageDenominator = 1_000_000;

        private static readonly string[] CanonicalRegionIds =
        {
            "arica_parinacota",
            "tarapaca",
            "antofagasta",
            "atacama",
            "coquimbo",
            "valparaiso",
            "metropolitana",
            "ohiggins",
            "maule",
            "nuble",
            "biobio",
            "araucania",
            "los_rios",
            "los_lagos",
            "aysen",
            "magallanes"
        };

        private static readonly TerritoryDynamicFieldRuntime[] CanonicalFields =
        {
            TerritoryDynamicFieldRuntime.Support,
            TerritoryDynamicFieldRuntime.Tension,
            TerritoryDynamicFieldRuntime.Organization,
            TerritoryDynamicFieldRuntime.RivalPresence
        };

        private readonly ReadOnlyDictionary<string, TerritoryRegionRuntime> _regionsById;
        private readonly ReadOnlyDictionary<TargetPath, TerritoryDriftBindingRuntime> _driftBindingsByOutput;
        private readonly ReadOnlyDictionary<string, TerritoryPullBindingRuntime> _pullBindingsById;
        private readonly ReadOnlyDictionary<TargetPath, TerritoryPullBindingRuntime> _pullBindingsByDestination;

        public TerritoryRuntimePlan(
            int scale,
            int midS,
            int ppmDenominator,
            int driftAlphaPpm,
            int driftCapPerWeekS,
            int driftHalfLifeWeeksMetadata,
            int pullAlphaPpm,
            int pullCapPerWeekS,
            int pullWeightedAverageDenominator,
            IReadOnlyList<TerritoryRegionRuntime> regions,
            IReadOnlyList<TerritoryDriftBindingRuntime> driftBindings,
            IReadOnlyList<TerritoryPullBindingRuntime> pullBindings)
        {
            ValidateConstants(scale, midS, ppmDenominator, driftAlphaPpm, driftCapPerWeekS, driftHalfLifeWeeksMetadata, pullAlphaPpm, pullCapPerWeekS, pullWeightedAverageDenominator);

            Regions = SnapshotRegions(regions, out Dictionary<string, TerritoryRegionRuntime> regionLookup);
            DriftBindings = SnapshotDriftBindings(driftBindings, regionLookup, out Dictionary<TargetPath, TerritoryDriftBindingRuntime> driftLookup);
            PullBindings = SnapshotPullBindings(pullBindings, out Dictionary<string, TerritoryPullBindingRuntime> pullIdLookup, out Dictionary<TargetPath, TerritoryPullBindingRuntime> pullDestinationLookup);

            _regionsById = new ReadOnlyDictionary<string, TerritoryRegionRuntime>(regionLookup);
            _driftBindingsByOutput = new ReadOnlyDictionary<TargetPath, TerritoryDriftBindingRuntime>(driftLookup);
            _pullBindingsById = new ReadOnlyDictionary<string, TerritoryPullBindingRuntime>(pullIdLookup);
            _pullBindingsByDestination = new ReadOnlyDictionary<TargetPath, TerritoryPullBindingRuntime>(pullDestinationLookup);

            Scale = scale;
            MidS = midS;
            DriftPpmDenominator = ppmDenominator;
            DriftAlphaPpmValue = driftAlphaPpm;
            DriftCapPerWeekSValue = driftCapPerWeekS;
            DriftHalfLifeWeeksMetadataValue = driftHalfLifeWeeksMetadata;
            PullAlphaPpmValue = pullAlphaPpm;
            PullCapPerWeekSValue = pullCapPerWeekS;
            PullWeightedAverageDenominatorValue = pullWeightedAverageDenominator;
        }

        public int Scale { get; }

        public int MidS { get; }

        public int DriftPpmDenominator { get; }

        public int DriftAlphaPpmValue { get; }

        public int DriftCapPerWeekSValue { get; }

        public int DriftHalfLifeWeeksMetadataValue { get; }

        public int PullAlphaPpmValue { get; }

        public int PullCapPerWeekSValue { get; }

        public int PullWeightedAverageDenominatorValue { get; }

        public IReadOnlyList<TerritoryRegionRuntime> Regions { get; }

        public IReadOnlyList<TerritoryDriftBindingRuntime> DriftBindings { get; }

        public IReadOnlyList<TerritoryPullBindingRuntime> PullBindings { get; }

        public IReadOnlyDictionary<string, TerritoryRegionRuntime> RegionsById => _regionsById;

        public IReadOnlyDictionary<TargetPath, TerritoryDriftBindingRuntime> DriftBindingsByOutput => _driftBindingsByOutput;

        public IReadOnlyDictionary<string, TerritoryPullBindingRuntime> PullBindingsById => _pullBindingsById;

        public IReadOnlyDictionary<TargetPath, TerritoryPullBindingRuntime> PullBindingsByDestination => _pullBindingsByDestination;

        public static IReadOnlyList<string> OrderedRegionIds => Array.AsReadOnly((string[])CanonicalRegionIds.Clone());

        public static IReadOnlyList<TerritoryDynamicFieldRuntime> OrderedFields => Array.AsReadOnly((TerritoryDynamicFieldRuntime[])CanonicalFields.Clone());

        public bool TryGetRegion(string regionId, out TerritoryRegionRuntime region)
        {
            if (regionId == null)
            {
                region = null;
                return false;
            }

            return _regionsById.TryGetValue(regionId, out region);
        }

        public TerritoryRegionRuntime GetRegion(string regionId)
        {
            if (TryGetRegion(regionId, out TerritoryRegionRuntime region))
            {
                return region;
            }

            throw new KeyNotFoundException("Region id is not present in the territory plan.");
        }

        public bool TryGetDriftBinding(TargetPath output, out TerritoryDriftBindingRuntime binding)
        {
            if (!output.IsValid)
            {
                binding = null;
                return false;
            }

            return _driftBindingsByOutput.TryGetValue(output, out binding);
        }

        public TerritoryDriftBindingRuntime GetDriftBinding(TargetPath output)
        {
            if (TryGetDriftBinding(output, out TerritoryDriftBindingRuntime binding))
            {
                return binding;
            }

            throw new KeyNotFoundException("Drift output target is not present in the territory plan.");
        }

        public bool TryGetPullBinding(string bindingId, out TerritoryPullBindingRuntime binding)
        {
            if (bindingId == null)
            {
                binding = null;
                return false;
            }

            return _pullBindingsById.TryGetValue(bindingId, out binding);
        }

        public TerritoryPullBindingRuntime GetPullBinding(string bindingId)
        {
            if (TryGetPullBinding(bindingId, out TerritoryPullBindingRuntime binding))
            {
                return binding;
            }

            throw new KeyNotFoundException("Pull binding id is not present in the territory plan.");
        }

        public bool TryGetPullBindingByDestination(TargetPath destination, out TerritoryPullBindingRuntime binding)
        {
            if (!destination.IsValid)
            {
                binding = null;
                return false;
            }

            return _pullBindingsByDestination.TryGetValue(destination, out binding);
        }

        public TerritoryPullBindingRuntime GetPullBindingByDestination(TargetPath destination)
        {
            if (TryGetPullBindingByDestination(destination, out TerritoryPullBindingRuntime binding))
            {
                return binding;
            }

            throw new KeyNotFoundException("Pull destination is not present in the territory plan.");
        }

        private static void ValidateConstants(int scale, int midS, int ppmDenominator, int driftAlphaPpm, int driftCapPerWeekS, int driftHalfLifeWeeksMetadata, int pullAlphaPpm, int pullCapPerWeekS, int pullWeightedAverageDenominator)
        {
            if (scale != RequiredScale) { throw new ArgumentOutOfRangeException(nameof(scale), "Territory scale must be exactly 100."); }
            if (midS != RequiredMidS) { throw new ArgumentOutOfRangeException(nameof(midS), "Territory midS must be exactly 5000."); }
            if (ppmDenominator != PpmDenominator) { throw new ArgumentOutOfRangeException(nameof(ppmDenominator), "Territory ppm denominator must be exactly 1_000_000."); }
            if (driftAlphaPpm != DriftAlphaPpm) { throw new ArgumentOutOfRangeException(nameof(driftAlphaPpm), "Territory drift alpha_ppm is not contractual."); }
            if (driftCapPerWeekS != DriftCapPerWeekS) { throw new ArgumentOutOfRangeException(nameof(driftCapPerWeekS), "Territory drift cap_per_weekS is not contractual."); }
            if (driftHalfLifeWeeksMetadata != DriftHalfLifeWeeksMetadata) { throw new ArgumentOutOfRangeException(nameof(driftHalfLifeWeeksMetadata), "Territory drift half-life metadata is not contractual."); }
            if (pullAlphaPpm != PullAlphaPpm) { throw new ArgumentOutOfRangeException(nameof(pullAlphaPpm), "Territory pull alpha_ppm is not contractual."); }
            if (pullCapPerWeekS != PullCapPerWeekS) { throw new ArgumentOutOfRangeException(nameof(pullCapPerWeekS), "Territory pull cap_per_weekS is not contractual."); }
            if (pullWeightedAverageDenominator != PullWeightedAverageDenominator) { throw new ArgumentOutOfRangeException(nameof(pullWeightedAverageDenominator), "Territory pull denominator is not contractual."); }
        }

        private static IReadOnlyList<TerritoryRegionRuntime> SnapshotRegions(
            IReadOnlyList<TerritoryRegionRuntime> regions,
            out Dictionary<string, TerritoryRegionRuntime> regionLookup)
        {
            if (regions == null)
            {
                throw new ArgumentNullException(nameof(regions));
            }

            if (regions.Count != RequiredRegionCount)
            {
                throw new ArgumentException("Territory plan must contain exactly 16 regions.", nameof(regions));
            }

            List<TerritoryRegionRuntime> snapshot = new List<TerritoryRegionRuntime>(regions.Count);
            regionLookup = new Dictionary<string, TerritoryRegionRuntime>(StringComparer.Ordinal);
            long weightSum = 0;
            for (int i = 0; i < regions.Count; i++)
            {
                TerritoryRegionRuntime region = regions[i];
                if (region == null)
                {
                    throw new ArgumentException("Territory regions cannot contain null entries.", nameof(regions));
                }

                if (!string.Equals(region.RegionId, CanonicalRegionIds[i], StringComparison.Ordinal))
                {
                    throw new ArgumentException("Territory region order is not canonical.", nameof(regions));
                }

                if (region.WeightPpm <= 0 || region.WeightPpm != RequiredRegionWeightPpm)
                {
                    throw new ArgumentException("Territory region weight_ppm is not contractual.", nameof(regions));
                }

                if (regionLookup.ContainsKey(region.RegionId))
                {
                    throw new ArgumentException("Territory regions cannot contain duplicate ids.", nameof(regions));
                }

                weightSum = checked(weightSum + region.WeightPpm);
                regionLookup.Add(region.RegionId, region);
                snapshot.Add(region);
            }

            if (weightSum != RequiredRegionWeightSumPpm)
            {
                throw new ArgumentException("Territory region weight_ppm sum is not contractual.", nameof(regions));
            }

            return Array.AsReadOnly(snapshot.ToArray());
        }

        private static IReadOnlyList<TerritoryDriftBindingRuntime> SnapshotDriftBindings(
            IReadOnlyList<TerritoryDriftBindingRuntime> driftBindings,
            Dictionary<string, TerritoryRegionRuntime> regionLookup,
            out Dictionary<TargetPath, TerritoryDriftBindingRuntime> driftLookup)
        {
            if (driftBindings == null)
            {
                throw new ArgumentNullException(nameof(driftBindings));
            }

            if (driftBindings.Count != RequiredDriftBindingCount)
            {
                throw new ArgumentException("Territory plan must contain exactly 64 drift bindings.", nameof(driftBindings));
            }

            List<TerritoryDriftBindingRuntime> snapshot = new List<TerritoryDriftBindingRuntime>(driftBindings.Count);
            driftLookup = new Dictionary<TargetPath, TerritoryDriftBindingRuntime>();
            HashSet<CauseRef> causes = new HashSet<CauseRef>();
            int expected = 0;
            for (int regionIndex = 0; regionIndex < CanonicalRegionIds.Length; regionIndex++)
            {
                for (int fieldIndex = 0; fieldIndex < CanonicalFields.Length; fieldIndex++)
                {
                    TerritoryDriftBindingRuntime binding = driftBindings[expected];
                    if (binding == null)
                    {
                        throw new ArgumentException("Territory drift bindings cannot contain null entries.", nameof(driftBindings));
                    }

                    string regionId = CanonicalRegionIds[regionIndex];
                    TerritoryDynamicFieldRuntime field = CanonicalFields[fieldIndex];
                    TargetPath expectedTarget = TargetPath.Parse("regions." + regionId + "." + TerritoryCauseMaterializer.FieldToTargetSegment(field));
                    if (!string.Equals(binding.RegionId, regionId, StringComparison.Ordinal)
                        || binding.Field != field
                        || binding.OutputTarget != expectedTarget
                        || !regionLookup.ContainsKey(binding.RegionId))
                    {
                        throw new ArgumentException("Territory drift binding order or output is not canonical.", nameof(driftBindings));
                    }

                    if (!driftLookup.AddIfAbsent(binding.OutputTarget, binding))
                    {
                        throw new ArgumentException("Territory drift bindings cannot contain duplicate outputs.", nameof(driftBindings));
                    }

                    if (!causes.Add(binding.Cause))
                    {
                        throw new ArgumentException("Territory drift bindings cannot contain duplicate causes.", nameof(driftBindings));
                    }

                    snapshot.Add(binding);
                    expected++;
                }
            }

            return Array.AsReadOnly(snapshot.ToArray());
        }

        private static IReadOnlyList<TerritoryPullBindingRuntime> SnapshotPullBindings(
            IReadOnlyList<TerritoryPullBindingRuntime> pullBindings,
            out Dictionary<string, TerritoryPullBindingRuntime> pullIdLookup,
            out Dictionary<TargetPath, TerritoryPullBindingRuntime> pullDestinationLookup)
        {
            if (pullBindings == null)
            {
                throw new ArgumentNullException(nameof(pullBindings));
            }

            if (pullBindings.Count != RequiredPullBindingCount)
            {
                throw new ArgumentException("Territory plan must contain exactly five pull bindings.", nameof(pullBindings));
            }

            string[] ids =
            {
                "support_to_coalition_strength",
                "organization_to_field_ops",
                "tension_to_protest_activity",
                "rival_presence_to_opposition_obstruction",
                "tension_to_movement_salience"
            };

            TargetPath[] destinations =
            {
                TargetPath.Parse("internals.leg.coalition_strength"),
                TargetPath.Parse("internals.party.field_ops"),
                TargetPath.Parse("internals.tension.protest_activity"),
                TargetPath.Parse("internals.leg.opposition_obstruction"),
                TargetPath.Parse("internals.agenda.movement_salience")
            };

            TerritoryDynamicFieldRuntime[] fields =
            {
                TerritoryDynamicFieldRuntime.Support,
                TerritoryDynamicFieldRuntime.Organization,
                TerritoryDynamicFieldRuntime.Tension,
                TerritoryDynamicFieldRuntime.RivalPresence,
                TerritoryDynamicFieldRuntime.Tension
            };

            List<TerritoryPullBindingRuntime> snapshot = new List<TerritoryPullBindingRuntime>(pullBindings.Count);
            pullIdLookup = new Dictionary<string, TerritoryPullBindingRuntime>(StringComparer.Ordinal);
            pullDestinationLookup = new Dictionary<TargetPath, TerritoryPullBindingRuntime>();
            HashSet<CauseRef> causes = new HashSet<CauseRef>();

            for (int i = 0; i < pullBindings.Count; i++)
            {
                TerritoryPullBindingRuntime binding = pullBindings[i];
                if (binding == null)
                {
                    throw new ArgumentException("Territory pull bindings cannot contain null entries.", nameof(pullBindings));
                }

                if (!string.Equals(binding.BindingId, ids[i], StringComparison.Ordinal)
                    || binding.RegionalSource != fields[i]
                    || binding.Destination != destinations[i])
                {
                    throw new ArgumentException("Territory pull binding order or destination is not canonical.", nameof(pullBindings));
                }

                if (!pullIdLookup.AddIfAbsent(binding.BindingId, binding))
                {
                    throw new ArgumentException("Territory pull bindings cannot contain duplicate ids.", nameof(pullBindings));
                }

                if (!pullDestinationLookup.AddIfAbsent(binding.Destination, binding))
                {
                    throw new ArgumentException("Territory pull bindings cannot contain duplicate destinations.", nameof(pullBindings));
                }

                if (!causes.Add(binding.Cause))
                {
                    throw new ArgumentException("Territory pull bindings cannot contain duplicate causes.", nameof(pullBindings));
                }

                snapshot.Add(binding);
            }

            return Array.AsReadOnly(snapshot.ToArray());
        }
    }

    public sealed class TerritoryRegionRuntime
    {
        public TerritoryRegionRuntime(string regionId, int weightPpm, int adminCapS, int industryCapS, int extractiveCapS, int socialCapS, int populationS)
        {
            if (string.IsNullOrEmpty(regionId))
            {
                throw new ArgumentException("Region id must not be empty.", nameof(regionId));
            }

            ValidatePpm(weightPpm, nameof(weightPpm), allowNegative: false);
            ValidateRange(adminCapS, nameof(adminCapS));
            ValidateRange(industryCapS, nameof(industryCapS));
            ValidateRange(extractiveCapS, nameof(extractiveCapS));
            ValidateRange(socialCapS, nameof(socialCapS));
            ValidateRange(populationS, nameof(populationS));

            RegionId = regionId;
            WeightPpm = weightPpm;
            AdminCapS = adminCapS;
            IndustryCapS = industryCapS;
            ExtractiveCapS = extractiveCapS;
            SocialCapS = socialCapS;
            PopulationS = populationS;
        }

        public string RegionId { get; }

        public int WeightPpm { get; }

        public int AdminCapS { get; }

        public int IndustryCapS { get; }

        public int ExtractiveCapS { get; }

        public int SocialCapS { get; }

        public int PopulationS { get; }

        internal static void ValidateRange(int value, string parameterName)
        {
            if (value < 0 || value > 10000)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Territory S values must be in 0..10000.");
            }
        }

        internal static void ValidatePpm(int value, string parameterName, bool allowNegative)
        {
            if (allowNegative)
            {
                if (value < -TerritoryRuntimePlan.PpmDenominator || value > TerritoryRuntimePlan.PpmDenominator)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "Coefficient ppm must be in -1_000_000..1_000_000.");
                }
            }
            else if (value <= 0 || value > TerritoryRuntimePlan.PpmDenominator)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Weight ppm must be in 1..1_000_000.");
            }
        }
    }

    public sealed class TerritoryDriftTermRuntime
    {
        public TerritoryDriftTermRuntime(TargetPath source, TerritoryDriftTransformRuntime transform, int coefficientPpm)
        {
            if (!source.IsValid)
            {
                throw new ArgumentException("Drift term source must be valid.", nameof(source));
            }

            if (!TerritoryCauseMaterializer.IsMetricTarget(source) && !TerritoryCauseMaterializer.IsRegionalDynamicTarget(source))
            {
                throw new ArgumentException("Drift term source must be metrics.* or regions.*.<dynamic_field>.", nameof(source));
            }

            if (!Enum.IsDefined(typeof(TerritoryDriftTransformRuntime), transform))
            {
                throw new ArgumentOutOfRangeException(nameof(transform), "Unknown drift transform.");
            }

            if (coefficientPpm == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(coefficientPpm), "Drift term coefficient cannot be zero.");
            }

            TerritoryRegionRuntime.ValidatePpm(coefficientPpm, nameof(coefficientPpm), allowNegative: true);

            Source = source;
            Transform = transform;
            CoefficientPpm = coefficientPpm;
        }

        public TargetPath Source { get; }

        public TerritoryDriftTransformRuntime Transform { get; }

        public int CoefficientPpm { get; }
    }

    public sealed class TerritoryDriftBindingRuntime
    {
        public TerritoryDriftBindingRuntime(
            string regionId,
            TerritoryDynamicFieldRuntime field,
            TargetPath outputTarget,
            TargetConfig outputConfig,
            CauseRef cause,
            IReadOnlyList<TerritoryDriftTermRuntime> terms)
        {
            if (string.IsNullOrEmpty(regionId))
            {
                throw new ArgumentException("Region id must not be empty.", nameof(regionId));
            }

            if (!Enum.IsDefined(typeof(TerritoryDynamicFieldRuntime), field))
            {
                throw new ArgumentOutOfRangeException(nameof(field), "Unknown territory field.");
            }

            if (!TerritoryCauseMaterializer.IsRegionalDynamicTarget(outputTarget))
            {
                throw new ArgumentException("Drift output target must be regions.*.<dynamic_field>.", nameof(outputTarget));
            }

            if (outputConfig == null)
            {
                throw new ArgumentNullException(nameof(outputConfig));
            }

            if (!outputConfig.Allows(TargetOperation.Set))
            {
                throw new ArgumentException("Drift output config must allow SET.", nameof(outputConfig));
            }

            ValidateTargetConfig(outputTarget, outputConfig, nameof(outputConfig));

            if (cause == null)
            {
                throw new ArgumentNullException(nameof(cause));
            }

            if (cause.Category != CauseCategory.System || cause.Parent != null)
            {
                throw new ArgumentException("Drift cause must be a SYSTEM CauseRef without parent.", nameof(cause));
            }

            IReadOnlyList<TerritoryDriftTermRuntime> snapshot = SnapshotTerms(terms);

            RegionId = regionId;
            Field = field;
            OutputTarget = outputTarget;
            OutputConfig = outputConfig;
            Cause = cause;
            Terms = snapshot;
        }

        public string RegionId { get; }

        public TerritoryDynamicFieldRuntime Field { get; }

        public TargetPath OutputTarget { get; }

        public TargetConfig OutputConfig { get; }

        public CauseRef Cause { get; }

        public IReadOnlyList<TerritoryDriftTermRuntime> Terms { get; }

        private static IReadOnlyList<TerritoryDriftTermRuntime> SnapshotTerms(IReadOnlyList<TerritoryDriftTermRuntime> terms)
        {
            if (terms == null)
            {
                throw new ArgumentNullException(nameof(terms));
            }

            if (terms.Count == 0)
            {
                throw new ArgumentException("Drift binding must contain at least one term.", nameof(terms));
            }

            List<TerritoryDriftTermRuntime> snapshot = new List<TerritoryDriftTermRuntime>(terms.Count);
            for (int i = 0; i < terms.Count; i++)
            {
                if (terms[i] == null)
                {
                    throw new ArgumentException("Drift terms cannot contain null entries.", nameof(terms));
                }

                snapshot.Add(terms[i]);
            }

            return Array.AsReadOnly(snapshot.ToArray());
        }

        internal static void ValidateTargetConfig(TargetPath target, TargetConfig config, string parameterName)
        {
            if (!config.Pattern.Matches(target))
            {
                throw new ArgumentException("TargetConfig does not match target path.", parameterName);
            }

            if (config.Scale != TerritoryRuntimePlan.RequiredScale
                || config.MinS != 0
                || config.MaxS != 10000
                || config.DefaultS != TerritoryRuntimePlan.RequiredMidS)
            {
                throw new ArgumentException("TargetConfig domain is not contractual for territory.", parameterName);
            }
        }
    }

    public sealed class TerritoryPullBindingRuntime
    {
        public TerritoryPullBindingRuntime(
            string bindingId,
            TerritoryDynamicFieldRuntime regionalSource,
            TargetPath destination,
            TargetConfig destinationConfig,
            CauseRef cause)
        {
            if (string.IsNullOrEmpty(bindingId))
            {
                throw new ArgumentException("Pull binding id must not be empty.", nameof(bindingId));
            }

            if (!Enum.IsDefined(typeof(TerritoryDynamicFieldRuntime), regionalSource))
            {
                throw new ArgumentOutOfRangeException(nameof(regionalSource), "Unknown territory source field.");
            }

            if (!TerritoryCauseMaterializer.IsInternalTarget(destination))
            {
                throw new ArgumentException("Pull destination must be internals.*.*.", nameof(destination));
            }

            if (destinationConfig == null)
            {
                throw new ArgumentNullException(nameof(destinationConfig));
            }

            if (!destinationConfig.Allows(TargetOperation.Set))
            {
                throw new ArgumentException("Pull destination config must allow SET.", nameof(destinationConfig));
            }

            TerritoryDriftBindingRuntime.ValidateTargetConfig(destination, destinationConfig, nameof(destinationConfig));

            if (cause == null)
            {
                throw new ArgumentNullException(nameof(cause));
            }

            if (cause.Category != CauseCategory.System || cause.Parent != null)
            {
                throw new ArgumentException("Pull cause must be a SYSTEM CauseRef without parent.", nameof(cause));
            }

            BindingId = bindingId;
            RegionalSource = regionalSource;
            Destination = destination;
            DestinationConfig = destinationConfig;
            Cause = cause;
        }

        public string BindingId { get; }

        public TerritoryDynamicFieldRuntime RegionalSource { get; }

        public TargetPath Destination { get; }

        public TargetConfig DestinationConfig { get; }

        public CauseRef Cause { get; }
    }

    internal static class DictionaryExtensions
    {
        public static bool AddIfAbsent<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
            {
                return false;
            }

            dictionary.Add(key, value);
            return true;
        }
    }
}
