using System;
using NUnit.Framework;
using VictoriantChile.Simulation.Core.Numerics;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class FixedMathTests
    {
        [Test]
        public void ConstantsMatchContract()
        {
            Assert.That(FixedMath.Scale, Is.EqualTo(100));
            Assert.That(FixedMath.HundredS, Is.EqualTo(10_000));
            Assert.That(FixedMath.MultiplierBaseS, Is.EqualTo(10_000));
        }

        [Test]
        public void RoundDivideRoundsHalfAwayFromZero()
        {
            Assert.That(FixedMath.RoundDivide(149, 100), Is.EqualTo(1));
            Assert.That(FixedMath.RoundDivide(150, 100), Is.EqualTo(2));
            Assert.That(FixedMath.RoundDivide(151, 100), Is.EqualTo(2));
            Assert.That(FixedMath.RoundDivide(-149, 100), Is.EqualTo(-1));
            Assert.That(FixedMath.RoundDivide(-150, 100), Is.EqualTo(-2));
            Assert.That(FixedMath.RoundDivide(-151, 100), Is.EqualTo(-2));
        }

        [Test]
        public void RoundDivideHandlesEvenAndOddDenominators()
        {
            Assert.That(FixedMath.RoundDivide(1, 2), Is.EqualTo(1));
            Assert.That(FixedMath.RoundDivide(-1, 2), Is.EqualTo(-1));
            Assert.That(FixedMath.RoundDivide(1, 3), Is.EqualTo(0));
            Assert.That(FixedMath.RoundDivide(2, 3), Is.EqualTo(1));
            Assert.That(FixedMath.RoundDivide(-1, 3), Is.EqualTo(0));
            Assert.That(FixedMath.RoundDivide(-2, 3), Is.EqualTo(-1));
        }

        [Test]
        public void RoundDivideRejectsInvalidDenominator()
        {
            Assert.Throws<DivideByZeroException>(() => FixedMath.RoundDivide(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixedMath.RoundDivide(1, -1));
        }

        [Test]
        public void RoundDivideHandlesLongExtremesWithoutOverflow()
        {
            Assert.That(FixedMath.RoundDivide(long.MaxValue, 1), Is.EqualTo(long.MaxValue));
            Assert.That(FixedMath.RoundDivide(long.MinValue, 1), Is.EqualTo(long.MinValue));
            Assert.That(FixedMath.RoundDivide(long.MaxValue, 2), Is.EqualTo(4_611_686_018_427_387_904L));
            Assert.That(FixedMath.RoundDivide(long.MinValue + 1, 2), Is.EqualTo(-4_611_686_018_427_387_904L));
        }

        [Test]
        public void RoundDivideToIntChecksOverflow()
        {
            Assert.That(FixedMath.RoundDivideToInt(int.MaxValue, 1), Is.EqualTo(int.MaxValue));
            Assert.That(FixedMath.RoundDivideToInt(int.MinValue, 1), Is.EqualTo(int.MinValue));
            Assert.Throws<OverflowException>(() => FixedMath.RoundDivideToInt((long)int.MaxValue + 1L, 1));
            Assert.Throws<OverflowException>(() => FixedMath.RoundDivideToInt((long)int.MinValue - 1L, 1));
        }

        [Test]
        public void FromWholeScalesAndChecksOverflow()
        {
            Assert.That(FixedMath.FromWhole(42), Is.EqualTo(4_200));
            Assert.That(FixedMath.FromWhole(-42), Is.EqualTo(-4_200));
            Assert.Throws<OverflowException>(() => FixedMath.FromWhole(int.MaxValue));
            Assert.Throws<OverflowException>(() => FixedMath.FromWhole(int.MinValue));
        }

        [Test]
        public void RoundToWholeUsesHalfAwayFromZero()
        {
            Assert.That(FixedMath.RoundToWhole(149), Is.EqualTo(1));
            Assert.That(FixedMath.RoundToWhole(150), Is.EqualTo(2));
            Assert.That(FixedMath.RoundToWhole(-149), Is.EqualTo(-1));
            Assert.That(FixedMath.RoundToWhole(-150), Is.EqualTo(-2));
        }

        [Test]
        public void AddCheckedRejectsWrap()
        {
            Assert.That(FixedMath.AddChecked(10, -3), Is.EqualTo(7));
            Assert.Throws<OverflowException>(() => FixedMath.AddChecked(int.MaxValue, 1));
            Assert.Throws<OverflowException>(() => FixedMath.AddChecked(int.MinValue, -1));
        }

        [Test]
        public void MultiplyScaledKeepsIdentityAndSigns()
        {
            Assert.That(FixedMath.MultiplyScaled(12_345, FixedMath.MultiplierBaseS), Is.EqualTo(12_345));
            Assert.That(FixedMath.MultiplyScaled(-12_345, FixedMath.MultiplierBaseS), Is.EqualTo(-12_345));
            Assert.That(FixedMath.MultiplyScaled(10_000, -5_000), Is.EqualTo(-5_000));
        }

        [Test]
        public void MultiplyScaledRoundsHalfAwayFromZero()
        {
            Assert.That(FixedMath.MultiplyScaled(1, 5_000), Is.EqualTo(1));
            Assert.That(FixedMath.MultiplyScaled(-1, 5_000), Is.EqualTo(-1));
            Assert.That(FixedMath.MultiplyScaled(1, 4_999), Is.EqualTo(0));
            Assert.That(FixedMath.MultiplyScaled(-1, 4_999), Is.EqualTo(0));
        }

        [Test]
        public void MultiplyScaledChecksFinalIntRange()
        {
            Assert.That(FixedMath.MultiplyScaled(int.MaxValue, FixedMath.MultiplierBaseS), Is.EqualTo(int.MaxValue));
            Assert.That(FixedMath.MultiplyScaled(int.MinValue, FixedMath.MultiplierBaseS), Is.EqualTo(int.MinValue));
            Assert.Throws<OverflowException>(() => FixedMath.MultiplyScaled(int.MaxValue, 10_001));
            Assert.Throws<OverflowException>(() => FixedMath.MultiplyScaled(int.MinValue, 10_001));
        }

        [Test]
        public void ClampIsInclusiveAndValidatesRange()
        {
            Assert.That(FixedMath.Clamp(5, -10, 10), Is.EqualTo(5));
            Assert.That(FixedMath.Clamp(-20, -10, 10), Is.EqualTo(-10));
            Assert.That(FixedMath.Clamp(20, -10, 10), Is.EqualTo(10));
            Assert.That(FixedMath.Clamp(-10, -10, 10), Is.EqualTo(-10));
            Assert.That(FixedMath.Clamp(10, -10, 10), Is.EqualTo(10));
            Assert.Throws<ArgumentException>(() => FixedMath.Clamp(0, 10, -10));
        }
    }
}
