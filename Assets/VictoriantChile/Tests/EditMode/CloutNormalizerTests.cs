using System;
using System.Collections.Generic;
using NUnit.Framework;
using VictoriantChile.Simulation.Core.State;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class CloutNormalizerTests
    {
        [Test]
        public void SingleEntryReceivesAllClout()
        {
            IReadOnlyList<InterestGroupCloutValue> result = CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_only", 1111)
            });

            Assert.That(result[0].InterestGroupId, Is.EqualTo("ig_only"));
            Assert.That(result[0].CloutS, Is.EqualTo(10000));
        }

        [Test]
        public void ProportionOneTwoThreeUsesFloorAndResidueWinner()
        {
            IReadOnlyList<InterestGroupCloutValue> result = CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_a", 1),
                new InterestGroupCloutValue("ig_b", 2),
                new InterestGroupCloutValue("ig_c", 3)
            });

            Assert.That(Value(result, "ig_a"), Is.EqualTo(1666));
            Assert.That(Value(result, "ig_b"), Is.EqualTo(3333));
            Assert.That(Value(result, "ig_c"), Is.EqualTo(5001));
            Assert.That(Sum(result), Is.EqualTo(10000));
        }

        [Test]
        public void InputOrderDoesNotChangeResult()
        {
            InterestGroupCloutValue[] first =
            {
                new InterestGroupCloutValue("ig_c", 3),
                new InterestGroupCloutValue("ig_a", 1),
                new InterestGroupCloutValue("ig_b", 2)
            };
            InterestGroupCloutValue[] second =
            {
                new InterestGroupCloutValue("ig_b", 2),
                new InterestGroupCloutValue("ig_c", 3),
                new InterestGroupCloutValue("ig_a", 1)
            };

            Assert.That(Snapshot(CloutNormalizer.Normalize(first)), Is.EqualTo(Snapshot(CloutNormalizer.Normalize(second))));
        }

        [Test]
        public void TieForResidueUsesOrdinalSmallestId()
        {
            IReadOnlyList<InterestGroupCloutValue> result = CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_b", 1111),
                new InterestGroupCloutValue("ig_a", 1111),
                new InterestGroupCloutValue("ig_c", 1111)
            });

            Assert.That(Value(result, "ig_a"), Is.EqualTo(3334));
            Assert.That(Value(result, "ig_b"), Is.EqualTo(3333));
            Assert.That(Value(result, "ig_c"), Is.EqualTo(3333));
        }

        [Test]
        public void NineEqualRawValuesAssignResidueToOrdinalSmallestId()
        {
            IReadOnlyList<InterestGroupCloutValue> result = CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_territorio_productivo", 1111),
                new InterestGroupCloutValue("ig_sindicatos_trabajo", 1111),
                new InterestGroupCloutValue("ig_sector_publico_burocracia", 1111),
                new InterestGroupCloutValue("ig_progresistas_urbanos", 1111),
                new InterestGroupCloutValue("ig_profesionales_clase_media", 1111),
                new InterestGroupCloutValue("ig_orden_seguridad", 1111),
                new InterestGroupCloutValue("ig_empresariado_finanzas", 1111),
                new InterestGroupCloutValue("ig_conservadores_civicos", 1111),
                new InterestGroupCloutValue("ig_ambiental_regionalista", 1111)
            });

            Assert.That(result[0].InterestGroupId, Is.EqualTo("ig_ambiental_regionalista"));
            Assert.That(Value(result, "ig_ambiental_regionalista"), Is.EqualTo(1112));
            Assert.That(Count(result, 1111), Is.EqualTo(8));
            Assert.That(Sum(result), Is.EqualTo(10000));
        }

        [Test]
        public void ZeroValuesCanMixWithPositiveValues()
        {
            IReadOnlyList<InterestGroupCloutValue> result = CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_zero", 0),
                new InterestGroupCloutValue("ig_a", 1),
                new InterestGroupCloutValue("ig_b", 1)
            });

            Assert.That(Value(result, "ig_zero"), Is.EqualTo(0));
            Assert.That(Value(result, "ig_a"), Is.EqualTo(5000));
            Assert.That(Value(result, "ig_b"), Is.EqualTo(5000));
        }

        [Test]
        public void InvalidInputsFail()
        {
            Assert.Throws<ArgumentException>(() => CloutNormalizer.Normalize(new InterestGroupCloutValue[0]));
            Assert.Throws<ArgumentException>(() => CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_a", 0),
                new InterestGroupCloutValue("ig_b", 0)
            }));
            Assert.Throws<ArgumentOutOfRangeException>(() => CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_a", -1)
            }));
            Assert.Throws<ArgumentException>(() => new InterestGroupCloutValue(string.Empty, 1));
            Assert.Throws<ArgumentException>(() => CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_a", 1),
                new InterestGroupCloutValue("ig_a", 2)
            }));
        }

        [Test]
        public void LargeInputsDoNotWrap()
        {
            IReadOnlyList<InterestGroupCloutValue> result = CloutNormalizer.Normalize(new[]
            {
                new InterestGroupCloutValue("ig_a", int.MaxValue),
                new InterestGroupCloutValue("ig_b", int.MaxValue)
            });

            Assert.That(Value(result, "ig_a"), Is.EqualTo(5000));
            Assert.That(Value(result, "ig_b"), Is.EqualTo(5000));
            Assert.That(Sum(result), Is.EqualTo(10000));
            Assert.Throws<NotSupportedException>(() => ((IList<InterestGroupCloutValue>)result).Clear());
        }

        private static int Value(IReadOnlyList<InterestGroupCloutValue> values, string id)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].InterestGroupId == id)
                {
                    return values[i].CloutS;
                }
            }

            throw new AssertionException("Missing clout value for " + id);
        }

        private static int Sum(IReadOnlyList<InterestGroupCloutValue> values)
        {
            int total = 0;
            for (int i = 0; i < values.Count; i++)
            {
                total += values[i].CloutS;
            }

            return total;
        }

        private static int Count(IReadOnlyList<InterestGroupCloutValue> values, int cloutS)
        {
            int total = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].CloutS == cloutS)
                {
                    total++;
                }
            }

            return total;
        }

        private static string Snapshot(IReadOnlyList<InterestGroupCloutValue> values)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                parts.Add(values[i].InterestGroupId + "=" + values[i].CloutS);
            }

            return string.Join(";", parts.ToArray());
        }
    }
}
