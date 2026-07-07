using System;
using NUnit.Framework;

namespace VictoriantChile.Simulation.Tests
{
    public sealed class FixedPointTests
    {
        [Test]
        public void FromWhole_UsesCanonicalScale()
        {
            Assert.That(FixedPoint.FromWhole(50), Is.EqualTo(5000));
        }

        [TestCase(-100, 0)]
        [TestCase(4200, 4200)]
        [TestCase(12000, 10000)]
        public void Clamp_RestrictsValueToConfiguredRange(int valueS, int expectedS)
        {
            Assert.That(FixedPoint.Clamp(valueS, 0, 10000), Is.EqualTo(expectedS));
        }

        [Test]
        public void Clamp_RejectsInvertedRange()
        {
            Assert.Throws<ArgumentException>(() => FixedPoint.Clamp(0, 10, 0));
        }
    }
}
