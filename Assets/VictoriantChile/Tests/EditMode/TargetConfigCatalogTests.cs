using System;
using System.Collections.Generic;
using NUnit.Framework;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class TargetConfigCatalogTests
    {
        private static TargetConfig Config(string pattern, int defaultS)
        {
            return new TargetConfig(
                TargetPattern.Parse(pattern),
                100,
                -10_000,
                10_000,
                defaultS,
                new[] { TargetOperation.Add, TargetOperation.Multiply, TargetOperation.Set });
        }

        [Test]
        public void EmptyCatalogHasNoMatch()
        {
            TargetConfigCatalog catalog = new TargetConfigCatalog(new TargetConfig[0]);
            Assert.That(catalog.Count, Is.EqualTo(0));
            Assert.That(catalog.TryResolve(TargetPath.Parse("metrics.legitimacy"), out _), Is.False);
            Assert.Throws<KeyNotFoundException>(() => catalog.Resolve(TargetPath.Parse("metrics.legitimacy")));
        }

        [Test]
        public void ExactPatternBeatsWildcard()
        {
            TargetConfig wildcard = Config("metrics.*", 1);
            TargetConfig exact = Config("metrics.legitimacy", 2);
            TargetConfigCatalog catalog = new TargetConfigCatalog(new[] { wildcard, exact });
            Assert.That(catalog.Resolve(TargetPath.Parse("metrics.legitimacy")), Is.SameAs(exact));
        }

        [Test]
        public void MoreLiteralSegmentsBeatGenericPattern()
        {
            TargetConfig generic = Config("internals.*.*", 1);
            TargetConfig specific = Config("internals.economy.*", 2);
            TargetConfigCatalog catalog = new TargetConfigCatalog(new[] { generic, specific });
            Assert.That(catalog.Resolve(TargetPath.Parse("internals.economy.inflation")), Is.SameAs(specific));
        }

        [Test]
        public void LongerCanonicalPatternBreaksLiteralCountTie()
        {
            TargetConfig shorter = Config("regions.all.*", 1);
            TargetConfig longer = Config("regions.*.support", 2);
            Assert.That(shorter.Pattern.LiteralSegmentCount, Is.EqualTo(longer.Pattern.LiteralSegmentCount));
            Assert.That(longer.Pattern.CanonicalLength, Is.GreaterThan(shorter.Pattern.CanonicalLength));

            TargetConfigCatalog catalog = new TargetConfigCatalog(new[] { shorter, longer });
            Assert.That(catalog.Resolve(TargetPath.Parse("regions.all.support")), Is.SameAs(longer));
        }

        [Test]
        public void LoadOrderWinsCompleteTie()
        {
            TargetConfig first = Config("regions.*.value", 1);
            TargetConfig second = Config("regions.alpha.*", 2);
            Assert.That(first.Pattern.LiteralSegmentCount, Is.EqualTo(second.Pattern.LiteralSegmentCount));
            Assert.That(first.Pattern.CanonicalLength, Is.EqualTo(second.Pattern.CanonicalLength));

            TargetConfigCatalog catalog = new TargetConfigCatalog(new[] { first, second });
            Assert.That(catalog.Resolve(TargetPath.Parse("regions.alpha.value")), Is.SameAs(first));
        }

        [Test]
        public void ConstructorRejectsNullsAndDuplicatePatterns()
        {
            Assert.Throws<ArgumentNullException>(() => new TargetConfigCatalog(null));
            Assert.Throws<ArgumentNullException>(() => new TargetConfigCatalog(new TargetConfig[] { null }));
            Assert.Throws<ArgumentException>(() => new TargetConfigCatalog(new[] { Config("metrics.*", 1), Config("metrics.*", 2) }));
        }

        [Test]
        public void CatalogSnapshotsInputSequence()
        {
            TargetConfig first = Config("metrics.*", 1);
            TargetConfig second = Config("metrics.legitimacy", 2);
            List<TargetConfig> configs = new List<TargetConfig> { first };
            TargetConfigCatalog catalog = new TargetConfigCatalog(configs);
            configs.Add(second);

            Assert.That(catalog.Count, Is.EqualTo(1));
            Assert.That(catalog.Resolve(TargetPath.Parse("metrics.legitimacy")), Is.SameAs(first));
        }

        [Test]
        public void RepeatedResolutionReturnsSameConfig()
        {
            TargetConfig exact = Config("metrics.legitimacy", 2);
            TargetConfigCatalog catalog = new TargetConfigCatalog(new[] { Config("metrics.*", 1), exact });
            TargetPath path = TargetPath.Parse("metrics.legitimacy");
            Assert.That(catalog.Resolve(path), Is.SameAs(exact));
            Assert.That(catalog.Resolve(path), Is.SameAs(exact));
        }
    }
}
