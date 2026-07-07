using System;
using NUnit.Framework;

namespace VictoriantChile.Simulation.Tests
{
    public sealed class GameStateTests
    {
        [Test]
        public void CreateNew_StartsAtTickZeroWithGivenSeed()
        {
            GameState state = GameState.CreateNew(rngSeed: 224);

            Assert.That(state.Meta.SaveVersion, Is.EqualTo(GameState.CurrentSaveVersion));
            Assert.That(state.Meta.RngSeed, Is.EqualTo(224));
            Assert.That(state.Meta.Tick, Is.Zero);
            Assert.That(state.Metrics, Is.Empty);
            Assert.That(state.Regions, Is.Empty);
        }

        [Test]
        public void InterestGroupState_ClampsVisibleRanges()
        {
            var state = new InterestGroupState(cloutS: 12000, approvalS: -15000);

            Assert.That(state.CloutS, Is.EqualTo(10000));
            Assert.That(state.ApprovalS, Is.EqualTo(-10000));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void MovementState_RejectsDirectionOutsideProAnti(int direction)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MovementState(intensityS: 5000, direction: direction));
        }
    }
}
