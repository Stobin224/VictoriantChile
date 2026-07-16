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
            Languages = Array.AsReadOnly(Snapshot(languages, nameof(languages)));
            Files = new ReadOnlyDictionary<string, string>(SnapshotDictionary(files, nameof(files)));
        }

        public string ContentPackId { get; }

        public int ContentPackVersion { get; }

        public int ContentSchemaVersion { get; }

        public int MinGameSchemaVersion { get; }

        public string DefaultLanguage { get; }

        public IReadOnlyList<string> Languages { get; }

        public IReadOnlyDictionary<string, string> Files { get; }

        private static string[] Snapshot(IEnumerable<string> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            return new List<string>(values).ToArray();
        }

        private static Dictionary<string, string> SnapshotDictionary(IEnumerable<KeyValuePair<string, string>> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> item in values)
            {
                result.Add(item.Key, item.Value);
            }

            return result;
        }
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
            Tags = Array.AsReadOnly(Snapshot(tags, nameof(tags)));
        }

        public string Id { get; }

        public string Name { get; }

        public IReadOnlyList<string> Tags { get; }

        private static string[] Snapshot(IEnumerable<string> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            return new List<string>(values).ToArray();
        }
    }

    public sealed class MovementDefinition
    {
        public MovementDefinition(string id, string name, IEnumerable<string> tags)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Tags = Array.AsReadOnly(Snapshot(tags, nameof(tags)));
        }

        public string Id { get; }

        public string Name { get; }

        public IReadOnlyList<string> Tags { get; }

        private static string[] Snapshot(IEnumerable<string> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            return new List<string>(values).ToArray();
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
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));

            TargetConfig[] targetConfigSnapshot = Snapshot(targetConfigs, nameof(targetConfigs));
            RegionDefinition[] regionSnapshot = Snapshot(regions, nameof(regions));
            InterestGroupDefinition[] interestGroupSnapshot = Snapshot(interestGroups, nameof(interestGroups));
            MovementDefinition[] movementSnapshot = Snapshot(movements, nameof(movements));

            TargetConfigs = Array.AsReadOnly(targetConfigSnapshot);
            TargetConfigCatalog = new TargetConfigCatalog(targetConfigSnapshot);
            Regions = Array.AsReadOnly(regionSnapshot);
            InterestGroups = Array.AsReadOnly(interestGroupSnapshot);
            Movements = Array.AsReadOnly(movementSnapshot);
            RegionsById = new ReadOnlyDictionary<string, RegionDefinition>(MapRegionsById(regionSnapshot));
            InterestGroupsById = new ReadOnlyDictionary<string, InterestGroupDefinition>(MapInterestGroupsById(interestGroupSnapshot));
            MovementsById = new ReadOnlyDictionary<string, MovementDefinition>(MapMovementsById(movementSnapshot));
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

        private static T[] Snapshot<T>(IEnumerable<T> values, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            return new List<T>(values).ToArray();
        }

        private static Dictionary<string, RegionDefinition> MapRegionsById(IEnumerable<RegionDefinition> values)
        {
            Dictionary<string, RegionDefinition> result = new Dictionary<string, RegionDefinition>(StringComparer.Ordinal);
            foreach (RegionDefinition value in values)
            {
                result.Add(value.Id, value);
            }

            return result;
        }

        private static Dictionary<string, InterestGroupDefinition> MapInterestGroupsById(IEnumerable<InterestGroupDefinition> values)
        {
            Dictionary<string, InterestGroupDefinition> result = new Dictionary<string, InterestGroupDefinition>(StringComparer.Ordinal);
            foreach (InterestGroupDefinition value in values)
            {
                result.Add(value.Id, value);
            }

            return result;
        }

        private static Dictionary<string, MovementDefinition> MapMovementsById(IEnumerable<MovementDefinition> values)
        {
            Dictionary<string, MovementDefinition> result = new Dictionary<string, MovementDefinition>(StringComparer.Ordinal);
            foreach (MovementDefinition value in values)
            {
                result.Add(value.Id, value);
            }

            return result;
        }
    }
}
