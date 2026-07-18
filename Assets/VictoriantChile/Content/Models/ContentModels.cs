using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public sealed class ContentPack
    {
        public ContentPack(
            ContentManifest manifest,
            IEnumerable<TargetConfig> targetConfigs,
            IEnumerable<RegionDefinition> regions,
            IEnumerable<InterestGroupDefinition> interestGroups,
            IEnumerable<MovementDefinition> movements)
            : this(manifest, targetConfigs, regions, interestGroups, movements, null, null, null, null)
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
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));

            TargetConfig[] targetConfigSnapshot = ModelSnapshot.Array(targetConfigs, nameof(targetConfigs));
            RegionDefinition[] regionSnapshot = ModelSnapshot.Array(regions, nameof(regions));
            InterestGroupDefinition[] interestGroupSnapshot = ModelSnapshot.Array(interestGroups, nameof(interestGroups));
            MovementDefinition[] movementSnapshot = ModelSnapshot.Array(movements, nameof(movements));
            EffectTemplate[] effectSnapshot = ModelSnapshot.ArrayOrEmpty(effects);

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
            LegislativeConfig = legislativeConfig;
            Effects = Array.AsReadOnly(effectSnapshot);
            EffectsById = ModelSnapshot.Dictionary(MapEffectsById(effectSnapshot), nameof(effects));
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
