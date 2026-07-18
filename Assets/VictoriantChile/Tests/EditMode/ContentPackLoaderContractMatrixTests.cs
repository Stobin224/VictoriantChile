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
    public sealed class ContentPackLoaderContractMatrixTests
    {
        [Test]
        public void RealPackExposesExpectedCountsVersionsResolutionAndHashes()
        {
            ContentLoadResult result = LoadRealPack();

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Pack, Is.Not.Null);
            Assert.That(ErrorCount(result), Is.EqualTo(0));
            Assert.That(result.Pack.Manifest.Files.Count, Is.EqualTo(10));
            Assert.That(result.Pack.TargetConfigs.Count, Is.EqualTo(20));
            Assert.That(result.Pack.Regions.Count, Is.EqualTo(16));
            Assert.That(result.Pack.InterestGroups.Count, Is.EqualTo(9));
            Assert.That(result.Pack.Movements.Count, Is.EqualTo(9));
            Assert.That(result.Pack.Manifest.ContentPackVersion, Is.EqualTo(3));
            Assert.That(result.Pack.Manifest.ContentSchemaVersion, Is.EqualTo(1));
            Assert.That(result.Pack.Manifest.MinGameSchemaVersion, Is.EqualTo(1));
            Assert.That(result.Pack.Localization, Is.Not.Null);
            Assert.That(result.Pack.AggregationConfig, Is.Not.Null);
            Assert.That(result.Pack.LegislativeConfig, Is.Not.Null);
            Assert.That(result.Pack.Effects.Count, Is.EqualTo(17));

            long regionalWeight = 0;
            foreach (RegionDefinition region in result.Pack.Regions)
            {
                regionalWeight += region.WeightPpm;
            }
            Assert.That(regionalWeight, Is.EqualTo(1000000));

            Assert.That(result.Pack.TargetConfigCatalog.Resolve(TargetPath.Parse("metrics.legitimacy")).Pattern.ToString(), Is.EqualTo("metrics.legitimacy"));
            Assert.That(result.Pack.TargetConfigCatalog.Resolve(TargetPath.Parse("igs.ig_sindicatos_trabajo.approval")).Pattern.ToString(), Is.EqualTo("igs.*.approval"));
            TargetConfig direction = result.Pack.TargetConfigCatalog.Resolve(TargetPath.Parse("movements.mov_trabajo_huelgas.direction"));
            Assert.That(direction.AllowedOperations, Is.EqualTo(new[] { TargetOperation.Set }));
            Assert.That(result.Pack.TargetConfigCatalog.Resolve(TargetPath.Parse("igs.ig_sindicatos_trabajo.clout")).NormalizeGroup, Is.EqualTo("igs.clout_sum_100"));
            Assert.That(result.Pack.Localization.ResolveRequired("effect.eff_legitimacy_down_small.title"), Is.EqualTo("Legitimidad (↓ leve)"));
            Assert.That(result.Pack.AggregationConfig.Passes[1].Metrics[2].Components[0].WeightPpm, Is.EqualTo(350000));
            Assert.That(result.Pack.LegislativeConfig.PlayerStrategiesById["COMPROMISE"].ImplementationEffectMultiplierPpm, Is.EqualTo(850000));
            Assert.That(result.Pack.EffectsById["eff_legitimacy_down_small"].LocalizationTitleKey, Is.EqualTo("effect.eff_legitimacy_down_small.title"));

            string root = ContentRoot();
            foreach (KeyValuePair<string, string> declared in result.Pack.Manifest.Files)
            {
                string actual = ContentHash.ComputeCanonicalSha256(File.ReadAllBytes(Path.Combine(root, declared.Key.Replace('/', Path.DirectorySeparatorChar))));
                Assert.That(actual, Is.EqualTo(declared.Value), declared.Key);
            }
        }

        [Test]
        public void DeclaredNonParsedFileHashMismatchFails()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["templates/events.json"] = Bytes("{\"events\":[\"tampered\"]}");

            AssertFailure(Load(files), ContentDiagnosticCode.HashMismatch);
        }

        [Test]
        public void HashLfCrlfAndCrAreEqual()
        {
            string lf = ContentHash.ComputeCanonicalSha256(Bytes("{\n\"a\":1\n}"));
            string crlf = ContentHash.ComputeCanonicalSha256(Bytes("{\r\n\"a\":1\r\n}"));
            string cr = ContentHash.ComputeCanonicalSha256(Bytes("{\r\"a\":1\r}"));

            Assert.That(crlf, Is.EqualTo(lf));
            Assert.That(cr, Is.EqualTo(lf));
        }

        [Test]
        public void HashChangesWhenNonLineEndingByteChanges()
        {
            string left = ContentHash.ComputeCanonicalSha256(Bytes("{\n\"a\":1\n}"));
            string right = ContentHash.ComputeCanonicalSha256(Bytes("{\n\"a\":2\n}"));

            Assert.That(right, Is.Not.EqualTo(left));
        }

        [Test]
        public void HashFormatUsesLowercaseSha256()
        {
            Assert.That(ContentHash.ComputeCanonicalSha256(Bytes("{}")), Does.Match("^sha256:[0-9a-f]{64}$"));
        }

        [Test]
        public void MissingDeclaredNonParsedFileFails()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files.Remove("templates/events.json");

            AssertFailure(Load(files), ContentDiagnosticCode.MissingDeclaredFile);
        }

        [Test]
        public void LocalizationValueMustBeString()
        {
            AssertFailure(Load(RebuildManifest(WithFile(ValidFixture(), "strings/es.json", "{\"key\":1}"))), ContentDiagnosticCode.InvalidPropertyType);
        }

        [Test]
        public void MissingEffectLocalizationKeyFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["templates/effects.json"] = Bytes(Text(files["templates/effects.json"]).Replace("effect.eff_legitimacy_down_small.title", "effect.missing.title"));
            files = RebuildManifest(files);

            AssertFailure(Load(files), ContentDiagnosticCode.MissingLocalizationKey);
        }

        [Test]
        public void DuplicateEffectIdFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["templates/effects.json"] = Bytes(Text(files["templates/effects.json"]).Replace("\"id\": \"eff_legitimacy_up_small\"", "\"id\": \"eff_legitimacy_down_small\""));
            files = RebuildManifest(files);

            AssertFailure(Load(files), ContentDiagnosticCode.DuplicateId);
        }

        [Test]
        public void EffectTargetOperationAndClampAreValidated()
        {
            Dictionary<string, byte[]> badTarget = ValidFixture();
            badTarget["templates/effects.json"] = Bytes(Text(badTarget["templates/effects.json"]).Replace("\"metrics.legitimacy\"", "\"unknown.bucket.value\""));
            AssertFailure(Load(RebuildManifest(badTarget)), ContentDiagnosticCode.InvalidTargetReference);

            Dictionary<string, byte[]> badOp = ValidFixture();
            badOp["templates/effects.json"] = Bytes(Text(badOp["templates/effects.json"]).Replace("\"op\": \"ADD\"", "\"op\": \"BAD\""));
            AssertFailure(Load(RebuildManifest(badOp)), ContentDiagnosticCode.InvalidEnum);

            Dictionary<string, byte[]> badClamp = ValidFixture();
            badClamp["templates/effects.json"] = Bytes(Text(badClamp["templates/effects.json"]).Replace("\"is_per_tick\": true", "\"is_per_tick\": true, \"clamp_minS\": 10, \"clamp_maxS\": 0"));
            AssertFailure(Load(RebuildManifest(badClamp)), ContentDiagnosticCode.InvalidRange);
        }

        [Test]
        public void AggregationWeightTargetAndExpressionFailuresAreClosed()
        {
            Dictionary<string, byte[]> badWeight = ValidFixture();
            badWeight["rules/aggregation_config.json"] = Bytes(Text(badWeight["rules/aggregation_config.json"]).Replace("\"weight_ppm\": 350000", "\"weight_ppm\": 349999"));
            AssertFailure(Load(RebuildManifest(badWeight)), ContentDiagnosticCode.InvalidWeightSum);

            Dictionary<string, byte[]> badTarget = ValidFixture();
            badTarget["rules/aggregation_config.json"] = Bytes(Text(badTarget["rules/aggregation_config.json"]).Replace("\"internals.economy.growth\"", "\"unknown.bucket.value\""));
            AssertFailure(Load(RebuildManifest(badTarget)), ContentDiagnosticCode.InvalidTargetReference);

            Dictionary<string, byte[]> badKind = ValidFixture();
            badKind["rules/aggregation_config.json"] = Bytes(Text(badKind["rules/aggregation_config.json"]).Replace("\"kind\": \"AVG\"", "\"kind\": \"BAD\""));
            AssertFailure(Load(RebuildManifest(badKind)), ContentDiagnosticCode.InvalidEnum);
        }

        [Test]
        public void LegislativePatternStrategyAndUnknownPropertyFailuresAreClosed()
        {
            Dictionary<string, byte[]> badPattern = ValidFixture();
            badPattern["rules/legislative_config.json"] = Bytes(Text(badPattern["rules/legislative_config.json"]).Replace("\"igs.*.clout\"", "\"igs.*.missing\""));
            AssertFailure(Load(RebuildManifest(badPattern)), ContentDiagnosticCode.InvalidTargetPattern);

            Dictionary<string, byte[]> badStrategy = ValidFixture();
            badStrategy["rules/legislative_config.json"] = Bytes(Text(badStrategy["rules/legislative_config.json"]).Replace("\"match_mode\": \"ANY\"", "\"match_mode\": \"BAD\""));
            AssertFailure(Load(RebuildManifest(badStrategy)), ContentDiagnosticCode.InvalidEnum);

            Dictionary<string, byte[]> badUnknownProperty = ValidFixture();
            badUnknownProperty["rules/legislative_config.json"] = Bytes(Text(badUnknownProperty["rules/legislative_config.json"]).Replace("\"gates\": {", "\"gates\": {\"unknown\":1,"));
            AssertFailure(Load(RebuildManifest(badUnknownProperty)), ContentDiagnosticCode.UnknownProperty);
        }

        [Test]
        public void SchemaVersionAndUnknownPropertyPerFamilyFailClosed()
        {
            Dictionary<string, byte[]> badAggSchema = ValidFixture();
            badAggSchema["rules/aggregation_config.json"] = Bytes(Text(badAggSchema["rules/aggregation_config.json"]).Replace("\"schema_version\": 1", "\"schema_version\": 2"));
            AssertFailure(Load(RebuildManifest(badAggSchema)), ContentDiagnosticCode.UnsupportedSchemaVersion);

            Dictionary<string, byte[]> badAggUnknown = ValidFixture();
            badAggUnknown["rules/aggregation_config.json"] = Bytes(Text(badAggUnknown["rules/aggregation_config.json"]).Replace("\"rounding\": \"HALF_AWAY_FROM_ZERO\",", "\"rounding\": \"HALF_AWAY_FROM_ZERO\",\n  \"unknown\": true,"));
            AssertFailure(Load(RebuildManifest(badAggUnknown)), ContentDiagnosticCode.UnknownProperty);

            Dictionary<string, byte[]> badEffectUnknown = ValidFixture();
            badEffectUnknown["templates/effects.json"] = Bytes(Text(badEffectUnknown["templates/effects.json"]).Replace("\"tags\": [\"theme.institucional\", \"theme.corrupcion\"]", "\"tags\": [\"theme.institucional\", \"theme.corrupcion\"], \"unknown\": true"));
            AssertFailure(Load(RebuildManifest(badEffectUnknown)), ContentDiagnosticCode.UnknownProperty);
        }

        [Test]
        public void EachDeclaredFileIsReadOnceDuringSuccessfulLoad()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            CountingContentFileSource source = new CountingContentFileSource(files);

            ContentLoadResult result = new ContentPackLoader().Load(source);

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            foreach (string path in files.Keys)
            {
                Assert.That(source.ReadCount(path), Is.EqualTo(1), path);
            }
        }

        [TestCase("core/regions.json", "{\"regions\":[", ContentDiagnosticCode.JsonMalformed, TestName = "JsonMalformedFailsClosed")]
        [TestCase("core/regions.json", "{\"regions\":[],\"regions\":[]}", ContentDiagnosticCode.DuplicateJsonProperty, TestName = "DuplicateJsonPropertyFailsClosed")]
        [TestCase("core/regions.json", "{\"regions\":/*comment*/[]}", ContentDiagnosticCode.JsonMalformed, TestName = "JsonCommentFailsClosed")]
        [TestCase("core/regions.json", "{\"regions\":[]}{}", ContentDiagnosticCode.JsonMalformed, TestName = "JsonTrailingContentFailsClosed")]
        [TestCase("core/regions.json", "[]", ContentDiagnosticCode.InvalidPropertyType, TestName = "JsonRootWrongTypeFailsClosed")]
        [TestCase("core/regions.json", "{\"regions\":[{\"name\":\"Metropolitana\",\"weight_ppm\":1000000,\"macrozone\":\"CENTER\"}]}", ContentDiagnosticCode.MissingRequiredProperty, TestName = "JsonMissingRequiredPropertyFailsClosed")]
        [TestCase("core/regions.json", "{\"regions\":[],\"unknown\":1}", ContentDiagnosticCode.UnknownProperty, TestName = "JsonUnknownPropertyFailsClosed")]
        public void JsonShapeFailuresFailClosed(string path, string text, ContentDiagnosticCode code)
        {
            AssertFailure(Load(RebuildManifest(WithFile(ValidFixture(), path, text))), code);
        }

        [Test]
        public void InvalidUtf8FailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["core/regions.json"] = new byte[] { 0xff, 0xfe };
            files = RebuildManifest(files);

            AssertFailure(Load(files), ContentDiagnosticCode.InvalidUtf8);
        }

        [TestCase("true", TestName = "BoolAsIntegerRejected")]
        [TestCase("1.5", TestName = "FloatAsIntegerRejected")]
        [TestCase("\"100\"", TestName = "StringNumericAsIntegerRejected")]
        public void NonPlainIntegerValuesAreRejected(string scaleJson)
        {
            string targetConfig = "[{\"pattern\":\"metrics.*\",\"scale\":" + scaleJson + ",\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]}]";

            AssertFailure(Load(RebuildManifest(WithFile(ValidFixture(), "rules/target_config.json", targetConfig))), ContentDiagnosticCode.InvalidPropertyType);
        }

        [TestCase("/absolute/events.json", ContentDiagnosticCode.UnsafeManifestPath, TestName = "ManifestPathAbsoluteFailsClosed")]
        [TestCase("../events.json", ContentDiagnosticCode.UnsafeManifestPath, TestName = "ManifestPathParentEscapeFailsClosed")]
        [TestCase("templates\\\\events.json", ContentDiagnosticCode.UnsafeManifestPath, TestName = "ManifestPathBackslashFailsClosed")]
        [TestCase("templates//events.json", ContentDiagnosticCode.UnsafeManifestPath, TestName = "ManifestPathEmptySegmentFailsClosed")]
        public void UnsafeManifestPathsFailClosed(string unsafePath, ContentDiagnosticCode code)
        {
            Dictionary<string, byte[]> files = ValidFixture();
            string jsonPath = unsafePath.Replace("\\", "\\\\");
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"templates/events.json\"", "\"" + jsonPath + "\""));

            AssertFailure(Load(files), code);
        }

        [Test]
        public void InvalidHashFormatFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            string validHash = ContentHash.ComputeCanonicalSha256(files["templates/events.json"]);
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace(validHash, "sha256:ABC"));

            AssertFailure(Load(files), ContentDiagnosticCode.InvalidHashFormat);
        }

        [Test]
        public void MissingRequiredManifestEntryFailsClosed()
        {
            AssertFailure(Load(ValidFixture(includeMovements: false)), ContentDiagnosticCode.MissingRequiredManifestEntry);
        }

        [Test]
        public void ManifestMissingRequiredPropertyFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"content_pack_id\":\"test_pack\",", string.Empty));

            AssertFailure(Load(files), ContentDiagnosticCode.MissingRequiredProperty);
        }

        [Test]
        public void DefaultLanguageOutsideLanguagesFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"default_language\":\"es\"", "\"default_language\":\"en\""));

            AssertFailure(Load(files), ContentDiagnosticCode.InvalidValue);
        }

        [Test]
        public void DuplicateLanguagesFailClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"languages\":[\"es\"]", "\"languages\":[\"es\",\"es\"]"));

            AssertFailure(Load(files), ContentDiagnosticCode.InvalidValue);
        }

        [Test]
        public void VersionZeroFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"content_pack_version\":1", "\"content_pack_version\":0"));

            AssertFailure(Load(files), ContentDiagnosticCode.InvalidValue);
        }

        [Test]
        public void ContentSchemaAnteriorZeroFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"content_schema_version\":1", "\"content_schema_version\":0"));

            AssertFailure(Load(files), ContentDiagnosticCode.UnsupportedContentSchemaVersion);
        }

        [Test]
        public void ContentSchemaPosteriorFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"content_schema_version\":1", "\"content_schema_version\":2"));

            AssertFailure(Load(files), ContentDiagnosticCode.UnsupportedContentSchemaVersion);
        }

        [Test]
        public void MinGameSchemaAboveCurrentFailsClosed()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"min_game_schema_version\":1", "\"min_game_schema_version\":2"));

            AssertFailure(Load(files), ContentDiagnosticCode.IncompatibleGameSchemaVersion);
        }

        [Test]
        public void ArbitraryPositivePackVersionIsAccepted()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"]).Replace("\"content_pack_version\":1", "\"content_pack_version\":99"));

            ContentLoadResult result = Load(files);

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Pack.Manifest.ContentPackVersion, Is.EqualTo(99));
        }

        [TestCase("{}", ContentDiagnosticCode.InvalidPropertyType, TestName = "TargetConfigRootNotArrayFailsClosed")]
        [TestCase("[true]", ContentDiagnosticCode.InvalidPropertyType, TestName = "TargetConfigRowNotObjectFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.Legitimacy\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]}]", ContentDiagnosticCode.InvalidTargetPattern, TestName = "TargetConfigPatternInvalidFailsClosed")]
        [TestCase("[{\"pattern\":\"regions.*.admin_capS\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"SET\"]}]", ContentDiagnosticCode.InvalidTargetPattern, TestName = "TargetConfigStaticRegionalResourceFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":0,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]}]", ContentDiagnosticCode.InvalidRange, TestName = "TargetConfigScaleNonPositiveFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":10,\"maxS\":0,\"defaultS\":5,\"allow_ops\":[\"ADD\"]}]", ContentDiagnosticCode.InvalidRange, TestName = "TargetConfigRangeInvalidFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":10001,\"allow_ops\":[\"ADD\"]}]", ContentDiagnosticCode.InvalidRange, TestName = "TargetConfigDefaultOutOfRangeFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"BAD\"]}]", ContentDiagnosticCode.InvalidTargetOperation, TestName = "TargetConfigUnknownOperationFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\",\"ADD\"]}]", ContentDiagnosticCode.InvalidTargetOperation, TestName = "TargetConfigDuplicateOperationFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]},{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]}]", ContentDiagnosticCode.DuplicateTargetPattern, TestName = "TargetConfigDuplicatePatternFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"],\"normalize_group\":\"other.group\"}]", ContentDiagnosticCode.InvalidValue, TestName = "TargetConfigUnknownNormalizeGroupFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"],\"ui\":true}]", ContentDiagnosticCode.InvalidPropertyType, TestName = "TargetConfigInvalidUiTypeFailsClosed")]
        [TestCase("[{\"pattern\":\"metrics.*\",\"scale\":100,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"],\"qual\":true}]", ContentDiagnosticCode.InvalidPropertyType, TestName = "TargetConfigQualNotObjectFailsClosed")]
        public void TargetConfigFailuresFailClosed(string targetConfigJson, ContentDiagnosticCode code)
        {
            AssertFailure(Load(RebuildManifest(WithFile(ValidFixture(), "rules/target_config.json", targetConfigJson))), code);
        }

        [Test]
        public void TargetConfigLoadOrderAndResolutionArePreserved()
        {
            ContentLoadResult result = Load(ValidFixture());

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Pack.TargetConfigs[0].Pattern.ToString(), Is.EqualTo("metrics.*"));
            Assert.That(result.Pack.TargetConfigs[1].Pattern.ToString(), Is.EqualTo("metrics.legitimacy"));
            Assert.That(result.Pack.TargetConfigCatalog.Resolve(TargetPath.Parse("metrics.legitimacy")).Pattern.ToString(), Is.EqualTo("metrics.legitimacy"));
            Assert.That(result.Pack.TargetConfigCatalog.Resolve(TargetPath.Parse("igs.ig_test.approval")).Pattern.ToString(), Is.EqualTo("igs.*.approval"));
        }

        [TestCase("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":500000,\"macrozone\":\"CENTER\"},{\"id\":\"metropolitana\",\"name\":\"Metro 2\",\"weight_ppm\":500000,\"macrozone\":\"CENTER\"}]}", ContentDiagnosticCode.DuplicateId, TestName = "RegionDuplicateIdFailsClosed")]
        [TestCase("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"\",\"weight_ppm\":1000000,\"macrozone\":\"CENTER\"}]}", ContentDiagnosticCode.InvalidValue, TestName = "RegionEmptyNameFailsClosed")]
        [TestCase("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":1000000,\"macrozone\":\"BAD\"}]}", ContentDiagnosticCode.InvalidMacrozone, TestName = "RegionUnknownMacrozoneFailsClosed")]
        [TestCase("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":-1,\"macrozone\":\"CENTER\"}]}", ContentDiagnosticCode.InvalidValue, TestName = "RegionNegativeWeightFailsClosed")]
        [TestCase("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":999999,\"macrozone\":\"CENTER\"}]}", ContentDiagnosticCode.RegionWeightTotalMismatch, TestName = "RegionWeightTotalMismatchFailsClosed")]
        [TestCase("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":1000000,\"macrozone\":\"CENTER\",\"admin_capS\":true}]}", ContentDiagnosticCode.InvalidPropertyType, TestName = "RegionBoolCapFailsClosed")]
        [TestCase("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":1000000,\"macrozone\":\"CENTER\",\"admin_capS\":1.5}]}", ContentDiagnosticCode.InvalidPropertyType, TestName = "RegionFloatCapFailsClosed")]
        [TestCase("{\"regions\":[{\"id\":\"metropolitana\",\"name\":\"Metropolitana\",\"weight_ppm\":1000000,\"macrozone\":\"CENTER\",\"admin_capS\":10001}]}", ContentDiagnosticCode.InvalidRange, TestName = "RegionCapOutOfRangeFailsClosed")]
        public void RegionFailuresFailClosed(string regionJson, ContentDiagnosticCode code)
        {
            AssertFailure(Load(RebuildManifest(WithFile(ValidFixture(), "core/regions.json", regionJson))), code);
        }

        [Test]
        public void RegionOptionalStaticFieldsDefaultTo5000()
        {
            ContentLoadResult result = Load(ValidFixture());

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Pack.Regions[0].AdminCapS, Is.EqualTo(5000));
            Assert.That(result.Pack.Regions[0].IndustryCapS, Is.EqualTo(5000));
            Assert.That(result.Pack.Regions[0].ExtractiveCapS, Is.EqualTo(5000));
            Assert.That(result.Pack.Regions[0].SocialCapS, Is.EqualTo(5000));
            Assert.That(result.Pack.Regions[0].PopulationS, Is.EqualTo(5000));
        }

        [TestCase("{\"igs\":[{\"id\":\"bad\",\"name\":\"Bad\",\"tags\":[\"political.left\",\"labor.union\"]}]}", ContentDiagnosticCode.InvalidId, TestName = "InterestGroupIdWithoutPrefixFailsClosed")]
        [TestCase("{\"igs\":[{\"id\":\"ig_test\",\"name\":\"Test\",\"tags\":[\"political.left\",\"labor.union\"]},{\"id\":\"ig_test\",\"name\":\"Other\",\"tags\":[\"political.right\",\"labor.union\"]}]}", ContentDiagnosticCode.DuplicateId, TestName = "InterestGroupDuplicateIdFailsClosed")]
        [TestCase("{\"igs\":[{\"id\":\"ig_test\",\"name\":\"\",\"tags\":[\"political.left\",\"labor.union\"]}]}", ContentDiagnosticCode.InvalidValue, TestName = "InterestGroupEmptyNameFailsClosed")]
        [TestCase("{\"igs\":[{\"id\":\"ig_test\",\"name\":\"Test\",\"tags\":[\"political.left\"]}]}", ContentDiagnosticCode.InvalidValue, TestName = "InterestGroupTooFewTagsFailsClosed")]
        [TestCase("{\"igs\":[{\"id\":\"ig_test\",\"name\":\"Test\",\"tags\":[\"a.b\",\"c.d\",\"e.f\",\"g.h\",\"i.j\"]}]}", ContentDiagnosticCode.InvalidValue, TestName = "InterestGroupTooManyTagsFailsClosed")]
        [TestCase("{\"igs\":[{\"id\":\"ig_test\",\"name\":\"Test\",\"tags\":[\"political.left\",\"badtag\"]}]}", ContentDiagnosticCode.InvalidValue, TestName = "InterestGroupInvalidTagFailsClosed")]
        [TestCase("{\"igs\":[{\"id\":\"ig_test\",\"name\":\"Test\",\"tags\":[\"political.left\",\"political.left\"]}]}", ContentDiagnosticCode.InvalidValue, TestName = "InterestGroupDuplicateTagFailsClosed")]
        public void InterestGroupFailuresFailClosed(string igJson, ContentDiagnosticCode code)
        {
            AssertFailure(Load(RebuildManifest(WithFile(ValidFixture(), "core/igs.json", igJson))), code);
        }

        [TestCase("{\"movements\":[{\"id\":\"bad\",\"name\":\"Bad\",\"tags\":[\"political.left\"]}]}", ContentDiagnosticCode.InvalidId, TestName = "MovementIdWithoutPrefixFailsClosed")]
        [TestCase("{\"movements\":[{\"id\":\"mov_test\",\"name\":\"Test\",\"tags\":[\"political.left\"]},{\"id\":\"mov_test\",\"name\":\"Other\",\"tags\":[\"political.right\"]}]}", ContentDiagnosticCode.DuplicateId, TestName = "MovementDuplicateIdFailsClosed")]
        [TestCase("{\"movements\":[{\"id\":\"mov_test\",\"name\":\"\",\"tags\":[\"political.left\"]}]}", ContentDiagnosticCode.InvalidValue, TestName = "MovementEmptyNameFailsClosed")]
        [TestCase("{\"movements\":[{\"id\":\"mov_test\",\"name\":\"Test\",\"tags\":[]}]}", ContentDiagnosticCode.InvalidValue, TestName = "MovementEmptyTagsFailsClosed")]
        [TestCase("{\"movements\":[{\"id\":\"mov_test\",\"name\":\"Test\",\"tags\":[\"badtag\"]}]}", ContentDiagnosticCode.InvalidValue, TestName = "MovementInvalidTagFailsClosed")]
        [TestCase("{\"movements\":[{\"id\":\"mov_test\",\"name\":\"Test\",\"tags\":[\"political.left\",\"political.left\"]}]}", ContentDiagnosticCode.InvalidValue, TestName = "MovementDuplicateTagFailsClosed")]
        public void MovementFailuresFailClosed(string movementJson, ContentDiagnosticCode code)
        {
            AssertFailure(Load(RebuildManifest(WithFile(ValidFixture(), "core/movements.json", movementJson))), code);
        }

        [Test]
        public void AnyErrorProducesNullPackAndStructuredDiagnostic()
        {
            ContentLoadResult result = Load(RebuildManifest(WithFile(ValidFixture(), "rules/target_config.json", "[{\"pattern\":\"metrics.*\",\"scale\":true,\"minS\":0,\"maxS\":10000,\"defaultS\":5000,\"allow_ops\":[\"ADD\"]}]")));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Pack, Is.Null);
            ContentDiagnostic diagnostic = result.Diagnostics[0];
            Assert.That(diagnostic.Severity, Is.EqualTo(ContentDiagnosticSeverity.Error));
            Assert.That(diagnostic.Code, Is.Not.EqualTo(default(ContentDiagnosticCode)));
            Assert.That(diagnostic.RelativeFile, Is.Not.Empty);
            Assert.That(diagnostic.JsonPath, Is.Not.Empty);
            Assert.That(diagnostic.Message, Is.Not.Empty);
        }

        [Test]
        public void DiagnosticsCollectionIsReadOnlyAndOrderedDeterministically()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            files["manifest.json"] = Bytes(Text(files["manifest.json"])
                .Replace("\"languages\":[\"es\"]", "\"languages\":[\"es\",\"es\"]")
                .Replace("\"default_language\":\"es\"", "\"default_language\":\"en\""));

            ContentLoadResult first = Load(files);
            ContentLoadResult second = Load(files);

            Assert.Throws<NotSupportedException>(() => ((IList<ContentDiagnostic>)first.Diagnostics).Add(first.Diagnostics[0]));
            Assert.That(Diagnostics(first), Is.EqualTo(Diagnostics(second)));
        }

        [Test]
        public void PackCollectionsAndLookupsAreReadOnlySnapshots()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            ContentLoadResult result = Load(files);
            int originalRegionCount = result.Pack.Regions.Count;
            string originalFirstRegionId = result.Pack.Regions[0].Id;

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.Throws<NotSupportedException>(() => ((IList<RegionDefinition>)result.Pack.Regions).Add(result.Pack.Regions[0]));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, RegionDefinition>)result.Pack.RegionsById).Add("other", result.Pack.Regions[0]));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)result.Pack.Localization.Entries).Add("new.key", "value"));
            Assert.Throws<NotSupportedException>(() => ((IList<EffectTemplate>)result.Pack.Effects).Add(result.Pack.Effects[0]));
            Assert.Throws<KeyNotFoundException>(() => result.Pack.Localization.ResolveRequired("missing.key"));

            files["core/regions.json"] = Bytes("{\"regions\":[]}");
            Assert.That(result.Pack.Regions.Count, Is.EqualTo(originalRegionCount));
            Assert.That(result.Pack.RegionsById.ContainsKey(originalFirstRegionId), Is.True);
            Assert.That(result.Pack.EffectsById.ContainsKey("eff_legitimacy_down_small"), Is.True);
        }

        [Test]
        public void TwoEquivalentLoadsProduceEquivalentResults()
        {
            Dictionary<string, byte[]> files = ValidFixture();
            ContentLoadResult first = Load(files);
            ContentLoadResult second = Load(files);

            Assert.That(first.IsSuccess, Is.True, Diagnostics(first));
            Assert.That(second.IsSuccess, Is.True, Diagnostics(second));
            Assert.That(second.Pack.Manifest.ContentPackVersion, Is.EqualTo(first.Pack.Manifest.ContentPackVersion));
            Assert.That(second.Pack.TargetConfigs.Count, Is.EqualTo(first.Pack.TargetConfigs.Count));
            Assert.That(second.Pack.RegionsById["metropolitana"].Name, Is.EqualTo(first.Pack.RegionsById["metropolitana"].Name));
            Assert.That(second.Pack.Localization.ResolveRequired("effect.eff_legitimacy_down_small.title"), Is.EqualTo(first.Pack.Localization.ResolveRequired("effect.eff_legitimacy_down_small.title")));
            Assert.That(second.Pack.EffectsById["eff_exceptional_route_cost_default"].Modifiers.Count, Is.EqualTo(first.Pack.EffectsById["eff_exceptional_route_cost_default"].Modifiers.Count));
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

        private static int ErrorCount(ContentLoadResult result)
        {
            int count = 0;
            foreach (ContentDiagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity == ContentDiagnosticSeverity.Error)
                {
                    count++;
                }
            }

            return count;
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
            Dictionary<string, byte[]> files = LoadRealFixture();
            if (!includeMovements)
            {
                files.Remove("core/movements.json");
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

        private static Dictionary<string, byte[]> LoadRealFixture()
        {
            string root = ContentRoot();
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string relativePath in new[]
            {
                "core/regions.json",
                "core/igs.json",
                "core/movements.json",
                "rules/target_config.json",
                "rules/aggregation_config.json",
                "rules/legislative_config.json",
                "strings/es.json",
                "templates/effects.json",
                "templates/events.json",
                "templates/reforms.json"
            })
            {
                files.Add(relativePath, File.ReadAllBytes(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))));
            }

            return files;
        }

        private class InMemoryContentFileSource : IContentFileSource
        {
            protected readonly Dictionary<string, byte[]> Files;

            public InMemoryContentFileSource(Dictionary<string, byte[]> files)
            {
                Files = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
            }

            public virtual ContentFileReadResult TryReadAllBytes(string relativePath)
            {
                if (!Files.TryGetValue(relativePath, out byte[] bytes))
                {
                    return ContentFileReadResult.Missing("Missing in-memory file.");
                }

                return ContentFileReadResult.FromBytes(bytes);
            }
        }

        private sealed class CountingContentFileSource : InMemoryContentFileSource
        {
            private readonly Dictionary<string, int> _readCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            public CountingContentFileSource(Dictionary<string, byte[]> files)
                : base(files)
            {
            }

            public override ContentFileReadResult TryReadAllBytes(string relativePath)
            {
                _readCounts.TryGetValue(relativePath, out int count);
                _readCounts[relativePath] = count + 1;
                return base.TryReadAllBytes(relativePath);
            }

            public int ReadCount(string relativePath)
            {
                _readCounts.TryGetValue(relativePath, out int count);
                return count;
            }
        }
    }
}
