using System;
using NUnit.Framework;

namespace VictoriantChile.Simulation.Tests
{
    public sealed class TargetPathTests
    {
        [TestCase("metrics.legitimacy", "metrics", 2)]
        [TestCase("internals.economy.inflation", "internals", 3)]
        [TestCase("regions.metropolitana.support", "regions", 3)]
        [TestCase("regions.metropolitana.industry_capS", "regions", 3)]
        [TestCase("igs.ig_sindicatos_trabajo.approval", "igs", 3)]
        [TestCase("movements.mov_trabajo_huelgas.intensity", "movements", 3)]
        public void Parse_AcceptsCanonicalConcretePaths(
            string raw,
            string expectedRoot,
            int expectedSegmentCount)
        {
            TargetPath path = TargetPath.Parse(raw);

            Assert.That(path.Value, Is.EqualTo(raw));
            Assert.That(path.Root, Is.EqualTo(expectedRoot));
            Assert.That(path.SegmentCount, Is.EqualTo(expectedSegmentCount));
            Assert.That(path.ToString(), Is.EqualTo(raw));
        }

        [Test]
        public void Equality_UsesOrdinalCanonicalValue()
        {
            TargetPath first = TargetPath.Parse("metrics.legitimacy");
            TargetPath second = TargetPath.Parse("metrics.legitimacy");
            TargetPath different = TargetPath.Parse("metrics.security");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first == second, Is.True);
            Assert.That(first != different, Is.True);
        }

        [TestCase("")]
        [TestCase("metrics")]
        [TestCase("metrics.")]
        [TestCase("metrics.Legitimacy")]
        [TestCase("metrics.valor١")]
        [TestCase("metrics.social__tension")]
        [TestCase("regions.metropolitana")]
        [TestCase("regions.*.support")]
        [TestCase("unknown.value")]
        public void Parse_RejectsMalformedOrNonConcretePaths(string raw)
        {
            Assert.Throws<FormatException>(() => TargetPath.Parse(raw));
        }

        [Test]
        public void Parse_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => TargetPath.Parse(null));
        }
    }
}
