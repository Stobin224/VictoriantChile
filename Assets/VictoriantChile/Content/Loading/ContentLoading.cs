using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VictoriantChile.Content.Diagnostics;
using VictoriantChile.Content.Models;
using VictoriantChile.Simulation.Core.Aggregation;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Content.Loading
{
    public sealed class ContentFileReadResult
    {
        private readonly byte[] _bytes;

        private ContentFileReadResult(bool success, bool notFound, byte[] bytes, string errorMessage)
        {
            Success = success;
            NotFound = notFound;
            _bytes = bytes == null ? null : (byte[])bytes.Clone();
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Success { get; }

        public bool NotFound { get; }

        public string ErrorMessage { get; }

        public byte[] GetBytesCopy()
        {
            return _bytes == null ? null : (byte[])_bytes.Clone();
        }

        public static ContentFileReadResult FromBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            return new ContentFileReadResult(true, false, bytes, string.Empty);
        }

        public static ContentFileReadResult Missing(string message)
        {
            return new ContentFileReadResult(false, true, null, message);
        }

        public static ContentFileReadResult Failed(string message)
        {
            return new ContentFileReadResult(false, false, null, message);
        }
    }

    public interface IContentFileSource
    {
        ContentFileReadResult TryReadAllBytes(string relativePath);
    }

    public sealed class DirectoryContentFileSource : IContentFileSource
    {
        private readonly string _rootFullPath;

        public DirectoryContentFileSource(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory))
            {
                throw new ArgumentException("Content root must be a non-empty path.", nameof(rootDirectory));
            }

            _rootFullPath = Path.GetFullPath(rootDirectory);
            if (!Directory.Exists(_rootFullPath))
            {
                throw new DirectoryNotFoundException($"Content root does not exist: {_rootFullPath}");
            }
        }

        public ContentFileReadResult TryReadAllBytes(string relativePath)
        {
            if (!ContentPathRules.IsSafeRelativeJsonPath(relativePath, allowManifest: true))
            {
                return ContentFileReadResult.Failed("Path is not a safe normalized relative JSON path.");
            }

            try
            {
                string candidate = Path.GetFullPath(Path.Combine(_rootFullPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                string rootWithSeparator = EnsureTrailingSeparator(_rootFullPath);
                if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) && !string.Equals(candidate, _rootFullPath, StringComparison.Ordinal))
                {
                    return ContentFileReadResult.Failed("Path escapes the content root.");
                }

                if (!File.Exists(candidate))
                {
                    return ContentFileReadResult.Missing("File does not exist.");
                }

                return ContentFileReadResult.FromBytes(File.ReadAllBytes(candidate));
            }
            catch (IOException ex)
            {
                return ContentFileReadResult.Failed($"I/O read failed: {ex.GetType().Name}.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return ContentFileReadResult.Failed($"Read access denied: {ex.GetType().Name}.");
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }
    }

    public static class ContentHash
    {
        public static string ComputeCanonicalSha256(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            byte[] normalized = NormalizeLineEndings(bytes);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(normalized);
                StringBuilder builder = new StringBuilder("sha256:".Length + digest.Length * 2);
                builder.Append("sha256:");
                for (int i = 0; i < digest.Length; i++)
                {
                    builder.Append(digest[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static byte[] NormalizeLineEndings(byte[] bytes)
        {
            List<byte> output = new List<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                byte current = bytes[i];
                if (current == 13)
                {
                    if (i + 1 < bytes.Length && bytes[i + 1] == 10)
                    {
                        i++;
                    }

                    output.Add(10);
                    continue;
                }

                output.Add(current);
            }

            return output.ToArray();
        }
    }

    public sealed class ContentLoadResult
    {
        public ContentLoadResult(ContentPack pack, IEnumerable<ContentDiagnostic> diagnostics)
        {
            Pack = pack;
            Diagnostics = Array.AsReadOnly(Snapshot(diagnostics));
            IsSuccess = Pack != null && !HasError(Diagnostics);
        }

        public bool IsSuccess { get; }

        public ContentPack Pack { get; }

        public IReadOnlyList<ContentDiagnostic> Diagnostics { get; }

        private static ContentDiagnostic[] Snapshot(IEnumerable<ContentDiagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            return new List<ContentDiagnostic>(diagnostics).ToArray();
        }

        private static bool HasError(IEnumerable<ContentDiagnostic> diagnostics)
        {
            foreach (ContentDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == ContentDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class ContentPackLoader
    {
        private const string ManifestPath = "manifest.json";
        private const string RegionsPath = "core/regions.json";
        private const string InterestGroupsPath = "core/igs.json";
        private const string MovementsPath = "core/movements.json";
        private const string TargetConfigPath = "rules/target_config.json";
        private const string AggregationConfigPath = "rules/aggregation_config.json";
        private const string LegislativeConfigPath = "rules/legislative_config.json";
        private const string EffectsPath = "templates/effects.json";
        private const string EventsPath = "templates/events.json";
        private const string ReformsPath = "templates/reforms.json";
        private const int DefaultStaticRegionS = 5000;

        private readonly List<ContentDiagnostic> _diagnostics = new List<ContentDiagnostic>();

        public ContentLoadResult Load(IContentFileSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            _diagnostics.Clear();
            Dictionary<string, byte[]> verifiedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            byte[] manifestBytes = ReadRequired(source, ManifestPath, ContentDiagnosticCode.ManifestMissing);
            JObject manifestRoot = ParseObject(ManifestPath, manifestBytes);
            ContentManifest manifest = manifestRoot == null ? null : LoadManifest(source, manifestRoot, verifiedFiles);

            if (manifest != null)
            {
                VerifyRequiredManifestEntry(manifest, RegionsPath);
                VerifyRequiredManifestEntry(manifest, InterestGroupsPath);
                VerifyRequiredManifestEntry(manifest, MovementsPath);
                VerifyRequiredManifestEntry(manifest, TargetConfigPath);
                VerifyRequiredManifestEntry(manifest, AggregationConfigPath);
                VerifyRequiredManifestEntry(manifest, LegislativeConfigPath);
                VerifyRequiredManifestEntry(manifest, EffectsPath);
                VerifyRequiredManifestEntry(manifest, EventsPath);
                VerifyRequiredManifestEntry(manifest, ReformsPath);
                VerifyRequiredManifestEntry(manifest, $"strings/{manifest.DefaultLanguage}.json");
            }

            List<TargetConfig> targetConfigs = null;
            List<RegionDefinition> regions = null;
            List<InterestGroupDefinition> interestGroups = null;
            List<MovementDefinition> movements = null;
            ContentLocalizationTable localization = null;
            AggregationConfig aggregationConfig = null;
            AggregationRuntimePlan aggregationRuntimePlan = null;
            LegislativeConfig legislativeConfig = null;
            List<EffectTemplate> effects = null;
            List<EventTemplate> events = null;
            List<ReformTemplate> reforms = null;
            TargetConfigCatalog targetCatalog = null;

            if (manifest != null && verifiedFiles.TryGetValue(TargetConfigPath, out byte[] targetConfigBytes))
            {
                targetConfigs = LoadTargetConfigs(ParseArray(TargetConfigPath, targetConfigBytes));
                if (!HasErrors())
                {
                    targetCatalog = new TargetConfigCatalog(targetConfigs);
                }
            }

            if (manifest != null && verifiedFiles.TryGetValue($"strings/{manifest.DefaultLanguage}.json", out byte[] localizationBytes))
            {
                localization = LoadLocalization(manifest.DefaultLanguage, ParseObject($"strings/{manifest.DefaultLanguage}.json", localizationBytes));
            }

            if (manifest != null && verifiedFiles.TryGetValue(RegionsPath, out byte[] regionBytes))
            {
                regions = LoadRegions(ParseObject(RegionsPath, regionBytes));
            }

            if (manifest != null && verifiedFiles.TryGetValue(InterestGroupsPath, out byte[] interestGroupBytes))
            {
                interestGroups = LoadInterestGroups(ParseObject(InterestGroupsPath, interestGroupBytes));
            }

            if (manifest != null && verifiedFiles.TryGetValue(MovementsPath, out byte[] movementBytes))
            {
                movements = LoadMovements(ParseObject(MovementsPath, movementBytes));
            }

            if (targetCatalog != null && verifiedFiles.TryGetValue(AggregationConfigPath, out byte[] aggregationBytes))
            {
                aggregationConfig = LoadAggregationConfig(ParseObject(AggregationConfigPath, aggregationBytes), targetCatalog);
                if (aggregationConfig != null && !HasErrors())
                {
                    aggregationRuntimePlan = CompileAggregationRuntimePlanForLoad(aggregationConfig);
                }
            }

            if (targetCatalog != null && verifiedFiles.TryGetValue(LegislativeConfigPath, out byte[] legislativeBytes))
            {
                legislativeConfig = LoadLegislativeConfig(ParseObject(LegislativeConfigPath, legislativeBytes), targetCatalog);
            }

            if (targetCatalog != null && verifiedFiles.TryGetValue(EffectsPath, out byte[] effectBytes))
            {
                effects = LoadEffects(ParseObject(EffectsPath, effectBytes), targetCatalog, localization);
            }

            if (targetCatalog != null
                && localization != null
                && movements != null
                && effects != null
                && verifiedFiles.TryGetValue(EventsPath, out byte[] eventBytes))
            {
                events = LoadEvents(ParseObject(EventsPath, eventBytes), targetCatalog, localization, movements, effects);
            }

            if (targetCatalog != null
                && localization != null
                && interestGroups != null
                && movements != null
                && legislativeConfig != null
                && effects != null
                && verifiedFiles.TryGetValue(ReformsPath, out byte[] reformBytes))
            {
                reforms = LoadReforms(ParseObject(ReformsPath, reformBytes), targetCatalog, localization, interestGroups, movements, legislativeConfig, effects);
            }

            if (HasErrors())
            {
                return new ContentLoadResult(null, _diagnostics);
            }

            ContentPack pack = new ContentPack(manifest, targetConfigs, regions, interestGroups, movements, localization, aggregationConfig, aggregationRuntimePlan, legislativeConfig, effects, events, reforms);
            return new ContentLoadResult(pack, _diagnostics);
        }

        private AggregationRuntimePlan CompileAggregationRuntimePlanForLoad(AggregationConfig aggregationConfig)
        {
            try
            {
                return ContentPack.CompileAggregationRuntimePlan(aggregationConfig);
            }
            catch (ContentAggregationCompileException exception)
            {
                Add(exception.Code, exception.RelativeFile, exception.JsonPath, exception.Message);
            }
            catch (ArgumentException)
            {
                Add(ContentDiagnosticCode.AggregationPassFieldConflict, AggregationConfigPath, "$", "Aggregation runtime plan validation failed.");
            }
            catch (KeyNotFoundException)
            {
                Add(ContentDiagnosticCode.AggregationPassFieldConflict, AggregationConfigPath, "$", "Aggregation runtime plan validation failed.");
            }
            catch (InvalidOperationException)
            {
                Add(ContentDiagnosticCode.AggregationPassFieldConflict, AggregationConfigPath, "$", "Aggregation runtime plan validation failed.");
            }

            return null;
        }

        private ContentManifest LoadManifest(IContentFileSource source, JObject root, Dictionary<string, byte[]> verifiedFiles)
        {
            ValidateUnknownProperties(ManifestPath, root, "$", new[]
            {
                "content_pack_id",
                "content_pack_version",
                "content_schema_version",
                "default_language",
                "files",
                "languages",
                "min_game_schema_version"
            });

            string packId = RequiredString(ManifestPath, root, "content_pack_id", "$.content_pack_id", nonEmpty: true);
            int? packVersion = RequiredInt(ManifestPath, root, "content_pack_version", "$.content_pack_version");
            int? schemaVersion = RequiredInt(ManifestPath, root, "content_schema_version", "$.content_schema_version");
            int? minGameSchema = RequiredInt(ManifestPath, root, "min_game_schema_version", "$.min_game_schema_version");
            string defaultLanguage = RequiredString(ManifestPath, root, "default_language", "$.default_language", nonEmpty: true);
            JArray languageArray = RequiredArray(ManifestPath, root, "languages", "$.languages");
            JObject filesObject = RequiredObject(ManifestPath, root, "files", "$.files");

            if (packId != null && !IsAsciiLowerSnake(packId))
            {
                Add(ContentDiagnosticCode.InvalidId, ManifestPath, "$.content_pack_id", "Content pack id must be ASCII lowercase snake_case.");
            }

            ValidatePositiveVersion(packVersion, ContentDiagnosticCode.InvalidValue, "$.content_pack_version", "content_pack_version");
            ValidatePositiveVersion(schemaVersion, ContentDiagnosticCode.UnsupportedContentSchemaVersion, "$.content_schema_version", "content_schema_version");
            ValidatePositiveVersion(minGameSchema, ContentDiagnosticCode.IncompatibleGameSchemaVersion, "$.min_game_schema_version", "min_game_schema_version");

            if (schemaVersion.HasValue && schemaVersion.Value != ContentCompatibility.SupportedContentSchemaVersion)
            {
                Add(ContentDiagnosticCode.UnsupportedContentSchemaVersion, ManifestPath, "$.content_schema_version", $"Unsupported content schema version {schemaVersion.Value}; expected {ContentCompatibility.SupportedContentSchemaVersion}.");
            }

            if (minGameSchema.HasValue && minGameSchema.Value > ContentCompatibility.CurrentGameSchemaVersion)
            {
                Add(ContentDiagnosticCode.IncompatibleGameSchemaVersion, ManifestPath, "$.min_game_schema_version", $"Minimum game schema version {minGameSchema.Value} is greater than supported {ContentCompatibility.CurrentGameSchemaVersion}.");
            }

            List<string> languages = LoadStringArray(ManifestPath, languageArray, "$.languages", minCount: 1, unique: true);
            if (defaultLanguage != null && languages.Count > 0 && !Contains(languages, defaultLanguage))
            {
                Add(ContentDiagnosticCode.InvalidValue, ManifestPath, "$.default_language", "Default language must be present in languages.");
            }

            List<KeyValuePair<string, string>> files = LoadManifestFiles(filesObject);
            foreach (KeyValuePair<string, string> file in Sorted(files))
            {
                ContentFileReadResult read = source.TryReadAllBytes(file.Key);
                if (!read.Success)
                {
                    Add(read.NotFound ? ContentDiagnosticCode.MissingDeclaredFile : ContentDiagnosticCode.SourceReadFailed, file.Key, string.Empty, read.ErrorMessage);
                    continue;
                }

                byte[] bytes = read.GetBytesCopy();
                string actualHash = ContentHash.ComputeCanonicalSha256(bytes);
                if (!string.Equals(actualHash, file.Value, StringComparison.Ordinal))
                {
                    Add(ContentDiagnosticCode.HashMismatch, ManifestPath, $"$.files[\"{file.Key}\"]", $"Hash mismatch for {file.Key}: expected {file.Value}, got {actualHash}.");
                    continue;
                }

                verifiedFiles[file.Key] = bytes;
            }

            if (HasErrors())
            {
                return null;
            }

            return new ContentManifest(packId, packVersion.Value, schemaVersion.Value, minGameSchema.Value, defaultLanguage, languages, files);
        }

        private List<KeyValuePair<string, string>> LoadManifestFiles(JObject filesObject)
        {
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            if (filesObject == null)
            {
                return result;
            }

            if (!filesObject.HasValues)
            {
                Add(ContentDiagnosticCode.InvalidValue, ManifestPath, "$.files", "Manifest files must not be empty.");
            }

            foreach (JProperty property in filesObject.Properties())
            {
                string path = property.Name;
                string jsonPath = $"$.files[\"{path}\"]";
                if (!ContentPathRules.IsSafeRelativeJsonPath(path, allowManifest: false))
                {
                    Add(ContentDiagnosticCode.UnsafeManifestPath, ManifestPath, jsonPath, "Manifest path must be a safe relative .json path using forward slashes.");
                }

                if (property.Value.Type != JTokenType.String)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, ManifestPath, jsonPath, "Manifest file hash must be a string.");
                    continue;
                }

                string hash = property.Value.Value<string>();
                if (!IsSha256Hash(hash))
                {
                    Add(ContentDiagnosticCode.InvalidHashFormat, ManifestPath, jsonPath, "Hash must match sha256:<64 lowercase hex>.");
                }

                result.Add(new KeyValuePair<string, string>(path, hash));
            }

            return result;
        }

        private List<TargetConfig> LoadTargetConfigs(JArray root)
        {
            List<TargetConfig> result = new List<TargetConfig>();
            if (root == null)
            {
                return result;
            }

            if (root.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, TargetConfigPath, "$", "Target config root array must not be empty.");
                return result;
            }

            Dictionary<string, string> patternLocations = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < root.Count; i++)
            {
                string rowPath = "$[" + i + "]";
                JObject row = root[i] as JObject;
                if (row == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, TargetConfigPath, rowPath, "Target config row must be an object.");
                    continue;
                }

                ValidateUnknownProperties(TargetConfigPath, row, rowPath, new[] { "pattern", "scale", "minS", "maxS", "defaultS", "allow_ops", "normalize_group", "ui", "qual" });
                string patternText = RequiredString(TargetConfigPath, row, "pattern", rowPath + ".pattern", nonEmpty: true);
                int? scale = RequiredInt(TargetConfigPath, row, "scale", rowPath + ".scale");
                int? minS = RequiredInt(TargetConfigPath, row, "minS", rowPath + ".minS");
                int? maxS = RequiredInt(TargetConfigPath, row, "maxS", rowPath + ".maxS");
                int? defaultS = RequiredInt(TargetConfigPath, row, "defaultS", rowPath + ".defaultS");
                JArray opsArray = RequiredArray(TargetConfigPath, row, "allow_ops", rowPath + ".allow_ops");
                string normalizeGroup = OptionalNullableString(TargetConfigPath, row, "normalize_group", rowPath + ".normalize_group");
                ValidateUi(row, rowPath);
                ValidateQual(row, rowPath);

                if (patternText == null || !TargetPattern.TryParse(patternText, out TargetPattern pattern))
                {
                    Add(ContentDiagnosticCode.InvalidTargetPattern, TargetConfigPath, rowPath + ".pattern", "Target pattern is invalid.");
                    continue;
                }

                if (IsStaticRegionalPattern(patternText))
                {
                    Add(ContentDiagnosticCode.InvalidTargetPattern, TargetConfigPath, rowPath + ".pattern", "Static regional resources are read-only and cannot be TargetConfig entries.");
                    continue;
                }

                if (patternLocations.TryGetValue(patternText, out string firstLocation))
                {
                    Add(ContentDiagnosticCode.DuplicateTargetPattern, TargetConfigPath, rowPath + ".pattern", $"Duplicate target pattern {patternText}; first declared at {firstLocation}.");
                    continue;
                }

                patternLocations.Add(patternText, rowPath + ".pattern");

                if (!scale.HasValue || !minS.HasValue || !maxS.HasValue || !defaultS.HasValue)
                {
                    continue;
                }

                if (scale.Value <= 0)
                {
                    Add(ContentDiagnosticCode.InvalidRange, TargetConfigPath, rowPath + ".scale", "Scale must be positive.");
                }

                if (minS.Value > maxS.Value)
                {
                    Add(ContentDiagnosticCode.InvalidRange, TargetConfigPath, rowPath + ".minS", "minS must be less than or equal to maxS.");
                }

                if (defaultS.Value < minS.Value || defaultS.Value > maxS.Value)
                {
                    Add(ContentDiagnosticCode.InvalidRange, TargetConfigPath, rowPath + ".defaultS", "defaultS must be inside [minS, maxS].");
                }

                if (normalizeGroup != null)
                {
                    if (!IsDottedLowercase(normalizeGroup) || normalizeGroup != "igs.clout_sum_100")
                    {
                        Add(ContentDiagnosticCode.InvalidValue, TargetConfigPath, rowPath + ".normalize_group", "Schema 1 only supports normalize_group igs.clout_sum_100.");
                    }
                }

                List<TargetOperation> operations = LoadOperations(opsArray, rowPath + ".allow_ops");
                if (HasErrorsForRow(rowPath))
                {
                    continue;
                }

                try
                {
                    result.Add(new TargetConfig(pattern, scale.Value, minS.Value, maxS.Value, defaultS.Value, operations, normalizeGroup));
                }
                catch (ArgumentException ex)
                {
                    Add(ContentDiagnosticCode.InvalidValue, TargetConfigPath, rowPath, ex.Message);
                }
            }

            return result;
        }

        private List<TargetOperation> LoadOperations(JArray opsArray, string jsonPath)
        {
            List<TargetOperation> result = new List<TargetOperation>();
            if (opsArray == null)
            {
                return result;
            }

            if (opsArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidTargetOperation, TargetConfigPath, jsonPath, "allow_ops must not be empty.");
            }

            HashSet<TargetOperation> seen = new HashSet<TargetOperation>();
            for (int i = 0; i < opsArray.Count; i++)
            {
                string itemPath = jsonPath + "[" + i + "]";
                if (opsArray[i].Type != JTokenType.String)
                {
                    Add(ContentDiagnosticCode.InvalidTargetOperation, TargetConfigPath, itemPath, "Operation must be a string.");
                    continue;
                }

                string text = opsArray[i].Value<string>();
                if (!TryMapOperation(text, out TargetOperation operation))
                {
                    Add(ContentDiagnosticCode.InvalidTargetOperation, TargetConfigPath, itemPath, "Operation must be ADD, MUL, or SET.");
                    continue;
                }

                if (!seen.Add(operation))
                {
                    Add(ContentDiagnosticCode.InvalidTargetOperation, TargetConfigPath, itemPath, "Duplicate operation is not allowed.");
                    continue;
                }

                result.Add(operation);
            }

            return result;
        }

        private List<RegionDefinition> LoadRegions(JObject root)
        {
            List<RegionDefinition> result = new List<RegionDefinition>();
            if (root == null)
            {
                return result;
            }

            ValidateUnknownProperties(RegionsPath, root, "$", new[] { "regions" });
            JArray array = RequiredArray(RegionsPath, root, "regions", "$.regions");
            if (array == null)
            {
                return result;
            }

            if (array.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, RegionsPath, "$.regions", "regions must not be empty.");
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            long weightTotal = 0;
            for (int i = 0; i < array.Count; i++)
            {
                string rowPath = "$.regions[" + i + "]";
                JObject row = array[i] as JObject;
                if (row == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, RegionsPath, rowPath, "Region row must be an object.");
                    continue;
                }

                ValidateUnknownProperties(RegionsPath, row, rowPath, new[] { "id", "name", "weight_ppm", "macrozone", "admin_capS", "industry_capS", "extractive_capS", "social_capS", "populationS" });
                string id = RequiredString(RegionsPath, row, "id", rowPath + ".id", nonEmpty: true);
                string name = RequiredString(RegionsPath, row, "name", rowPath + ".name", nonEmpty: true);
                int? weightPpm = RequiredInt(RegionsPath, row, "weight_ppm", rowPath + ".weight_ppm");
                string macrozoneText = RequiredString(RegionsPath, row, "macrozone", rowPath + ".macrozone", nonEmpty: true);
                int adminCapS = OptionalIntInRange(row, "admin_capS", rowPath + ".admin_capS", 0, 10000, DefaultStaticRegionS, RegionsPath);
                int industryCapS = OptionalIntInRange(row, "industry_capS", rowPath + ".industry_capS", 0, 10000, DefaultStaticRegionS, RegionsPath);
                int extractiveCapS = OptionalIntInRange(row, "extractive_capS", rowPath + ".extractive_capS", 0, 10000, DefaultStaticRegionS, RegionsPath);
                int socialCapS = OptionalIntInRange(row, "social_capS", rowPath + ".social_capS", 0, 10000, DefaultStaticRegionS, RegionsPath);
                int populationS = OptionalIntInRange(row, "populationS", rowPath + ".populationS", 0, 10000, DefaultStaticRegionS, RegionsPath);

                if (id != null)
                {
                    if (!IsAsciiLowerSnake(id))
                    {
                        Add(ContentDiagnosticCode.InvalidId, RegionsPath, rowPath + ".id", "Region id must be ASCII lowercase snake_case.");
                    }
                    else if (!ids.Add(id))
                    {
                        Add(ContentDiagnosticCode.DuplicateId, RegionsPath, rowPath + ".id", $"Duplicate region id {id}.");
                    }
                }

                if (weightPpm.HasValue)
                {
                    if (weightPpm.Value < 0)
                    {
                        Add(ContentDiagnosticCode.InvalidValue, RegionsPath, rowPath + ".weight_ppm", "weight_ppm must be non-negative.");
                    }
                    else
                    {
                        weightTotal += weightPpm.Value;
                    }
                }

                if (!TryMapMacrozone(macrozoneText, out RegionMacrozone macrozone))
                {
                    Add(ContentDiagnosticCode.InvalidMacrozone, RegionsPath, rowPath + ".macrozone", "Macrozone must be NORTH, CENTER, SOUTH, or AUSTRAL.");
                }

                if (HasErrorsForRow(rowPath) || id == null || name == null || !weightPpm.HasValue || !TryMapMacrozone(macrozoneText, out macrozone))
                {
                    continue;
                }

                result.Add(new RegionDefinition(id, name, weightPpm.Value, macrozone, adminCapS, industryCapS, extractiveCapS, socialCapS, populationS));
            }

            if (weightTotal != 1000000)
            {
                Add(ContentDiagnosticCode.RegionWeightTotalMismatch, RegionsPath, "$.regions", $"Region weight_ppm total must be 1000000, got {weightTotal}.");
            }

            return result;
        }

        private List<InterestGroupDefinition> LoadInterestGroups(JObject root)
        {
            List<InterestGroupDefinition> result = new List<InterestGroupDefinition>();
            if (root == null)
            {
                return result;
            }

            ValidateUnknownProperties(InterestGroupsPath, root, "$", new[] { "igs" });
            JArray array = RequiredArray(InterestGroupsPath, root, "igs", "$.igs");
            if (array == null)
            {
                return result;
            }

            LoadTaggedDefinitions(array, InterestGroupsPath, "$.igs", "ig_", 2, 4, (id, name, tags) => result.Add(new InterestGroupDefinition(id, name, tags)));
            return result;
        }

        private List<MovementDefinition> LoadMovements(JObject root)
        {
            List<MovementDefinition> result = new List<MovementDefinition>();
            if (root == null)
            {
                return result;
            }

            ValidateUnknownProperties(MovementsPath, root, "$", new[] { "movements" });
            JArray array = RequiredArray(MovementsPath, root, "movements", "$.movements");
            if (array == null)
            {
                return result;
            }

            LoadTaggedDefinitions(array, MovementsPath, "$.movements", "mov_", 1, int.MaxValue, (id, name, tags) => result.Add(new MovementDefinition(id, name, tags)));
            return result;
        }

        private void LoadTaggedDefinitions(JArray array, string file, string rootPath, string idPrefix, int minTags, int maxTags, Action<string, string, List<string>> add)
        {
            if (array.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, file, rootPath, "Definition array must not be empty.");
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                string rowPath = rootPath + "[" + i + "]";
                JObject row = array[i] as JObject;
                if (row == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, file, rowPath, "Definition row must be an object.");
                    continue;
                }

                ValidateUnknownProperties(file, row, rowPath, new[] { "id", "name", "tags" });
                string id = RequiredString(file, row, "id", rowPath + ".id", nonEmpty: true);
                string name = RequiredString(file, row, "name", rowPath + ".name", nonEmpty: true);
                JArray tagArray = RequiredArray(file, row, "tags", rowPath + ".tags");
                List<string> tags = LoadStringArray(file, tagArray, rowPath + ".tags", minTags, unique: true);

                if (tags.Count > maxTags)
                {
                    Add(ContentDiagnosticCode.InvalidValue, file, rowPath + ".tags", $"tags must contain at most {maxTags} entries.");
                }

                if (id != null)
                {
                    if (!IsAsciiLowerSnake(id) || !id.StartsWith(idPrefix, StringComparison.Ordinal))
                    {
                        Add(ContentDiagnosticCode.InvalidId, file, rowPath + ".id", $"Id must be ASCII lowercase snake_case with prefix {idPrefix}.");
                    }
                    else if (!ids.Add(id))
                    {
                        Add(ContentDiagnosticCode.DuplicateId, file, rowPath + ".id", $"Duplicate id {id}.");
                    }
                }

                for (int tagIndex = 0; tagIndex < tags.Count; tagIndex++)
                {
                    if (!IsTwoSegmentDottedLowercase(tags[tagIndex]))
                    {
                        Add(ContentDiagnosticCode.InvalidValue, file, rowPath + ".tags[" + tagIndex + "]", "Tag must use ASCII lowercase dotted format namespace.value.");
                    }
                }

                if (HasErrorsForRow(rowPath) || id == null || name == null)
                {
                    continue;
                }

                add(id, name, tags);
            }
        }

        private ContentLocalizationTable LoadLocalization(string language, JObject root)
        {
            string file = "strings/" + language + ".json";
            if (root == null)
            {
                return null;
            }

            List<KeyValuePair<string, string>> ordered = new List<KeyValuePair<string, string>>();
            foreach (JProperty property in root.Properties())
            {
                string jsonPath = "$[\"" + property.Name + "\"]";
                if (string.IsNullOrEmpty(property.Name))
                {
                    Add(ContentDiagnosticCode.InvalidValue, file, jsonPath, "Localization key must not be empty.");
                    continue;
                }

                if (property.Value.Type != JTokenType.String)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Localization value must be a string.");
                    continue;
                }

                ordered.Add(new KeyValuePair<string, string>(property.Name, property.Value.Value<string>()));
            }

            ordered.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
            return HasErrors() ? null : new ContentLocalizationTable(language, ordered);
        }

        private AggregationConfig LoadAggregationConfig(JObject root, TargetConfigCatalog catalog)
        {
            if (root == null)
            {
                return null;
            }

            ValidateUnknownProperties(AggregationConfigPath, root, "$", new[] { "schema_version", "scale", "midS", "rounding", "passes" });
            int? schemaVersion = RequiredInt(AggregationConfigPath, root, "schema_version", "$.schema_version");
            int? scale = RequiredInt(AggregationConfigPath, root, "scale", "$.scale");
            int? midS = RequiredInt(AggregationConfigPath, root, "midS", "$.midS");
            string roundingText = RequiredString(AggregationConfigPath, root, "rounding", "$.rounding", nonEmpty: true);
            JArray passesArray = RequiredArray(AggregationConfigPath, root, "passes", "$.passes");

            if (schemaVersion.HasValue && schemaVersion.Value != 1)
            {
                Add(ContentDiagnosticCode.UnsupportedSchemaVersion, AggregationConfigPath, "$.schema_version", "Unsupported schema version " + schemaVersion.Value + "; expected 1.");
            }

            if (scale.HasValue && scale.Value <= 0)
            {
                Add(ContentDiagnosticCode.InvalidRange, AggregationConfigPath, "$.scale", "scale must be positive.");
            }

            ContentRoundingMode rounding;
            if (!TryMapRounding(roundingText, out rounding))
            {
                Add(ContentDiagnosticCode.InvalidEnum, AggregationConfigPath, "$.rounding", "rounding must be HALF_AWAY_FROM_ZERO.");
            }

            if (passesArray != null && passesArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, AggregationConfigPath, "$.passes", "passes must not be empty.");
            }

            List<AggregationPass> passes = new List<AggregationPass>();
            for (int i = 0; passesArray != null && i < passesArray.Count; i++)
            {
                string passPath = "$.passes[" + i + "]";
                JObject passObject = passesArray[i] as JObject;
                if (passObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, AggregationConfigPath, passPath, "Pass must be an object.");
                    continue;
                }

                string typeText = RequiredString(AggregationConfigPath, passObject, "type", passPath + ".type", nonEmpty: true);
                string causePrefix = RequiredString(AggregationConfigPath, passObject, "cause_prefix", passPath + ".cause_prefix", nonEmpty: true);
                AggregationPassType passType;
                if (!TryMapAggregationPassType(typeText, out passType))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, AggregationConfigPath, passPath + ".type", "Unknown aggregation pass type.");
                    ValidateUnknownProperties(AggregationConfigPath, passObject, passPath, new[] { "type", "cause_prefix", "midS", "groups", "skip_targets", "log_components", "weights_abs_sum_ppm_required", "metrics", "rules" });
                    continue;
                }

                if (passType == AggregationPassType.InternalReversion)
                {
                    passes.Add(LoadInternalReversionPass(passObject, passPath, causePrefix, catalog));
                }
                else if (passType == AggregationPassType.MetricAggregation)
                {
                    passes.Add(LoadMetricAggregationPass(passObject, passPath, causePrefix, catalog));
                }
                else
                {
                    passes.Add(LoadDerivedInternalsPass(passObject, passPath, causePrefix, catalog));
                }
            }

            if (HasErrors())
            {
                return null;
            }

            return new AggregationConfig(schemaVersion.Value, scale.Value, midS.Value, rounding, passes);
        }

        private AggregationPass LoadInternalReversionPass(JObject passObject, string passPath, string causePrefix, TargetConfigCatalog catalog)
        {
            ValidateUnknownProperties(AggregationConfigPath, passObject, passPath, new[] { "type", "cause_prefix", "midS", "groups", "skip_targets" });
            int? midS = RequiredInt(AggregationConfigPath, passObject, "midS", passPath + ".midS");
            JArray groupsArray = RequiredArray(AggregationConfigPath, passObject, "groups", passPath + ".groups");
            JArray skipTargetsArray = RequiredArray(AggregationConfigPath, passObject, "skip_targets", passPath + ".skip_targets");

            List<AggregationReversionGroup> groups = new List<AggregationReversionGroup>();
            for (int i = 0; groupsArray != null && i < groupsArray.Count; i++)
            {
                string groupPath = passPath + ".groups[" + i + "]";
                JObject groupObject = groupsArray[i] as JObject;
                if (groupObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, AggregationConfigPath, groupPath, "Reversion group must be an object.");
                    continue;
                }

                ValidateUnknownProperties(AggregationConfigPath, groupObject, groupPath, new[] { "pattern", "half_life_weeks", "alpha_ppm" });
                TargetPattern? pattern = RequiredTargetPattern(AggregationConfigPath, groupObject, "pattern", groupPath + ".pattern", catalog);
                int? halfLifeWeeks = RequiredInt(AggregationConfigPath, groupObject, "half_life_weeks", groupPath + ".half_life_weeks");
                int? alphaPpm = RequiredInt(AggregationConfigPath, groupObject, "alpha_ppm", groupPath + ".alpha_ppm");
                ValidatePositive(AggregationConfigPath, groupPath + ".half_life_weeks", halfLifeWeeks, "half_life_weeks");
                ValidatePpm(AggregationConfigPath, groupPath + ".alpha_ppm", alphaPpm, "alpha_ppm");
                if (HasErrorsForRow(groupPath) || !pattern.HasValue || !halfLifeWeeks.HasValue || !alphaPpm.HasValue)
                {
                    continue;
                }

                groups.Add(new AggregationReversionGroup(pattern.Value, halfLifeWeeks.Value, alphaPpm.Value));
            }

            List<TargetPath> skipTargets = new List<TargetPath>();
            HashSet<string> seenSkipTargets = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; skipTargetsArray != null && i < skipTargetsArray.Count; i++)
            {
                string targetPath = passPath + ".skip_targets[" + i + "]";
                TargetPath? target = ReadTargetPathToken(AggregationConfigPath, skipTargetsArray[i], targetPath, catalog, allowMutation: false, requiredOperation: null);
                if (!target.HasValue)
                {
                    continue;
                }

                string canonical = target.Value.ToString();
                if (!seenSkipTargets.Add(canonical))
                {
                    Add(ContentDiagnosticCode.InvalidValue, AggregationConfigPath, targetPath, "Duplicate skip target " + canonical + ".");
                    continue;
                }

                skipTargets.Add(target.Value);
            }

            return new AggregationPass(AggregationPassType.InternalReversion, causePrefix, midS, groups, skipTargets, null, null, null, null);
        }

        private AggregationPass LoadMetricAggregationPass(JObject passObject, string passPath, string causePrefix, TargetConfigCatalog catalog)
        {
            ValidateUnknownProperties(AggregationConfigPath, passObject, passPath, new[] { "type", "cause_prefix", "log_components", "weights_abs_sum_ppm_required", "metrics" });
            bool? logComponents = RequiredBool(AggregationConfigPath, passObject, "log_components", passPath + ".log_components");
            int? requiredWeightSum = RequiredInt(AggregationConfigPath, passObject, "weights_abs_sum_ppm_required", passPath + ".weights_abs_sum_ppm_required");
            JArray metricsArray = RequiredArray(AggregationConfigPath, passObject, "metrics", passPath + ".metrics");
            ValidatePositive(AggregationConfigPath, passPath + ".weights_abs_sum_ppm_required", requiredWeightSum, "weights_abs_sum_ppm_required");

            List<AggregationMetric> metrics = new List<AggregationMetric>();
            HashSet<string> metricIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; metricsArray != null && i < metricsArray.Count; i++)
            {
                string metricPath = passPath + ".metrics[" + i + "]";
                JObject metricObject = metricsArray[i] as JObject;
                if (metricObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, AggregationConfigPath, metricPath, "Metric aggregation row must be an object.");
                    continue;
                }

                ValidateUnknownProperties(AggregationConfigPath, metricObject, metricPath, new[] { "metric", "half_life_weeks", "alpha_ppm", "cap_per_weekS", "components" });
                TargetPath? metricTarget = RequiredTargetPath(AggregationConfigPath, metricObject, "metric", metricPath + ".metric", catalog, allowMutation: true, requiredOperation: null);
                int? halfLifeWeeks = RequiredInt(AggregationConfigPath, metricObject, "half_life_weeks", metricPath + ".half_life_weeks");
                int? alphaPpm = RequiredInt(AggregationConfigPath, metricObject, "alpha_ppm", metricPath + ".alpha_ppm");
                int? capPerWeekS = RequiredInt(AggregationConfigPath, metricObject, "cap_per_weekS", metricPath + ".cap_per_weekS");
                JArray componentsArray = RequiredArray(AggregationConfigPath, metricObject, "components", metricPath + ".components");
                ValidatePositive(AggregationConfigPath, metricPath + ".half_life_weeks", halfLifeWeeks, "half_life_weeks");
                ValidatePpm(AggregationConfigPath, metricPath + ".alpha_ppm", alphaPpm, "alpha_ppm");
                if (capPerWeekS.HasValue && capPerWeekS.Value < 0)
                {
                    Add(ContentDiagnosticCode.InvalidRange, AggregationConfigPath, metricPath + ".cap_per_weekS", "cap_per_weekS must be non-negative.");
                }

                List<WeightedTargetComponent> components = new List<WeightedTargetComponent>();
                HashSet<string> componentTargets = new HashSet<string>(StringComparer.Ordinal);
                long absoluteWeightTotal = 0;
                for (int j = 0; componentsArray != null && j < componentsArray.Count; j++)
                {
                    string componentPath = metricPath + ".components[" + j + "]";
                    JObject componentObject = componentsArray[j] as JObject;
                    if (componentObject == null)
                    {
                        Add(ContentDiagnosticCode.InvalidPropertyType, AggregationConfigPath, componentPath, "Aggregation component must be an object.");
                        continue;
                    }

                    ValidateUnknownProperties(AggregationConfigPath, componentObject, componentPath, new[] { "target", "weight_ppm" });
                    TargetPath? target = RequiredTargetPath(AggregationConfigPath, componentObject, "target", componentPath + ".target", catalog, allowMutation: false, requiredOperation: null);
                    int? weightPpm = RequiredInt(AggregationConfigPath, componentObject, "weight_ppm", componentPath + ".weight_ppm");
                    if (HasErrorsForRow(componentPath) || !target.HasValue || !weightPpm.HasValue)
                    {
                        continue;
                    }

                    string canonical = target.Value.ToString();
                    if (!componentTargets.Add(canonical))
                    {
                        Add(ContentDiagnosticCode.InvalidValue, AggregationConfigPath, componentPath + ".target", "Duplicate aggregation component target " + canonical + ".");
                        continue;
                    }

                    absoluteWeightTotal += Math.Abs((long)weightPpm.Value);
                    components.Add(new WeightedTargetComponent(target.Value, weightPpm.Value));
                }

                if (requiredWeightSum.HasValue && absoluteWeightTotal != requiredWeightSum.Value)
                {
                    Add(ContentDiagnosticCode.InvalidWeightSum, AggregationConfigPath, metricPath + ".components", "Absolute weight_ppm sum must equal " + requiredWeightSum.Value + ", got " + absoluteWeightTotal + ".");
                }

                if (HasErrorsForRow(metricPath) || !metricTarget.HasValue || !halfLifeWeeks.HasValue || !alphaPpm.HasValue || !capPerWeekS.HasValue)
                {
                    continue;
                }

                string canonicalMetric = metricTarget.Value.ToString();
                if (!metricIds.Add(canonicalMetric))
                {
                    Add(ContentDiagnosticCode.DuplicateId, AggregationConfigPath, metricPath + ".metric", "Duplicate metric aggregation target " + canonicalMetric + ".");
                    continue;
                }

                metrics.Add(new AggregationMetric(metricTarget.Value, halfLifeWeeks.Value, alphaPpm.Value, capPerWeekS.Value, components));
            }

            return new AggregationPass(AggregationPassType.MetricAggregation, causePrefix, null, null, null, logComponents, requiredWeightSum, metrics, null);
        }

        private AggregationPass LoadDerivedInternalsPass(JObject passObject, string passPath, string causePrefix, TargetConfigCatalog catalog)
        {
            ValidateUnknownProperties(AggregationConfigPath, passObject, passPath, new[] { "type", "cause_prefix", "rules" });
            JArray rulesArray = RequiredArray(AggregationConfigPath, passObject, "rules", passPath + ".rules");
            List<DerivedAggregationRule> rules = new List<DerivedAggregationRule>();
            for (int i = 0; rulesArray != null && i < rulesArray.Count; i++)
            {
                string rulePath = passPath + ".rules[" + i + "]";
                JObject ruleObject = rulesArray[i] as JObject;
                if (ruleObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, AggregationConfigPath, rulePath, "Derived rule must be an object.");
                    continue;
                }

                ValidateUnknownProperties(AggregationConfigPath, ruleObject, rulePath, new[] { "target", "op", "expr" });
                TargetPath? target = RequiredTargetPath(AggregationConfigPath, ruleObject, "target", rulePath + ".target", catalog, allowMutation: true, requiredOperation: TargetOperation.Set);
                string opText = RequiredString(AggregationConfigPath, ruleObject, "op", rulePath + ".op", nonEmpty: true);
                TargetOperation operation;
                if (!TryMapOperation(opText, out operation))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, AggregationConfigPath, rulePath + ".op", "op must be ADD, MUL, or SET.");
                }

                JObject exprObject = RequiredObject(AggregationConfigPath, ruleObject, "expr", rulePath + ".expr");
                AggregationExpression expression = LoadAggregationExpression(exprObject, rulePath + ".expr", catalog);
                if (HasErrorsForRow(rulePath) || !target.HasValue || expression == null)
                {
                    continue;
                }

                rules.Add(new DerivedAggregationRule(target.Value, operation, expression));
            }

            return new AggregationPass(AggregationPassType.DerivedInternals, causePrefix, null, null, null, null, null, null, rules);
        }

        private AggregationExpression LoadAggregationExpression(JObject exprObject, string jsonPath, TargetConfigCatalog catalog)
        {
            if (exprObject == null)
            {
                return null;
            }

            ValidateUnknownProperties(AggregationConfigPath, exprObject, jsonPath, new[] { "kind", "target", "targets" });
            string kindText = RequiredString(AggregationConfigPath, exprObject, "kind", jsonPath + ".kind", nonEmpty: true);
            AggregationExpressionKind kind;
            if (!TryMapAggregationExpressionKind(kindText, out kind))
            {
                Add(ContentDiagnosticCode.InvalidEnum, AggregationConfigPath, jsonPath + ".kind", "expr.kind must be AVG or COPY.");
                return null;
            }

            if (kind == AggregationExpressionKind.Copy)
            {
                TargetPath? target = RequiredTargetPath(AggregationConfigPath, exprObject, "target", jsonPath + ".target", catalog, allowMutation: false, requiredOperation: null);
                ValidateAbsent(exprObject, "targets", AggregationConfigPath, jsonPath + ".targets");
                return target.HasValue ? new AggregationExpression(kind, target.Value, null) : null;
            }

            JArray targetsArray = RequiredArray(AggregationConfigPath, exprObject, "targets", jsonPath + ".targets");
            ValidateAbsent(exprObject, "target", AggregationConfigPath, jsonPath + ".target");
            List<TargetPath> targets = new List<TargetPath>();
            HashSet<string> seenTargets = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; targetsArray != null && i < targetsArray.Count; i++)
            {
                string targetPath = jsonPath + ".targets[" + i + "]";
                TargetPath? target = ReadTargetPathToken(AggregationConfigPath, targetsArray[i], targetPath, catalog, allowMutation: false, requiredOperation: null);
                if (!target.HasValue)
                {
                    continue;
                }

                string canonical = target.Value.ToString();
                if (!seenTargets.Add(canonical))
                {
                    Add(ContentDiagnosticCode.InvalidValue, AggregationConfigPath, targetPath, "Duplicate AVG source target " + canonical + ".");
                    continue;
                }

                targets.Add(target.Value);
            }

            if (targets.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, AggregationConfigPath, jsonPath + ".targets", "AVG expression must declare at least one target.");
            }

            return HasErrorsForRow(jsonPath) ? null : new AggregationExpression(kind, null, targets);
        }

        private LegislativeConfig LoadLegislativeConfig(JObject root, TargetConfigCatalog catalog)
        {
            if (root == null)
            {
                return null;
            }

            ValidateUnknownProperties(LegislativeConfigPath, root, "$", new[] { "schema_version", "scale", "midS", "rounding", "limits", "constants", "gates", "senate", "movement_matching", "exceptional_route", "support_model", "stage_model", "player_strategies", "cause_prefixes" });
            int? schemaVersion = RequiredInt(LegislativeConfigPath, root, "schema_version", "$.schema_version");
            int? scale = RequiredInt(LegislativeConfigPath, root, "scale", "$.scale");
            int? midS = RequiredInt(LegislativeConfigPath, root, "midS", "$.midS");
            string roundingText = RequiredString(LegislativeConfigPath, root, "rounding", "$.rounding", nonEmpty: true);
            ContentRoundingMode rounding;
            if (!TryMapRounding(roundingText, out rounding))
            {
                Add(ContentDiagnosticCode.InvalidEnum, LegislativeConfigPath, "$.rounding", "rounding must be HALF_AWAY_FROM_ZERO.");
            }

            if (schemaVersion.HasValue && schemaVersion.Value != 1)
            {
                Add(ContentDiagnosticCode.UnsupportedSchemaVersion, LegislativeConfigPath, "$.schema_version", "Unsupported schema version " + schemaVersion.Value + "; expected 1.");
            }

            if (scale.HasValue && scale.Value <= 0)
            {
                Add(ContentDiagnosticCode.InvalidRange, LegislativeConfigPath, "$.scale", "scale must be positive.");
            }

            LegislativeLimits limits = LoadLegislativeLimits(RequiredObject(LegislativeConfigPath, root, "limits", "$.limits"));
            LegislativeConstants constants = LoadLegislativeConstants(RequiredObject(LegislativeConfigPath, root, "constants", "$.constants"));
            LegislativeGates gates = LoadLegislativeGates(RequiredObject(LegislativeConfigPath, root, "gates", "$.gates"));
            LegislativeSenate senate = LoadLegislativeSenate(RequiredObject(LegislativeConfigPath, root, "senate", "$.senate"));
            LegislativeMovementMatching movementMatching = LoadLegislativeMovementMatching(RequiredObject(LegislativeConfigPath, root, "movement_matching", "$.movement_matching"));
            LegislativeExceptionalRoute exceptionalRoute = LoadLegislativeExceptionalRoute(RequiredObject(LegislativeConfigPath, root, "exceptional_route", "$.exceptional_route"), catalog);
            LegislativeSupportModel supportModel = LoadLegislativeSupportModel(RequiredObject(LegislativeConfigPath, root, "support_model", "$.support_model"), catalog);
            LegislativeStageModel stageModel = LoadLegislativeStageModel(RequiredObject(LegislativeConfigPath, root, "stage_model", "$.stage_model"), catalog);
            List<LegislativePlayerStrategyEntry> playerStrategies = LoadLegislativePlayerStrategies(RequiredObject(LegislativeConfigPath, root, "player_strategies", "$.player_strategies"), catalog);
            LegislativeCausePrefixes causePrefixes = LoadLegislativeCausePrefixes(RequiredObject(LegislativeConfigPath, root, "cause_prefixes", "$.cause_prefixes"));

            if (HasErrors())
            {
                return null;
            }

            return new LegislativeConfig(schemaVersion.Value, scale.Value, midS.Value, rounding, limits, constants, gates, senate, movementMatching, exceptionalRoute, supportModel, stageModel, playerStrategies, causePrefixes);
        }

        private LegislativeLimits LoadLegislativeLimits(JObject obj)
        {
            const string path = "$.limits";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "max_active_reforms", "max_stages" });
            int? maxActiveReforms = RequiredInt(LegislativeConfigPath, obj, "max_active_reforms", path + ".max_active_reforms");
            int? maxStages = RequiredInt(LegislativeConfigPath, obj, "max_stages", path + ".max_stages");
            ValidatePositive(LegislativeConfigPath, path + ".max_active_reforms", maxActiveReforms, "max_active_reforms");
            ValidatePositive(LegislativeConfigPath, path + ".max_stages", maxStages, "max_stages");
            return maxActiveReforms.HasValue && maxStages.HasValue ? new LegislativeLimits(maxActiveReforms.Value, maxStages.Value) : null;
        }

        private LegislativeConstants LoadLegislativeConstants(JObject obj)
        {
            const string path = "$.constants";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "S", "HUNDRED_S" });
            int? scale = RequiredInt(LegislativeConfigPath, obj, "S", path + ".S");
            int? hundredS = RequiredInt(LegislativeConfigPath, obj, "HUNDRED_S", path + ".HUNDRED_S");
            ValidatePositive(LegislativeConfigPath, path + ".S", scale, "S");
            ValidatePositive(LegislativeConfigPath, path + ".HUNDRED_S", hundredS, "HUNDRED_S");
            return scale.HasValue && hundredS.HasValue ? new LegislativeConstants(scale.Value, hundredS.Value) : null;
        }

        private LegislativeGates LoadLegislativeGates(JObject obj)
        {
            const string path = "$.gates";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "normal_legitimacy_minS", "cohesion_block_minS", "exceptional_movement_minS", "anti_movement_crisis_minS" });
            int? normalLegitimacyMinS = RequiredInt(LegislativeConfigPath, obj, "normal_legitimacy_minS", path + ".normal_legitimacy_minS");
            int? cohesionBlockMinS = RequiredInt(LegislativeConfigPath, obj, "cohesion_block_minS", path + ".cohesion_block_minS");
            int? exceptionalMovementMinS = RequiredInt(LegislativeConfigPath, obj, "exceptional_movement_minS", path + ".exceptional_movement_minS");
            int? antiMovementCrisisMinS = RequiredInt(LegislativeConfigPath, obj, "anti_movement_crisis_minS", path + ".anti_movement_crisis_minS");
            return normalLegitimacyMinS.HasValue && cohesionBlockMinS.HasValue && exceptionalMovementMinS.HasValue && antiMovementCrisisMinS.HasValue
                ? new LegislativeGates(normalLegitimacyMinS.Value, cohesionBlockMinS.Value, exceptionalMovementMinS.Value, antiMovementCrisisMinS.Value)
                : null;
        }

        private LegislativeSenate LoadLegislativeSenate(JObject obj)
        {
            const string path = "$.senate";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "brakeS" });
            int? brakeS = RequiredInt(LegislativeConfigPath, obj, "brakeS", path + ".brakeS");
            return brakeS.HasValue ? new LegislativeSenate(brakeS.Value) : null;
        }

        private LegislativeMovementMatching LoadLegislativeMovementMatching(JObject obj)
        {
            const string path = "$.movement_matching";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "reform_tags_source", "match_mode", "direction_pro", "direction_anti" });
            string reformTagsSource = RequiredString(LegislativeConfigPath, obj, "reform_tags_source", path + ".reform_tags_source", nonEmpty: true);
            string matchModeText = RequiredString(LegislativeConfigPath, obj, "match_mode", path + ".match_mode", nonEmpty: true);
            int? directionPro = RequiredInt(LegislativeConfigPath, obj, "direction_pro", path + ".direction_pro");
            int? directionAnti = RequiredInt(LegislativeConfigPath, obj, "direction_anti", path + ".direction_anti");
            LegislativeMovementMatchMode matchMode;
            if (!TryMapMovementMatchMode(matchModeText, out matchMode))
            {
                Add(ContentDiagnosticCode.InvalidEnum, LegislativeConfigPath, path + ".match_mode", "match_mode must be ANY.");
            }

            if (directionPro.HasValue && directionPro.Value == 0)
            {
                Add(ContentDiagnosticCode.InvalidRange, LegislativeConfigPath, path + ".direction_pro", "direction_pro must not be zero.");
            }

            if (directionAnti.HasValue && directionAnti.Value == 0)
            {
                Add(ContentDiagnosticCode.InvalidRange, LegislativeConfigPath, path + ".direction_anti", "direction_anti must not be zero.");
            }

            return reformTagsSource != null && directionPro.HasValue && directionAnti.HasValue ? new LegislativeMovementMatching(reformTagsSource, matchMode, directionPro.Value, directionAnti.Value) : null;
        }

        private LegislativeExceptionalRoute LoadLegislativeExceptionalRoute(JObject obj, TargetConfigCatalog catalog)
        {
            const string path = "$.exceptional_route";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "enabled", "bypass_legitimacy_gate", "requires_pro_movement", "costs_per_tick" });
            bool? enabled = RequiredBool(LegislativeConfigPath, obj, "enabled", path + ".enabled");
            bool? bypassLegitimacyGate = RequiredBool(LegislativeConfigPath, obj, "bypass_legitimacy_gate", path + ".bypass_legitimacy_gate");
            bool? requiresProMovement = RequiredBool(LegislativeConfigPath, obj, "requires_pro_movement", path + ".requires_pro_movement");
            List<TargetDeltaDefinition> costsPerTick = LoadTargetDeltaArray(RequiredArray(LegislativeConfigPath, obj, "costs_per_tick", path + ".costs_per_tick"), path + ".costs_per_tick", catalog);
            return enabled.HasValue && bypassLegitimacyGate.HasValue && requiresProMovement.HasValue ? new LegislativeExceptionalRoute(enabled.Value, bypassLegitimacyGate.Value, requiresProMovement.Value, costsPerTick) : null;
        }

        private LegislativeSupportModel LoadLegislativeSupportModel(JObject obj, TargetConfigCatalog catalog)
        {
            const string path = "$.support_model";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "support_rangeS", "discipline", "base_component", "legitimacy_component", "ig_alignment_component", "movement_component", "upper_chamber" });
            LegislativeRange supportRange = LoadLegislativeRange(RequiredObject(LegislativeConfigPath, obj, "support_rangeS", path + ".support_rangeS"), path + ".support_rangeS");
            LegislativeDiscipline discipline = LoadLegislativeDiscipline(RequiredObject(LegislativeConfigPath, obj, "discipline", path + ".discipline"), path + ".discipline");
            LegislativeBaseComponent baseComponent = LoadLegislativeBaseComponent(RequiredObject(LegislativeConfigPath, obj, "base_component", path + ".base_component"), path + ".base_component", catalog);
            LegislativeLegitimacyComponent legitimacyComponent = LoadLegislativeLegitimacyComponent(RequiredObject(LegislativeConfigPath, obj, "legitimacy_component", path + ".legitimacy_component"), path + ".legitimacy_component", catalog);
            LegislativeIgAlignmentComponent igAlignmentComponent = LoadLegislativeIgAlignmentComponent(RequiredObject(LegislativeConfigPath, obj, "ig_alignment_component", path + ".ig_alignment_component"), path + ".ig_alignment_component", catalog);
            LegislativeMovementComponent movementComponent = LoadLegislativeMovementComponent(RequiredObject(LegislativeConfigPath, obj, "movement_component", path + ".movement_component"), path + ".movement_component");
            LegislativeUpperChamber upperChamber = LoadLegislativeUpperChamber(RequiredObject(LegislativeConfigPath, obj, "upper_chamber", path + ".upper_chamber"));
            return supportRange != null && discipline != null && baseComponent != null && legitimacyComponent != null && igAlignmentComponent != null && movementComponent != null && upperChamber != null
                ? new LegislativeSupportModel(supportRange, discipline, baseComponent, legitimacyComponent, igAlignmentComponent, movementComponent, upperChamber)
                : null;
        }

        private LegislativeRange LoadLegislativeRange(JObject obj, string path)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "minS", "maxS" });
            int? minS = RequiredInt(LegislativeConfigPath, obj, "minS", path + ".minS");
            int? maxS = RequiredInt(LegislativeConfigPath, obj, "maxS", path + ".maxS");
            if (minS.HasValue && maxS.HasValue && minS.Value > maxS.Value)
            {
                Add(ContentDiagnosticCode.InvalidRange, LegislativeConfigPath, path, "minS must be less than or equal to maxS.");
            }

            return minS.HasValue && maxS.HasValue ? new LegislativeRange(minS.Value, maxS.Value) : null;
        }

        private LegislativeDiscipline LoadLegislativeDiscipline(JObject obj, string path)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "party_organization_weight_ppm", "internal_cohesion_weight_ppm" });
            int? partyOrganizationWeightPpm = RequiredInt(LegislativeConfigPath, obj, "party_organization_weight_ppm", path + ".party_organization_weight_ppm");
            int? internalCohesionWeightPpm = RequiredInt(LegislativeConfigPath, obj, "internal_cohesion_weight_ppm", path + ".internal_cohesion_weight_ppm");
            return partyOrganizationWeightPpm.HasValue && internalCohesionWeightPpm.HasValue ? new LegislativeDiscipline(partyOrganizationWeightPpm.Value, internalCohesionWeightPpm.Value) : null;
        }

        private LegislativeBaseComponent LoadLegislativeBaseComponent(JObject obj, string path, TargetConfigCatalog catalog)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "metric", "addS" });
            TargetPath? metric = RequiredTargetPath(LegislativeConfigPath, obj, "metric", path + ".metric", catalog, allowMutation: false, requiredOperation: null);
            int? addS = RequiredInt(LegislativeConfigPath, obj, "addS", path + ".addS");
            return metric.HasValue && addS.HasValue ? new LegislativeBaseComponent(metric.Value, addS.Value) : null;
        }

        private LegislativeLegitimacyComponent LoadLegislativeLegitimacyComponent(JObject obj, string path, TargetConfigCatalog catalog)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "metric", "midS", "divS" });
            TargetPath? metric = RequiredTargetPath(LegislativeConfigPath, obj, "metric", path + ".metric", catalog, allowMutation: false, requiredOperation: null);
            int? midS = RequiredInt(LegislativeConfigPath, obj, "midS", path + ".midS");
            int? divS = RequiredInt(LegislativeConfigPath, obj, "divS", path + ".divS");
            ValidatePositive(LegislativeConfigPath, path + ".divS", divS, "divS");
            return metric.HasValue && midS.HasValue && divS.HasValue ? new LegislativeLegitimacyComponent(metric.Value, midS.Value, divS.Value) : null;
        }

        private LegislativeIgAlignmentComponent LoadLegislativeIgAlignmentComponent(JObject obj, string path, TargetConfigCatalog catalog)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "uses", "approval_to_01", "effective_stance_factor", "stance_input_range", "stance_scale_to_S", "clout_denomS", "term_div", "apply_discipline", "post_divS" });
            LegislativeIgAlignmentUses uses = LoadLegislativeIgAlignmentUses(RequiredObject(LegislativeConfigPath, obj, "uses", path + ".uses"), path + ".uses", catalog);
            LegislativeApprovalTo01 approvalTo01 = LoadLegislativeApprovalTo01(RequiredObject(LegislativeConfigPath, obj, "approval_to_01", path + ".approval_to_01"), path + ".approval_to_01");
            LegislativeEffectiveStanceFactor effectiveStanceFactor = LoadLegislativeEffectiveStanceFactor(RequiredObject(LegislativeConfigPath, obj, "effective_stance_factor", path + ".effective_stance_factor"), path + ".effective_stance_factor");
            int? stanceInputRange = RequiredInt(LegislativeConfigPath, obj, "stance_input_range", path + ".stance_input_range");
            int? stanceScaleToS = RequiredInt(LegislativeConfigPath, obj, "stance_scale_to_S", path + ".stance_scale_to_S");
            int? cloutDenomS = RequiredInt(LegislativeConfigPath, obj, "clout_denomS", path + ".clout_denomS");
            int? termDiv = RequiredInt(LegislativeConfigPath, obj, "term_div", path + ".term_div");
            bool? applyDiscipline = RequiredBool(LegislativeConfigPath, obj, "apply_discipline", path + ".apply_discipline");
            int? postDivS = RequiredInt(LegislativeConfigPath, obj, "post_divS", path + ".post_divS");
            ValidatePositive(LegislativeConfigPath, path + ".stance_input_range", stanceInputRange, "stance_input_range");
            ValidatePositive(LegislativeConfigPath, path + ".stance_scale_to_S", stanceScaleToS, "stance_scale_to_S");
            ValidatePositive(LegislativeConfigPath, path + ".clout_denomS", cloutDenomS, "clout_denomS");
            ValidatePositive(LegislativeConfigPath, path + ".term_div", termDiv, "term_div");
            ValidatePositive(LegislativeConfigPath, path + ".post_divS", postDivS, "post_divS");
            return uses != null && approvalTo01 != null && effectiveStanceFactor != null && stanceInputRange.HasValue && stanceScaleToS.HasValue && cloutDenomS.HasValue && termDiv.HasValue && applyDiscipline.HasValue && postDivS.HasValue
                ? new LegislativeIgAlignmentComponent(uses, approvalTo01, effectiveStanceFactor, stanceInputRange.Value, stanceScaleToS.Value, cloutDenomS.Value, termDiv.Value, applyDiscipline.Value, postDivS.Value)
                : null;
        }

        private LegislativeIgAlignmentUses LoadLegislativeIgAlignmentUses(JObject obj, string path, TargetConfigCatalog catalog)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "clout_target_pattern", "approval_target_pattern" });
            TargetPattern? cloutTargetPattern = RequiredTargetPattern(LegislativeConfigPath, obj, "clout_target_pattern", path + ".clout_target_pattern", catalog);
            TargetPattern? approvalTargetPattern = RequiredTargetPattern(LegislativeConfigPath, obj, "approval_target_pattern", path + ".approval_target_pattern", catalog);
            return cloutTargetPattern.HasValue && approvalTargetPattern.HasValue ? new LegislativeIgAlignmentUses(cloutTargetPattern.Value, approvalTargetPattern.Value) : null;
        }

        private LegislativeApprovalTo01 LoadLegislativeApprovalTo01(JObject obj, string path)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "offsetS", "divS" });
            int? offsetS = RequiredInt(LegislativeConfigPath, obj, "offsetS", path + ".offsetS");
            int? divS = RequiredInt(LegislativeConfigPath, obj, "divS", path + ".divS");
            ValidatePositive(LegislativeConfigPath, path + ".divS", divS, "divS");
            return offsetS.HasValue && divS.HasValue ? new LegislativeApprovalTo01(offsetS.Value, divS.Value) : null;
        }

        private LegislativeEffectiveStanceFactor LoadLegislativeEffectiveStanceFactor(JObject obj, string path)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "baseS", "approval01_divS", "denomS" });
            int? baseS = RequiredInt(LegislativeConfigPath, obj, "baseS", path + ".baseS");
            int? approval01DivS = RequiredInt(LegislativeConfigPath, obj, "approval01_divS", path + ".approval01_divS");
            int? denomS = RequiredInt(LegislativeConfigPath, obj, "denomS", path + ".denomS");
            ValidatePositive(LegislativeConfigPath, path + ".approval01_divS", approval01DivS, "approval01_divS");
            ValidatePositive(LegislativeConfigPath, path + ".denomS", denomS, "denomS");
            return baseS.HasValue && approval01DivS.HasValue && denomS.HasValue ? new LegislativeEffectiveStanceFactor(baseS.Value, approval01DivS.Value, denomS.Value) : null;
        }

        private LegislativeMovementComponent LoadLegislativeMovementComponent(JObject obj, string path)
        {
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "term_clampS", "support_divS" });
            int? termClampS = RequiredInt(LegislativeConfigPath, obj, "term_clampS", path + ".term_clampS");
            int? supportDivS = RequiredInt(LegislativeConfigPath, obj, "support_divS", path + ".support_divS");
            ValidatePositive(LegislativeConfigPath, path + ".support_divS", supportDivS, "support_divS");
            return termClampS.HasValue && supportDivS.HasValue ? new LegislativeMovementComponent(termClampS.Value, supportDivS.Value) : null;
        }

        private LegislativeUpperChamber LoadLegislativeUpperChamber(JObject obj)
        {
            const string path = "$.support_model.upper_chamber";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "type", "deltaS" });
            string typeText = RequiredString(LegislativeConfigPath, obj, "type", path + ".type", nonEmpty: true);
            int? deltaS = RequiredInt(LegislativeConfigPath, obj, "deltaS", path + ".deltaS");
            LegislativeUpperChamberAdjustmentType type;
            if (!TryMapUpperChamberAdjustmentType(typeText, out type))
            {
                Add(ContentDiagnosticCode.InvalidEnum, LegislativeConfigPath, path + ".type", "upper_chamber.type must be SUBTRACT_CONST.");
            }

            return deltaS.HasValue ? new LegislativeUpperChamber(type, deltaS.Value) : null;
        }

        private LegislativeStageModel LoadLegislativeStageModel(JObject obj, TargetConfigCatalog catalog)
        {
            const string path = "$.stage_model";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "base_difficulty_defaultS", "stage_weight", "throughput", "vote" });
            int? baseDifficultyDefaultS = RequiredInt(LegislativeConfigPath, obj, "base_difficulty_defaultS", path + ".base_difficulty_defaultS");
            LegislativeStageWeight stageWeight = LoadLegislativeStageWeight(RequiredObject(LegislativeConfigPath, obj, "stage_weight", path + ".stage_weight"));
            LegislativeThroughput throughput = LoadLegislativeThroughput(RequiredObject(LegislativeConfigPath, obj, "throughput", path + ".throughput"), catalog);
            LegislativeVote vote = LoadLegislativeVote(RequiredObject(LegislativeConfigPath, obj, "vote", path + ".vote"), catalog);
            return baseDifficultyDefaultS.HasValue && stageWeight != null && throughput != null && vote != null ? new LegislativeStageModel(baseDifficultyDefaultS.Value, stageWeight, throughput, vote) : null;
        }

        private LegislativeStageWeight LoadLegislativeStageWeight(JObject obj)
        {
            const string path = "$.stage_model.stage_weight";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "scale_denomS", "default_weightS" });
            int? scaleDenomS = RequiredInt(LegislativeConfigPath, obj, "scale_denomS", path + ".scale_denomS");
            ValidatePositive(LegislativeConfigPath, path + ".scale_denomS", scaleDenomS, "scale_denomS");
            JObject defaultWeightS = RequiredObject(LegislativeConfigPath, obj, "default_weightS", path + ".default_weightS");
            Dictionary<string, int> weights = new Dictionary<string, int>(StringComparer.Ordinal);
            if (defaultWeightS != null)
            {
                ValidateUnknownProperties(LegislativeConfigPath, defaultWeightS, path + ".default_weightS", new[] { "WORK", "VOTE" });
                foreach (string key in new[] { "WORK", "VOTE" })
                {
                    if (!defaultWeightS.TryGetValue(key, out JToken token))
                    {
                        Add(ContentDiagnosticCode.MissingRequiredProperty, LegislativeConfigPath, path + ".default_weightS." + key, "Missing required property " + key + ".");
                        continue;
                    }

                    int? value = ReadInt(LegislativeConfigPath, token, path + ".default_weightS." + key);
                    if (value.HasValue)
                    {
                        weights[key] = value.Value;
                    }
                }
            }

            return scaleDenomS.HasValue && weights.Count == 2 ? new LegislativeStageWeight(scaleDenomS.Value, weights) : null;
        }

        private LegislativeThroughput LoadLegislativeThroughput(JObject obj, TargetConfigCatalog catalog)
        {
            const string path = "$.stage_model.throughput";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "baseS", "governability_metric", "metric_denomS", "support_denomS", "chamber_both_support" });
            int? baseS = RequiredInt(LegislativeConfigPath, obj, "baseS", path + ".baseS");
            TargetPath? governabilityMetric = RequiredTargetPath(LegislativeConfigPath, obj, "governability_metric", path + ".governability_metric", catalog, allowMutation: false, requiredOperation: null);
            int? metricDenomS = RequiredInt(LegislativeConfigPath, obj, "metric_denomS", path + ".metric_denomS");
            int? supportDenomS = RequiredInt(LegislativeConfigPath, obj, "support_denomS", path + ".support_denomS");
            string chamberBothSupportText = RequiredString(LegislativeConfigPath, obj, "chamber_both_support", path + ".chamber_both_support", nonEmpty: true);
            ValidatePositive(LegislativeConfigPath, path + ".metric_denomS", metricDenomS, "metric_denomS");
            ValidatePositive(LegislativeConfigPath, path + ".support_denomS", supportDenomS, "support_denomS");
            LegislativeChamberSupportMode chamberBothSupport;
            if (!TryMapChamberSupportMode(chamberBothSupportText, out chamberBothSupport))
            {
                Add(ContentDiagnosticCode.InvalidEnum, LegislativeConfigPath, path + ".chamber_both_support", "chamber_both_support must be MIN.");
            }

            return baseS.HasValue && governabilityMetric.HasValue && metricDenomS.HasValue && supportDenomS.HasValue
                ? new LegislativeThroughput(baseS.Value, governabilityMetric.Value, metricDenomS.Value, supportDenomS.Value, chamberBothSupport)
                : null;
        }

        private LegislativeVote LoadLegislativeVote(JObject obj, TargetConfigCatalog catalog)
        {
            const string path = "$.stage_model.vote";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "support_floorS", "support_spanS", "pass_thresholdS", "fail_reset_progressS", "fail_penalties" });
            int? supportFloorS = RequiredInt(LegislativeConfigPath, obj, "support_floorS", path + ".support_floorS");
            int? supportSpanS = RequiredInt(LegislativeConfigPath, obj, "support_spanS", path + ".support_spanS");
            int? passThresholdS = RequiredInt(LegislativeConfigPath, obj, "pass_thresholdS", path + ".pass_thresholdS");
            int? failResetProgressS = RequiredInt(LegislativeConfigPath, obj, "fail_reset_progressS", path + ".fail_reset_progressS");
            List<TargetDeltaDefinition> failPenalties = LoadTargetDeltaArray(RequiredArray(LegislativeConfigPath, obj, "fail_penalties", path + ".fail_penalties"), path + ".fail_penalties", catalog);
            return supportFloorS.HasValue && supportSpanS.HasValue && passThresholdS.HasValue && failResetProgressS.HasValue
                ? new LegislativeVote(supportFloorS.Value, supportSpanS.Value, passThresholdS.Value, failResetProgressS.Value, failPenalties)
                : null;
        }

        private List<LegislativePlayerStrategyEntry> LoadLegislativePlayerStrategies(JObject obj, TargetConfigCatalog catalog)
        {
            const string path = "$.player_strategies";
            string[] expected = { "STEADY", "PUSH", "COMPROMISE", "DELAY" };
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, expected);

            List<LegislativePlayerStrategyEntry> result = new List<LegislativePlayerStrategyEntry>();
            foreach (string strategyId in expected)
            {
                JObject strategyObject = RequiredObject(LegislativeConfigPath, obj, strategyId, path + "." + strategyId);
                LegislativePlayerStrategy strategy = LoadLegislativePlayerStrategy(strategyObject, path + "." + strategyId, catalog);
                if (strategy != null)
                {
                    result.Add(new LegislativePlayerStrategyEntry(strategyId, strategy));
                }
            }

            return result;
        }

        private LegislativePlayerStrategy LoadLegislativePlayerStrategy(JObject obj, string path, TargetConfigCatalog catalog)
        {
            if (obj == null)
            {
                return null;
            }

            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "support_bonusS", "throughput_multiplier_ppm", "implementation_effect_multiplier_ppm", "per_tick_deltas" });
            int? supportBonusS = RequiredInt(LegislativeConfigPath, obj, "support_bonusS", path + ".support_bonusS");
            int? throughputMultiplierPpm = RequiredInt(LegislativeConfigPath, obj, "throughput_multiplier_ppm", path + ".throughput_multiplier_ppm");
            int? implementationEffectMultiplierPpm = OptionalInt(LegislativeConfigPath, obj, "implementation_effect_multiplier_ppm", path + ".implementation_effect_multiplier_ppm");
            List<TargetDeltaDefinition> perTickDeltas = LoadTargetDeltaArray(RequiredArray(LegislativeConfigPath, obj, "per_tick_deltas", path + ".per_tick_deltas"), path + ".per_tick_deltas", catalog);
            ValidatePositive(LegislativeConfigPath, path + ".throughput_multiplier_ppm", throughputMultiplierPpm, "throughput_multiplier_ppm");
            if (implementationEffectMultiplierPpm.HasValue && implementationEffectMultiplierPpm.Value <= 0)
            {
                Add(ContentDiagnosticCode.InvalidRange, LegislativeConfigPath, path + ".implementation_effect_multiplier_ppm", "implementation_effect_multiplier_ppm must be positive when present.");
            }

            return supportBonusS.HasValue && throughputMultiplierPpm.HasValue ? new LegislativePlayerStrategy(supportBonusS.Value, throughputMultiplierPpm.Value, implementationEffectMultiplierPpm, perTickDeltas) : null;
        }

        private LegislativeCausePrefixes LoadLegislativeCausePrefixes(JObject obj)
        {
            const string path = "$.cause_prefixes";
            ValidateUnknownProperties(LegislativeConfigPath, obj, path, new[] { "progress", "support", "block", "exception_cost", "vote_fail" });
            string progress = RequiredString(LegislativeConfigPath, obj, "progress", path + ".progress", nonEmpty: true);
            string support = RequiredString(LegislativeConfigPath, obj, "support", path + ".support", nonEmpty: true);
            string block = RequiredString(LegislativeConfigPath, obj, "block", path + ".block", nonEmpty: true);
            string exceptionCost = RequiredString(LegislativeConfigPath, obj, "exception_cost", path + ".exception_cost", nonEmpty: true);
            string voteFail = RequiredString(LegislativeConfigPath, obj, "vote_fail", path + ".vote_fail", nonEmpty: true);
            return progress != null && support != null && block != null && exceptionCost != null && voteFail != null ? new LegislativeCausePrefixes(progress, support, block, exceptionCost, voteFail) : null;
        }

        private List<TargetDeltaDefinition> LoadTargetDeltaArray(JArray array, string jsonPath, TargetConfigCatalog catalog)
        {
            List<TargetDeltaDefinition> result = new List<TargetDeltaDefinition>();
            for (int i = 0; array != null && i < array.Count; i++)
            {
                string rowPath = jsonPath + "[" + i + "]";
                JObject row = array[i] as JObject;
                if (row == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, LegislativeConfigPath, rowPath, "Delta row must be an object.");
                    continue;
                }

                ValidateUnknownProperties(LegislativeConfigPath, row, rowPath, new[] { "target", "deltaS", "cause" });
                TargetPath? target = RequiredTargetPath(LegislativeConfigPath, row, "target", rowPath + ".target", catalog, allowMutation: true, requiredOperation: TargetOperation.Add);
                int? deltaS = RequiredInt(LegislativeConfigPath, row, "deltaS", rowPath + ".deltaS");
                string cause = RequiredString(LegislativeConfigPath, row, "cause", rowPath + ".cause", nonEmpty: true);
                if (HasErrorsForRow(rowPath) || !target.HasValue || !deltaS.HasValue || cause == null)
                {
                    continue;
                }

                result.Add(new TargetDeltaDefinition(target.Value, deltaS.Value, cause));
            }

            return result;
        }

        private List<EffectTemplate> LoadEffects(JObject root, TargetConfigCatalog catalog, ContentLocalizationTable localization)
        {
            if (root == null)
            {
                return null;
            }

            ValidateUnknownProperties(EffectsPath, root, "$", new[] { "effects" });
            JArray effectArray = RequiredArray(EffectsPath, root, "effects", "$.effects");
            List<EffectTemplate> result = new List<EffectTemplate>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; effectArray != null && i < effectArray.Count; i++)
            {
                string rowPath = "$.effects[" + i + "]";
                JObject row = effectArray[i] as JObject;
                if (row == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EffectsPath, rowPath, "Effect row must be an object.");
                    continue;
                }

                ValidateUnknownProperties(EffectsPath, row, rowPath, new[] { "id", "loc_title", "mods", "tags" });
                string id = RequiredString(EffectsPath, row, "id", rowPath + ".id", nonEmpty: true);
                string locTitle = RequiredString(EffectsPath, row, "loc_title", rowPath + ".loc_title", nonEmpty: true);
                JArray modsArray = RequiredArray(EffectsPath, row, "mods", rowPath + ".mods");
                JArray tagsArray = RequiredArray(EffectsPath, row, "tags", rowPath + ".tags");
                if (id != null)
                {
                    if (!IsAsciiLowerSnake(id) || !id.StartsWith("eff_", StringComparison.Ordinal))
                    {
                        Add(ContentDiagnosticCode.InvalidId, EffectsPath, rowPath + ".id", "Effect id must be ASCII lowercase snake_case with prefix eff_.");
                    }
                    else if (!ids.Add(id))
                    {
                        Add(ContentDiagnosticCode.DuplicateId, EffectsPath, rowPath + ".id", "Duplicate effect id " + id + ".");
                    }
                }

                if (localization != null && locTitle != null && !localization.TryResolve(locTitle, out _))
                {
                    Add(ContentDiagnosticCode.MissingLocalizationKey, EffectsPath, rowPath + ".loc_title", "Missing localization key " + locTitle + ".");
                }

                List<EffectModifier> modifiers = new List<EffectModifier>();
                for (int j = 0; modsArray != null && j < modsArray.Count; j++)
                {
                    string modPath = rowPath + ".mods[" + j + "]";
                    JObject modObject = modsArray[j] as JObject;
                    if (modObject == null)
                    {
                        Add(ContentDiagnosticCode.InvalidPropertyType, EffectsPath, modPath, "Effect modifier must be an object.");
                        continue;
                    }

                    ValidateUnknownProperties(EffectsPath, modObject, modPath, new[] { "target", "op", "valueS", "is_per_tick", "clamp_minS", "clamp_maxS" });
                    TargetPath? target = RequiredTargetPath(EffectsPath, modObject, "target", modPath + ".target", catalog, allowMutation: true, requiredOperation: null);
                    string opText = RequiredString(EffectsPath, modObject, "op", modPath + ".op", nonEmpty: true);
                    int? valueS = RequiredInt(EffectsPath, modObject, "valueS", modPath + ".valueS");
                    bool? isPerTick = RequiredBool(EffectsPath, modObject, "is_per_tick", modPath + ".is_per_tick");
                    int? clampMinS = OptionalInt(EffectsPath, modObject, "clamp_minS", modPath + ".clamp_minS");
                    int? clampMaxS = OptionalInt(EffectsPath, modObject, "clamp_maxS", modPath + ".clamp_maxS");
                    if (clampMinS.HasValue && clampMaxS.HasValue && clampMinS.Value > clampMaxS.Value)
                    {
                        Add(ContentDiagnosticCode.InvalidRange, EffectsPath, modPath, "clamp_minS must be less than or equal to clamp_maxS.");
                    }

                    TargetOperation operation;
                    if (!TryMapOperation(opText, out operation))
                    {
                        Add(ContentDiagnosticCode.InvalidEnum, EffectsPath, modPath + ".op", "op must be ADD, MUL, or SET.");
                    }

                    if (target.HasValue && catalog.TryResolve(target.Value, out TargetConfig config) && !config.Allows(operation))
                    {
                        Add(ContentDiagnosticCode.InvalidTargetOperation, EffectsPath, modPath + ".op", "Operation " + opText + " is not allowed for target " + target.Value + ".");
                    }

                    if (HasErrorsForRow(modPath) || !target.HasValue || !valueS.HasValue || !isPerTick.HasValue)
                    {
                        continue;
                    }

                    modifiers.Add(new EffectModifier(target.Value, operation, valueS.Value, isPerTick.Value, (clampMinS.HasValue || clampMaxS.HasValue) ? new EffectClamp(clampMinS, clampMaxS) : null));
                }

                if (modsArray != null && modsArray.Count == 0)
                {
                    Add(ContentDiagnosticCode.InvalidValue, EffectsPath, rowPath + ".mods", "mods must not be empty.");
                }

                List<string> tags = LoadTags(EffectsPath, tagsArray, rowPath + ".tags");
                if (HasErrorsForRow(rowPath) || id == null || locTitle == null)
                {
                    continue;
                }

                result.Add(new EffectTemplate(id, locTitle, modifiers, tags));
            }

            return result;
        }

        private List<string> LoadTags(string file, JArray tagsArray, string jsonPath)
        {
            List<string> tags = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (tagsArray == null)
            {
                return tags;
            }

            if (tagsArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, file, jsonPath, "tags must not be empty.");
            }

            for (int i = 0; i < tagsArray.Count; i++)
            {
                string tagPath = jsonPath + "[" + i + "]";
                if (tagsArray[i].Type != JTokenType.String)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, file, tagPath, "Tag must be a string.");
                    continue;
                }

                string tag = tagsArray[i].Value<string>();
                if (string.IsNullOrEmpty(tag))
                {
                    Add(ContentDiagnosticCode.InvalidValue, file, tagPath, "Tag must not be empty.");
                    continue;
                }

                if (!IsTwoSegmentDottedLowercase(tag))
                {
                    Add(ContentDiagnosticCode.InvalidValue, file, tagPath, "Tag must use ASCII lowercase dotted format namespace.value.");
                    continue;
                }

                if (!seen.Add(tag))
                {
                    Add(ContentDiagnosticCode.InvalidValue, file, tagPath, "Duplicate tag " + tag + ".");
                    continue;
                }

                tags.Add(tag);
            }

            return tags;
        }

        private List<EventTemplate> LoadEvents(
            JObject root,
            TargetConfigCatalog catalog,
            ContentLocalizationTable localization,
            IEnumerable<MovementDefinition> movements,
            IEnumerable<EffectTemplate> effects)
        {
            if (root == null)
            {
                return null;
            }

            ValidateUnknownProperties(EventsPath, root, "$", new[] { "schema_version", "events" });
            int? schemaVersion = RequiredInt(EventsPath, root, "schema_version", "$.schema_version");
            if (schemaVersion.HasValue && schemaVersion.Value != 1)
            {
                Add(ContentDiagnosticCode.UnsupportedSchemaVersion, EventsPath, "$.schema_version", "Unsupported schema version " + schemaVersion.Value + "; expected 1.");
            }

            JArray eventArray = RequiredArray(EventsPath, root, "events", "$.events");
            if (eventArray != null && eventArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, EventsPath, "$.events", "events must not be empty.");
            }

            HashSet<string> movementIds = CollectMovementIds(movements);
            HashSet<string> allowedThemeTags = CollectAllowedEventThemeTags(movements);
            HashSet<string> effectIds = CollectEffectIds(effects);
            List<EventTemplate> result = new List<EventTemplate>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; eventArray != null && i < eventArray.Count; i++)
            {
                string rowPath = "$.events[" + i + "]";
                JObject row = eventArray[i] as JObject;
                if (row == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, rowPath, "Event row must be an object.");
                    continue;
                }

                ValidateUnknownProperties(EventsPath, row, rowPath, new[]
                {
                    "id",
                    "loc_title",
                    "kind",
                    "scope",
                    "blocking",
                    "base_priority",
                    "weight",
                    "cooldown_weeks",
                    "max_per_campaign",
                    "tags",
                    "vars",
                    "conditions",
                    "options",
                    "auto_option_id"
                });

                string id = RequiredString(EventsPath, row, "id", rowPath + ".id", nonEmpty: true);
                string locTitle = RequiredString(EventsPath, row, "loc_title", rowPath + ".loc_title", nonEmpty: true);
                string kindText = RequiredString(EventsPath, row, "kind", rowPath + ".kind", nonEmpty: true);
                string scopeText = RequiredString(EventsPath, row, "scope", rowPath + ".scope", nonEmpty: true);
                bool? blocking = RequiredBool(EventsPath, row, "blocking", rowPath + ".blocking");
                int? basePriority = RequiredInt(EventsPath, row, "base_priority", rowPath + ".base_priority");
                int? weight = RequiredInt(EventsPath, row, "weight", rowPath + ".weight");
                int? cooldownWeeks = RequiredInt(EventsPath, row, "cooldown_weeks", rowPath + ".cooldown_weeks");
                int? maxPerCampaign = RequiredInt(EventsPath, row, "max_per_campaign", rowPath + ".max_per_campaign");
                JArray tagsArray = RequiredArray(EventsPath, row, "tags", rowPath + ".tags");
                JObject varsObject = RequiredObject(EventsPath, row, "vars", rowPath + ".vars");
                JObject conditionsObject = RequiredObject(EventsPath, row, "conditions", rowPath + ".conditions");
                JArray optionsArray = RequiredArray(EventsPath, row, "options", rowPath + ".options");
                string autoOptionId = OptionalNullableString(EventsPath, row, "auto_option_id", rowPath + ".auto_option_id");

                if (id != null)
                {
                    if (!IsAsciiLowerSnake(id) || !id.StartsWith("evt_", StringComparison.Ordinal))
                    {
                        Add(ContentDiagnosticCode.InvalidId, EventsPath, rowPath + ".id", "Event id must be ASCII lowercase snake_case with prefix evt_.");
                    }
                    else if (!ids.Add(id))
                    {
                        Add(ContentDiagnosticCode.DuplicateId, EventsPath, rowPath + ".id", "Duplicate event id " + id + ".");
                    }
                }

                if (basePriority.HasValue == true && basePriority.Value < 0)
                {
                    Add(ContentDiagnosticCode.InvalidRange, EventsPath, rowPath + ".base_priority", "base_priority must be non-negative.");
                }

                if (weight.HasValue == true && weight.Value < 0)
                {
                    Add(ContentDiagnosticCode.InvalidRange, EventsPath, rowPath + ".weight", "weight must be non-negative.");
                }

                if (cooldownWeeks.HasValue == true && cooldownWeeks.Value < 0)
                {
                    Add(ContentDiagnosticCode.InvalidRange, EventsPath, rowPath + ".cooldown_weeks", "cooldown_weeks must be non-negative.");
                }

                ValidatePositive(EventsPath, rowPath + ".max_per_campaign", maxPerCampaign, "max_per_campaign");

                if (localization != null && locTitle != null && !localization.TryResolve(locTitle, out _))
                {
                    Add(ContentDiagnosticCode.MissingLocalizationKey, EventsPath, rowPath + ".loc_title", "Missing localization key " + locTitle + ".");
                }

                EventKind kind;
                if (!TryMapEventKind(kindText, out kind))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, EventsPath, rowPath + ".kind", "kind must be AUTO, CHOICE, or CRISIS.");
                }

                EventScope scope;
                if (!TryMapEventScope(scopeText, out scope))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, EventsPath, rowPath + ".scope", "scope must be NATIONAL or REGION.");
                }

                List<string> tags = LoadTags(EventsPath, tagsArray, rowPath + ".tags");
                ValidateThemeTags(tags, allowedThemeTags, EventsPath, rowPath + ".tags");
                List<EventVariableBinding> variables = LoadEventVariables(varsObject, rowPath + ".vars", catalog);
                EventConditionNode conditions = LoadEventConditionNode(conditionsObject, rowPath + ".conditions", catalog, movementIds);
                List<EventOption> options = LoadEventOptions(optionsArray, rowPath + ".options", catalog, movementIds, localization, effectIds);

                if (scope == EventScope.Region && !ContainsRegionBinding(variables))
                {
                    Add(ContentDiagnosticCode.InvalidReference, EventsPath, rowPath + ".vars", "REGION events must declare at least one pick_region binding.");
                }

                if (kind == EventKind.Crisis && blocking.HasValue && !blocking.Value)
                {
                    Add(ContentDiagnosticCode.InvalidValue, EventsPath, rowPath + ".blocking", "CRISIS events must set blocking to true.");
                }

                if ((kind == EventKind.Auto || kind == EventKind.Choice) && blocking.HasValue && blocking.Value)
                {
                    Add(ContentDiagnosticCode.InvalidValue, EventsPath, rowPath + ".blocking", "Only CRISIS events may set blocking to true.");
                }

                if (kind == EventKind.Auto)
                {
                    if (options.Count != 1)
                    {
                        Add(ContentDiagnosticCode.InvalidValue, EventsPath, rowPath + ".options", "AUTO events must declare exactly one option.");
                    }

                    if (string.IsNullOrEmpty(autoOptionId))
                    {
                        Add(ContentDiagnosticCode.InvalidReference, EventsPath, rowPath + ".auto_option_id", "AUTO events must declare auto_option_id.");
                    }
                    else if (options.Count == 1 && !string.Equals(options[0].Id, autoOptionId, StringComparison.Ordinal))
                    {
                        Add(ContentDiagnosticCode.InvalidReference, EventsPath, rowPath + ".auto_option_id", "AUTO event auto_option_id must match the only option.");
                    }
                }
                else if (autoOptionId != null)
                {
                    Add(ContentDiagnosticCode.InvalidValue, EventsPath, rowPath + ".auto_option_id", "Only AUTO events may declare auto_option_id.");
                }

                if (HasErrorsForRow(rowPath)
                    || id == null
                    || locTitle == null
                    || !basePriority.HasValue
                    || !weight.HasValue
                    || !cooldownWeeks.HasValue
                    || !maxPerCampaign.HasValue
                    || !blocking.HasValue)
                {
                    continue;
                }

                EventTemplate template = new EventTemplate(
                    id,
                    locTitle,
                    kind,
                    scope,
                    blocking.Value,
                    basePriority.Value,
                    weight.Value,
                    cooldownWeeks.Value,
                    maxPerCampaign.Value,
                    tags,
                    variables,
                    conditions,
                    options,
                    autoOptionId);
                result.Add(template);
            }

            ValidateEventCrossReferences(result);
            return HasErrors() ? null : result;
        }

        private List<ReformTemplate> LoadReforms(
            JObject root,
            TargetConfigCatalog catalog,
            ContentLocalizationTable localization,
            IReadOnlyList<InterestGroupDefinition> interestGroups,
            IEnumerable<MovementDefinition> movements,
            LegislativeConfig legislativeConfig,
            IEnumerable<EffectTemplate> effects)
        {
            if (root == null)
            {
                return null;
            }

            ValidateUnknownProperties(ReformsPath, root, "$", new[] { "reforms" });
            JArray reformArray = RequiredArray(ReformsPath, root, "reforms", "$.reforms");
            if (reformArray != null && reformArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, ReformsPath, "$.reforms", "reforms must not be empty.");
            }

            HashSet<string> effectIds = CollectEffectIds(effects);
            HashSet<string> movementTagVocabulary = CollectMovementTagVocabulary(movements);
            HashSet<string> interestGroupIds = CollectInterestGroupIds(interestGroups);
            List<ReformTemplate> result = new List<ReformTemplate>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; reformArray != null && i < reformArray.Count; i++)
            {
                string rowPath = "$.reforms[" + i + "]";
                JObject row = reformArray[i] as JObject;
                if (row == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, ReformsPath, rowPath, "Reform row must be an object.");
                    continue;
                }

                ValidateUnknownProperties(ReformsPath, row, rowPath, new[]
                {
                    "id",
                    "loc_title",
                    "loc_desc",
                    "area",
                    "kind",
                    "cooldown_weeks",
                    "max_per_campaign",
                    "movement_tags",
                    "policy_tags",
                    "igs_stance",
                    "prereqs",
                    "base_difficultyS",
                    "stages",
                    "on_pass_effects"
                });

                string id = RequiredString(ReformsPath, row, "id", rowPath + ".id", nonEmpty: true);
                string locTitle = RequiredString(ReformsPath, row, "loc_title", rowPath + ".loc_title", nonEmpty: true);
                string locDesc = RequiredString(ReformsPath, row, "loc_desc", rowPath + ".loc_desc", nonEmpty: true);
                string area = RequiredString(ReformsPath, row, "area", rowPath + ".area", nonEmpty: true);
                string kindText = RequiredString(ReformsPath, row, "kind", rowPath + ".kind", nonEmpty: true);
                int? cooldownWeeks = RequiredInt(ReformsPath, row, "cooldown_weeks", rowPath + ".cooldown_weeks");
                int? maxPerCampaign = RequiredInt(ReformsPath, row, "max_per_campaign", rowPath + ".max_per_campaign");
                JArray movementTagsArray = RequiredArray(ReformsPath, row, "movement_tags", rowPath + ".movement_tags");
                JArray policyTagsArray = RequiredArray(ReformsPath, row, "policy_tags", rowPath + ".policy_tags");
                JObject explicitStancesObject = RequiredObject(ReformsPath, row, "igs_stance", rowPath + ".igs_stance");
                JArray prereqArray = RequiredArray(ReformsPath, row, "prereqs", rowPath + ".prereqs");
                int? baseDifficultyS = RequiredInt(ReformsPath, row, "base_difficultyS", rowPath + ".base_difficultyS");
                JArray stagesArray = RequiredArray(ReformsPath, row, "stages", rowPath + ".stages");
                JArray onPassEffectsArray = RequiredArray(ReformsPath, row, "on_pass_effects", rowPath + ".on_pass_effects");

                if (id != null)
                {
                    if (!IsAsciiLowerSnake(id) || !id.StartsWith("ref_", StringComparison.Ordinal))
                    {
                        Add(ContentDiagnosticCode.InvalidId, ReformsPath, rowPath + ".id", "Reform id must be ASCII lowercase snake_case with prefix ref_.");
                    }
                    else if (!ids.Add(id))
                    {
                        Add(ContentDiagnosticCode.DuplicateId, ReformsPath, rowPath + ".id", "Duplicate reform id " + id + ".");
                    }
                }

                if (localization != null && locTitle != null && !localization.TryResolve(locTitle, out _))
                {
                    Add(ContentDiagnosticCode.MissingLocalizationKey, ReformsPath, rowPath + ".loc_title", "Missing localization key " + locTitle + ".");
                }

                if (localization != null && locDesc != null && !localization.TryResolve(locDesc, out _))
                {
                    Add(ContentDiagnosticCode.MissingLocalizationKey, ReformsPath, rowPath + ".loc_desc", "Missing localization key " + locDesc + ".");
                }

                if (area != null && !Contains(AllowedReformAreas, area))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, ReformsPath, rowPath + ".area", "area is outside the supported reform vocabulary.");
                }

                ReformKind kind;
                if (!TryMapReformKind(kindText, out kind))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, ReformsPath, rowPath + ".kind", "kind must be NORMAL or SPECIAL_CONSTITUTIONAL.");
                }

                if (cooldownWeeks.HasValue && cooldownWeeks.Value < 0)
                {
                    Add(ContentDiagnosticCode.InvalidRange, ReformsPath, rowPath + ".cooldown_weeks", "cooldown_weeks must be non-negative.");
                }

                ValidatePositive(ReformsPath, rowPath + ".max_per_campaign", maxPerCampaign, "max_per_campaign");
                ValidatePositive(ReformsPath, rowPath + ".base_difficultyS", baseDifficultyS, "base_difficultyS");

                List<string> movementTags = LoadTags(ReformsPath, movementTagsArray, rowPath + ".movement_tags");
                ValidateAllowedTags(movementTags, movementTagVocabulary, ReformsPath, rowPath + ".movement_tags", "movement tag");
                List<string> policyTags = LoadTags(ReformsPath, policyTagsArray, rowPath + ".policy_tags");
                ValidateAllowedTags(policyTags, ReformContentCompiler.PolicyTags, ReformsPath, rowPath + ".policy_tags", "policy tag");
                List<ReformInterestGroupStance> explicitStances = LoadExplicitInterestGroupStances(explicitStancesObject, rowPath + ".igs_stance", interestGroupIds);
                List<ReformPrerequisite> prerequisites = LoadReformPrerequisites(prereqArray, rowPath + ".prereqs", catalog);
                List<ReformStage> stages = LoadReformStages(stagesArray, rowPath + ".stages", legislativeConfig);
                List<EffectTemplateInvocation> onPassEffects = LoadEffectInvocations(onPassEffectsArray, rowPath + ".on_pass_effects", effectIds, requirePositiveDuration: true, allowEmpty: true, file: ReformsPath);
                List<ReformInterestGroupStance> effectiveStances = ReformContentCompiler.TryCompileEffectiveStances(
                    interestGroups,
                    explicitStances,
                    policyTags,
                    out string compilerError);

                if (compilerError != null)
                {
                    Add(ContentDiagnosticCode.InvalidReference, ReformsPath, rowPath + ".policy_tags", compilerError);
                }

                if (HasErrorsForRow(rowPath)
                    || id == null
                    || locTitle == null
                    || locDesc == null
                    || area == null
                    || !cooldownWeeks.HasValue
                    || !maxPerCampaign.HasValue
                    || !baseDifficultyS.HasValue
                    || effectiveStances == null)
                {
                    continue;
                }

                result.Add(new ReformTemplate(
                    id,
                    locTitle,
                    locDesc,
                    area,
                    kind,
                    cooldownWeeks.Value,
                    maxPerCampaign.Value,
                    movementTags,
                    policyTags,
                    explicitStances,
                    effectiveStances,
                    prerequisites,
                    baseDifficultyS.Value,
                    stages,
                    onPassEffects));
            }

            return HasErrors() ? null : result;
        }

        private List<EventVariableBinding> LoadEventVariables(JObject varsObject, string jsonPath, TargetConfigCatalog catalog)
        {
            List<EventVariableBinding> variables = new List<EventVariableBinding>();
            if (varsObject == null)
            {
                return variables;
            }

            foreach (JProperty property in varsObject.Properties())
            {
                string bindingPath = jsonPath + "." + property.Name;
                if (!IsAsciiLowerSnake(property.Name))
                {
                    Add(ContentDiagnosticCode.InvalidId, EventsPath, bindingPath, "Variable name must be ASCII lowercase snake_case.");
                }

                JObject bindingObject = property.Value as JObject;
                if (bindingObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, bindingPath, "Variable binding must be an object.");
                    continue;
                }

                string bindText = RequiredString(EventsPath, bindingObject, "bind", bindingPath + ".bind", nonEmpty: true);
                if (bindText == "pick_region")
                {
                    ValidateUnknownProperties(EventsPath, bindingObject, bindingPath, new[] { "bind", "mode", "target" });
                    string modeText = RequiredString(EventsPath, bindingObject, "mode", bindingPath + ".mode", nonEmpty: true);
                    EventSelectorMode mode;
                    if (!TryMapSelectorMode(modeText, out mode))
                    {
                        Add(ContentDiagnosticCode.InvalidEnum, EventsPath, bindingPath + ".mode", "mode must be ARGMAX or WEIGHTED.");
                    }

                    TargetPattern? pattern = RequiredTargetPattern(EventsPath, bindingObject, "target", bindingPath + ".target", catalog, allowStaticRegionalReadOnly: true);
                    if (pattern.HasValue && !pattern.Value.ToString().StartsWith("regions.*.", StringComparison.Ordinal))
                    {
                        Add(ContentDiagnosticCode.InvalidReference, EventsPath, bindingPath + ".target", "pick_region bindings must target regions.* selectors.");
                    }

                    if (HasErrorsForRow(bindingPath) || !pattern.HasValue)
                    {
                        continue;
                    }

                    variables.Add(new EventRegionBinding(property.Name, mode, pattern.Value));
                }
                else if (bindText == "pick_ig")
                {
                    ValidateUnknownProperties(EventsPath, bindingObject, bindingPath, new[] { "bind", "mode", "target" });
                    string modeText = RequiredString(EventsPath, bindingObject, "mode", bindingPath + ".mode", nonEmpty: true);
                    EventSelectorMode mode;
                    if (!TryMapSelectorMode(modeText, out mode))
                    {
                        Add(ContentDiagnosticCode.InvalidEnum, EventsPath, bindingPath + ".mode", "mode must be ARGMAX or WEIGHTED.");
                    }

                    TargetPattern? pattern = RequiredTargetPattern(EventsPath, bindingObject, "target", bindingPath + ".target", catalog, allowStaticRegionalReadOnly: true);
                    if (pattern.HasValue && !pattern.Value.ToString().StartsWith("igs.*.", StringComparison.Ordinal))
                    {
                        Add(ContentDiagnosticCode.InvalidReference, EventsPath, bindingPath + ".target", "pick_ig bindings must target igs.* selectors.");
                    }

                    if (HasErrorsForRow(bindingPath) || !pattern.HasValue)
                    {
                        continue;
                    }

                    variables.Add(new EventInterestGroupBinding(property.Name, mode, pattern.Value));
                }
                else if (bindText == "severity_from")
                {
                    ValidateUnknownProperties(EventsPath, bindingObject, bindingPath, new[] { "bind", "target", "bands" });
                    TargetPath? target = RequiredTargetPath(EventsPath, bindingObject, "target", bindingPath + ".target", catalog, allowMutation: false, requiredOperation: null);
                    if (target.HasValue && !target.Value.ToString().StartsWith("metrics.", StringComparison.Ordinal))
                    {
                        Add(ContentDiagnosticCode.InvalidReference, EventsPath, bindingPath + ".target", "severity_from bindings must target concrete metrics.* values.");
                    }

                    JArray bandsArray = RequiredArray(EventsPath, bindingObject, "bands", bindingPath + ".bands");
                    List<EventSeverityBand> bands = LoadSeverityBands(bandsArray, bindingPath + ".bands");
                    if (HasErrorsForRow(bindingPath) || !target.HasValue)
                    {
                        continue;
                    }

                    variables.Add(new EventSeverityBinding(property.Name, target.Value, bands));
                }
                else
                {
                    ValidateUnknownProperties(EventsPath, bindingObject, bindingPath, new[] { "bind", "mode", "target", "bands" });
                    Add(ContentDiagnosticCode.InvalidEnum, EventsPath, bindingPath + ".bind", "bind must be pick_region, pick_ig, or severity_from.");
                }
            }

            return variables;
        }

        private List<EventSeverityBand> LoadSeverityBands(JArray bandsArray, string jsonPath)
        {
            List<EventSeverityBand> result = new List<EventSeverityBand>();
            if (bandsArray == null)
            {
                return result;
            }

            if (bandsArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, EventsPath, jsonPath, "bands must not be empty.");
                return result;
            }

            int? previousMax = null;
            for (int i = 0; i < bandsArray.Count; i++)
            {
                string bandPath = jsonPath + "[" + i + "]";
                JArray bandArray = bandsArray[i] as JArray;
                if (bandArray == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, bandPath, "Band must be an array.");
                    continue;
                }

                if (bandArray.Count != 3)
                {
                    Add(ContentDiagnosticCode.InvalidValue, EventsPath, bandPath, "Band must contain exactly three integers.");
                    continue;
                }

                int? minValueS = ReadInt(EventsPath, bandArray[0], bandPath + "[0]");
                int? maxValueS = ReadInt(EventsPath, bandArray[1], bandPath + "[1]");
                int? severity = ReadInt(EventsPath, bandArray[2], bandPath + "[2]");
                if (minValueS.HasValue && maxValueS.HasValue)
                {
                    if (minValueS.Value > maxValueS.Value)
                    {
                        Add(ContentDiagnosticCode.InvalidRange, EventsPath, bandPath, "Band minimum must be less than or equal to maximum.");
                    }

                    if (previousMax.HasValue && minValueS.Value <= previousMax.Value)
                    {
                        Add(ContentDiagnosticCode.InvalidRange, EventsPath, bandPath, "Bands must be strictly ordered and must not overlap.");
                    }
                }

                if (severity.HasValue && severity.Value <= 0)
                {
                    Add(ContentDiagnosticCode.InvalidRange, EventsPath, bandPath + "[2]", "Band severity must be positive.");
                }

                if (!minValueS.HasValue || !maxValueS.HasValue || !severity.HasValue || HasErrorsForRow(bandPath))
                {
                    continue;
                }

                previousMax = maxValueS.Value;
                result.Add(new EventSeverityBand(minValueS.Value, maxValueS.Value, severity.Value));
            }

            return result;
        }

        private EventConditionNode LoadEventConditionNode(JObject node, string jsonPath, TargetConfigCatalog catalog, HashSet<string> movementIds)
        {
            if (node == null)
            {
                return new EventConditionAllNode(Array.Empty<EventConditionNode>());
            }

            int propertyCount = 0;
            string propertyName = null;
            foreach (JProperty property in node.Properties())
            {
                propertyCount++;
                propertyName = property.Name;
            }

            if (propertyCount != 1 || propertyName == null)
            {
                Add(ContentDiagnosticCode.InvalidConditionShape, EventsPath, jsonPath, "Condition node must contain exactly one variant.");
                return new EventConditionAllNode(Array.Empty<EventConditionNode>());
            }

            if (propertyName == "all")
            {
                JArray childArray = node[propertyName] as JArray;
                if (childArray == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, jsonPath + ".all", "all must be an array.");
                    return new EventConditionAllNode(Array.Empty<EventConditionNode>());
                }

                List<EventConditionNode> children = new List<EventConditionNode>();
                for (int i = 0; i < childArray.Count; i++)
                {
                    string childPath = jsonPath + ".all[" + i + "]";
                    JObject childObject = childArray[i] as JObject;
                    if (childObject == null)
                    {
                        Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, childPath, "Condition child must be an object.");
                        continue;
                    }

                    children.Add(LoadEventConditionNode(childObject, childPath, catalog, movementIds));
                }

                return new EventConditionAllNode(children);
            }

            if (propertyName == "cmp")
            {
                JObject cmpObject = node[propertyName] as JObject;
                if (cmpObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, jsonPath + ".cmp", "cmp must be an object.");
                    return new EventConditionAllNode(Array.Empty<EventConditionNode>());
                }

                ValidateUnknownProperties(EventsPath, cmpObject, jsonPath + ".cmp", new[] { "target", "op", "value" });
                TargetPath? target = RequiredTargetPath(EventsPath, cmpObject, "target", jsonPath + ".cmp.target", catalog, allowMutation: false, requiredOperation: null);
                string opText = RequiredString(EventsPath, cmpObject, "op", jsonPath + ".cmp.op", nonEmpty: true);
                int? value = RequiredInt(EventsPath, cmpObject, "value", jsonPath + ".cmp.value");
                EventComparator comparator;
                if (!TryMapComparator(opText, out comparator))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, EventsPath, jsonPath + ".cmp.op", "Comparator is not supported.");
                }

                return target.HasValue && value.HasValue && !HasErrorsForRow(jsonPath + ".cmp")
                    ? new EventConditionCompareTargetNode(target.Value, comparator, value.Value)
                    : new EventConditionAllNode(Array.Empty<EventConditionNode>());
            }

            if (propertyName == "movement_cmp")
            {
                JObject cmpObject = node[propertyName] as JObject;
                if (cmpObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, jsonPath + ".movement_cmp", "movement_cmp must be an object.");
                    return new EventConditionAllNode(Array.Empty<EventConditionNode>());
                }

                ValidateUnknownProperties(EventsPath, cmpObject, jsonPath + ".movement_cmp", new[] { "movement_id", "op", "value" });
                string movementId = RequiredString(EventsPath, cmpObject, "movement_id", jsonPath + ".movement_cmp.movement_id", nonEmpty: true);
                string opText = RequiredString(EventsPath, cmpObject, "op", jsonPath + ".movement_cmp.op", nonEmpty: true);
                int? value = RequiredInt(EventsPath, cmpObject, "value", jsonPath + ".movement_cmp.value");
                if (movementId != null && !movementIds.Contains(movementId))
                {
                    Add(ContentDiagnosticCode.InvalidReference, EventsPath, jsonPath + ".movement_cmp.movement_id", "Unknown movement id " + movementId + ".");
                }

                EventComparator comparator;
                if (!TryMapComparator(opText, out comparator))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, EventsPath, jsonPath + ".movement_cmp.op", "Comparator is not supported.");
                }

                return movementId != null && value.HasValue && !HasErrorsForRow(jsonPath + ".movement_cmp")
                    ? new EventConditionCompareMovementNode(movementId, comparator, value.Value)
                    : new EventConditionAllNode(Array.Empty<EventConditionNode>());
            }

            if (propertyName == "flag_is_set")
            {
                string flagId = node[propertyName]?.Type == JTokenType.String ? node[propertyName].Value<string>() : null;
                if (flagId == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, jsonPath + ".flag_is_set", "flag_is_set must be a string.");
                    return new EventConditionAllNode(Array.Empty<EventConditionNode>());
                }

                ValidateFlagId(flagId, jsonPath + ".flag_is_set");
                return new EventConditionFlagIsSetNode(flagId);
            }

            if (propertyName == "cooldown_ready")
            {
                if (node[propertyName].Type != JTokenType.Boolean || !node[propertyName].Value<bool>())
                {
                    Add(ContentDiagnosticCode.InvalidConditionShape, EventsPath, jsonPath + ".cooldown_ready", "cooldown_ready must be the literal boolean true.");
                }

                return new EventConditionCooldownReadyNode();
            }

            if (propertyName == "max_count_not_reached")
            {
                if (node[propertyName].Type != JTokenType.Boolean || !node[propertyName].Value<bool>())
                {
                    Add(ContentDiagnosticCode.InvalidConditionShape, EventsPath, jsonPath + ".max_count_not_reached", "max_count_not_reached must be the literal boolean true.");
                }

                return new EventConditionMaxCountNotReachedNode();
            }

            Add(ContentDiagnosticCode.InvalidConditionShape, EventsPath, jsonPath, "Unsupported condition variant " + propertyName + ".");
            return new EventConditionAllNode(Array.Empty<EventConditionNode>());
        }

        private List<EventOption> LoadEventOptions(
            JArray optionsArray,
            string jsonPath,
            TargetConfigCatalog catalog,
            HashSet<string> movementIds,
            ContentLocalizationTable localization,
            HashSet<string> effectIds)
        {
            List<EventOption> result = new List<EventOption>();
            HashSet<string> optionIds = new HashSet<string>(StringComparer.Ordinal);
            if (optionsArray == null)
            {
                return result;
            }

            if (optionsArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, EventsPath, jsonPath, "options must not be empty.");
            }

            for (int i = 0; i < optionsArray.Count; i++)
            {
                string optionPath = jsonPath + "[" + i + "]";
                JObject optionObject = optionsArray[i] as JObject;
                if (optionObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, optionPath, "Option must be an object.");
                    continue;
                }

                ValidateUnknownProperties(EventsPath, optionObject, optionPath, new[] { "id", "loc_label", "requirements", "effects", "memory", "followups" });
                string id = RequiredString(EventsPath, optionObject, "id", optionPath + ".id", nonEmpty: true);
                string locLabel = RequiredString(EventsPath, optionObject, "loc_label", optionPath + ".loc_label", nonEmpty: true);
                JObject requirementsObject = RequiredObject(EventsPath, optionObject, "requirements", optionPath + ".requirements");
                JArray effectsArray = RequiredArray(EventsPath, optionObject, "effects", optionPath + ".effects");
                JObject memoryObject = RequiredObject(EventsPath, optionObject, "memory", optionPath + ".memory");
                JArray followupsArray = OptionalArray(EventsPath, optionObject, "followups", optionPath + ".followups");

                if (id != null)
                {
                    if (!IsAsciiLowerSnake(id))
                    {
                        Add(ContentDiagnosticCode.InvalidId, EventsPath, optionPath + ".id", "Option id must be ASCII lowercase snake_case.");
                    }
                    else if (!optionIds.Add(id))
                    {
                        Add(ContentDiagnosticCode.DuplicateId, EventsPath, optionPath + ".id", "Duplicate option id " + id + ".");
                    }
                }

                if (localization != null && locLabel != null && !localization.TryResolve(locLabel, out _))
                {
                    Add(ContentDiagnosticCode.MissingLocalizationKey, EventsPath, optionPath + ".loc_label", "Missing localization key " + locLabel + ".");
                }

                EventConditionNode requirements = LoadEventConditionNode(requirementsObject, optionPath + ".requirements", catalog, movementIds);
                List<EffectTemplateInvocation> effects = LoadEffectInvocations(effectsArray, optionPath + ".effects", effectIds, requirePositiveDuration: true, allowEmpty: false, file: EventsPath);
                EventMemoryMutation memory = LoadEventMemoryMutation(memoryObject, optionPath + ".memory");
                List<EventFollowup> followups = LoadEventFollowups(followupsArray, optionPath + ".followups");
                if (HasErrorsForRow(optionPath) || id == null || locLabel == null || memory == null)
                {
                    continue;
                }

                result.Add(new EventOption(id, locLabel, requirements, effects, memory, followups));
            }

            return result;
        }

        private EventMemoryMutation LoadEventMemoryMutation(JObject memoryObject, string jsonPath)
        {
            if (memoryObject == null)
            {
                return null;
            }

            ValidateUnknownProperties(EventsPath, memoryObject, jsonPath, new[] { "set_flag", "clear_flag", "set_cooldown" });
            JArray setFlagArray = OptionalArray(EventsPath, memoryObject, "set_flag", jsonPath + ".set_flag");
            JArray clearFlagArray = OptionalArray(EventsPath, memoryObject, "clear_flag", jsonPath + ".clear_flag");
            bool setCooldown = OptionalBool(memoryObject, "set_cooldown", jsonPath + ".set_cooldown", EventsPath) ?? false;
            List<string> setFlags = LoadFlagArray(setFlagArray, jsonPath + ".set_flag");
            List<string> clearFlags = LoadFlagArray(clearFlagArray, jsonPath + ".clear_flag");
            HashSet<string> conflicts = new HashSet<string>(setFlags, StringComparer.Ordinal);
            conflicts.IntersectWith(clearFlags);
            foreach (string conflict in conflicts)
            {
                Add(ContentDiagnosticCode.InvalidValue, EventsPath, jsonPath, "Flag " + conflict + " cannot be both set and cleared in the same option.");
            }

            return new EventMemoryMutation(setFlags, clearFlags, setCooldown);
        }

        private List<EventFollowup> LoadEventFollowups(JArray followupsArray, string jsonPath)
        {
            List<EventFollowup> result = new List<EventFollowup>();
            if (followupsArray == null)
            {
                return result;
            }

            for (int i = 0; i < followupsArray.Count; i++)
            {
                string followupPath = jsonPath + "[" + i + "]";
                JObject followupObject = followupsArray[i] as JObject;
                if (followupObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, followupPath, "Followup must be an object.");
                    continue;
                }

                ValidateUnknownProperties(EventsPath, followupObject, followupPath, new[] { "after_weeks", "event_id" });
                int? afterWeeks = RequiredInt(EventsPath, followupObject, "after_weeks", followupPath + ".after_weeks");
                string eventId = RequiredString(EventsPath, followupObject, "event_id", followupPath + ".event_id", nonEmpty: true);
                ValidatePositive(EventsPath, followupPath + ".after_weeks", afterWeeks, "after_weeks");
                if (HasErrorsForRow(followupPath) || !afterWeeks.HasValue || eventId == null)
                {
                    continue;
                }

                result.Add(new EventFollowup(afterWeeks.Value, eventId));
            }

            return result;
        }

        private List<ReformInterestGroupStance> LoadExplicitInterestGroupStances(JObject stancesObject, string jsonPath, HashSet<string> interestGroupIds)
        {
            List<ReformInterestGroupStance> result = new List<ReformInterestGroupStance>();
            if (stancesObject == null)
            {
                return result;
            }

            foreach (JProperty property in stancesObject.Properties())
            {
                string stancePath = jsonPath + "." + property.Name;
                int? stance = ReadInt(ReformsPath, property.Value, stancePath);
                if (!interestGroupIds.Contains(property.Name))
                {
                    Add(ContentDiagnosticCode.InvalidReference, ReformsPath, stancePath, "Unknown interest group id " + property.Name + ".");
                }

                if (stance.HasValue && (stance.Value < -100 || stance.Value > 100))
                {
                    Add(ContentDiagnosticCode.InvalidRange, ReformsPath, stancePath, "Stance must be in [-100, 100].");
                }

                if (!stance.HasValue)
                {
                    continue;
                }

                result.Add(new ReformInterestGroupStance(property.Name, stance.Value));
            }

            return result;
        }

        private List<ReformPrerequisite> LoadReformPrerequisites(JArray prereqArray, string jsonPath, TargetConfigCatalog catalog)
        {
            List<ReformPrerequisite> result = new List<ReformPrerequisite>();
            if (prereqArray == null)
            {
                return result;
            }

            for (int i = 0; i < prereqArray.Count; i++)
            {
                string prereqPath = jsonPath + "[" + i + "]";
                JObject prereqObject = prereqArray[i] as JObject;
                if (prereqObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, ReformsPath, prereqPath, "Prerequisite must be an object.");
                    continue;
                }

                ValidateUnknownProperties(ReformsPath, prereqObject, prereqPath, new[] { "type", "target", "op", "valueS" });
                string typeText = RequiredString(ReformsPath, prereqObject, "type", prereqPath + ".type", nonEmpty: true);
                string opText = RequiredString(ReformsPath, prereqObject, "op", prereqPath + ".op", nonEmpty: true);
                TargetPath? target = RequiredTargetPath(ReformsPath, prereqObject, "target", prereqPath + ".target", catalog, allowMutation: false, requiredOperation: null);
                int? valueS = RequiredInt(ReformsPath, prereqObject, "valueS", prereqPath + ".valueS");
                ReformPrerequisiteType type;
                if (!TryMapReformPrerequisiteType(typeText, out type))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, ReformsPath, prereqPath + ".type", "Prerequisite type must be METRIC.");
                }

                EventComparator comparator;
                if (!TryMapComparator(opText, out comparator))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, ReformsPath, prereqPath + ".op", "Comparator is not supported.");
                }

                if (HasErrorsForRow(prereqPath) || !target.HasValue || !valueS.HasValue)
                {
                    continue;
                }

                result.Add(new ReformPrerequisite(type, target.Value, comparator, valueS.Value));
            }

            return result;
        }

        private List<ReformStage> LoadReformStages(JArray stagesArray, string jsonPath, LegislativeConfig legislativeConfig)
        {
            List<ReformStage> result = new List<ReformStage>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (stagesArray == null)
            {
                return result;
            }

            if (stagesArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, ReformsPath, jsonPath, "stages must not be empty.");
                return result;
            }

            if (legislativeConfig != null && stagesArray.Count > legislativeConfig.Limits.MaxStages)
            {
                Add(ContentDiagnosticCode.InvalidRange, ReformsPath, jsonPath, "Stage count exceeds legislative_config.limits.max_stages.");
            }

            for (int i = 0; i < stagesArray.Count; i++)
            {
                string stagePath = jsonPath + "[" + i + "]";
                JObject stageObject = stagesArray[i] as JObject;
                if (stageObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, ReformsPath, stagePath, "Stage must be an object.");
                    continue;
                }

                ValidateUnknownProperties(ReformsPath, stageObject, stagePath, new[] { "id", "kind", "chamber", "weightS" });
                string id = RequiredString(ReformsPath, stageObject, "id", stagePath + ".id", nonEmpty: true);
                string kindText = RequiredString(ReformsPath, stageObject, "kind", stagePath + ".kind", nonEmpty: true);
                string chamberText = RequiredString(ReformsPath, stageObject, "chamber", stagePath + ".chamber", nonEmpty: true);
                int? weightS = RequiredInt(ReformsPath, stageObject, "weightS", stagePath + ".weightS");
                ReformStageKind kind;
                if (!TryMapReformStageKind(kindText, out kind))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, ReformsPath, stagePath + ".kind", "Stage kind must be WORK or VOTE.");
                }

                ReformStageChamber chamber;
                if (!TryMapReformStageChamber(chamberText, out chamber))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, ReformsPath, stagePath + ".chamber", "Stage chamber must be NONE, LOWER, UPPER, or BOTH.");
                }

                if (id != null)
                {
                    if (!IsAsciiLowerSnake(id))
                    {
                        Add(ContentDiagnosticCode.InvalidId, ReformsPath, stagePath + ".id", "Stage id must be ASCII lowercase snake_case.");
                    }
                    else if (!ids.Add(id))
                    {
                        Add(ContentDiagnosticCode.DuplicateId, ReformsPath, stagePath + ".id", "Duplicate stage id " + id + ".");
                    }
                }

                ValidatePositive(ReformsPath, stagePath + ".weightS", weightS, "weightS");
                if (kind == ReformStageKind.Vote && chamber == ReformStageChamber.None)
                {
                    Add(ContentDiagnosticCode.InvalidValue, ReformsPath, stagePath + ".chamber", "VOTE stages cannot use chamber NONE.");
                }

                if (HasErrorsForRow(stagePath) || id == null || !weightS.HasValue)
                {
                    continue;
                }

                result.Add(new ReformStage(id, kind, chamber, weightS.Value));
            }

            return result;
        }

        private List<EffectTemplateInvocation> LoadEffectInvocations(JArray effectsArray, string jsonPath, HashSet<string> effectIds, bool requirePositiveDuration, bool allowEmpty, string file)
        {
            List<EffectTemplateInvocation> result = new List<EffectTemplateInvocation>();
            if (effectsArray == null)
            {
                return result;
            }

            if (!allowEmpty && effectsArray.Count == 0)
            {
                Add(ContentDiagnosticCode.InvalidValue, file, jsonPath, "effects must not be empty.");
            }

            for (int i = 0; i < effectsArray.Count; i++)
            {
                string effectPath = jsonPath + "[" + i + "]";
                JObject effectObject = effectsArray[i] as JObject;
                if (effectObject == null)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, file, effectPath, "Effect invocation must be an object.");
                    continue;
                }

                ValidateUnknownProperties(file, effectObject, effectPath, new[] { "type", "template_id", "duration_weeks" });
                string typeText = RequiredString(file, effectObject, "type", effectPath + ".type", nonEmpty: true);
                string templateId = RequiredString(file, effectObject, "template_id", effectPath + ".template_id", nonEmpty: true);
                int? durationWeeks = RequiredInt(file, effectObject, "duration_weeks", effectPath + ".duration_weeks");
                EffectInvocationType type;
                if (!TryMapEffectInvocationType(typeText, out type))
                {
                    Add(ContentDiagnosticCode.InvalidEnum, file, effectPath + ".type", "Effect invocation type must be MODIFIER.");
                }

                if (templateId != null && !effectIds.Contains(templateId))
                {
                    Add(ContentDiagnosticCode.InvalidReference, file, effectPath + ".template_id", "Unknown effect template id " + templateId + ".");
                }

                if (requirePositiveDuration)
                {
                    ValidatePositive(file, effectPath + ".duration_weeks", durationWeeks, "duration_weeks");
                }

                if (HasErrorsForRow(effectPath) || templateId == null || !durationWeeks.HasValue)
                {
                    continue;
                }

                result.Add(new EffectTemplateInvocation(type, templateId, durationWeeks.Value));
            }

            return result;
        }

        private void ValidateEventCrossReferences(IReadOnlyList<EventTemplate> events)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EventTemplate template in events)
            {
                ids.Add(template.Id);
            }

            for (int i = 0; i < events.Count; i++)
            {
                EventTemplate template = events[i];
                string rowPath = "$.events[" + i + "]";
                if (template.AutoOptionId != null && !template.OptionsById.ContainsKey(template.AutoOptionId))
                {
                    Add(ContentDiagnosticCode.InvalidReference, EventsPath, rowPath + ".auto_option_id", "auto_option_id does not exist within options.");
                }

                for (int optionIndex = 0; optionIndex < template.Options.Count; optionIndex++)
                {
                    EventOption option = template.Options[optionIndex];
                    for (int followupIndex = 0; followupIndex < option.Followups.Count; followupIndex++)
                    {
                        EventFollowup followup = option.Followups[followupIndex];
                        if (!ids.Contains(followup.EventId))
                        {
                            Add(ContentDiagnosticCode.InvalidReference, EventsPath, rowPath + ".options[" + optionIndex + "].followups[" + followupIndex + "].event_id", "Unknown followup event id " + followup.EventId + ".");
                        }
                    }
                }
            }
        }

        private void ValidateThemeTags(IEnumerable<string> tags, HashSet<string> allowedThemeTags, string file, string jsonPath)
        {
            int index = 0;
            foreach (string tag in tags)
            {
                if (!allowedThemeTags.Contains(tag))
                {
                    Add(ContentDiagnosticCode.InvalidReference, file, jsonPath + "[" + index + "]", "Unknown theme tag " + tag + ".");
                }

                index++;
            }
        }

        private void ValidateAllowedTags(IEnumerable<string> tags, IEnumerable<string> vocabulary, string file, string jsonPath, string label)
        {
            int index = 0;
            foreach (string tag in tags)
            {
                if (!Contains(vocabulary, tag))
                {
                    Add(ContentDiagnosticCode.InvalidReference, file, jsonPath + "[" + index + "]", "Unknown " + label + " " + tag + ".");
                }

                index++;
            }
        }

        private List<string> LoadFlagArray(JArray flagsArray, string jsonPath)
        {
            List<string> flags = new List<string>();
            if (flagsArray == null)
            {
                return flags;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < flagsArray.Count; i++)
            {
                string flagPath = jsonPath + "[" + i + "]";
                if (flagsArray[i].Type != JTokenType.String)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, EventsPath, flagPath, "Flag must be a string.");
                    continue;
                }

                string flagId = flagsArray[i].Value<string>();
                ValidateFlagId(flagId, flagPath);
                if (!seen.Add(flagId))
                {
                    Add(ContentDiagnosticCode.InvalidValue, EventsPath, flagPath, "Duplicate flag " + flagId + ".");
                    continue;
                }

                flags.Add(flagId);
            }

            return flags;
        }

        private void ValidateFlagId(string flagId, string jsonPath)
        {
            if (string.IsNullOrEmpty(flagId) || !IsDottedLowercase(flagId))
            {
                Add(ContentDiagnosticCode.InvalidFlagFormat, EventsPath, jsonPath, "Flag ids must use ASCII lowercase dotted format namespace.value.");
            }
        }

        private bool ContainsRegionBinding(IEnumerable<EventVariableBinding> bindings)
        {
            foreach (EventVariableBinding binding in bindings)
            {
                if (binding is EventRegionBinding)
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> CollectMovementIds(IEnumerable<MovementDefinition> movements)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (MovementDefinition movement in movements)
            {
                ids.Add(movement.Id);
            }

            return ids;
        }

        private static HashSet<string> CollectInterestGroupIds(IEnumerable<InterestGroupDefinition> interestGroups)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (InterestGroupDefinition interestGroup in interestGroups)
            {
                ids.Add(interestGroup.Id);
            }

            return ids;
        }

        private static HashSet<string> CollectEffectIds(IEnumerable<EffectTemplate> effects)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EffectTemplate effect in effects)
            {
                ids.Add(effect.Id);
            }

            return ids;
        }

        private static HashSet<string> CollectMovementTagVocabulary(IEnumerable<MovementDefinition> movements)
        {
            HashSet<string> tags = new HashSet<string>(StringComparer.Ordinal);
            foreach (MovementDefinition movement in movements)
            {
                foreach (string tag in movement.Tags)
                {
                    tags.Add(tag);
                }
            }

            return tags;
        }

        private static HashSet<string> CollectAllowedEventThemeTags(IEnumerable<MovementDefinition> movements)
        {
            HashSet<string> tags = CollectMovementTagVocabulary(movements);
            tags.Add("theme.economia");
            return tags;
        }

        private static readonly string[] AllowedReformAreas =
        {
            "constitucional",
            "educacion",
            "institucional",
            "pensiones",
            "salud",
            "seguridad",
            "trabajo"
        };

        private JArray OptionalArray(string file, JObject obj, string propertyName, string jsonPath)
        {
            if (!obj.TryGetValue(propertyName, out JToken token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            JArray result = token as JArray;
            if (result == null)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected array.");
            }

            return result;
        }

        private bool? OptionalBool(JObject obj, string propertyName, string jsonPath, string file)
        {
            if (!obj.TryGetValue(propertyName, out JToken token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type != JTokenType.Boolean)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected boolean.");
                return null;
            }

            return token.Value<bool>();
        }

        private static class ReformContentCompiler
        {
            private static readonly string[] OrderedPolicyTagsArray =
            {
                "policy.tax_cut",
                "policy.tax_increase",
                "policy.spending_increase",
                "policy.spending_cut",
                "policy.deregulation",
                "policy.regulation_increase",
                "policy.labor_rights_up",
                "policy.labor_flexibility",
                "policy.security_crackdown",
                "policy.police_accountability",
                "policy.environment_protection",
                "policy.extractive_promotion",
                "policy.decentralization",
                "policy.centralization",
                "policy.social_traditional",
                "policy.social_progressive",
                "policy.anti_corruption",
                "policy.institutional_reform",
                "policy.indigenous_recognition"
            };

            private static readonly string[] OrderedIdeologyTagsArray =
            {
                "ideol.market",
                "ideol.fiscal_austerity",
                "ideol.public_spending",
                "ideol.labor",
                "ideol.extractive_growth",
                "ideol.statist",
                "ideol.green",
                "ideol.security_hardline",
                "ideol.social_traditional",
                "ideol.civil_liberties",
                "ideol.social_progressive",
                "ideol.indigenous_rights",
                "ideol.decentralization",
                "ideol.centralization",
                "ideol.institutionalism",
                "ideol.anti_corruption"
            };

            private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> PolicyScoreTable = BuildPolicyScoreTable();
            private static readonly HashSet<string> IdeologyTagsSet = new HashSet<string>(OrderedIdeologyTagsArray, StringComparer.Ordinal);

            internal static IReadOnlyCollection<string> PolicyTags => OrderedPolicyTagsArray;

            internal static List<ReformInterestGroupStance> TryCompileEffectiveStances(
                IReadOnlyList<InterestGroupDefinition> interestGroups,
                IReadOnlyList<ReformInterestGroupStance> explicitStances,
                IReadOnlyList<string> policyTags,
                out string error)
            {
                if (interestGroups == null)
                {
                    throw new ArgumentNullException(nameof(interestGroups));
                }

                if (explicitStances == null)
                {
                    throw new ArgumentNullException(nameof(explicitStances));
                }

                if (policyTags == null)
                {
                    throw new ArgumentNullException(nameof(policyTags));
                }

                Dictionary<string, int> explicitById = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (ReformInterestGroupStance explicitStance in explicitStances)
                {
                    explicitById[explicitStance.InterestGroupId] = explicitStance.Stance;
                }

                List<ReformInterestGroupStance> result = new List<ReformInterestGroupStance>(interestGroups.Count);
                foreach (InterestGroupDefinition interestGroup in interestGroups)
                {
                    if (explicitById.TryGetValue(interestGroup.Id, out int explicitValue))
                    {
                        result.Add(new ReformInterestGroupStance(interestGroup.Id, explicitValue));
                        continue;
                    }

                    long stance = 0;
                    foreach (string policyTag in policyTags)
                    {
                        if (!PolicyScoreTable.TryGetValue(policyTag, out IReadOnlyDictionary<string, int> scores))
                        {
                            error = "Unknown policy tag " + policyTag + " in stance compiler.";
                            return null;
                        }

                        foreach (string ideologicalTag in interestGroup.Tags)
                        {
                            if (!IdeologyTagsSet.Contains(ideologicalTag))
                            {
                                error = "Unknown ideological tag " + ideologicalTag + " in interest group " + interestGroup.Id + ".";
                                return null;
                            }

                            if (scores.TryGetValue(ideologicalTag, out int delta))
                            {
                                stance += delta;
                            }
                        }
                    }

                    result.Add(new ReformInterestGroupStance(interestGroup.Id, ClampStance(stance)));
                }

                if (result.Count != interestGroups.Count)
                {
                    error = "Compiled reform stance mapping did not cover every declared interest group.";
                    return null;
                }

                error = null;
                return result;
            }

            private static int ClampStance(long value)
            {
                if (value < -100)
                {
                    return -100;
                }

                if (value > 100)
                {
                    return 100;
                }

                return (int)value;
            }

            private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> BuildPolicyScoreTable()
            {
                Dictionary<string, IReadOnlyDictionary<string, int>> table = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
                {
                    { "policy.tax_cut", Scores(("ideol.market", 25), ("ideol.fiscal_austerity", 15), ("ideol.public_spending", -15), ("ideol.labor", -10)) },
                    { "policy.tax_increase", Scores(("ideol.public_spending", 15), ("ideol.labor", 10), ("ideol.market", -20), ("ideol.fiscal_austerity", -10)) },
                    { "policy.spending_increase", Scores(("ideol.public_spending", 25), ("ideol.statist", 10), ("ideol.fiscal_austerity", -20), ("ideol.market", -10)) },
                    { "policy.spending_cut", Scores(("ideol.fiscal_austerity", 25), ("ideol.market", 10), ("ideol.public_spending", -20), ("ideol.statist", -10)) },
                    { "policy.deregulation", Scores(("ideol.market", 25), ("ideol.extractive_growth", 10), ("ideol.statist", -15), ("ideol.green", -10)) },
                    { "policy.regulation_increase", Scores(("ideol.statist", 15), ("ideol.green", 10), ("ideol.market", -20), ("ideol.extractive_growth", -10)) },
                    { "policy.labor_rights_up", Scores(("ideol.labor", 30), ("ideol.public_spending", 5), ("ideol.market", -25)) },
                    { "policy.labor_flexibility", Scores(("ideol.market", 20), ("ideol.labor", -30)) },
                    { "policy.security_crackdown", Scores(("ideol.security_hardline", 30), ("ideol.social_traditional", 10), ("ideol.civil_liberties", -25), ("ideol.social_progressive", -10)) },
                    { "policy.police_accountability", Scores(("ideol.civil_liberties", 25), ("ideol.social_progressive", 15), ("ideol.security_hardline", -25)) },
                    { "policy.environment_protection", Scores(("ideol.green", 30), ("ideol.indigenous_rights", 10), ("ideol.extractive_growth", -25), ("ideol.market", -5)) },
                    { "policy.extractive_promotion", Scores(("ideol.extractive_growth", 30), ("ideol.market", 10), ("ideol.green", -25), ("ideol.indigenous_rights", -10)) },
                    { "policy.decentralization", Scores(("ideol.decentralization", 30), ("ideol.indigenous_rights", 10), ("ideol.centralization", -25)) },
                    { "policy.centralization", Scores(("ideol.centralization", 30), ("ideol.institutionalism", 5), ("ideol.decentralization", -25)) },
                    { "policy.social_traditional", Scores(("ideol.social_traditional", 30), ("ideol.social_progressive", -25)) },
                    { "policy.social_progressive", Scores(("ideol.social_progressive", 30), ("ideol.civil_liberties", 10), ("ideol.social_traditional", -25)) },
                    { "policy.anti_corruption", Scores(("ideol.anti_corruption", 30), ("ideol.institutionalism", 10)) },
                    { "policy.institutional_reform", Scores(("ideol.institutionalism", 25), ("ideol.anti_corruption", 10), ("ideol.statist", 5)) },
                    { "policy.indigenous_recognition", Scores(("ideol.indigenous_rights", 30), ("ideol.decentralization", 10), ("ideol.centralization", -10), ("ideol.social_traditional", -5)) }
                };

                return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>(table);
            }

            private static IReadOnlyDictionary<string, int> Scores(params (string tag, int value)[] values)
            {
                Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < values.Length; i++)
                {
                    result.Add(values[i].tag, values[i].value);
                }

                return new ReadOnlyDictionary<string, int>(result);
            }
        }

        private byte[] ReadRequired(IContentFileSource source, string path, ContentDiagnosticCode missingCode)
        {
            ContentFileReadResult read = source.TryReadAllBytes(path);
            if (read.Success)
            {
                return read.GetBytesCopy();
            }

            Add(read.NotFound ? missingCode : ContentDiagnosticCode.SourceReadFailed, path, string.Empty, read.ErrorMessage);
            return null;
        }

        private JObject ParseObject(string file, byte[] bytes)
        {
            JToken token = ParseJson(file, bytes);
            if (token == null)
            {
                return null;
            }

            JObject obj = token as JObject;
            if (obj == null)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, "$", "JSON root must be an object.");
            }

            return obj;
        }

        private JArray ParseArray(string file, byte[] bytes)
        {
            JToken token = ParseJson(file, bytes);
            if (token == null)
            {
                return null;
            }

            JArray array = token as JArray;
            if (array == null)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, "$", "JSON root must be an array.");
            }

            return array;
        }

        private JToken ParseJson(string file, byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                Add(ContentDiagnosticCode.InvalidUtf8, file, "$", ex.Message);
                return null;
            }

            try
            {
                if (RejectsComments(file, text))
                {
                    return null;
                }

                JsonLoadSettings settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Ignore
                };
                JsonTextReader reader = new JsonTextReader(new StringReader(text))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    MaxDepth = 64,
                    SupportMultipleContent = false
                };
                JToken token = JToken.ReadFrom(reader, settings);
                if (ContainsComment(token))
                {
                    Add(ContentDiagnosticCode.JsonMalformed, file, "$", "JSON comments are not allowed.");
                    return null;
                }

                if (reader.Read())
                {
                    Add(ContentDiagnosticCode.JsonMalformed, file, "$", "Trailing JSON content is not allowed.");
                    return null;
                }

                return token;
            }
            catch (JsonReaderException ex)
            {
                ContentDiagnosticCode code = ex.Message.IndexOf("Property with the name", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ContentDiagnosticCode.DuplicateJsonProperty
                    : ContentDiagnosticCode.JsonMalformed;
                Add(code, file, "$", ex.Message);
                return null;
            }
        }

        private bool RejectsComments(string file, string text)
        {
            try
            {
                JsonTextReader reader = new JsonTextReader(new StringReader(text))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    MaxDepth = 64,
                    SupportMultipleContent = true
                };

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.Comment)
                    {
                        Add(ContentDiagnosticCode.JsonMalformed, file, "$", "JSON comments are not allowed.");
                        return true;
                    }
                }

                return false;
            }
            catch (JsonReaderException ex)
            {
                Add(ContentDiagnosticCode.JsonMalformed, file, "$", ex.Message);
                return true;
            }
        }

        private static bool ContainsComment(JToken token)
        {
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Comment)
            {
                return true;
            }

            JContainer container = token as JContainer;
            if (container == null)
            {
                return false;
            }

            foreach (JToken child in container.Children())
            {
                if (ContainsComment(child))
                {
                    return true;
                }
            }

            return false;
        }

        private void ValidateUnknownProperties(string file, JObject obj, string jsonPath, string[] known)
        {
            foreach (JProperty property in obj.Properties())
            {
                if (!Contains(known, property.Name))
                {
                    Add(ContentDiagnosticCode.UnknownProperty, file, jsonPath + "." + property.Name, $"Unknown property {property.Name}.");
                }
            }
        }

        private string RequiredString(string file, JObject obj, string propertyName, string jsonPath, bool nonEmpty)
        {
            JToken token = RequiredToken(file, obj, propertyName, jsonPath);
            if (token == null)
            {
                return null;
            }

            if (token.Type != JTokenType.String)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected string.");
                return null;
            }

            string value = token.Value<string>();
            if (nonEmpty && string.IsNullOrEmpty(value))
            {
                Add(ContentDiagnosticCode.InvalidValue, file, jsonPath, "String must not be empty.");
                return null;
            }

            return value;
        }

        private string OptionalNullableString(string file, JObject obj, string propertyName, string jsonPath)
        {
            if (!obj.TryGetValue(propertyName, out JToken token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type != JTokenType.String)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected string or null.");
                return null;
            }

            string value = token.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                Add(ContentDiagnosticCode.InvalidValue, file, jsonPath, "String must not be empty.");
                return null;
            }

            return value;
        }

        private bool? RequiredBool(string file, JObject obj, string propertyName, string jsonPath)
        {
            JToken token = RequiredToken(file, obj, propertyName, jsonPath);
            if (token == null)
            {
                return null;
            }

            if (token.Type != JTokenType.Boolean)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected boolean.");
                return null;
            }

            return token.Value<bool>();
        }

        private int? RequiredInt(string file, JObject obj, string propertyName, string jsonPath)
        {
            JToken token = RequiredToken(file, obj, propertyName, jsonPath);
            return token == null ? null : ReadInt(file, token, jsonPath);
        }

        private int? OptionalInt(string file, JObject obj, string propertyName, string jsonPath)
        {
            if (!obj.TryGetValue(propertyName, out JToken token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            return ReadInt(file, token, jsonPath);
        }

        private int? ReadInt(string file, JToken token, string jsonPath)
        {
            if (token.Type != JTokenType.Integer)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected integer.");
                return null;
            }

            try
            {
                long value = token.Value<long>();
                if (value < int.MinValue || value > int.MaxValue)
                {
                    Add(ContentDiagnosticCode.InvalidValue, file, jsonPath, "Integer is outside Int32 range.");
                    return null;
                }

                return (int)value;
            }
            catch (Exception ex) when (ex is OverflowException || ex is FormatException)
            {
                Add(ContentDiagnosticCode.InvalidValue, file, jsonPath, "Integer is outside Int32 range.");
                return null;
            }
        }

        private JObject RequiredObject(string file, JObject obj, string propertyName, string jsonPath)
        {
            JToken token = RequiredToken(file, obj, propertyName, jsonPath);
            if (token == null)
            {
                return null;
            }

            JObject result = token as JObject;
            if (result == null)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected object.");
            }

            return result;
        }

        private void ValidateAbsent(JObject obj, string propertyName, string file, string jsonPath)
        {
            if (obj.TryGetValue(propertyName, out JToken token) && token.Type != JTokenType.Null)
            {
                Add(ContentDiagnosticCode.UnknownProperty, file, jsonPath, "Property " + propertyName + " is not allowed for this expression kind.");
            }
        }

        private JArray RequiredArray(string file, JObject obj, string propertyName, string jsonPath)
        {
            JToken token = RequiredToken(file, obj, propertyName, jsonPath);
            if (token == null)
            {
                return null;
            }

            JArray result = token as JArray;
            if (result == null)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected array.");
            }

            return result;
        }

        private JToken RequiredToken(string file, JObject obj, string propertyName, string jsonPath)
        {
            if (!obj.TryGetValue(propertyName, out JToken token))
            {
                Add(ContentDiagnosticCode.MissingRequiredProperty, file, jsonPath, $"Missing required property {propertyName}.");
                return null;
            }

            if (token.Type == JTokenType.Null)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Null is not allowed for required property.");
                return null;
            }

            return token;
        }

        private int OptionalIntInRange(JObject obj, string propertyName, string jsonPath, int min, int max, int defaultValue, string file)
        {
            if (!obj.TryGetValue(propertyName, out JToken token))
            {
                return defaultValue;
            }

            int? value = ReadInt(file, token, jsonPath);
            if (!value.HasValue)
            {
                return defaultValue;
            }

            if (value.Value < min || value.Value > max)
            {
                Add(ContentDiagnosticCode.InvalidRange, file, jsonPath, $"Value must be in [{min}, {max}].");
                return defaultValue;
            }

            return value.Value;
        }

        private void ValidatePositive(string file, string jsonPath, int? value, string name)
        {
            if (value.HasValue && value.Value <= 0)
            {
                Add(ContentDiagnosticCode.InvalidRange, file, jsonPath, name + " must be positive.");
            }
        }

        private void ValidatePpm(string file, string jsonPath, int? value, string name)
        {
            if (value.HasValue && (value.Value <= 0 || value.Value > 1000000))
            {
                Add(ContentDiagnosticCode.InvalidRange, file, jsonPath, name + " must be in 1..1000000.");
            }
        }

        private TargetPath? RequiredTargetPath(string file, JObject obj, string propertyName, string jsonPath, TargetConfigCatalog catalog, bool allowMutation, TargetOperation? requiredOperation)
        {
            JToken token = RequiredToken(file, obj, propertyName, jsonPath);
            return token == null ? null : ReadTargetPathToken(file, token, jsonPath, catalog, allowMutation, requiredOperation);
        }

        private TargetPath? ReadTargetPathToken(string file, JToken token, string jsonPath, TargetConfigCatalog catalog, bool allowMutation, TargetOperation? requiredOperation)
        {
            if (token.Type != JTokenType.String)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected string.");
                return null;
            }

            string targetText = token.Value<string>();
            if (!TargetPath.TryParse(targetText, out TargetPath target))
            {
                Add(ContentDiagnosticCode.InvalidTargetReference, file, jsonPath, "Target path is invalid.");
                return null;
            }

            if (!catalog.TryResolve(target, out TargetConfig config))
            {
                Add(ContentDiagnosticCode.InvalidTargetReference, file, jsonPath, "Target path does not resolve against TargetConfig.");
                return null;
            }

            if (requiredOperation.HasValue && !config.Allows(requiredOperation.Value))
            {
                Add(ContentDiagnosticCode.InvalidTargetOperation, file, jsonPath, "Target does not allow operation " + requiredOperation.Value + ".");
                return null;
            }

            return target;
        }

        private TargetPattern? RequiredTargetPattern(string file, JObject obj, string propertyName, string jsonPath, TargetConfigCatalog catalog, bool allowStaticRegionalReadOnly = false)
        {
            JToken token = RequiredToken(file, obj, propertyName, jsonPath);
            if (token == null)
            {
                return null;
            }

            if (token.Type != JTokenType.String)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, file, jsonPath, "Expected string.");
                return null;
            }

            string patternText = token.Value<string>();
            if (!TargetPattern.TryParse(patternText, out TargetPattern pattern))
            {
                Add(ContentDiagnosticCode.InvalidTargetPattern, file, jsonPath, "Target pattern is invalid.");
                return null;
            }

            if (!allowStaticRegionalReadOnly && IsStaticRegionalPattern(patternText))
            {
                Add(ContentDiagnosticCode.InvalidTargetPattern, file, jsonPath, "Static regional resources are read-only and cannot be targeted by runtime patterns.");
                return null;
            }

            if (allowStaticRegionalReadOnly && IsStaticRegionalPattern(patternText))
            {
                return pattern;
            }

            if (!IsPatternCoveredByCatalog(pattern, catalog))
            {
                Add(ContentDiagnosticCode.InvalidTargetPattern, file, jsonPath, "Target pattern does not match any supported TargetConfig pattern.");
                return null;
            }

            return pattern;
        }

        private static bool IsPatternCoveredByCatalog(TargetPattern pattern, TargetConfigCatalog catalog)
        {
            string[] segments = pattern.ToString().Split('.');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "*")
                {
                    segments[i] = "probe";
                }
            }

            if (!TargetPath.TryParse(string.Join(".", segments), out TargetPath candidate))
            {
                return false;
            }

            return catalog.TryResolve(candidate, out _);
        }

        private List<string> LoadStringArray(string file, JArray array, string jsonPath, int minCount, bool unique)
        {
            List<string> result = new List<string>();
            if (array == null)
            {
                return result;
            }

            if (array.Count < minCount)
            {
                Add(ContentDiagnosticCode.InvalidValue, file, jsonPath, $"Array must contain at least {minCount} entries.");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                string itemPath = jsonPath + "[" + i + "]";
                if (array[i].Type != JTokenType.String)
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, file, itemPath, "Expected string.");
                    continue;
                }

                string value = array[i].Value<string>();
                if (string.IsNullOrEmpty(value))
                {
                    Add(ContentDiagnosticCode.InvalidValue, file, itemPath, "String must not be empty.");
                    continue;
                }

                if (unique && !seen.Add(value))
                {
                    Add(ContentDiagnosticCode.InvalidValue, file, itemPath, $"Duplicate value {value}.");
                    continue;
                }

                result.Add(value);
            }

            return result;
        }

        private void ValidateUi(JObject row, string rowPath)
        {
            if (!row.TryGetValue("ui", out JToken token))
            {
                return;
            }

            JObject ui = token as JObject;
            if (ui == null)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, TargetConfigPath, rowPath + ".ui", "ui must be an object.");
                return;
            }

            ValidateUnknownProperties(TargetConfigPath, ui, rowPath + ".ui", new[] { "label", "decimals" });
            if (ui.TryGetValue("label", out JToken label))
            {
                if (label.Type != JTokenType.String || string.IsNullOrEmpty(label.Value<string>()))
                {
                    Add(ContentDiagnosticCode.InvalidPropertyType, TargetConfigPath, rowPath + ".ui.label", "ui.label must be a non-empty string.");
                }
            }

            if (ui.TryGetValue("decimals", out JToken decimals))
            {
                int? value = ReadInt(TargetConfigPath, decimals, rowPath + ".ui.decimals");
                if (value.HasValue && value.Value < 0)
                {
                    Add(ContentDiagnosticCode.InvalidValue, TargetConfigPath, rowPath + ".ui.decimals", "ui.decimals must be non-negative.");
                }
            }
        }

        private void ValidateQual(JObject row, string rowPath)
        {
            if (row.TryGetValue("qual", out JToken token) && token.Type != JTokenType.Object)
            {
                Add(ContentDiagnosticCode.InvalidPropertyType, TargetConfigPath, rowPath + ".qual", "qual must be an object when present.");
            }
        }

        private void ValidatePositiveVersion(int? version, ContentDiagnosticCode code, string jsonPath, string name)
        {
            if (version.HasValue && version.Value <= 0)
            {
                Add(code, ManifestPath, jsonPath, $"{name} must be positive.");
            }
        }

        private void VerifyRequiredManifestEntry(ContentManifest manifest, string path)
        {
            if (!manifest.Files.ContainsKey(path))
            {
                Add(ContentDiagnosticCode.MissingRequiredManifestEntry, ManifestPath, "$.files", $"Manifest must declare {path}.");
            }
        }

        private bool HasErrors()
        {
            for (int i = 0; i < _diagnostics.Count; i++)
            {
                if (_diagnostics[i].Severity == ContentDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasErrorsForRow(string rowPath)
        {
            for (int i = 0; i < _diagnostics.Count; i++)
            {
                ContentDiagnostic diagnostic = _diagnostics[i];
                if (diagnostic.Severity == ContentDiagnosticSeverity.Error && IsSamePathOrChild(diagnostic.JsonPath, rowPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSamePathOrChild(string candidate, string parent)
        {
            return string.Equals(candidate, parent, StringComparison.Ordinal)
                || candidate.StartsWith(parent + ".", StringComparison.Ordinal)
                || candidate.StartsWith(parent + "[", StringComparison.Ordinal);
        }

        private void Add(ContentDiagnosticCode code, string relativeFile, string jsonPath, string message)
        {
            _diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.Error, code, relativeFile, jsonPath, message));
        }

        private static bool TryMapOperation(string text, out TargetOperation operation)
        {
            if (text == "ADD")
            {
                operation = TargetOperation.Add;
                return true;
            }

            if (text == "MUL")
            {
                operation = TargetOperation.Multiply;
                return true;
            }

            if (text == "SET")
            {
                operation = TargetOperation.Set;
                return true;
            }

            operation = default;
            return false;
        }

        private static bool TryMapEventKind(string text, out EventKind kind)
        {
            if (text == "AUTO")
            {
                kind = EventKind.Auto;
                return true;
            }

            if (text == "CHOICE")
            {
                kind = EventKind.Choice;
                return true;
            }

            if (text == "CRISIS")
            {
                kind = EventKind.Crisis;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryMapEventScope(string text, out EventScope scope)
        {
            if (text == "NATIONAL")
            {
                scope = EventScope.National;
                return true;
            }

            if (text == "REGION")
            {
                scope = EventScope.Region;
                return true;
            }

            scope = default;
            return false;
        }

        private static bool TryMapSelectorMode(string text, out EventSelectorMode mode)
        {
            if (text == "ARGMAX")
            {
                mode = EventSelectorMode.ArgMax;
                return true;
            }

            if (text == "WEIGHTED")
            {
                mode = EventSelectorMode.Weighted;
                return true;
            }

            mode = default;
            return false;
        }

        private static bool TryMapComparator(string text, out EventComparator comparator)
        {
            if (text == "<")
            {
                comparator = EventComparator.LessThan;
                return true;
            }

            if (text == "<=")
            {
                comparator = EventComparator.LessThanOrEqual;
                return true;
            }

            if (text == "==")
            {
                comparator = EventComparator.Equal;
                return true;
            }

            if (text == ">=")
            {
                comparator = EventComparator.GreaterThanOrEqual;
                return true;
            }

            if (text == ">")
            {
                comparator = EventComparator.GreaterThan;
                return true;
            }

            comparator = default;
            return false;
        }

        private static bool TryMapEffectInvocationType(string text, out EffectInvocationType type)
        {
            if (text == "MODIFIER")
            {
                type = EffectInvocationType.Modifier;
                return true;
            }

            type = default;
            return false;
        }

        private static bool TryMapReformKind(string text, out ReformKind kind)
        {
            if (text == "NORMAL")
            {
                kind = ReformKind.Normal;
                return true;
            }

            if (text == "SPECIAL_CONSTITUTIONAL")
            {
                kind = ReformKind.SpecialConstitutional;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryMapReformPrerequisiteType(string text, out ReformPrerequisiteType type)
        {
            if (text == "METRIC")
            {
                type = ReformPrerequisiteType.Metric;
                return true;
            }

            type = default;
            return false;
        }

        private static bool TryMapReformStageKind(string text, out ReformStageKind kind)
        {
            if (text == "WORK")
            {
                kind = ReformStageKind.Work;
                return true;
            }

            if (text == "VOTE")
            {
                kind = ReformStageKind.Vote;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryMapReformStageChamber(string text, out ReformStageChamber chamber)
        {
            if (text == "NONE")
            {
                chamber = ReformStageChamber.None;
                return true;
            }

            if (text == "LOWER")
            {
                chamber = ReformStageChamber.Lower;
                return true;
            }

            if (text == "UPPER")
            {
                chamber = ReformStageChamber.Upper;
                return true;
            }

            if (text == "BOTH")
            {
                chamber = ReformStageChamber.Both;
                return true;
            }

            chamber = default;
            return false;
        }

        private static bool TryMapMacrozone(string text, out RegionMacrozone macrozone)
        {
            if (text == "NORTH")
            {
                macrozone = RegionMacrozone.North;
                return true;
            }

            if (text == "CENTER")
            {
                macrozone = RegionMacrozone.Center;
                return true;
            }

            if (text == "SOUTH")
            {
                macrozone = RegionMacrozone.South;
                return true;
            }

            if (text == "AUSTRAL")
            {
                macrozone = RegionMacrozone.Austral;
                return true;
            }

            macrozone = default;
            return false;
        }

        private static bool TryMapRounding(string text, out ContentRoundingMode rounding)
        {
            if (text == "HALF_AWAY_FROM_ZERO")
            {
                rounding = ContentRoundingMode.HalfAwayFromZero;
                return true;
            }

            rounding = default;
            return false;
        }

        private static bool TryMapAggregationPassType(string text, out AggregationPassType passType)
        {
            if (text == "INTERNAL_REVERSION")
            {
                passType = AggregationPassType.InternalReversion;
                return true;
            }

            if (text == "METRIC_AGGREGATION")
            {
                passType = AggregationPassType.MetricAggregation;
                return true;
            }

            if (text == "DERIVED_INTERNALS")
            {
                passType = AggregationPassType.DerivedInternals;
                return true;
            }

            passType = default;
            return false;
        }

        private static bool TryMapAggregationExpressionKind(string text, out AggregationExpressionKind kind)
        {
            if (text == "AVG")
            {
                kind = AggregationExpressionKind.Avg;
                return true;
            }

            if (text == "COPY")
            {
                kind = AggregationExpressionKind.Copy;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryMapMovementMatchMode(string text, out LegislativeMovementMatchMode mode)
        {
            if (text == "ANY")
            {
                mode = LegislativeMovementMatchMode.Any;
                return true;
            }

            mode = default;
            return false;
        }

        private static bool TryMapUpperChamberAdjustmentType(string text, out LegislativeUpperChamberAdjustmentType type)
        {
            if (text == "SUBTRACT_CONST")
            {
                type = LegislativeUpperChamberAdjustmentType.SubtractConst;
                return true;
            }

            type = default;
            return false;
        }

        private static bool TryMapChamberSupportMode(string text, out LegislativeChamberSupportMode mode)
        {
            if (text == "MIN")
            {
                mode = LegislativeChamberSupportMode.Min;
                return true;
            }

            mode = default;
            return false;
        }

        private static bool IsAsciiLowerSnake(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] < 'a' || value[0] > 'z')
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDottedLowercase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string[] parts = value.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!IsAsciiLowerSnake(parts[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsTwoSegmentDottedLowercase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string[] parts = value.Split('.');
            return parts.Length == 2 && IsAsciiLowerSnake(parts[0]) && IsAsciiLowerSnake(parts[1]);
        }

        private static bool IsSha256Hash(string value)
        {
            if (value == null || value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 7; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsStaticRegionalPattern(string patternText)
        {
            string[] parts = patternText.Split('.');
            return parts.Length == 3
                && parts[0] == "regions"
                && ContentPathRules.IsStaticRegionField(parts[2]);
        }

        private static bool Contains(IEnumerable<string> values, string value)
        {
            foreach (string candidate in values)
            {
                if (candidate == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<KeyValuePair<string, string>> Sorted(IEnumerable<KeyValuePair<string, string>> files)
        {
            List<KeyValuePair<string, string>> sorted = new List<KeyValuePair<string, string>>(files);
            sorted.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
            return sorted;
        }
    }

    internal static class ContentPathRules
    {
        private static readonly string[] StaticRegionFields =
        {
            "admin_capS",
            "industry_capS",
            "extractive_capS",
            "social_capS",
            "populationS"
        };

        internal static bool IsSafeRelativeJsonPath(string path, bool allowManifest)
        {
            if (string.IsNullOrEmpty(path)
                || path.IndexOf('\\') >= 0
                || path.StartsWith("/", StringComparison.Ordinal)
                || path.EndsWith("/", StringComparison.Ordinal)
                || path.IndexOf(':') >= 0
                || !path.EndsWith(".json", StringComparison.Ordinal))
            {
                return false;
            }

            if (!allowManifest && path == "manifest.json")
            {
                return false;
            }

            string[] parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 || parts[i] == "." || parts[i] == "..")
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsStaticRegionField(string value)
        {
            for (int i = 0; i < StaticRegionFields.Length; i++)
            {
                if (value == StaticRegionFields[i])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
