using System;
using NUnit.Framework;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class TargetPathTests
    {
        [TestCase("metrics.legitimacy", "metrics", 2)]
        [TestCase("metrics.social_tension", "metrics", 2)]
        [TestCase("regions.metropolitana.support", "regions", 3)]
        [TestCase("regions.arica_parinacota.tension", "regions", 3)]
        [TestCase("regions.metropolitana.admin_capS", "regions", 3)]
        [TestCase("regions.arica_parinacota.industry_capS", "regions", 3)]
        [TestCase("igs.ig_sindicatos_trabajo.approval", "igs", 3)]
        [TestCase("movements.mov_seguridad_mano_dura.intensity", "movements", 3)]
        [TestCase("internals.economy.inflation", "internals", 3)]
        public void ValidPathsParse(string text, string expectedNamespace, int expectedSegments)
        {
            TargetPath path = TargetPath.Parse(text);
            Assert.That(path.ToString(), Is.EqualTo(text));
            Assert.That(path.Namespace, Is.EqualTo(expectedNamespace));
            Assert.That(path.SegmentCount, Is.EqualTo(expectedSegments));
        }

        [Test]
        public void IndexerExposesReadOnlySegments()
        {
            TargetPath path = TargetPath.Parse("regions.metropolitana.support");
            Assert.That(path[0], Is.EqualTo("regions"));
            Assert.That(path[1], Is.EqualTo("metropolitana"));
            Assert.That(path[2], Is.EqualTo("support"));
        }

        [Test]
        public void EqualityHashAndOrdinalComparisonUseCanonicalText()
        {
            TargetPath first = TargetPath.Parse("metrics.economy");
            TargetPath same = TargetPath.Parse("metrics.economy");
            TargetPath other = TargetPath.Parse("metrics.security");
            Assert.That(first, Is.EqualTo(same));
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(first == same, Is.True);
            Assert.That(first != other, Is.True);
            Assert.That(first.CompareTo(other), Is.LessThan(0));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" metrics.legitimacy")]
        [TestCase("metrics.legitimacy ")]
        [TestCase("metrics")]
        [TestCase("metrics.*")]
        [TestCase("Metrics.legitimacy")]
        [TestCase("metrics.legitimidad-á")]
        [TestCase("regions.Metropolitana.admin_capS")]
        [TestCase("regions.metropolitana.Admin_capS")]
        [TestCase("regions.metropolitana.admin_caps")]
        [TestCase("regions.metropolitana.otherFieldS")]
        [TestCase("internals.economy.valueS")]
        [TestCase("metrics.someValueS")]
        [TestCase("metrics..legitimacy")]
        [TestCase("regions.metropolitana")]
        [TestCase("regions.metropolitana.support.extra")]
        [TestCase("unknown.foo")]
        public void InvalidPathsFailParseAndTryParse(string text)
        {
            Assert.That(TargetPath.TryParse(text, out _), Is.False);
            Assert.Throws<ArgumentException>(() => TargetPath.Parse(text));
        }

        [Test]
        public void TryParseDoesNotThrowForInvalidInput()
        {
            Assert.DoesNotThrow(() => TargetPath.TryParse("regions.*.support", out _));
            Assert.That(TargetPath.TryParse("regions.*.support", out _), Is.False);
        }
    }
}
