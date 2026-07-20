using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VictoriantChile.Content.Diagnostics;
using VictoriantChile.Simulation.Core.Aggregation;
using VictoriantChile.Simulation.Core.Effects;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Content.Models
{
    public static class ContentCompatibility
    {
        public const int CurrentGameSchemaVersion = 1;
        public const int SupportedContentSchemaVersion = 1;
    }

    public sealed class ContentManifest
    {
        public ContentManifest(
            string contentPackId,
            int contentPackVersion,
            int contentSchemaVersion,
            int minGameSchemaVersion,
            string defaultLanguage,
            IEnumerable<string> languages,
            IEnumerable<KeyValuePair<string, string>> files)
        {
            ContentPackId = contentPackId ?? throw new ArgumentNullException(nameof(contentPackId));
            ContentPackVersion = contentPackVersion;
            ContentSchemaVersion = contentSchemaVersion;
            MinGameSchemaVersion = minGameSchemaVersion;
            DefaultLanguage = defaultLanguage ?? throw new ArgumentNullException(nameof(defaultLanguage));
            Languages = Array.AsReadOnly(ModelSnapshot.Array(languages, nameof(languages)));
            Files = ModelSnapshot.Dictionary(files, nameof(files));
        }

        public string ContentPackId { get; }

        public int ContentPackVersion { get; }

        public int ContentSchemaVersion { get; }

        public int MinGameSchemaVersion { get; }

        public string DefaultLanguage { get; }

        public IReadOnlyList<string> Languages { get; }

        public IReadOnlyDictionary<string, string> Files { get; }
    }

    public enum RegionMacrozone
    {
        North,
        Center,
        South,
        Austral
    }

    public sealed class RegionDefinition
    {
        public RegionDefinition(
            string id,
            string name,
            int weightPpm,
            RegionMacrozone macrozone,
            int adminCapS,
            int industryCapS,
            int extractiveCapS,
            int socialCapS,
            int populationS)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            WeightPpm = weightPpm;
            Macrozone = macrozone;
            AdminCapS = adminCapS;
            IndustryCapS = industryCapS;
            ExtractiveCapS = extractiveCapS;
            SocialCapS = socialCapS;
            PopulationS = populationS;
        }

        public string Id { get; }

        public string Name { get; }

        public int WeightPpm { get; }

        public RegionMacrozone Macrozone { get; }

        public int AdminCapS { get; }

        public int IndustryCapS { get; }

        public int ExtractiveCapS { get; }

        public int SocialCapS { get; }

        public int PopulationS { get; }
    }

    public sealed class InterestGroupDefinition
    {
        public InterestGroupDefinition(string id, string name, IEnumerable<string> tags)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Tags = Array.AsReadOnly(ModelSnapshot.Array(tags, nameof(tags)));
        }

        public string Id { get; }

        public string Name { get; }

        public IReadOnlyList<string> Tags { get; }
    }

    public sealed class MovementDefinition
    {
        public MovementDefinition(string id, string name, IEnumerable<string> tags)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Tags = Array.AsReadOnly(ModelSnapshot.Array(tags, nameof(tags)));
        }

        public string Id { get; }

        public string Name { get; }

        public IReadOnlyList<string> Tags { get; }
    }

    public sealed class ContentLocalizationTable
    {
        public ContentLocalizationTable(string language, IEnumerable<KeyValuePair<string, string>> entries)
        {
            Language = language ?? throw new ArgumentNullException(nameof(language));
            Entries = ModelSnapshot.Dictionary(entries, nameof(entries));
        }

        public string Language { get; }

        public IReadOnlyDictionary<string, string> Entries { get; }

        public bool TryResolve(string key, out string value)
        {
            return Entries.TryGetValue(key, out value);
        }

        public string ResolveRequired(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Localization key must be a non-empty string.", nameof(key));
            }

            if (Entries.TryGetValue(key, out string value))
            {
                return value;
            }

            throw new KeyNotFoundException("Missing localization key: " + key);
        }
    }

    public enum ContentRoundingMode
    {
        HalfAwayFromZero
    }

    public enum AggregationPassType
    {
        InternalReversion,
        MetricAggregation,
        DerivedInternals
    }

    public enum AggregationExpressionKind
    {
        Avg,
        Copy
    }

    public sealed class AggregationConfig
    {
        public AggregationConfig(int schemaVersion, int scale, int midS, ContentRoundingMode rounding, IEnumerable<AggregationPass> passes)
        {
            SchemaVersion = schemaVersion;
            Scale = scale;
            MidS = midS;
            Rounding = rounding;
            Passes = Array.AsReadOnly(ModelSnapshot.Array(passes, nameof(passes)));
        }

        public int SchemaVersion { get; }

        public int Scale { get; }

        public int MidS { get; }

        public ContentRoundingMode Rounding { get; }

        public IReadOnlyList<AggregationPass> Passes { get; }
    }

    public sealed class AggregationPass
    {
        public AggregationPass(
            AggregationPassType type,
            string causePrefix,
            int? midS,
            IEnumerable<AggregationReversionGroup> groups,
            IEnumerable<TargetPath> skipTargets,
            bool? logComponents,
            int? weightsAbsSumPpmRequired,
            IEnumerable<AggregationMetric> metrics,
            IEnumerable<DerivedAggregationRule> rules)
        {
            Type = type;
            CausePrefix = causePrefix ?? throw new ArgumentNullException(nameof(causePrefix));
            MidS = midS;
            Groups = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(groups));
            SkipTargets = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(skipTargets));
            LogComponents = logComponents;
            WeightsAbsSumPpmRequired = weightsAbsSumPpmRequired;
            Metrics = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(metrics));
            Rules = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(rules));
        }

        public AggregationPassType Type { get; }

        public string CausePrefix { get; }

        public int? MidS { get; }

        public IReadOnlyList<AggregationReversionGroup> Groups { get; }

        public IReadOnlyList<TargetPath> SkipTargets { get; }

        public bool? LogComponents { get; }

        public int? WeightsAbsSumPpmRequired { get; }

        public IReadOnlyList<AggregationMetric> Metrics { get; }

        public IReadOnlyList<DerivedAggregationRule> Rules { get; }
    }

    public sealed class AggregationReversionGroup
    {
        public AggregationReversionGroup(TargetPattern pattern, int halfLifeWeeks, int alphaPpm)
        {
            Pattern = pattern;
            HalfLifeWeeks = halfLifeWeeks;
            AlphaPpm = alphaPpm;
        }

        public TargetPattern Pattern { get; }

        public int HalfLifeWeeks { get; }

        public int AlphaPpm { get; }
    }

    public sealed class AggregationMetric
    {
        public AggregationMetric(TargetPath metric, int halfLifeWeeks, int alphaPpm, int capPerWeekS, IEnumerable<WeightedTargetComponent> components)
        {
            Metric = metric;
            HalfLifeWeeks = halfLifeWeeks;
            AlphaPpm = alphaPpm;
            CapPerWeekS = capPerWeekS;
            Components = Array.AsReadOnly(ModelSnapshot.Array(components, nameof(components)));
        }

        public TargetPath Metric { get; }

        public int HalfLifeWeeks { get; }

        public int AlphaPpm { get; }

        public int CapPerWeekS { get; }

        public IReadOnlyList<WeightedTargetComponent> Components { get; }
    }

    public sealed class WeightedTargetComponent
    {
        public WeightedTargetComponent(TargetPath target, int weightPpm)
        {
            Target = target;
            WeightPpm = weightPpm;
        }

        public TargetPath Target { get; }

        public int WeightPpm { get; }
    }

    public sealed class DerivedAggregationRule
    {
        public DerivedAggregationRule(TargetPath target, TargetOperation operation, AggregationExpression expression)
        {
            Target = target;
            Operation = operation;
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }

        public TargetPath Target { get; }

        public TargetOperation Operation { get; }

        public AggregationExpression Expression { get; }
    }

    public sealed class AggregationExpression
    {
        public AggregationExpression(AggregationExpressionKind kind, TargetPath? target, IEnumerable<TargetPath> targets)
        {
            Kind = kind;
            Target = target;
            Targets = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(targets));
        }

        public AggregationExpressionKind Kind { get; }

        public TargetPath? Target { get; }

        public IReadOnlyList<TargetPath> Targets { get; }
    }

    public enum LegislativeMovementMatchMode
    {
        Any
    }

    public enum LegislativeUpperChamberAdjustmentType
    {
        SubtractConst
    }

    public enum LegislativeChamberSupportMode
    {
        Min
    }

    public sealed class LegislativeConfig
    {
        public LegislativeConfig(
            int schemaVersion,
            int scale,
            int midS,
            ContentRoundingMode rounding,
            LegislativeLimits limits,
            LegislativeConstants constants,
            LegislativeGates gates,
            LegislativeSenate senate,
            LegislativeMovementMatching movementMatching,
            LegislativeExceptionalRoute exceptionalRoute,
            LegislativeSupportModel supportModel,
            LegislativeStageModel stageModel,
            IEnumerable<LegislativePlayerStrategyEntry> playerStrategies,
            LegislativeCausePrefixes causePrefixes)
        {
            SchemaVersion = schemaVersion;
            Scale = scale;
            MidS = midS;
            Rounding = rounding;
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            Constants = constants ?? throw new ArgumentNullException(nameof(constants));
            Gates = gates ?? throw new ArgumentNullException(nameof(gates));
            Senate = senate ?? throw new ArgumentNullException(nameof(senate));
            MovementMatching = movementMatching ?? throw new ArgumentNullException(nameof(movementMatching));
            ExceptionalRoute = exceptionalRoute ?? throw new ArgumentNullException(nameof(exceptionalRoute));
            SupportModel = supportModel ?? throw new ArgumentNullException(nameof(supportModel));
            StageModel = stageModel ?? throw new ArgumentNullException(nameof(stageModel));

            LegislativePlayerStrategyEntry[] strategyEntries = ModelSnapshot.Array(playerStrategies, nameof(playerStrategies));
            PlayerStrategies = Array.AsReadOnly(strategyEntries);
            PlayerStrategiesById = ModelSnapshot.Dictionary(MapStrategyDictionary(strategyEntries), nameof(playerStrategies));

            CausePrefixes = causePrefixes ?? throw new ArgumentNullException(nameof(causePrefixes));
        }

        public int SchemaVersion { get; }

        public int Scale { get; }

        public int MidS { get; }

        public ContentRoundingMode Rounding { get; }

        public LegislativeLimits Limits { get; }

        public LegislativeConstants Constants { get; }

        public LegislativeGates Gates { get; }

        public LegislativeSenate Senate { get; }

        public LegislativeMovementMatching MovementMatching { get; }

        public LegislativeExceptionalRoute ExceptionalRoute { get; }

        public LegislativeSupportModel SupportModel { get; }

        public LegislativeStageModel StageModel { get; }

        public IReadOnlyList<LegislativePlayerStrategyEntry> PlayerStrategies { get; }

        public IReadOnlyDictionary<string, LegislativePlayerStrategy> PlayerStrategiesById { get; }

        public LegislativeCausePrefixes CausePrefixes { get; }

        private static IEnumerable<KeyValuePair<string, LegislativePlayerStrategy>> MapStrategyDictionary(IEnumerable<LegislativePlayerStrategyEntry> entries)
        {
            foreach (LegislativePlayerStrategyEntry entry in entries)
            {
                yield return new KeyValuePair<string, LegislativePlayerStrategy>(entry.StrategyId, entry.Strategy);
            }
        }
    }

    public sealed class LegislativeLimits
    {
        public LegislativeLimits(int maxActiveReforms, int maxStages)
        {
            MaxActiveReforms = maxActiveReforms;
            MaxStages = maxStages;
        }

        public int MaxActiveReforms { get; }

        public int MaxStages { get; }
    }

    public sealed class LegislativeConstants
    {
        public LegislativeConstants(int scale, int hundredS)
        {
            Scale = scale;
            HundredS = hundredS;
        }

        public int Scale { get; }

        public int HundredS { get; }
    }

    public sealed class LegislativeGates
    {
        public LegislativeGates(int normalLegitimacyMinS, int cohesionBlockMinS, int exceptionalMovementMinS, int antiMovementCrisisMinS)
        {
            NormalLegitimacyMinS = normalLegitimacyMinS;
            CohesionBlockMinS = cohesionBlockMinS;
            ExceptionalMovementMinS = exceptionalMovementMinS;
            AntiMovementCrisisMinS = antiMovementCrisisMinS;
        }

        public int NormalLegitimacyMinS { get; }

        public int CohesionBlockMinS { get; }

        public int ExceptionalMovementMinS { get; }

        public int AntiMovementCrisisMinS { get; }
    }

    public sealed class LegislativeSenate
    {
        public LegislativeSenate(int brakeS)
        {
            BrakeS = brakeS;
        }

        public int BrakeS { get; }
    }

    public sealed class LegislativeMovementMatching
    {
        public LegislativeMovementMatching(string reformTagsSource, LegislativeMovementMatchMode matchMode, int directionPro, int directionAnti)
        {
            ReformTagsSource = reformTagsSource ?? throw new ArgumentNullException(nameof(reformTagsSource));
            MatchMode = matchMode;
            DirectionPro = directionPro;
            DirectionAnti = directionAnti;
        }

        public string ReformTagsSource { get; }

        public LegislativeMovementMatchMode MatchMode { get; }

        public int DirectionPro { get; }

        public int DirectionAnti { get; }
    }

    public sealed class LegislativeExceptionalRoute
    {
        public LegislativeExceptionalRoute(bool enabled, bool bypassLegitimacyGate, bool requiresProMovement, IEnumerable<TargetDeltaDefinition> costsPerTick)
        {
            Enabled = enabled;
            BypassLegitimacyGate = bypassLegitimacyGate;
            RequiresProMovement = requiresProMovement;
            CostsPerTick = Array.AsReadOnly(ModelSnapshot.Array(costsPerTick, nameof(costsPerTick)));
        }

        public bool Enabled { get; }

        public bool BypassLegitimacyGate { get; }

        public bool RequiresProMovement { get; }

        public IReadOnlyList<TargetDeltaDefinition> CostsPerTick { get; }
    }

    public sealed class LegislativeSupportModel
    {
        public LegislativeSupportModel(
            LegislativeRange supportRangeS,
            LegislativeDiscipline discipline,
            LegislativeBaseComponent baseComponent,
            LegislativeLegitimacyComponent legitimacyComponent,
            LegislativeIgAlignmentComponent igAlignmentComponent,
            LegislativeMovementComponent movementComponent,
            LegislativeUpperChamber upperChamber)
        {
            SupportRangeS = supportRangeS ?? throw new ArgumentNullException(nameof(supportRangeS));
            Discipline = discipline ?? throw new ArgumentNullException(nameof(discipline));
            BaseComponent = baseComponent ?? throw new ArgumentNullException(nameof(baseComponent));
            LegitimacyComponent = legitimacyComponent ?? throw new ArgumentNullException(nameof(legitimacyComponent));
            IgAlignmentComponent = igAlignmentComponent ?? throw new ArgumentNullException(nameof(igAlignmentComponent));
            MovementComponent = movementComponent ?? throw new ArgumentNullException(nameof(movementComponent));
            UpperChamber = upperChamber ?? throw new ArgumentNullException(nameof(upperChamber));
        }

        public LegislativeRange SupportRangeS { get; }

        public LegislativeDiscipline Discipline { get; }

        public LegislativeBaseComponent BaseComponent { get; }

        public LegislativeLegitimacyComponent LegitimacyComponent { get; }

        public LegislativeIgAlignmentComponent IgAlignmentComponent { get; }

        public LegislativeMovementComponent MovementComponent { get; }

        public LegislativeUpperChamber UpperChamber { get; }
    }

    public sealed class LegislativeRange
    {
        public LegislativeRange(int minS, int maxS)
        {
            MinS = minS;
            MaxS = maxS;
        }

        public int MinS { get; }

        public int MaxS { get; }
    }

    public sealed class LegislativeDiscipline
    {
        public LegislativeDiscipline(int partyOrganizationWeightPpm, int internalCohesionWeightPpm)
        {
            PartyOrganizationWeightPpm = partyOrganizationWeightPpm;
            InternalCohesionWeightPpm = internalCohesionWeightPpm;
        }

        public int PartyOrganizationWeightPpm { get; }

        public int InternalCohesionWeightPpm { get; }
    }

    public sealed class LegislativeBaseComponent
    {
        public LegislativeBaseComponent(TargetPath metric, int addS)
        {
            Metric = metric;
            AddS = addS;
        }

        public TargetPath Metric { get; }

        public int AddS { get; }
    }

    public sealed class LegislativeLegitimacyComponent
    {
        public LegislativeLegitimacyComponent(TargetPath metric, int midS, int divS)
        {
            Metric = metric;
            MidS = midS;
            DivS = divS;
        }

        public TargetPath Metric { get; }

        public int MidS { get; }

        public int DivS { get; }
    }

    public sealed class LegislativeIgAlignmentComponent
    {
        public LegislativeIgAlignmentComponent(
            LegislativeIgAlignmentUses uses,
            LegislativeApprovalTo01 approvalTo01,
            LegislativeEffectiveStanceFactor effectiveStanceFactor,
            int stanceInputRange,
            int stanceScaleToS,
            int cloutDenomS,
            int termDiv,
            bool applyDiscipline,
            int postDivS)
        {
            Uses = uses ?? throw new ArgumentNullException(nameof(uses));
            ApprovalTo01 = approvalTo01 ?? throw new ArgumentNullException(nameof(approvalTo01));
            EffectiveStanceFactor = effectiveStanceFactor ?? throw new ArgumentNullException(nameof(effectiveStanceFactor));
            StanceInputRange = stanceInputRange;
            StanceScaleToS = stanceScaleToS;
            CloutDenomS = cloutDenomS;
            TermDiv = termDiv;
            ApplyDiscipline = applyDiscipline;
            PostDivS = postDivS;
        }

        public LegislativeIgAlignmentUses Uses { get; }

        public LegislativeApprovalTo01 ApprovalTo01 { get; }

        public LegislativeEffectiveStanceFactor EffectiveStanceFactor { get; }

        public int StanceInputRange { get; }

        public int StanceScaleToS { get; }

        public int CloutDenomS { get; }

        public int TermDiv { get; }

        public bool ApplyDiscipline { get; }

        public int PostDivS { get; }
    }

    public sealed class LegislativeIgAlignmentUses
    {
        public LegislativeIgAlignmentUses(TargetPattern cloutTargetPattern, TargetPattern approvalTargetPattern)
        {
            CloutTargetPattern = cloutTargetPattern;
            ApprovalTargetPattern = approvalTargetPattern;
        }

        public TargetPattern CloutTargetPattern { get; }

        public TargetPattern ApprovalTargetPattern { get; }
    }

    public sealed class LegislativeApprovalTo01
    {
        public LegislativeApprovalTo01(int offsetS, int divS)
        {
            OffsetS = offsetS;
            DivS = divS;
        }

        public int OffsetS { get; }

        public int DivS { get; }
    }

    public sealed class LegislativeEffectiveStanceFactor
    {
        public LegislativeEffectiveStanceFactor(int baseS, int approval01DivS, int denomS)
        {
            BaseS = baseS;
            Approval01DivS = approval01DivS;
            DenomS = denomS;
        }

        public int BaseS { get; }

        public int Approval01DivS { get; }

        public int DenomS { get; }
    }

    public sealed class LegislativeMovementComponent
    {
        public LegislativeMovementComponent(int termClampS, int supportDivS)
        {
            TermClampS = termClampS;
            SupportDivS = supportDivS;
        }

        public int TermClampS { get; }

        public int SupportDivS { get; }
    }

    public sealed class LegislativeUpperChamber
    {
        public LegislativeUpperChamber(LegislativeUpperChamberAdjustmentType type, int deltaS)
        {
            Type = type;
            DeltaS = deltaS;
        }

        public LegislativeUpperChamberAdjustmentType Type { get; }

        public int DeltaS { get; }
    }

    public sealed class LegislativeStageModel
    {
        public LegislativeStageModel(
            int baseDifficultyDefaultS,
            LegislativeStageWeight stageWeight,
            LegislativeThroughput throughput,
            LegislativeVote vote)
        {
            BaseDifficultyDefaultS = baseDifficultyDefaultS;
            StageWeight = stageWeight ?? throw new ArgumentNullException(nameof(stageWeight));
            Throughput = throughput ?? throw new ArgumentNullException(nameof(throughput));
            Vote = vote ?? throw new ArgumentNullException(nameof(vote));
        }

        public int BaseDifficultyDefaultS { get; }

        public LegislativeStageWeight StageWeight { get; }

        public LegislativeThroughput Throughput { get; }

        public LegislativeVote Vote { get; }
    }

    public sealed class LegislativeStageWeight
    {
        public LegislativeStageWeight(int scaleDenomS, IEnumerable<KeyValuePair<string, int>> defaultWeightS)
        {
            ScaleDenomS = scaleDenomS;
            DefaultWeightS = ModelSnapshot.Dictionary(defaultWeightS, nameof(defaultWeightS));
        }

        public int ScaleDenomS { get; }

        public IReadOnlyDictionary<string, int> DefaultWeightS { get; }
    }

    public sealed class LegislativeThroughput
    {
        public LegislativeThroughput(int baseS, TargetPath governabilityMetric, int metricDenomS, int supportDenomS, LegislativeChamberSupportMode chamberBothSupport)
        {
            BaseS = baseS;
            GovernabilityMetric = governabilityMetric;
            MetricDenomS = metricDenomS;
            SupportDenomS = supportDenomS;
            ChamberBothSupport = chamberBothSupport;
        }

        public int BaseS { get; }

        public TargetPath GovernabilityMetric { get; }

        public int MetricDenomS { get; }

        public int SupportDenomS { get; }

        public LegislativeChamberSupportMode ChamberBothSupport { get; }
    }

    public sealed class LegislativeVote
    {
        public LegislativeVote(
            int supportFloorS,
            int supportSpanS,
            int passThresholdS,
            int failResetProgressS,
            IEnumerable<TargetDeltaDefinition> failPenalties)
        {
            SupportFloorS = supportFloorS;
            SupportSpanS = supportSpanS;
            PassThresholdS = passThresholdS;
            FailResetProgressS = failResetProgressS;
            FailPenalties = Array.AsReadOnly(ModelSnapshot.Array(failPenalties, nameof(failPenalties)));
        }

        public int SupportFloorS { get; }

        public int SupportSpanS { get; }

        public int PassThresholdS { get; }

        public int FailResetProgressS { get; }

        public IReadOnlyList<TargetDeltaDefinition> FailPenalties { get; }
    }

    public sealed class LegislativePlayerStrategyEntry
    {
        public LegislativePlayerStrategyEntry(string strategyId, LegislativePlayerStrategy strategy)
        {
            StrategyId = strategyId ?? throw new ArgumentNullException(nameof(strategyId));
            Strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        }

        public string StrategyId { get; }

        public LegislativePlayerStrategy Strategy { get; }
    }

    public sealed class LegislativePlayerStrategy
    {
        public LegislativePlayerStrategy(
            int supportBonusS,
            int throughputMultiplierPpm,
            int? implementationEffectMultiplierPpm,
            IEnumerable<TargetDeltaDefinition> perTickDeltas)
        {
            SupportBonusS = supportBonusS;
            ThroughputMultiplierPpm = throughputMultiplierPpm;
            ImplementationEffectMultiplierPpm = implementationEffectMultiplierPpm;
            PerTickDeltas = Array.AsReadOnly(ModelSnapshot.Array(perTickDeltas, nameof(perTickDeltas)));
        }

        public int SupportBonusS { get; }

        public int ThroughputMultiplierPpm { get; }

        public int? ImplementationEffectMultiplierPpm { get; }

        public IReadOnlyList<TargetDeltaDefinition> PerTickDeltas { get; }
    }

    public sealed class LegislativeCausePrefixes
    {
        public LegislativeCausePrefixes(string progress, string support, string block, string exceptionCost, string voteFail)
        {
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            Support = support ?? throw new ArgumentNullException(nameof(support));
            Block = block ?? throw new ArgumentNullException(nameof(block));
            ExceptionCost = exceptionCost ?? throw new ArgumentNullException(nameof(exceptionCost));
            VoteFail = voteFail ?? throw new ArgumentNullException(nameof(voteFail));
        }

        public string Progress { get; }

        public string Support { get; }

        public string Block { get; }

        public string ExceptionCost { get; }

        public string VoteFail { get; }
    }

    public sealed class TargetDeltaDefinition
    {
        public TargetDeltaDefinition(TargetPath target, int deltaS, string cause)
        {
            Target = target;
            DeltaS = deltaS;
            Cause = cause ?? throw new ArgumentNullException(nameof(cause));
        }

        public TargetPath Target { get; }

        public int DeltaS { get; }

        public string Cause { get; }
    }

    public sealed class EffectTemplate
    {
        public EffectTemplate(string id, string localizationTitleKey, IEnumerable<EffectModifier> modifiers, IEnumerable<string> tags)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            LocalizationTitleKey = localizationTitleKey ?? throw new ArgumentNullException(nameof(localizationTitleKey));
            Modifiers = Array.AsReadOnly(ModelSnapshot.Array(modifiers, nameof(modifiers)));
            Tags = Array.AsReadOnly(ModelSnapshot.Array(tags, nameof(tags)));
        }

        public string Id { get; }

        public string LocalizationTitleKey { get; }

        public IReadOnlyList<EffectModifier> Modifiers { get; }

        public IReadOnlyList<string> Tags { get; }
    }

    public sealed class EffectModifier
    {
        public EffectModifier(TargetPath target, TargetOperation operation, int valueS, bool isPerTick, EffectClamp clamp)
        {
            Target = target;
            Operation = operation;
            ValueS = valueS;
            IsPerTick = isPerTick;
            Clamp = clamp;
        }

        public TargetPath Target { get; }

        public TargetOperation Operation { get; }

        public int ValueS { get; }

        public bool IsPerTick { get; }

        public EffectClamp Clamp { get; }
    }

    public sealed class EffectClamp
    {
        public EffectClamp(int? minS, int? maxS)
        {
            MinS = minS;
            MaxS = maxS;
        }

        public int? MinS { get; }

        public int? MaxS { get; }
    }

    public enum EventKind
    {
        Auto,
        Choice,
        Crisis
    }

    public enum EventScope
    {
        National,
        Region
    }

    public enum EventSelectorMode
    {
        ArgMax,
        Weighted
    }

    public enum EventComparator
    {
        LessThan,
        LessThanOrEqual,
        Equal,
        GreaterThanOrEqual,
        GreaterThan
    }

    public enum EffectInvocationType
    {
        Modifier
    }

    public abstract class EventVariableBinding
    {
        protected EventVariableBinding(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }
    }

    public sealed class EventRegionBinding : EventVariableBinding
    {
        public EventRegionBinding(string name, EventSelectorMode mode, TargetPattern targetPattern)
            : base(name)
        {
            Mode = mode;
            TargetPattern = targetPattern;
        }

        public EventSelectorMode Mode { get; }

        public TargetPattern TargetPattern { get; }
    }

    public sealed class EventInterestGroupBinding : EventVariableBinding
    {
        public EventInterestGroupBinding(string name, EventSelectorMode mode, TargetPattern targetPattern)
            : base(name)
        {
            Mode = mode;
            TargetPattern = targetPattern;
        }

        public EventSelectorMode Mode { get; }

        public TargetPattern TargetPattern { get; }
    }

    public sealed class EventSeverityBinding : EventVariableBinding
    {
        public EventSeverityBinding(string name, TargetPath target, IEnumerable<EventSeverityBand> bands)
            : base(name)
        {
            Target = target;
            Bands = Array.AsReadOnly(ModelSnapshot.Array(bands, nameof(bands)));
        }

        public TargetPath Target { get; }

        public IReadOnlyList<EventSeverityBand> Bands { get; }
    }

    public sealed class EventSeverityBand
    {
        public EventSeverityBand(int minValueS, int maxValueS, int severity)
        {
            MinValueS = minValueS;
            MaxValueS = maxValueS;
            Severity = severity;
        }

        public int MinValueS { get; }

        public int MaxValueS { get; }

        public int Severity { get; }
    }

    public abstract class EventConditionNode
    {
    }

    public sealed class EventConditionAllNode : EventConditionNode
    {
        public EventConditionAllNode(IEnumerable<EventConditionNode> children)
        {
            Children = Array.AsReadOnly(ModelSnapshot.Array(children, nameof(children)));
        }

        public IReadOnlyList<EventConditionNode> Children { get; }
    }

    public sealed class EventConditionCompareTargetNode : EventConditionNode
    {
        public EventConditionCompareTargetNode(TargetPath target, EventComparator comparator, int valueS)
        {
            Target = target;
            Comparator = comparator;
            ValueS = valueS;
        }

        public TargetPath Target { get; }

        public EventComparator Comparator { get; }

        public int ValueS { get; }
    }

    public sealed class EventConditionCompareMovementNode : EventConditionNode
    {
        public EventConditionCompareMovementNode(string movementId, EventComparator comparator, int valueS)
        {
            MovementId = movementId ?? throw new ArgumentNullException(nameof(movementId));
            Comparator = comparator;
            ValueS = valueS;
        }

        public string MovementId { get; }

        public EventComparator Comparator { get; }

        public int ValueS { get; }
    }

    public sealed class EventConditionFlagIsSetNode : EventConditionNode
    {
        public EventConditionFlagIsSetNode(string flagId)
        {
            FlagId = flagId ?? throw new ArgumentNullException(nameof(flagId));
        }

        public string FlagId { get; }
    }

    public sealed class EventConditionCooldownReadyNode : EventConditionNode
    {
    }

    public sealed class EventConditionMaxCountNotReachedNode : EventConditionNode
    {
    }

    public sealed class EffectTemplateInvocation
    {
        public EffectTemplateInvocation(EffectInvocationType type, string templateId, int durationWeeks)
        {
            Type = type;
            TemplateId = templateId ?? throw new ArgumentNullException(nameof(templateId));
            DurationWeeks = durationWeeks;
        }

        public EffectInvocationType Type { get; }

        public string TemplateId { get; }

        public int DurationWeeks { get; }
    }

    public sealed class EventMemoryMutation
    {
        public EventMemoryMutation(IEnumerable<string> setFlags, IEnumerable<string> clearFlags, bool setCooldown)
        {
            SetFlags = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(setFlags));
            ClearFlags = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(clearFlags));
            SetCooldown = setCooldown;
        }

        public IReadOnlyList<string> SetFlags { get; }

        public IReadOnlyList<string> ClearFlags { get; }

        public bool SetCooldown { get; }
    }

    public sealed class EventFollowup
    {
        public EventFollowup(int afterWeeks, string eventId)
        {
            AfterWeeks = afterWeeks;
            EventId = eventId ?? throw new ArgumentNullException(nameof(eventId));
        }

        public int AfterWeeks { get; }

        public string EventId { get; }
    }

    public sealed class EventOption
    {
        public EventOption(
            string id,
            string localizationLabelKey,
            EventConditionNode requirements,
            IEnumerable<EffectTemplateInvocation> effects,
            EventMemoryMutation memory,
            IEnumerable<EventFollowup> followups)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            LocalizationLabelKey = localizationLabelKey ?? throw new ArgumentNullException(nameof(localizationLabelKey));
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            Effects = Array.AsReadOnly(ModelSnapshot.Array(effects, nameof(effects)));
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            Followups = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(followups));
        }

        public string Id { get; }

        public string LocalizationLabelKey { get; }

        public EventConditionNode Requirements { get; }

        public IReadOnlyList<EffectTemplateInvocation> Effects { get; }

        public EventMemoryMutation Memory { get; }

        public IReadOnlyList<EventFollowup> Followups { get; }
    }

    public sealed class EventTemplate
    {
        public EventTemplate(
            string id,
            string localizationTitleKey,
            EventKind kind,
            EventScope scope,
            bool blocking,
            int basePriority,
            int weight,
            int cooldownWeeks,
            int maxPerCampaign,
            IEnumerable<string> tags,
            IEnumerable<EventVariableBinding> variables,
            EventConditionNode conditions,
            IEnumerable<EventOption> options,
            string autoOptionId)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            LocalizationTitleKey = localizationTitleKey ?? throw new ArgumentNullException(nameof(localizationTitleKey));
            Kind = kind;
            Scope = scope;
            Blocking = blocking;
            BasePriority = basePriority;
            Weight = weight;
            CooldownWeeks = cooldownWeeks;
            MaxPerCampaign = maxPerCampaign;
            Tags = Array.AsReadOnly(ModelSnapshot.Array(tags, nameof(tags)));
            EventVariableBinding[] variableSnapshot = ModelSnapshot.ArrayOrEmpty(variables);
            Variables = Array.AsReadOnly(variableSnapshot);
            VariablesByName = ModelSnapshot.Dictionary(MapEventVariablesByName(variableSnapshot), nameof(variables));
            Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            EventOption[] optionSnapshot = ModelSnapshot.Array(options, nameof(options));
            Options = Array.AsReadOnly(optionSnapshot);
            OptionsById = ModelSnapshot.Dictionary(MapEventOptionsById(optionSnapshot), nameof(options));
            AutoOptionId = autoOptionId;
        }

        public string Id { get; }

        public string LocalizationTitleKey { get; }

        public EventKind Kind { get; }

        public EventScope Scope { get; }

        public bool Blocking { get; }

        public int BasePriority { get; }

        public int Weight { get; }

        public int CooldownWeeks { get; }

        public int MaxPerCampaign { get; }

        public IReadOnlyList<string> Tags { get; }

        public IReadOnlyList<EventVariableBinding> Variables { get; }

        public IReadOnlyDictionary<string, EventVariableBinding> VariablesByName { get; }

        public EventConditionNode Conditions { get; }

        public IReadOnlyList<EventOption> Options { get; }

        public IReadOnlyDictionary<string, EventOption> OptionsById { get; }

        public string AutoOptionId { get; }

        private static IEnumerable<KeyValuePair<string, EventVariableBinding>> MapEventVariablesByName(IEnumerable<EventVariableBinding> values)
        {
            foreach (EventVariableBinding value in values)
            {
                yield return new KeyValuePair<string, EventVariableBinding>(value.Name, value);
            }
        }

        private static IEnumerable<KeyValuePair<string, EventOption>> MapEventOptionsById(IEnumerable<EventOption> values)
        {
            foreach (EventOption value in values)
            {
                yield return new KeyValuePair<string, EventOption>(value.Id, value);
            }
        }
    }

    public enum ReformKind
    {
        Normal,
        SpecialConstitutional
    }

    public enum ReformPrerequisiteType
    {
        Metric
    }

    public enum ReformStageKind
    {
        Work,
        Vote
    }

    public enum ReformStageChamber
    {
        None,
        Lower,
        Upper,
        Both
    }

    public sealed class ReformInterestGroupStance
    {
        public ReformInterestGroupStance(string interestGroupId, int stance)
        {
            InterestGroupId = interestGroupId ?? throw new ArgumentNullException(nameof(interestGroupId));
            Stance = stance;
        }

        public string InterestGroupId { get; }

        public int Stance { get; }
    }

    public sealed class ReformPrerequisite
    {
        public ReformPrerequisite(ReformPrerequisiteType type, TargetPath target, EventComparator comparator, int valueS)
        {
            Type = type;
            Target = target;
            Comparator = comparator;
            ValueS = valueS;
        }

        public ReformPrerequisiteType Type { get; }

        public TargetPath Target { get; }

        public EventComparator Comparator { get; }

        public int ValueS { get; }
    }

    public sealed class ReformStage
    {
        public ReformStage(string id, ReformStageKind kind, ReformStageChamber chamber, int weightS)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Kind = kind;
            Chamber = chamber;
            WeightS = weightS;
        }

        public string Id { get; }

        public ReformStageKind Kind { get; }

        public ReformStageChamber Chamber { get; }

        public int WeightS { get; }
    }

    public sealed class ReformTemplate
    {
        public ReformTemplate(
            string id,
            string localizationTitleKey,
            string localizationDescriptionKey,
            string area,
            ReformKind kind,
            int cooldownWeeks,
            int maxPerCampaign,
            IEnumerable<string> movementTags,
            IEnumerable<string> policyTags,
            IEnumerable<ReformInterestGroupStance> explicitInterestGroupStances,
            IEnumerable<ReformInterestGroupStance> effectiveInterestGroupStances,
            IEnumerable<ReformPrerequisite> prerequisites,
            int baseDifficultyS,
            IEnumerable<ReformStage> stages,
            IEnumerable<EffectTemplateInvocation> onPassEffects)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            LocalizationTitleKey = localizationTitleKey ?? throw new ArgumentNullException(nameof(localizationTitleKey));
            LocalizationDescriptionKey = localizationDescriptionKey ?? throw new ArgumentNullException(nameof(localizationDescriptionKey));
            Area = area ?? throw new ArgumentNullException(nameof(area));
            Kind = kind;
            CooldownWeeks = cooldownWeeks;
            MaxPerCampaign = maxPerCampaign;
            MovementTags = Array.AsReadOnly(ModelSnapshot.Array(movementTags, nameof(movementTags)));
            PolicyTags = Array.AsReadOnly(ModelSnapshot.Array(policyTags, nameof(policyTags)));

            ReformInterestGroupStance[] explicitSnapshot = ModelSnapshot.Array(explicitInterestGroupStances, nameof(explicitInterestGroupStances));
            ExplicitInterestGroupStances = Array.AsReadOnly(explicitSnapshot);
            ExplicitInterestGroupStancesById = ModelSnapshot.Dictionary(MapInterestGroupStancesById(explicitSnapshot), nameof(explicitInterestGroupStances));

            ReformInterestGroupStance[] effectiveSnapshot = ModelSnapshot.Array(effectiveInterestGroupStances, nameof(effectiveInterestGroupStances));
            EffectiveInterestGroupStances = Array.AsReadOnly(effectiveSnapshot);
            EffectiveInterestGroupStancesById = ModelSnapshot.Dictionary(MapInterestGroupStancesById(effectiveSnapshot), nameof(effectiveInterestGroupStances));

            Prerequisites = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(prerequisites));
            BaseDifficultyS = baseDifficultyS;

            ReformStage[] stageSnapshot = ModelSnapshot.Array(stages, nameof(stages));
            Stages = Array.AsReadOnly(stageSnapshot);
            StagesById = ModelSnapshot.Dictionary(MapStagesById(stageSnapshot), nameof(stages));

            OnPassEffects = Array.AsReadOnly(ModelSnapshot.ArrayOrEmpty(onPassEffects));
        }

        public string Id { get; }

        public string LocalizationTitleKey { get; }

        public string LocalizationDescriptionKey { get; }

        public string Area { get; }

        public ReformKind Kind { get; }

        public int CooldownWeeks { get; }

        public int MaxPerCampaign { get; }

        public IReadOnlyList<string> MovementTags { get; }

        public IReadOnlyList<string> PolicyTags { get; }

        public IReadOnlyList<ReformInterestGroupStance> ExplicitInterestGroupStances { get; }

        public IReadOnlyDictionary<string, int> ExplicitInterestGroupStancesById { get; }

        public IReadOnlyList<ReformInterestGroupStance> EffectiveInterestGroupStances { get; }

        public IReadOnlyDictionary<string, int> EffectiveInterestGroupStancesById { get; }

        public IReadOnlyList<ReformPrerequisite> Prerequisites { get; }

        public int BaseDifficultyS { get; }

        public IReadOnlyList<ReformStage> Stages { get; }

        public IReadOnlyDictionary<string, ReformStage> StagesById { get; }

        public IReadOnlyList<EffectTemplateInvocation> OnPassEffects { get; }

        private static IEnumerable<KeyValuePair<string, int>> MapInterestGroupStancesById(IEnumerable<ReformInterestGroupStance> values)
        {
            foreach (ReformInterestGroupStance value in values)
            {
                yield return new KeyValuePair<string, int>(value.InterestGroupId, value.Stance);
            }
        }

        private static IEnumerable<KeyValuePair<string, ReformStage>> MapStagesById(IEnumerable<ReformStage> values)
        {
            foreach (ReformStage value in values)
            {
                yield return new KeyValuePair<string, ReformStage>(value.Id, value);
            }
        }
    }

    public sealed class ContentPack
    {
        public ContentPack(
            ContentManifest manifest,
            IEnumerable<TargetConfig> targetConfigs,
            IEnumerable<RegionDefinition> regions,
            IEnumerable<InterestGroupDefinition> interestGroups,
            IEnumerable<MovementDefinition> movements)
            : this(manifest, targetConfigs, regions, interestGroups, movements, null, null, null, null, null, null)
        {
        }

        public ContentPack(
            ContentManifest manifest,
            IEnumerable<TargetConfig> targetConfigs,
            IEnumerable<RegionDefinition> regions,
            IEnumerable<InterestGroupDefinition> interestGroups,
            IEnumerable<MovementDefinition> movements,
            ContentLocalizationTable localization,
            AggregationConfig aggregationConfig,
            LegislativeConfig legislativeConfig,
            IEnumerable<EffectTemplate> effects)
            : this(manifest, targetConfigs, regions, interestGroups, movements, localization, aggregationConfig, legislativeConfig, effects, null, null)
        {
        }

        public ContentPack(
            ContentManifest manifest,
            IEnumerable<TargetConfig> targetConfigs,
            IEnumerable<RegionDefinition> regions,
            IEnumerable<InterestGroupDefinition> interestGroups,
            IEnumerable<MovementDefinition> movements,
            ContentLocalizationTable localization,
            AggregationConfig aggregationConfig,
            LegislativeConfig legislativeConfig,
            IEnumerable<EffectTemplate> effects,
            IEnumerable<EventTemplate> events,
            IEnumerable<ReformTemplate> reforms)
            : this(
                manifest,
                targetConfigs,
                regions,
                interestGroups,
                movements,
                localization,
                aggregationConfig,
                aggregationConfig == null ? null : CompileAggregationRuntimePlan(aggregationConfig),
                legislativeConfig,
                effects,
                events,
                reforms)
        {
        }

        internal ContentPack(
            ContentManifest manifest,
            IEnumerable<TargetConfig> targetConfigs,
            IEnumerable<RegionDefinition> regions,
            IEnumerable<InterestGroupDefinition> interestGroups,
            IEnumerable<MovementDefinition> movements,
            ContentLocalizationTable localization,
            AggregationConfig aggregationConfig,
            AggregationRuntimePlan aggregationRuntimePlan,
            LegislativeConfig legislativeConfig,
            IEnumerable<EffectTemplate> effects,
            IEnumerable<EventTemplate> events,
            IEnumerable<ReformTemplate> reforms)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));

            TargetConfig[] targetConfigSnapshot = ModelSnapshot.Array(targetConfigs, nameof(targetConfigs));
            RegionDefinition[] regionSnapshot = ModelSnapshot.Array(regions, nameof(regions));
            InterestGroupDefinition[] interestGroupSnapshot = ModelSnapshot.Array(interestGroups, nameof(interestGroups));
            MovementDefinition[] movementSnapshot = ModelSnapshot.Array(movements, nameof(movements));
            EffectTemplate[] effectSnapshot = ModelSnapshot.ArrayOrEmpty(effects);
            EventTemplate[] eventSnapshot = ModelSnapshot.ArrayOrEmpty(events);
            ReformTemplate[] reformSnapshot = ModelSnapshot.ArrayOrEmpty(reforms);

            TargetConfigs = Array.AsReadOnly(targetConfigSnapshot);
            TargetConfigCatalog = new TargetConfigCatalog(targetConfigSnapshot);
            Regions = Array.AsReadOnly(regionSnapshot);
            InterestGroups = Array.AsReadOnly(interestGroupSnapshot);
            Movements = Array.AsReadOnly(movementSnapshot);
            RegionsById = ModelSnapshot.Dictionary(MapRegionsById(regionSnapshot), nameof(regions));
            InterestGroupsById = ModelSnapshot.Dictionary(MapInterestGroupsById(interestGroupSnapshot), nameof(interestGroups));
            MovementsById = ModelSnapshot.Dictionary(MapMovementsById(movementSnapshot), nameof(movements));
            Localization = localization;
            AggregationConfig = aggregationConfig;
            AggregationRuntimePlan = aggregationRuntimePlan;
            LegislativeConfig = legislativeConfig;
            Effects = Array.AsReadOnly(effectSnapshot);
            EffectsById = ModelSnapshot.Dictionary(MapEffectsById(effectSnapshot), nameof(effects));
            EffectRuntimeCatalog = new EffectRuntimeCatalog(MapEffectRuntimeTemplates(effectSnapshot));
            Events = Array.AsReadOnly(eventSnapshot);
            EventsById = ModelSnapshot.Dictionary(MapEventsById(eventSnapshot), nameof(events));
            Reforms = Array.AsReadOnly(reformSnapshot);
            ReformsById = ModelSnapshot.Dictionary(MapReformsById(reformSnapshot), nameof(reforms));
        }

        public ContentManifest Manifest { get; }

        public IReadOnlyList<TargetConfig> TargetConfigs { get; }

        public TargetConfigCatalog TargetConfigCatalog { get; }

        public IReadOnlyList<RegionDefinition> Regions { get; }

        public IReadOnlyDictionary<string, RegionDefinition> RegionsById { get; }

        public IReadOnlyList<InterestGroupDefinition> InterestGroups { get; }

        public IReadOnlyDictionary<string, InterestGroupDefinition> InterestGroupsById { get; }

        public IReadOnlyList<MovementDefinition> Movements { get; }

        public IReadOnlyDictionary<string, MovementDefinition> MovementsById { get; }

        public ContentLocalizationTable Localization { get; }

        public AggregationConfig AggregationConfig { get; }

        public LegislativeConfig LegislativeConfig { get; }

        public IReadOnlyList<EffectTemplate> Effects { get; }

        public IReadOnlyDictionary<string, EffectTemplate> EffectsById { get; }

        public EffectRuntimeCatalog EffectRuntimeCatalog { get; }

        public AggregationRuntimePlan AggregationRuntimePlan { get; }

        public IReadOnlyList<EventTemplate> Events { get; }

        public IReadOnlyDictionary<string, EventTemplate> EventsById { get; }

        public IReadOnlyList<ReformTemplate> Reforms { get; }

        public IReadOnlyDictionary<string, ReformTemplate> ReformsById { get; }

        private static IEnumerable<KeyValuePair<string, RegionDefinition>> MapRegionsById(IEnumerable<RegionDefinition> values)
        {
            foreach (RegionDefinition value in values)
            {
                yield return new KeyValuePair<string, RegionDefinition>(value.Id, value);
            }
        }

        private static IEnumerable<KeyValuePair<string, InterestGroupDefinition>> MapInterestGroupsById(IEnumerable<InterestGroupDefinition> values)
        {
            foreach (InterestGroupDefinition value in values)
            {
                yield return new KeyValuePair<string, InterestGroupDefinition>(value.Id, value);
            }
        }

        private static IEnumerable<KeyValuePair<string, MovementDefinition>> MapMovementsById(IEnumerable<MovementDefinition> values)
        {
            foreach (MovementDefinition value in values)
            {
                yield return new KeyValuePair<string, MovementDefinition>(value.Id, value);
            }
        }

        private static IEnumerable<KeyValuePair<string, EffectTemplate>> MapEffectsById(IEnumerable<EffectTemplate> values)
        {
            foreach (EffectTemplate value in values)
            {
                yield return new KeyValuePair<string, EffectTemplate>(value.Id, value);
            }
        }

        private static IEnumerable<EffectTemplateRuntime> MapEffectRuntimeTemplates(IEnumerable<EffectTemplate> values)
        {
            foreach (EffectTemplate value in values)
            {
                List<EffectModifierRuntime> modifiers = new List<EffectModifierRuntime>(value.Modifiers.Count);
                for (int i = 0; i < value.Modifiers.Count; i++)
                {
                    EffectModifier modifier = value.Modifiers[i];
                    modifiers.Add(new EffectModifierRuntime(
                        modifier.Target,
                        modifier.Operation,
                        modifier.ValueS,
                        modifier.IsPerTick,
                        modifier.Clamp == null ? null : new EffectClampRuntime(modifier.Clamp.MinS, modifier.Clamp.MaxS)));
                }

                yield return new EffectTemplateRuntime(value.Id, modifiers);
            }
        }

        private static IEnumerable<KeyValuePair<string, EventTemplate>> MapEventsById(IEnumerable<EventTemplate> values)
        {
            foreach (EventTemplate value in values)
            {
                yield return new KeyValuePair<string, EventTemplate>(value.Id, value);
            }
        }

        private static IEnumerable<KeyValuePair<string, ReformTemplate>> MapReformsById(IEnumerable<ReformTemplate> values)
        {
            foreach (ReformTemplate value in values)
            {
                yield return new KeyValuePair<string, ReformTemplate>(value.Id, value);
            }
        }

        internal static AggregationRuntimePlan CompileAggregationRuntimePlan(AggregationConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            ValidateAggregationConstants(config);

            AggregationReversionPassRuntime reversionPass = null;
            AggregationDerivedPassRuntime derivedPass = null;
            AggregationMetricsPassRuntime primaryPass = null;
            AggregationMetricsPassRuntime legitimacyPass = null;
            HashSet<TargetPath> allMetricTargets = new HashSet<TargetPath>();

            for (int i = 0; i < config.Passes.Count; i++)
            {
                AggregationPass pass = config.Passes[i];
                if (pass == null)
                {
                    throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, "$.passes[" + i + "]", "Aggregation passes cannot contain null entries.");
                }

                ValidateEnum(pass.Type, "$.passes[" + i + "].type", "Unknown aggregation pass type.");
                ValidatePassPrefix(pass, "$.passes[" + i + "]");

                switch (pass.Type)
                {
                    case AggregationPassType.InternalReversion:
                        if (reversionPass != null)
                        {
                            throw AggregationCompileError(ContentDiagnosticCode.AggregationMissingRequiredPassType, "$.passes", "Content pack must declare exactly one InternalReversion pass.");
                        }

                        ValidateInternalReversionShape(pass, "$.passes[" + i + "]");
                        reversionPass = new AggregationReversionPassRuntime(
                            MapGroups(pass.Groups),
                            pass.SkipTargets);
                        break;

                    case AggregationPassType.DerivedInternals:
                        if (derivedPass != null)
                        {
                            throw AggregationCompileError(ContentDiagnosticCode.AggregationMissingRequiredPassType, "$.passes", "Content pack must declare exactly one DerivedInternals pass.");
                        }

                        ValidateDerivedShape(pass, "$.passes[" + i + "]");
                        derivedPass = new AggregationDerivedPassRuntime(
                            MapDerivedRules(pass.Rules));
                        break;

                    case AggregationPassType.MetricAggregation:
                        ValidateMetricPassShape(pass, "$.passes[" + i + "]");
                        IReadOnlyList<AggregationMetricRuntime> mapped = MapMetrics(pass.Metrics);
                        AggregationMetricsPassRuntime metricPass = new AggregationMetricsPassRuntime(mapped);
                        bool isLegitimacy = mapped.Count == 1
                            && mapped[0].Metric == TargetPath.Parse("metrics.legitimacy");

                        ValidateMetricTargets(mapped, allMetricTargets, "$.passes[" + i + "]");

                        if (isLegitimacy)
                        {
                            if (legitimacyPass != null)
                            {
                                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, "$.passes", "Multiple metric passes contain metrics.legitimacy; exactly one legitimacy pass is required.");
                            }

                            legitimacyPass = metricPass;
                        }
                        else
                        {
                            if (primaryPass != null)
                            {
                                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, "$.passes", "Multiple primary metric passes found; exactly one primary metric pass is required.");
                            }

                            if (ContainsLegitimacy(mapped))
                            {
                                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, "$.passes[" + i + "]", "Legitimacy pass must not mix metrics.legitimacy with other metrics.");
                            }

                            primaryPass = metricPass;
                        }

                        break;
                }
            }

            if (reversionPass == null)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationMissingRequiredPassType, "$.passes", "Content pack must declare exactly one InternalReversion pass.");
            }

            if (derivedPass == null)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationMissingRequiredPassType, "$.passes", "Content pack must declare exactly one DerivedInternals pass.");
            }

            if (primaryPass == null)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationMissingRequiredPassType, "$.passes", "Primary metrics pass not found. One metric pass must target metrics other than metrics.legitimacy.");
            }

            if (legitimacyPass == null)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationMissingRequiredPassType, "$.passes", "Legitimacy metrics pass not found. One metric pass must target exactly metrics.legitimacy.");
            }

            if (primaryPass.Metrics.Count != AggregationRuntimePlan.RequiredPrimaryMetricCount)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationMissingRequiredPassType, "$.passes.metrics", "Primary metrics pass must contain exactly 9 metrics.");
            }

            if (legitimacyPass.Metrics.Count != 1 || legitimacyPass.Metrics[0].Metric != TargetPath.Parse("metrics.legitimacy"))
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, "$.passes.metrics", "Legitimacy pass must contain exactly metrics.legitimacy.");
            }

            return new AggregationRuntimePlan(
                config.Scale,
                config.MidS,
                AggregationRoundingModeRuntime.HalfAwayFromZero,
                reversionPass,
                derivedPass,
                primaryPass,
                legitimacyPass);
        }

        private static void ValidateAggregationConstants(AggregationConfig config)
        {
            if (config.SchemaVersion != 1)
            {
                throw AggregationCompileError(ContentDiagnosticCode.UnsupportedSchemaVersion, "$.schema_version", "Unsupported aggregation schema version.");
            }

            if (config.Scale != AggregationRuntimePlan.RequiredScale)
            {
                throw AggregationCompileError(ContentDiagnosticCode.InvalidRange, "$.scale", "Aggregation scale must be exactly 100.");
            }

            if (config.MidS != AggregationRuntimePlan.RequiredMidS)
            {
                throw AggregationCompileError(ContentDiagnosticCode.InvalidRange, "$.midS", "Aggregation midS must be exactly 5000.");
            }

            ValidateEnum(config.Rounding, "$.rounding", "rounding must be HALF_AWAY_FROM_ZERO.");
            if (config.Rounding != ContentRoundingMode.HalfAwayFromZero)
            {
                throw AggregationCompileError(ContentDiagnosticCode.InvalidEnum, "$.rounding", "rounding must be HALF_AWAY_FROM_ZERO.");
            }

            if (config.Passes == null || config.Passes.Count == 0)
            {
                throw AggregationCompileError(ContentDiagnosticCode.InvalidValue, "$.passes", "passes must not be empty.");
            }
        }

        private static void ValidateInternalReversionShape(AggregationPass pass, string passPath)
        {
            if (pass.MidS != AggregationRuntimePlan.RequiredMidS)
            {
                throw AggregationCompileError(ContentDiagnosticCode.InvalidRange, passPath + ".midS", "Internal reversion midS must be exactly 5000.");
            }

            if (pass.Groups.Count == 0)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".groups", "InternalReversion passes must declare at least one reversion group.");
            }

            if (pass.Metrics.Count != 0 || pass.Rules.Count != 0)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath, "InternalReversion pass contains fields from another pass type.");
            }

            HashSet<TargetPattern> patterns = new HashSet<TargetPattern>();
            for (int i = 0; i < pass.Groups.Count; i++)
            {
                AggregationReversionGroup group = pass.Groups[i];
                if (group == null)
                {
                    throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".groups[" + i + "]", "Reversion groups cannot contain null entries.");
                }

                if (string.IsNullOrEmpty(group.Pattern.ToString()) || !group.Pattern.ToString().StartsWith("internals.", StringComparison.Ordinal))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidTargetPattern, passPath + ".groups[" + i + "].pattern", "Reversion group pattern must target internals.*.");
                }

                if (!patterns.Add(group.Pattern))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".groups[" + i + "].pattern", "Duplicate reversion pattern.");
                }
            }

            HashSet<TargetPath> skipTargets = new HashSet<TargetPath>();
            for (int i = 0; i < pass.SkipTargets.Count; i++)
            {
                if (!pass.SkipTargets[i].IsValid || !IsInternalTarget(pass.SkipTargets[i]))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidTargetReference, passPath + ".skip_targets[" + i + "]", "skip_targets must contain valid internals.* target paths.");
                }

                if (!skipTargets.Add(pass.SkipTargets[i]))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidValue, passPath + ".skip_targets[" + i + "]", "Duplicate skip target.");
                }
            }
        }

        private static void ValidateMetricPassShape(AggregationPass pass, string passPath)
        {
            if (pass.Groups.Count != 0 || pass.Rules.Count != 0 || pass.MidS.HasValue)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath, "MetricAggregation pass contains fields from another pass type.");
            }

            if (pass.LogComponents != true)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".log_components", "MetricAggregation pass must log components.");
            }

            if (pass.WeightsAbsSumPpmRequired != AggregationRuntimePlan.PpmDenominator)
            {
                throw AggregationCompileError(ContentDiagnosticCode.InvalidWeightSum, passPath + ".weights_abs_sum_ppm_required", "weights_abs_sum_ppm_required must be exactly 1_000_000.");
            }

            if (pass.Metrics.Count == 0)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".metrics", "MetricAggregation passes must declare at least one metric.");
            }
        }

        private static void ValidateDerivedShape(AggregationPass pass, string passPath)
        {
            if (pass.Groups.Count != 0 || pass.Metrics.Count != 0 || pass.MidS.HasValue)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath, "DerivedInternals pass contains fields from another pass type.");
            }

            if (pass.Rules.Count == 0)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".rules", "DerivedInternals passes must declare at least one rule.");
            }

            HashSet<TargetPath> targets = new HashSet<TargetPath>();
            for (int i = 0; i < pass.Rules.Count; i++)
            {
                DerivedAggregationRule rule = pass.Rules[i];
                if (rule == null)
                {
                    throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".rules[" + i + "]", "Derived rules cannot contain null entries.");
                }

                if (!rule.Target.IsValid || !IsInternalTarget(rule.Target))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidTargetReference, passPath + ".rules[" + i + "].target", "Derived rule target must be a valid internals.* target.");
                }

                if (!targets.Add(rule.Target))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".rules[" + i + "].target", "Duplicate derived rule target.");
                }

                ValidateEnum(rule.Operation, passPath + ".rules[" + i + "].op", "Unknown derived rule operation.");
                if (rule.Operation != TargetOperation.Set)
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidTargetOperation, passPath + ".rules[" + i + "].op", "Derived aggregation rules only support SET.");
                }

                ValidateExpression(rule.Expression, passPath + ".rules[" + i + "].expr");
            }
        }

        private static void ValidateExpression(AggregationExpression expression, string jsonPath)
        {
            if (expression == null)
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, jsonPath, "Derived expression is required.");
            }

            ValidateEnum(expression.Kind, jsonPath + ".kind", "Unknown aggregation expression kind.");
            if (expression.Kind == AggregationExpressionKind.Copy)
            {
                if (!expression.Target.HasValue || !expression.Target.Value.IsValid || !IsMetricTarget(expression.Target.Value) || expression.Targets.Count != 0)
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidTargetReference, jsonPath, "COPY requires exactly one metric target and zero plural targets.");
                }

                return;
            }

            if (expression.Target.HasValue || expression.Targets.Count == 0)
            {
                throw AggregationCompileError(ContentDiagnosticCode.InvalidValue, jsonPath, "AVG requires no singular target and at least one plural target.");
            }

            HashSet<TargetPath> targets = new HashSet<TargetPath>();
            for (int i = 0; i < expression.Targets.Count; i++)
            {
                if (!expression.Targets[i].IsValid || !IsMetricTarget(expression.Targets[i]))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidTargetReference, jsonPath + ".targets[" + i + "]", "AVG targets must be valid metrics.* target paths.");
                }

                if (!targets.Add(expression.Targets[i]))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidValue, jsonPath + ".targets[" + i + "]", "Duplicate AVG source target.");
                }
            }
        }

        private static void ValidateMetricTargets(
            IReadOnlyList<AggregationMetricRuntime> metrics,
            HashSet<TargetPath> allMetricTargets,
            string passPath)
        {
            for (int i = 0; i < metrics.Count; i++)
            {
                AggregationMetricRuntime metric = metrics[i];
                if (!IsMetricTarget(metric.Metric))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.InvalidTargetReference, passPath + ".metrics[" + i + "].metric", "Metric aggregation target must be metrics.*.");
                }

                if (!allMetricTargets.Add(metric.Metric))
                {
                    throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".metrics[" + i + "].metric", "Duplicate metric target across passes.");
                }

                HashSet<TargetPath> components = new HashSet<TargetPath>();
                for (int j = 0; j < metric.Components.Count; j++)
                {
                    if (!IsInternalTarget(metric.Components[j].Target))
                    {
                        throw AggregationCompileError(ContentDiagnosticCode.InvalidTargetReference, passPath + ".metrics[" + i + "].components[" + j + "].target", "Metric components must be internals.* targets.");
                    }

                    if (!components.Add(metric.Components[j].Target))
                    {
                        throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, passPath + ".metrics[" + i + "].components[" + j + "].target", "Duplicate metric component target.");
                    }
                }
            }
        }

        private static bool ContainsLegitimacy(IReadOnlyList<AggregationMetricRuntime> metrics)
        {
            TargetPath legitimacy = TargetPath.Parse("metrics.legitimacy");
            for (int i = 0; i < metrics.Count; i++)
            {
                if (metrics[i].Metric == legitimacy)
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<AggregationReversionGroupRuntime> MapGroups(IReadOnlyList<AggregationReversionGroup> groups)
        {
            AggregationReversionGroupRuntime[] result = new AggregationReversionGroupRuntime[groups.Count];
            for (int i = 0; i < groups.Count; i++)
            {
                AggregationReversionGroup g = groups[i];
                result[i] = new AggregationReversionGroupRuntime(g.Pattern, g.HalfLifeWeeks, g.AlphaPpm);
            }

            return Array.AsReadOnly(result);
        }

        private static IReadOnlyList<AggregationMetricRuntime> MapMetrics(IReadOnlyList<AggregationMetric> metrics)
        {
            AggregationMetricRuntime[] result = new AggregationMetricRuntime[metrics.Count];
            for (int i = 0; i < metrics.Count; i++)
            {
                AggregationMetric m = metrics[i];
                if (m == null)
                {
                    throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, "$.passes.metrics[" + i + "]", "Metrics cannot contain null entries.");
                }

                WeightedTargetComponentRuntime[] components = new WeightedTargetComponentRuntime[m.Components.Count];
                for (int j = 0; j < m.Components.Count; j++)
                {
                    WeightedTargetComponent c = m.Components[j];
                    if (c == null)
                    {
                        throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, "$.passes.metrics[" + i + "].components[" + j + "]", "Metric components cannot contain null entries.");
                    }

                    components[j] = new WeightedTargetComponentRuntime(c.Target, c.WeightPpm);
                }

                result[i] = new AggregationMetricRuntime(m.Metric, m.HalfLifeWeeks, m.AlphaPpm, m.CapPerWeekS, Array.AsReadOnly(components));
            }

            return Array.AsReadOnly(result);
        }

        private static IReadOnlyList<DerivedAggregationRuleRuntime> MapDerivedRules(IReadOnlyList<DerivedAggregationRule> rules)
        {
            DerivedAggregationRuleRuntime[] result = new DerivedAggregationRuleRuntime[rules.Count];
            for (int i = 0; i < rules.Count; i++)
            {
                DerivedAggregationRule r = rules[i];
                if (r == null)
                {
                    throw AggregationCompileError(ContentDiagnosticCode.AggregationPassFieldConflict, "$.passes.rules[" + i + "]", "Derived rules cannot contain null entries.");
                }

                result[i] = new DerivedAggregationRuleRuntime(r.Target, r.Operation, MapExpression(r.Expression));
            }

            return Array.AsReadOnly(result);
        }

        private static AggregationExpressionRuntime MapExpression(AggregationExpression expression)
        {
            return new AggregationExpressionRuntime(
                MapExpressionKind(expression.Kind),
                expression.Target,
                expression.Targets);
        }

        private static AggregationExpressionKindRuntime MapExpressionKind(AggregationExpressionKind kind)
        {
            switch (kind)
            {
                case AggregationExpressionKind.Avg:
                    return AggregationExpressionKindRuntime.Avg;
                case AggregationExpressionKind.Copy:
                    return AggregationExpressionKindRuntime.Copy;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported aggregation expression kind.");
            }
        }

        private static void ValidatePassPrefix(AggregationPass pass, string passPath)
        {
            string expected;
            if (pass.Type == AggregationPassType.InternalReversion)
            {
                expected = "SYSTEM:REVERSION";
            }
            else if (pass.Type == AggregationPassType.DerivedInternals)
            {
                expected = "SYSTEM:DERIVED";
            }
            else if (pass.Type == AggregationPassType.MetricAggregation)
            {
                expected = "SYSTEM:AGG";
            }
            else
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationInvalidPrefix, passPath + ".cause_prefix", "Unknown aggregation pass type.");
            }

            if (!string.Equals(pass.CausePrefix, expected, StringComparison.Ordinal))
            {
                throw AggregationCompileError(ContentDiagnosticCode.AggregationInvalidPrefix, passPath + ".cause_prefix", "Invalid aggregation cause prefix.");
            }
        }

        private static bool IsMetricTarget(TargetPath target)
        {
            return target.IsValid && string.Equals(target.Namespace, "metrics", StringComparison.Ordinal);
        }

        private static bool IsInternalTarget(TargetPath target)
        {
            return target.IsValid && string.Equals(target.Namespace, "internals", StringComparison.Ordinal);
        }

        private static void ValidateEnum<TEnum>(TEnum value, string jsonPath, string message) where TEnum : struct
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
            {
                throw AggregationCompileError(ContentDiagnosticCode.InvalidEnum, jsonPath, message);
            }
        }

        private static ContentAggregationCompileException AggregationCompileError(ContentDiagnosticCode code, string jsonPath, string message)
        {
            return new ContentAggregationCompileException(code, AggregationConfigPathForDiagnostics, jsonPath, message);
        }

        private const string AggregationConfigPathForDiagnostics = "rules/aggregation_config.json";
    }

    internal sealed class ContentAggregationCompileException : InvalidOperationException
    {
        public ContentAggregationCompileException(ContentDiagnosticCode code, string relativeFile, string jsonPath, string message)
            : base(message)
        {
            Code = code;
            RelativeFile = relativeFile ?? string.Empty;
            JsonPath = jsonPath ?? string.Empty;
        }

        public ContentDiagnosticCode Code { get; }

        public string RelativeFile { get; }

        public string JsonPath { get; }
    }

    internal static class ModelSnapshot
    {
        public static T[] Array<T>(IEnumerable<T> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            return new List<T>(values).ToArray();
        }

        public static T[] ArrayOrEmpty<T>(IEnumerable<T> values)
        {
            return values == null ? System.Array.Empty<T>() : new List<T>(values).ToArray();
        }

        public static ReadOnlyDictionary<string, T> Dictionary<T>(IEnumerable<KeyValuePair<string, T>> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            Dictionary<string, T> result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, T> item in values)
            {
                result.Add(item.Key, item.Value);
            }

            return new ReadOnlyDictionary<string, T>(result);
        }
    }
}
