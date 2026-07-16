using System;
using NUnit.Framework;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class TargetPatternTests
    {
        [TestCase("metrics.*", false, 1, 2)]
        [TestCase("metrics.legitimacy", true, 2, 2)]
        [TestCase("regions.*.support", false, 2, 3)]
        [TestCase("regions.metropolitana.*", false, 2, 3)]
        [TestCase("regions.*.admin_capS", false, 2, 3)]
        [TestCase("regions.*.industry_capS", false, 2, 3)]
        [TestCase("regions.*.extractive_capS", false, 2, 3)]
        [TestCase("regions.*.social_capS", false, 2, 3)]
        [TestCase("regions.*.populationS", false, 2, 3)]
        [TestCase("igs.*.approval", false, 2, 3)]
        [TestCase("movements.*.intensity", false, 2, 3)]
        [TestCase("internals.*.*", false, 1, 3)]
        public void ValidPatternsParse(string text, bool exact, int literalCount, int segmentCount)
        {
            TargetPattern pattern = TargetPattern.Parse(text);
            Assert.That(pattern.ToString(), Is.EqualTo(text));
            Assert.That(pattern.IsExact, Is.EqualTo(exact));
            Assert.That(pattern.LiteralSegmentCount, Is.EqualTo(literalCount));
            Assert.That(pattern.SegmentCount, Is.EqualTo(segmentCount));
            Assert.That(pattern.CanonicalLength, Is.EqualTo(text.Length));
        }

        [Test]
        public void MatchesUsesOneSegmentWildcards()
        {
            Assert.That(TargetPattern.Parse("metrics.*").Matches(TargetPath.Parse("metrics.legitimacy")), Is.True);
            Assert.That(TargetPattern.Parse("regions.*.support").Matches(TargetPath.Parse("regions.metropolitana.support")), Is.True);
            Assert.That(TargetPattern.Parse("regions.*.admin_capS").Matches(TargetPath.Parse("regions.metropolitana.admin_capS")), Is.True);
            Assert.That(TargetPattern.Parse("regions.metropolitana.*").Matches(TargetPath.Parse("regions.metropolitana.tension")), Is.True);
            Assert.That(TargetPattern.Parse("regions.*.support").Matches(TargetPath.Parse("regions.metropolitana.tension")), Is.False);
            Assert.That(TargetPattern.Parse("metrics.economy").Matches(TargetPath.Parse("metrics.security")), Is.False);
        }

        [TestCase("*")]
        [TestCase("regions.**")]
        [TestCase("regions.met*.support")]
        [TestCase("regions.* ")]
        [TestCase("regions.*.*.*")]
        [TestCase("metrics.*.extra")]
        [TestCase("regions.Metropolitana.admin_capS")]
        [TestCase("regions.*.Admin_capS")]
        [TestCase("regions.*.admin_caps")]
        [TestCase("regions.*.otherFieldS")]
        [TestCase("internals.economy.valueS")]
        [TestCase("metrics.someValueS")]
        public void InvalidPatternsFail(string text)
        {
            Assert.That(TargetPattern.TryParse(text, out _), Is.False);
            Assert.Throws<ArgumentException>(() => TargetPattern.Parse(text));
        }

        [Test]
        public void EqualityUsesCanonicalText()
        {
            TargetPattern first = TargetPattern.Parse("igs.*.approval");
            TargetPattern same = TargetPattern.Parse("igs.*.approval");
            TargetPattern other = TargetPattern.Parse("igs.*.clout");
            Assert.That(first, Is.EqualTo(same));
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(first == same, Is.True);
            Assert.That(first != other, Is.True);
        }
    }
}
