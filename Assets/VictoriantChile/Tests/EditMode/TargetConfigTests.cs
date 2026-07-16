using System;
using NUnit.Framework;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class TargetConfigTests
    {
        private static TargetOperation[] AllOps()
        {
            return new[] { TargetOperation.Add, TargetOperation.Multiply, TargetOperation.Set };
        }

        [Test]
        public void RepresentsRealMetricApprovalDirectionAndCloutConfigs()
        {
            TargetConfig metrics = new TargetConfig(TargetPattern.Parse("metrics.*"), 100, 0, 10_000, 5_000, AllOps());
            TargetConfig approval = new TargetConfig(TargetPattern.Parse("igs.*.approval"), 100, -10_000, 10_000, 0, AllOps());
            TargetConfig direction = new TargetConfig(TargetPattern.Parse("movements.*.direction"), 1, -1, 1, 1, new[] { TargetOperation.Set });
            TargetConfig clout = new TargetConfig(TargetPattern.Parse("igs.*.clout"), 100, 0, 10_000, 1_111, AllOps(), "igs.clout_sum_100");

            Assert.That(metrics.DefaultS, Is.EqualTo(5_000));
            Assert.That(approval.MinS, Is.EqualTo(-10_000));
            Assert.That(direction.Allows(TargetOperation.Set), Is.True);
            Assert.That(direction.Allows(TargetOperation.Add), Is.False);
            Assert.That(clout.NormalizeGroup, Is.EqualTo("igs.clout_sum_100"));
        }

        [Test]
        public void ValidatesScaleRangeDefaultAndOperations()
        {
            TargetPattern pattern = TargetPattern.Parse("metrics.*");
            Assert.Throws<ArgumentException>(() => new TargetConfig(default, 100, 0, 10_000, 5_000, AllOps()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TargetConfig(pattern, 0, 0, 10_000, 5_000, AllOps()));
            Assert.Throws<ArgumentException>(() => new TargetConfig(pattern, 100, 10, 0, 5, AllOps()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TargetConfig(pattern, 100, 0, 10, 11, AllOps()));
            Assert.Throws<ArgumentNullException>(() => new TargetConfig(pattern, 100, 0, 10, 5, null));
            Assert.Throws<ArgumentException>(() => new TargetConfig(pattern, 100, 0, 10, 5, new TargetOperation[0]));
            Assert.Throws<ArgumentException>(() => new TargetConfig(pattern, 100, 0, 10, 5, new[] { TargetOperation.Add, TargetOperation.Add }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TargetConfig(pattern, 100, 0, 10, 5, new[] { (TargetOperation)99 }));
        }

        [Test]
        public void ValidatesNormalizeGroup()
        {
            TargetPattern pattern = TargetPattern.Parse("igs.*.clout");
            Assert.DoesNotThrow(() => new TargetConfig(pattern, 100, 0, 10_000, 1_111, AllOps(), null));
            Assert.DoesNotThrow(() => new TargetConfig(pattern, 100, 0, 10_000, 1_111, AllOps(), "igs.clout_sum_100"));
            Assert.Throws<ArgumentException>(() => new TargetConfig(pattern, 100, 0, 10_000, 1_111, AllOps(), string.Empty));
            Assert.Throws<ArgumentException>(() => new TargetConfig(pattern, 100, 0, 10_000, 1_111, AllOps(), "igs..bad"));
            Assert.Throws<ArgumentException>(() => new TargetConfig(pattern, 100, 0, 10_000, 1_111, AllOps(), "IGS.clout"));
        }

        [Test]
        public void ClampUsesConfiguredRange()
        {
            TargetConfig approval = new TargetConfig(TargetPattern.Parse("igs.*.approval"), 100, -10_000, 10_000, 0, AllOps());
            Assert.That(approval.Clamp(-20_000), Is.EqualTo(-10_000));
            Assert.That(approval.Clamp(1_234), Is.EqualTo(1_234));
            Assert.That(approval.Clamp(20_000), Is.EqualTo(10_000));
        }

        [Test]
        public void AllowedOperationsAreDefensivelyCopied()
        {
            TargetOperation[] operations = AllOps();
            TargetConfig config = new TargetConfig(TargetPattern.Parse("metrics.*"), 100, 0, 10_000, 5_000, operations);
            operations[0] = TargetOperation.Set;
            Assert.That(config.Allows(TargetOperation.Add), Is.True);
            Assert.That(config.AllowedOperations.Count, Is.EqualTo(3));
            Assert.That(config.AllowedOperations, Is.Not.InstanceOf<TargetOperation[]>());
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<TargetOperation>)config.AllowedOperations)[0] = TargetOperation.Set);
        }
    }
}
