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
                VerifyRequiredManifestEntry(manifest, $"strings/{manifest.DefaultLanguage}.json");
            }

            List<TargetConfig> targetConfigs = null;
            List<RegionDefinition> regions = null;
            List<InterestGroupDefinition> interestGroups = null;
            List<MovementDefinition> movements = null;

            if (manifest != null && verifiedFiles.TryGetValue(TargetConfigPath, out byte[] targetConfigBytes))
            {
                targetConfigs = LoadTargetConfigs(ParseArray(TargetConfigPath, targetConfigBytes));
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

            if (HasErrors())
            {
                return new ContentLoadResult(null, _diagnostics);
            }

            ContentPack pack = new ContentPack(manifest, targetConfigs, regions, interestGroups, movements);
            return new ContentLoadResult(pack, _diagnostics);
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

        private int? RequiredInt(string file, JObject obj, string propertyName, string jsonPath)
        {
            JToken token = RequiredToken(file, obj, propertyName, jsonPath);
            return token == null ? null : ReadInt(file, token, jsonPath);
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
