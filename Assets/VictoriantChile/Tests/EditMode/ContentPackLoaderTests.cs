using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using VictoriantChile.Content.Diagnostics;
using VictoriantChile.Content.Loading;
using VictoriantChile.Content.Models;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class ContentPackLoaderTests
    {
        [Test]
        public void RealContentPackLoadsSuccessfully()
        {
            ContentLoadResult result = LoadRealPack();

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Pack, Is.Not.Null);
            Assert.That(result.Pack.Manifest.ContentPackId, Is.EqualTo("base_chile_fictional"));
            Assert.That(result.Pack.Manifest.ContentSchemaVersion, Is.EqualTo(1));
            Assert.That(result.Pack.TargetConfigs.Count, Is.EqualTo(20));
            Assert.That(result.Pack.Regions.Count, Is.EqualTo(16));
            Assert.That(result.Pack.InterestGroups.Count, Is.EqualTo(9));
            Assert.That(result.Pack.Movements.Count, Is.EqualTo(9));
            Assert.That(result.Pack.TargetConfigCatalog.Resolve(TargetPath.Parse("metrics.legitimacy")).Pattern.ToString(), Is.EqualTo("metrics.legitimacy"));
            Assert.That(result.Pack.RegionsById["metropolitana"].Name, Is.EqualTo("Metropolitana"));
        }

        [Test]
        public void CanonicalHashIgnoresLineEndingStyleOnly()
        {
            byte[] lf = Encoding.UTF8.GetBytes("{\n  \"value\": 1\n}\n");
            byte[] crlf = Encoding.UTF8.GetBytes("{\r\n  \"value\": 1\r\n}\r\n");
            byte[] cr = Encoding.UTF8.GetBytes("{\r  \"value\": 1\r}\r");
            byte[] changed = Encoding.UTF8.GetBytes("{\n  \"value\": 2\n}\n");

            string hash = ContentHash.ComputeCanonicalSha256(lf);
            Assert.That(ContentHash.ComputeCanonicalSha256(crlf), Is.EqualTo(hash));
            Assert.That(ContentHash.ComputeCanonicalSha256(cr), Is.EqualTo(hash));
            Assert.That(ContentHash.ComputeCanonicalSha256(changed), Is.Not.EqualTo(hash));
            Assert.That(hash, Does.Match("^sha256:[0-9a-f]{64}$"));
        }

        [Test]
        public void RealPackHashesMatchManifest()
        {
            string root = ContentRoot();
            string manifest = File.ReadAllText(Path.Combine(root, "manifest.json"), Encoding.UTF8);

            Assert.That(manifest, Does.Contain($"\"core/regions.json\": \"{ContentHash.ComputeCanonicalSha256(File.ReadAllBytes(Path.Combine(root, "core/regions.json")))}\""));
            Assert.That(manifest, Does.Contain($"\"core/igs.json\": \"{ContentHash.ComputeCanonicalSha256(File.ReadAllBytes(Path.Combine(root, "core/igs.json")))}\""));
            Assert.That(manifest, Does.Contain($"\"core/movements.json\": \"{ContentHash.ComputeCanonicalSha256(File.ReadAllBytes(Path.Combine(root, "core/movements.json")))}\""));
            Assert.That(manifest, Does.Contain($"\"rules/target_config.json\": \"{ContentHash.ComputeCanonicalSha256(File.ReadAllBytes(Path.Combine(root, "rules/target_config.json")))}\""));
        }

        [Test]
        public void MissingManifestFailsClosed()
        {
            ContentLoadResult result = new ContentPackLoader().Load(new InMemoryContentFileSource(new Dictionary<string, byte[]>()));

            AssertFailure(result, ContentDiagnosticCode.ManifestMissing);
            Assert.That(result.Pack, Is.Null);
        }

        [Test]
        public void HashMismatchFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["core/regions.json"] = Bytes("{\"regions\":[]}");

            ContentLoadResult result = Load(files);

            AssertFailure(result, ContentDiagnosticCode.HashMismatch);
        }

        [Test]
        public void UnsafeManifestPathFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            string manifest = Text(files["manifest.json"]).Replace("\"strings/es.json\"", "\"../strings/es.json\"");
            files.Remove("manifest.json");
            files["manifest.json"] = Bytes(manifest);

            ContentLoadResult result = Load(files);

            AssertFailure(result, ContentDiagnosticCode.UnsafeManifestPath);
        }

        [Test]
        public void MissingRequiredManifestEntryFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture(includeMovements: false);

            ContentLoadResult result = Load(files);

            AssertFailure(result, ContentDiagnosticCode.MissingRequiredManifestEntry);
        }

        [Test]
        public void InvalidUtf8FailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["core/regions.json"] = new byte[] { 0xff, 0xff };
            files = RebuildManifest(files);

            ContentLoadResult result = Load(files);

            AssertFailure(result, ContentDiagnosticCode.InvalidUtf8);
        }

        [Test]
        public void MalformedDuplicateAndCommentedJsonFailClosed()
        {
            Dictionary<string, byte[]> malformed = RebuildManifest(WithFile(ValidFixture(), "core/regions.json", "{\"regions\":["));
            AssertFailure(Load(malformed), ContentDiagnosticCode.JsonMalformed);

            Dictionary<string, byte[]> duplicate = RebuildManifest(WithFile(ValidFixture(), "core/regions.json", "{\"regions\":[],\"regions\":[]}"));
            AssertFailure(Load(duplicate), ContentDiagnosticCode.DuplicateJsonProperty);

            Dictionary<string, byte[]> commented = RebuildManifest(WithFile(ValidFixture(), "core/regions.json", "{\"regions\":/*no*/[]}"));
            AssertFailure(Load(commented), ContentDiagnosticCode.JsonMalformed);

            Dictionary<string, byte[]> trailing = RebuildManifest(WithFile(ValidFixture(), "core/regions.json", "{\"regions\":[]}{}"));
            AssertFailure(Load(trailing), ContentDiagnosticCode.JsonMalformed);
        }

        [Test]
        public void ManifestVersionCompatibilityFailsClosed()
        {
            Dictionary<string, byte[]> unsupportedSchema = ValidFixture();
            unsupportedSchema["manifest.json"] = Bytes(Text(unsupportedSchema["manifest.json"]).Replace("\"content_schema_version\":1", "\"content_schema_version\":2"));
            AssertFailure(Load(unsupportedSchema), ContentDiagnosticCode.UnsupportedContentSchemaVersion);

            Dictionary<string, byte[]> incompatibleGame = ValidFixture();
            incompatibleGame["manifest.json"] = Bytes(Text(incompatibleGame["manifest.json"]).Replace("\"min_game_schema_version\":1", "\"min_game_schema_version\":2"));
            AssertFailure(Load(incompatibleGame), ContentDiagnosticCode.IncompatibleGameSchemaVersion);
        }

        [Test]
        public void BoolNumericFieldIsRejected()
        {
            Dictionary<string, byte[]> files = RebuildManifest(WithFile(ValidFixture(), "rules/target_config.json", "[{\"pattern\":\"metrics.*\",\"scale\":true,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]}]"));

            ContentLoadResult result = Load(files);

            AssertFailure(result, ContentDiagnosticCode.InvalidPropertyType);
            Assert.That(result.Pack, Is.Null);
        }

        [Test]
        public void TargetConfigRejectsInvalidOperationDuplicatePatternAndStaticRegionalResource()
        {
            Dictionary<string, byte[]> invalidOp = RebuildManifest(WithFile(ValidFixture(), "rules/target_config.json", "[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"BAD\"]}]"));
            AssertFailure(Load(invalidOp), ContentDiagnosticCode.InvalidTargetOperation);

            Dictionary<string, byte[]> duplicatePattern = RebuildManifest(WithFile(ValidFixture(), "rules/target_config.json", "[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]},{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]}]"));
            AssertFailure(Load(duplicatePattern), ContentDiagnosticCode.DuplicateTargetPattern);

            Dictionary<string, byte[]> staticRegion = RebuildManifest(WithFile(ValidFixture(), "rules/target_config.json", "[{\"pattern\":\"regions.*.admin_capS\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"SET\"]}]"));
            AssertFailure(Load(staticRegion), ContentDiagnosticCode.InvalidTargetPattern);
        }

        [Test]
        public void RegionLoaderValidatesWeightTotalAndDefaultsStaticFields()
        {
            Dictionary<string, byte[]> invalid = RebuildManifest(WithFile(ValidFixture(), "core/regions.json", "{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":999999,\"macrozone\":\"CENTER\"}]}"));
            AssertFailure(Load(invalid), ContentDiagnosticCode.RegionWeightTotalMismatch);

            Dictionary<string, byte[]> valid = ValidFixture();
            ContentLoadResult result = Load(valid);

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Pack.Regions[0].AdminCapS, Is.EqualTo(5000));
            Assert.That(result.Pack.Regions[0].PopulationS, Is.EqualTo(5000));
        }

        [Test]
        public void InterestGroupsAndMovementsValidateIdsAndTags()
        {
            Dictionary<string, byte[]> badIg = RebuildManifest(WithFile(ValidFixture(), "core/igs.json", "{\"igs\":[{\"id\":\"bad\",\"name\":\"Bad\",\"tags\":[\"political.left\",\"political.left\"]}]}"));
            AssertFailure(Load(badIg), ContentDiagnosticCode.InvalidId);

            Dictionary<string, byte[]> badMovement = RebuildManifest(WithFile(ValidFixture(), "core/movements.json", "{\"movements\":[{\"id\":\"mov_test\",\"name\":\"Test\",\"tags\":[\"badtag\"]}]}"));
            AssertFailure(Load(badMovement), ContentDiagnosticCode.InvalidValue);
        }

        [Test]
        public void DirectorySourceRejectsUnsafePaths()
        {
            DirectoryContentFileSource source = new DirectoryContentFileSource(ContentRoot());

            Assert.That(source.TryReadAllBytes("../manifest.json").Success, Is.False);
            Assert.That(source.TryReadAllBytes("core\\regions.json").Success, Is.False);
            Assert.That(source.TryReadAllBytes(Path.GetFullPath(Path.Combine(ContentRoot(), "manifest.json"))).Success, Is.False);
        }

        private static ContentLoadResult LoadRealPack()
        {
            return new ContentPackLoader().Load(new DirectoryContentFileSource(ContentRoot()));
        }

        private static ContentLoadResult Load(Dictionary<string, byte[]> files)
        {
            return new ContentPackLoader().Load(new InMemoryContentFileSource(files));
        }

        private static void AssertFailure(ContentLoadResult result, ContentDiagnosticCode code)
        {
            Assert.That(result.IsSuccess, Is.False, Diagnostics(result));
            Assert.That(result.Pack, Is.Null);
            Assert.That(ContainsCode(result, code), Is.True, Diagnostics(result));
        }

        private static bool ContainsCode(ContentLoadResult result, ContentDiagnosticCode code)
        {
            foreach (ContentDiagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Diagnostics(ContentLoadResult result)
        {
            List<string> lines = new List<string>();
            foreach (ContentDiagnostic diagnostic in result.Diagnostics)
            {
                lines.Add(diagnostic.ToString());
            }

            return string.Join("\n", lines.ToArray());
        }

        private static string ContentRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Assets", "StreamingAssets", "content"));
        }

        private static Dictionary<string, byte[]> ValidFixture(bool includeMovements = true)
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "core/regions.json", Bytes("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":1000000,\"macrozone\":\"CENTER\"}]}") },
                { "core/igs.json", Bytes("{\"igs\":[{\"id\":\"ig_test\",\"name\":\"Test IG\",\"tags\":[\"political.left\",\"labor.union\"]}]}") },
                { "rules/target_config.json", Bytes("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\",\"MUL\",\"SET\"]}]") },
                { "strings/es.json", Bytes("{}") }
            };

            if (includeMovements)
            {
                files.Add("core/movements.json", Bytes("{\"movements\":[{\"id\":\"mov_test\",\"name\":\"Test Movement\",\"tags\":[\"political.left\"]}]}"));
            }

            return RebuildManifest(files);
        }

        private static Dictionary<string, byte[]> RebuildManifest(Dictionary<string, byte[]> files)
        {
            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
            List<string> paths = new List<string>();
            foreach (string path in result.Keys)
            {
                if (path != "manifest.json")
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);
            StringBuilder filesJson = new StringBuilder();
            for (int i = 0; i < paths.Count; i++)
            {
                if (i > 0)
                {
                    filesJson.Append(",");
                }

                string path = paths[i];
                filesJson.Append("\"").Append(path).Append("\":\"").Append(ContentHash.ComputeCanonicalSha256(result[path])).Append("\"");
            }

            string manifest = "{\"content_pack_id\":\"test_pack\",\"content_pack_version\":1,\"content_schema_version\":1,\"default_language\":\"es\",\"files\":{"
                + filesJson
                + "},\"languages\":[\"es\"],\"min_game_schema_version\":1}";
            result["manifest.json"] = Bytes(manifest);
            return result;
        }

        private static Dictionary<string, byte[]> WithFile(Dictionary<string, byte[]> files, string path, string text)
        {
            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
            result[path] = Bytes(text);
            return result;
        }

        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private static string Text(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        private sealed class InMemoryContentFileSource : IContentFileSource
        {
            private readonly Dictionary<string, byte[]> _files;

            public InMemoryContentFileSource(Dictionary<string, byte[]> files)
            {
                _files = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
            }

            public ContentFileReadResult TryReadAllBytes(string relativePath)
            {
                if (!_files.TryGetValue(relativePath, out byte[] bytes))
                {
                    return ContentFileReadResult.Missing("Missing in-memory file.");
                }

                return ContentFileReadResult.FromBytes(bytes);
            }
        }
    }
}
