using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using VictoriantChile.Content.Loading;
using VictoriantChile.Content.Models;
using VictoriantChile.Content.State;
using VictoriantChile.Simulation.Core.Causality;
using VictoriantChile.Simulation.Core.Resolution;
using VictoriantChile.Simulation.Core.State;
using VictoriantChile.Simulation.Core.Targets;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class CausalLedgerTests
    {
        private static readonly CauseRef DecisionTax = new CauseRef(CauseCategory.Decision, "dec_tax_relief");
        private static readonly CauseRef EventSecurity = new CauseRef(CauseCategory.Event, "ev_security_flashpoint");
        private static readonly CauseRef ReformRouteA = new CauseRef(CauseCategory.Reform, "ref_constitutional_route_a");
        private static readonly CauseRef MovementPressure = new CauseRef(CauseCategory.Movement, "mov_trabajo_huelgas");
        private static readonly CauseRef ModifierSecurity = new CauseRef(CauseCategory.Modifier, "eff_security_bonus", EventSecurity);
        private static readonly CauseRef ModifierReform = new CauseRef(CauseCategory.Modifier, "eff_route_a_momentum", ReformRouteA);

        [Test]
        public void CauseCategoryOrderAndReservedSystemRefsAreExact()
        {
            Assert.That((int)CauseCategory.Decision, Is.EqualTo(1));
            Assert.That((int)CauseCategory.Event, Is.EqualTo(2));
            Assert.That((int)CauseCategory.Reform, Is.EqualTo(3));
            Assert.That((int)CauseCategory.Movement, Is.EqualTo(4));
            Assert.That((int)CauseCategory.Modifier, Is.EqualTo(5));
            Assert.That((int)CauseCategory.System, Is.EqualTo(6));
            Assert.That(CauseRef.SystemClamp.DisplayText, Is.EqualTo("SYSTEM:CLAMP"));
            Assert.That(CauseRef.SystemRounding.DisplayText, Is.EqualTo("SYSTEM:ROUNDING"));
            Assert.That(CauseRef.SystemIgCloutNormalize.DisplayText, Is.EqualTo("SYSTEM:IG_CLOUT_NORMALIZE"));
        }

        [Test]
        public void CauseRefUsesStructuralIdentityHashAndOrdinalOrdering()
        {
            CauseRef same = new CauseRef(CauseCategory.Modifier, "eff_security_bonus", new CauseRef(CauseCategory.Event, "ev_security_flashpoint"));
            CauseRef differentParent = new CauseRef(CauseCategory.Modifier, "eff_security_bonus", new CauseRef(CauseCategory.Decision, "dec_tax_relief"));

            Assert.That(ModifierSecurity.Equals(same), Is.True);
            Assert.That(ModifierSecurity.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(ModifierSecurity.Equals(differentParent), Is.False);
            Assert.That(DecisionTax.CompareTo(EventSecurity), Is.LessThan(0));
            Assert.That(ModifierSecurity.CanonicalKey, Is.EqualTo("MODIFIER:eff_security_bonus|parent=EVENT:ev_security_flashpoint"));
        }

        [Test]
        public void CauseRefRejectsInvalidIdsAndParentShapes()
        {
            Assert.That(new CauseRef(CauseCategory.Modifier, "eff_from_decision", DecisionTax).Parent, Is.EqualTo(DecisionTax));
            Assert.That(new CauseRef(CauseCategory.Modifier, "eff_from_event", EventSecurity).Parent, Is.EqualTo(EventSecurity));
            Assert.That(new CauseRef(CauseCategory.Modifier, "eff_from_reform", ReformRouteA).Parent, Is.EqualTo(ReformRouteA));
            Assert.That(new CauseRef(CauseCategory.Modifier, "eff_from_movement", MovementPressure).Parent, Is.EqualTo(MovementPressure));

            AssertCausalCode(
                delegate { _ = new CauseRef(CauseCategory.Decision, ""); },
                CausalLedgerErrorCodes.InvalidCause);
            AssertCausalCode(
                delegate { _ = new CauseRef(CauseCategory.Decision, " bad"); },
                CausalLedgerErrorCodes.InvalidCause);
            AssertCausalCode(
                delegate { _ = new CauseRef(CauseCategory.Decision, "bad:id"); },
                CausalLedgerErrorCodes.InvalidCause);
            AssertCausalCode(
                delegate { _ = new CauseRef(CauseCategory.Modifier, "eff_missing_parent"); },
                CausalLedgerErrorCodes.InvalidParent);
            AssertCausalCode(
                delegate { _ = new CauseRef(CauseCategory.Decision, "dec_bad_parent", EventSecurity); },
                CausalLedgerErrorCodes.InvalidParent);
            AssertCausalCode(
                delegate { _ = new CauseRef(CauseCategory.Modifier, "eff_bad_parent", CauseRef.SystemClamp); },
                CausalLedgerErrorCodes.InvalidParent);
            AssertCausalCode(
                delegate { _ = new CauseRef(CauseCategory.Modifier, "eff_chain", new CauseRef(CauseCategory.Modifier, "eff_parent", EventSecurity)); },
                CausalLedgerErrorCodes.InvalidParent);
        }

        [Test]
        public void VisibleTargetCatalogMatchesFrozenMvpSurfaceAndOrder()
        {
            ContentPack pack = LoadRealPack();
            GameState state = CreateState(pack, 10);
            VisibleTargetCatalog catalog = CreateCatalog(pack, state);

            int expectedCount = 10 + (4 * pack.Regions.Count) + (2 * pack.InterestGroups.Count) + (2 * pack.Movements.Count);
            Assert.That(catalog.Targets.Count, Is.EqualTo(expectedCount));
            Assert.That(catalog.Targets[0].ToString(), Is.EqualTo("metrics.legitimacy"));
            Assert.That(catalog.Targets[9].ToString(), Is.EqualTo("metrics.governability"));
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.support")), Is.True);
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.tension")), Is.True);
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.organization")), Is.True);
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.rival_presence")), Is.True);
            Assert.That(catalog.IsVisible(TargetPath.Parse("igs.ig_sindicatos_trabajo.clout")), Is.True);
            Assert.That(catalog.IsVisible(TargetPath.Parse("igs.ig_sindicatos_trabajo.approval")), Is.True);
            Assert.That(catalog.IsVisible(TargetPath.Parse("movements.mov_seguridad_mano_dura.intensity")), Is.True);
            Assert.That(catalog.IsVisible(TargetPath.Parse("movements.mov_seguridad_mano_dura.direction")), Is.True);
            Assert.That(catalog.IsVisible(TargetPath.Parse("internals.economy.growth")), Is.False);
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.admin_capS")), Is.False);
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.industry_capS")), Is.False);
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.extractive_capS")), Is.False);
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.social_capS")), Is.False);
            Assert.That(catalog.IsVisible(TargetPath.Parse("regions.metropolitana.populationS")), Is.False);

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < catalog.Targets.Count; i++)
            {
                Assert.That(seen.Add(catalog.Targets[i].ToString()), Is.True);
            }
        }

        [Test]
        public void VisibleTargetCatalogRejectsMissingOrUnknownDynamicIds()
        {
            ContentPack pack = LoadRealPack();
            GameState state = CreateState(pack, 10);
            List<string> regions = ProjectIds(pack.Regions, delegate(RegionDefinition value) { return value.Id; });
            List<string> igs = ProjectIds(pack.InterestGroups, delegate(InterestGroupDefinition value) { return value.Id; });
            List<string> movements = ProjectIds(pack.Movements, delegate(MovementDefinition value) { return value.Id; });

            regions.RemoveAt(regions.Count - 1);
            AssertCausalCode(
                delegate { _ = VisibleTargetCatalog.CreateForMvp(state, regions, igs, movements); },
                CausalLedgerErrorCodes.InvalidTargetOrder);

            movements = ProjectIds(pack.Movements, delegate(MovementDefinition value) { return value.Id; });
            movements[0] = "mov_missing";
            AssertCausalCode(
                delegate { _ = VisibleTargetCatalog.CreateForMvp(state, ProjectIds(pack.Regions, delegate(RegionDefinition value) { return value.Id; }), igs, movements); },
                CausalLedgerErrorCodes.InvalidTargetOrder);
        }

        [Test]
        public void VisibleTargetCatalogCreateCanonicalFromStateUsesStableOrdinalFallbackOrdering()
        {
            ContentPack pack = LoadRealPack();
            GameState state = CreateState(pack, 10);

            VisibleTargetCatalog fromState = VisibleTargetCatalog.CreateCanonicalFromState(state);
            List<string> expectedRegionIds = new List<string>(state.RegionsById.Keys);
            expectedRegionIds.Sort(StringComparer.Ordinal);

            Assert.That(fromState.Targets.Count, Is.EqualTo(CreateCatalog(pack, state).Targets.Count));
            for (int i = 0; i < expectedRegionIds.Count; i++)
            {
                int baseIndex = 10 + (i * 4);
                Assert.That(fromState.Targets[baseIndex].ToString(), Is.EqualTo("regions." + expectedRegionIds[i] + ".support"));
                Assert.That(fromState.Targets[baseIndex + 1].ToString(), Is.EqualTo("regions." + expectedRegionIds[i] + ".tension"));
                Assert.That(fromState.Targets[baseIndex + 2].ToString(), Is.EqualTo("regions." + expectedRegionIds[i] + ".organization"));
                Assert.That(fromState.Targets[baseIndex + 3].ToString(), Is.EqualTo("regions." + expectedRegionIds[i] + ".rival_presence"));
            }
        }

        [Test]
        public void TickBufferAggregatesContributionsAndIgnoresZeroNoise()
        {
            VisibleTargetCatalog catalog = CreateCatalog(LoadRealPack(), CreateState(LoadRealPack(), 10));
            TickCausalBuffer buffer = new TickCausalBuffer(4, catalog);
            TargetPath target = TargetPath.Parse("metrics.legitimacy");

            buffer.TrackTarget(target, 5000);
            buffer.RecordContribution(target, DecisionTax, 400);
            buffer.RecordContribution(target, DecisionTax, 100);
            buffer.RecordContribution(target, EventSecurity, -100);
            buffer.RecordContribution(target, EventSecurity, 100);
            buffer.RecordContribution(target, MovementPressure, 0);
            buffer.CloseTarget(target, 5500);
            TickCausalSnapshot snapshot = buffer.Seal();

            Assert.That(snapshot.ChangedTargets.Count, Is.EqualTo(1));
            Assert.That(snapshot.ChangedTargets[0].DeltaTotalS, Is.EqualTo(500));
            Assert.That(snapshot.ChangedTargets[0].Contributions.Count, Is.EqualTo(1));
            Assert.That(snapshot.ChangedTargets[0].Contributions[0].Cause, Is.EqualTo(DecisionTax));
            Assert.That(snapshot.ChangedTargets[0].Contributions[0].DeltaS, Is.EqualTo(500));
            Assert.Throws<NotSupportedException>(delegate { ((IList<TargetCausalSnapshot>)snapshot.ChangedTargets).Clear(); });
            Assert.Throws<NotSupportedException>(delegate { ((IList<CausalContribution>)snapshot.ChangedTargets[0].Contributions).Clear(); });
        }

        [Test]
        public void TickBufferRejectsInvalidLifecycleAndAccountingMismatch()
        {
            VisibleTargetCatalog catalog = CreateCatalog(LoadRealPack(), CreateState(LoadRealPack(), 10));
            TickCausalBuffer buffer = new TickCausalBuffer(7, catalog);
            TargetPath visible = TargetPath.Parse("metrics.legitimacy");

            AssertCausalCode(
                delegate { buffer.RecordContribution(visible, DecisionTax, 1); },
                CausalLedgerErrorCodes.ContributionBeforeBaseline);

            buffer.TrackTarget(visible, 5000);
            AssertCausalCode(
                delegate { buffer.TrackTarget(visible, 5000); },
                CausalLedgerErrorCodes.DuplicateTargetBaseline);
            AssertCausalCode(
                delegate { buffer.RecordContribution(TargetPath.Parse("internals.economy.growth"), DecisionTax, 1); },
                CausalLedgerErrorCodes.NonVisibleTarget);

            buffer.RecordContribution(visible, DecisionTax, 200);
            AssertCausalCode(
                delegate { buffer.CloseTarget(visible, 5300); },
                CausalLedgerErrorCodes.AccountingMismatch);

            buffer.CloseTarget(visible, 5200);
            AssertCausalCode(
                delegate { buffer.CloseTarget(visible, 5200); },
                CausalLedgerErrorCodes.DuplicateClose);
            AssertCausalCode(
                delegate { buffer.RecordContribution(visible, DecisionTax, 1); },
                CausalLedgerErrorCodes.ContributionAfterClose);

            TickCausalSnapshot snapshot = buffer.Seal();
            Assert.That(snapshot.ChangedTargets.Count, Is.EqualTo(1));
            AssertCausalCode(
                delegate { buffer.TrackTarget(TargetPath.Parse("metrics.security"), 5000); },
                CausalLedgerErrorCodes.MutationAfterSeal);
        }

        [Test]
        public void TickBufferRequiresAllTargetsToBeClosedAndRejectsOverflow()
        {
            VisibleTargetCatalog catalog = CreateCatalog(LoadRealPack(), CreateState(LoadRealPack(), 10));
            TickCausalBuffer unclosed = new TickCausalBuffer(8, catalog);
            TargetPath target = TargetPath.Parse("metrics.legitimacy");

            unclosed.TrackTarget(target, 5000);
            AssertCausalCode(
                delegate { _ = unclosed.Seal(); },
                CausalLedgerErrorCodes.UnclosedTarget);

            TickCausalBuffer overflow = new TickCausalBuffer(9, catalog);
            overflow.TrackTarget(target, 0);
            overflow.RecordContribution(target, DecisionTax, long.MaxValue);
            AssertCausalCode(
                delegate { overflow.RecordContribution(target, DecisionTax, 1L); },
                CausalLedgerErrorCodes.ArithmeticOverflow);
        }

        [Test]
        public void ZeroDeltaTargetsAreOmittedFromPublicTickSnapshots()
        {
            VisibleTargetCatalog catalog = CreateCatalog(LoadRealPack(), CreateState(LoadRealPack(), 10));
            TickCausalBuffer buffer = new TickCausalBuffer(10, catalog);
            TargetPath target = TargetPath.Parse("metrics.security");

            buffer.TrackTarget(target, 5000);
            buffer.RecordContribution(target, DecisionTax, 100);
            buffer.RecordContribution(target, EventSecurity, -100);
            buffer.CloseTarget(target, 5000);

            TickCausalSnapshot snapshot = buffer.Seal();
            Assert.That(snapshot.AuditedTargets.Count, Is.EqualTo(1));
            Assert.That(snapshot.AuditedTargets[0].InitialValueS, Is.EqualTo(5000));
            Assert.That(snapshot.AuditedTargets[0].FinalValueS, Is.EqualTo(5000));
            Assert.That(snapshot.ChangedTargets, Is.Empty);
        }

        [Test]
        public void TickBufferFailuresAreAtomicAndLeaveTrackedTargetsRecoverable()
        {
            VisibleTargetCatalog catalog = CreateCatalog(LoadRealPack(), CreateState(LoadRealPack(), 10));
            TargetPath target = TargetPath.Parse("metrics.legitimacy");

            TickCausalBuffer contributionOverflow = new TickCausalBuffer(11, catalog);
            contributionOverflow.TrackTarget(target, 0);
            contributionOverflow.RecordContribution(target, DecisionTax, 1);
            AssertCausalCode(
                delegate { contributionOverflow.RecordContribution(target, DecisionTax, long.MaxValue); },
                CausalLedgerErrorCodes.ArithmeticOverflow);
            contributionOverflow.CloseTarget(target, 1);
            Assert.That(contributionOverflow.Seal().ChangedTargets[0].DeltaTotalS, Is.EqualTo(1L));

            TickCausalBuffer closeMismatch = new TickCausalBuffer(12, catalog);
            closeMismatch.TrackTarget(target, 5000);
            closeMismatch.RecordContribution(target, DecisionTax, 200);
            AssertCausalCode(
                delegate { closeMismatch.CloseTarget(target, 5300); },
                CausalLedgerErrorCodes.AccountingMismatch);
            closeMismatch.CloseTarget(target, 5200);
            Assert.That(closeMismatch.Seal().ChangedTargets[0].FinalValueS, Is.EqualTo(5200));
        }

        [Test]
        public void CloutNormalizationLedgerBuildsSystemContributionsAndRejectsInvalidShapes()
        {
            IReadOnlyList<CausalTargetContribution> contributions = InterestGroupCloutNormalizationLedger.BuildContributions(
                new[]
                {
                    new InterestGroupCloutValue("ig_a", 1111),
                    new InterestGroupCloutValue("ig_b", 1111)
                },
                new[]
                {
                    new InterestGroupCloutValue("ig_a", 1200),
                    new InterestGroupCloutValue("ig_b", 8800)
                });

            Assert.That(contributions.Count, Is.EqualTo(2));
            Assert.That(contributions[0].Cause, Is.EqualTo(CauseRef.SystemIgCloutNormalize));
            Assert.That(contributions[0].Target.ToString(), Is.EqualTo("igs.ig_a.clout"));
            Assert.That(contributions[0].DeltaS, Is.EqualTo(89));
            Assert.That(contributions[1].DeltaS, Is.EqualTo(7689));

            AssertCausalCode(
                delegate
                {
                    _ = InterestGroupCloutNormalizationLedger.BuildContributions(
                        new[] { new InterestGroupCloutValue("ig_a", 1) },
                        new[] { new InterestGroupCloutValue("ig_b", 10000) });
                },
                CausalLedgerErrorCodes.InvalidNormalization);
            AssertCausalCode(
                delegate
                {
                    _ = InterestGroupCloutNormalizationLedger.BuildContributions(
                        new[] { new InterestGroupCloutValue("ig_a", 1) },
                        new[] { new InterestGroupCloutValue("ig_a", 9999) });
                },
                CausalLedgerErrorCodes.InvalidNormalization);
            AssertCausalCode(
                delegate
                {
                    _ = InterestGroupCloutNormalizationLedger.BuildContributions(
                        new[]
                        {
                            new InterestGroupCloutValue("ig_a", 1000),
                            new InterestGroupCloutValue("ig_b", 9000),
                        },
                        new[]
                        {
                            new InterestGroupCloutValue("ig_a", 1000),
                            new InterestGroupCloutValue("ig_c", 9000),
                        });
                },
                CausalLedgerErrorCodes.InvalidNormalization);
            AssertCausalCode(
                delegate
                {
                    _ = InterestGroupCloutNormalizationLedger.BuildContributions(
                        new[]
                        {
                            new InterestGroupCloutValue("ig_a", 1000),
                            new InterestGroupCloutValue("ig_b", 9000),
                        },
                        new[]
                        {
                            new InterestGroupCloutValue("ig_a", 1000),
                            new InterestGroupCloutValue("ig_b", 9001),
                        });
                },
                CausalLedgerErrorCodes.InvalidNormalization);
        }

        [Test]
        public void CloutNormalizationLedgerIsOrderIndependentAndDoesNotReturnPartialContributions()
        {
            IReadOnlyList<CausalTargetContribution> first = InterestGroupCloutNormalizationLedger.BuildContributions(
                new[]
                {
                    new InterestGroupCloutValue("ig_b", 7000),
                    new InterestGroupCloutValue("ig_a", 3000),
                },
                new[]
                {
                    new InterestGroupCloutValue("ig_b", 6500),
                    new InterestGroupCloutValue("ig_a", 3500),
                });
            IReadOnlyList<CausalTargetContribution> second = InterestGroupCloutNormalizationLedger.BuildContributions(
                new[]
                {
                    new InterestGroupCloutValue("ig_a", 3000),
                    new InterestGroupCloutValue("ig_b", 7000),
                },
                new[]
                {
                    new InterestGroupCloutValue("ig_a", 3500),
                    new InterestGroupCloutValue("ig_b", 6500),
                });

            Assert.That(first.Count, Is.EqualTo(2));
            Assert.That(second.Count, Is.EqualTo(2));
            Assert.That(first[0].Target, Is.EqualTo(second[0].Target));
            Assert.That(first[0].DeltaS, Is.EqualTo(second[0].DeltaS));
            Assert.That(first[1].Target, Is.EqualTo(second[1].Target));
            Assert.That(first[1].DeltaS, Is.EqualTo(second[1].DeltaS));
        }

        [Test]
        public void PeriodAccumulatorRequiresConsecutiveTicksAndContinuity()
        {
            TickCausalSnapshot first = BuildTickSnapshot(
                1,
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.legitimacy"),
                    5000,
                    5200,
                    new[] { new CausalContribution(DecisionTax, 200) }));
            TickCausalSnapshot second = BuildTickSnapshot(
                2,
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.legitimacy"),
                    5200,
                    5100,
                    new[] { new CausalContribution(EventSecurity, -100) }));
            PeriodCausalSnapshot period = CausalPeriodAccumulator.Accumulate(new[] { first, second });

            Assert.That(period.TickStart, Is.EqualTo(1));
            Assert.That(period.TickEnd, Is.EqualTo(2));
            Assert.That(period.TickCount, Is.EqualTo(2));
            Assert.That(period.ChangedTargets.Count, Is.EqualTo(1));
            Assert.That(period.ChangedTargets[0].DeltaTotalS, Is.EqualTo(100));
            Assert.That(period.ChangedTargets[0].Contributions.Count, Is.EqualTo(2));

            AssertCausalCode(
                delegate { _ = CausalPeriodAccumulator.Accumulate(new[] { second, first }); },
                CausalLedgerErrorCodes.DuplicateTick);
            AssertCausalCode(
                delegate { _ = CausalPeriodAccumulator.Accumulate(new[] { first, BuildTickSnapshot(3) }); },
                CausalLedgerErrorCodes.NonConsecutiveTicks);
            AssertCausalCode(
                delegate
                {
                    _ = CausalPeriodAccumulator.Accumulate(new[]
                    {
                        first,
                        BuildTickSnapshot(
                            2,
                            new TargetCausalSnapshot(
                                TargetPath.Parse("metrics.legitimacy"),
                                5300,
                                5400,
                                new[] { new CausalContribution(EventSecurity, 100) }))
                    });
                },
                CausalLedgerErrorCodes.DiscontinuousTargetValues);
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(12)]
        public void PeriodAccumulatorSupportsExpectedWindowLengths(int tickCount)
        {
            List<TickCausalSnapshot> snapshots = new List<TickCausalSnapshot>();
            int initial = 5000;
            int current = initial;
            for (int tick = 1; tick <= tickCount; tick++)
            {
                int next = current + 10;
                snapshots.Add(BuildTickSnapshot(
                    tick,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        current,
                        next,
                        new[] { new CausalContribution(DecisionTax, 10) })));
                current = next;
            }

            PeriodCausalSnapshot period = CausalPeriodAccumulator.Accumulate(snapshots);
            Assert.That(period.TickStart, Is.EqualTo(1));
            Assert.That(period.TickEnd, Is.EqualTo(tickCount));
            Assert.That(period.TickCount, Is.EqualTo(tickCount));
            Assert.That(period.ChangedTargets[0].InitialValueS, Is.EqualTo(initial));
            Assert.That(period.ChangedTargets[0].FinalValueS, Is.EqualTo(current));
            Assert.That(period.ChangedTargets[0].DeltaTotalS, Is.EqualTo(tickCount * 10));
        }

        [Test]
        public void PeriodAccumulatorAllowsIntermediateTicksWithoutVisibleChanges()
        {
            PeriodCausalSnapshot period = CausalPeriodAccumulator.Accumulate(new[]
            {
                BuildTickSnapshot(
                    1,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        5000,
                        5100,
                        new[] { new CausalContribution(DecisionTax, 100) })),
                BuildTickSnapshot(
                    2,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        5100,
                        5100,
                        Array.Empty<CausalContribution>())),
                BuildTickSnapshot(
                    3,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        5100,
                        4900,
                        new[] { new CausalContribution(EventSecurity, -200) }))
            });

            Assert.That(period.TickStart, Is.EqualTo(1));
            Assert.That(period.TickEnd, Is.EqualTo(3));
            Assert.That(period.TickCount, Is.EqualTo(3));
            Assert.That(period.ChangedTargets[0].InitialValueS, Is.EqualTo(5000));
            Assert.That(period.ChangedTargets[0].FinalValueS, Is.EqualTo(4900));
            Assert.That(period.ChangedTargets[0].DeltaTotalS, Is.EqualTo(-100));
        }

        [Test]
        public void PeriodAccumulatorPreservesContinuityAcrossAuditedZeroDeltaTicks()
        {
            PeriodCausalSnapshot period = CausalPeriodAccumulator.Accumulate(new[]
            {
                BuildTickSnapshot(
                    1,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        5000,
                        5100,
                        new[] { new CausalContribution(DecisionTax, 100) })),
                BuildTickSnapshot(
                    2,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        5100,
                        5100,
                        Array.Empty<CausalContribution>())),
                BuildTickSnapshot(
                    3,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        5100,
                        5200,
                        new[] { new CausalContribution(EventSecurity, 100) }))
            });

            Assert.That(period.AuditedTargets.Count, Is.EqualTo(1));
            Assert.That(period.ChangedTargets.Count, Is.EqualTo(1));
            Assert.That(period.ChangedTargets[0].InitialValueS, Is.EqualTo(5000));
            Assert.That(period.ChangedTargets[0].FinalValueS, Is.EqualTo(5200));
            Assert.That(period.ChangedTargets[0].DeltaTotalS, Is.EqualTo(200L));
        }

        [Test]
        public void PeriodAccumulatorAllowsLateFirstAuditButRejectsGapsAndHiddenDiscontinuities()
        {
            PeriodCausalSnapshot lateFirstAudit = CausalPeriodAccumulator.Accumulate(new[]
            {
                BuildTickSnapshot(1),
                BuildTickSnapshot(
                    2,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        5100,
                        5100,
                        Array.Empty<CausalContribution>())),
                BuildTickSnapshot(
                    3,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        5100,
                        5200,
                        new[] { new CausalContribution(DecisionTax, 100) }))
            });

            Assert.That(lateFirstAudit.ChangedTargets[0].InitialValueS, Is.EqualTo(5100));
            Assert.That(lateFirstAudit.ChangedTargets[0].FinalValueS, Is.EqualTo(5200));

            AssertCausalCode(
                delegate
                {
                    _ = CausalPeriodAccumulator.Accumulate(new[]
                    {
                        BuildTickSnapshot(
                            1,
                            new TargetCausalSnapshot(
                                TargetPath.Parse("metrics.legitimacy"),
                                5000,
                                5100,
                                new[] { new CausalContribution(DecisionTax, 100) })),
                        BuildTickSnapshot(2),
                        BuildTickSnapshot(
                            3,
                            new TargetCausalSnapshot(
                                TargetPath.Parse("metrics.legitimacy"),
                                5100,
                                5200,
                                new[] { new CausalContribution(EventSecurity, 100) }))
                    });
                },
                CausalLedgerErrorCodes.DiscontinuousTargetValues);

            AssertCausalCode(
                delegate
                {
                    _ = CausalPeriodAccumulator.Accumulate(new[]
                    {
                        BuildTickSnapshot(
                            1,
                            new TargetCausalSnapshot(
                                TargetPath.Parse("metrics.legitimacy"),
                                5000,
                                5100,
                                new[] { new CausalContribution(DecisionTax, 100) })),
                        BuildTickSnapshot(
                            2,
                            new TargetCausalSnapshot(
                                TargetPath.Parse("metrics.legitimacy"),
                                5200,
                                5200,
                                Array.Empty<CausalContribution>())),
                        BuildTickSnapshot(
                            3,
                            new TargetCausalSnapshot(
                                TargetPath.Parse("metrics.legitimacy"),
                                5200,
                                5300,
                                new[] { new CausalContribution(EventSecurity, 100) }))
                    });
                },
                CausalLedgerErrorCodes.DiscontinuousTargetValues);
        }

        [Test]
        public void TopNProjectionUsesFrozenOrderingAndOtherDelta()
        {
            PeriodCausalSnapshot period = BuildGoldenPeriod();
            CausalTopNProjection projection = CausalTopNProjector.Project(period);

            Assert.That(projection.Targets.Count, Is.EqualTo(4));
            Assert.That(projection.Targets[0].Target.ToString(), Is.EqualTo("metrics.security"));
            Assert.That(projection.Targets[1].Target.ToString(), Is.EqualTo("metrics.legitimacy"));
            Assert.That(projection.Targets[2].Target.ToString(), Is.EqualTo("igs.ig_empresariado_finanzas.clout"));
            Assert.That(projection.Targets[3].Target.ToString(), Is.EqualTo("igs.ig_ambiental_regionalista.clout"));

            ProjectedCausalTarget legitimacy = projection.Targets[1];
            Assert.That(legitimacy.TopCauses.Count, Is.EqualTo(3));
            Assert.That(legitimacy.TopCauses[0].Cause, Is.EqualTo(DecisionTax));
            Assert.That(legitimacy.TopCauses[1].Cause, Is.EqualTo(EventSecurity));
            Assert.That(legitimacy.TopCauses[2].Cause, Is.EqualTo(CauseRef.SystemClamp));
            Assert.That(legitimacy.OtherDeltaS, Is.EqualTo(351));
            long sum = legitimacy.OtherDeltaS;
            for (int i = 0; i < legitimacy.TopCauses.Count; i++)
            {
                sum += legitimacy.TopCauses[i].DeltaS;
            }

            Assert.That(sum, Is.EqualTo(legitimacy.DeltaTotalS));
        }

        [Test]
        public void TopNProjectionAppliesTargetAndCauseTieBreakersAndLimit()
        {
            List<TargetCausalSnapshot> targets = new List<TargetCausalSnapshot>();
            for (int i = 0; i < 11; i++)
            {
                string suffix = ((char)('a' + i)).ToString();
                targets.Add(new TargetCausalSnapshot(
                    TargetPath.Parse("metrics." + suffix),
                    0,
                    20,
                    new[]
                    {
                        new CausalContribution(new CauseRef(CauseCategory.Movement, "mov_" + suffix), 10),
                        new CausalContribution(new CauseRef(CauseCategory.Decision, "dec_" + suffix), -10),
                        new CausalContribution(new CauseRef(CauseCategory.Event, "ev_" + suffix), 10),
                        new CausalContribution(new CauseRef(CauseCategory.Reform, "ref_" + suffix), 10)
                    }));
            }

            CausalTopNProjection projection = CausalTopNProjector.Project(new TickCausalSnapshot(1, targets));
            Assert.That(projection.Targets.Count, Is.EqualTo(10));
            Assert.That(projection.Targets[0].Target.ToString(), Is.EqualTo("metrics.a"));
            Assert.That(projection.Targets[9].Target.ToString(), Is.EqualTo("metrics.j"));
            Assert.That(projection.Targets[0].TopCauses[0].Cause.CanonicalKey, Is.EqualTo("DECISION:dec_a"));
            Assert.That(projection.Targets[0].TopCauses[1].Cause.CanonicalKey, Is.EqualTo("EVENT:ev_a"));
            Assert.That(projection.Targets[0].TopCauses[2].Cause.CanonicalKey, Is.EqualTo("REFORM:ref_a"));
            Assert.That(projection.Targets[0].OtherDeltaS, Is.EqualTo(10));
        }

        [Test]
        public void TopNProjectionHandlesExtremeNegativeMagnitudesWithoutOverflow()
        {
            TickCausalSnapshot snapshot = BuildTickSnapshot(
                4,
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.legitimacy"),
                    0,
                    int.MinValue,
                    new[] { new CausalContribution(EventSecurity, int.MinValue) }),
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.security"),
                    0,
                    1,
                    new[] { new CausalContribution(DecisionTax, 1) }));

            CausalTopNProjection projection = CausalTopNProjector.Project(snapshot);
            Assert.That(projection.Targets[0].Target.ToString(), Is.EqualTo("metrics.legitimacy"));
        }

        [Test]
        public void TopNProjectionAndPeriodAccumulationUseLongArithmeticForExtremeContributions()
        {
            CauseRef decisionHuge = new CauseRef(CauseCategory.Decision, "dec_huge");
            CauseRef eventHuge = new CauseRef(CauseCategory.Event, "ev_huge");
            CauseRef reformHuge = new CauseRef(CauseCategory.Reform, "ref_huge");
            CauseRef movementHuge = new CauseRef(CauseCategory.Movement, "mov_huge");

            PeriodCausalSnapshot period = CausalPeriodAccumulator.Accumulate(new[]
            {
                BuildTickSnapshot(
                    1,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        0,
                        int.MaxValue,
                        new[] { new CausalContribution(decisionHuge, int.MaxValue) })),
                BuildTickSnapshot(
                    2,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        int.MaxValue,
                        0,
                        new[]
                        {
                            new CausalContribution(decisionHuge, 1L),
                            new CausalContribution(eventHuge, -(long)int.MaxValue - 1L)
                        })),
                BuildTickSnapshot(
                    3,
                    new TargetCausalSnapshot(
                        TargetPath.Parse("metrics.legitimacy"),
                        0,
                        1,
                        new[]
                        {
                            new CausalContribution(reformHuge, long.MaxValue),
                            new CausalContribution(movementHuge, -long.MaxValue + 1L)
                        }))
            });

            Assert.That(period.ChangedTargets[0].DeltaTotalS, Is.EqualTo(1L));
            Assert.That(period.ChangedTargets[0].Contributions[0].DeltaS, Is.EqualTo((long)int.MaxValue + 1L));

            CausalTopNProjection projection = CausalTopNProjector.Project(period);
            Assert.That(projection.Targets.Count, Is.EqualTo(1));
            Assert.That(projection.Targets[0].TopCauses.Count, Is.EqualTo(3));
            Assert.That(projection.Targets[0].OtherDeltaS, Is.EqualTo(-(long)int.MaxValue - 1L));
            Assert.That(
                projection.Targets[0].TopCauses[0].Cause.CanonicalKey,
                Is.EqualTo("REFORM:ref_huge"));
        }

        [Test]
        public void NetZeroTargetsRemainAuditableButStayOutOfChangedTargetsAndTopN()
        {
            TickCausalSnapshot snapshot = BuildTickSnapshot(
                5,
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.security"),
                    5000,
                    5000,
                    new[]
                    {
                        new CausalContribution(DecisionTax, 100),
                        new CausalContribution(EventSecurity, -100)
                    }));

            Assert.That(snapshot.AuditedTargets.Count, Is.EqualTo(1));
            Assert.That(snapshot.AuditedTargets[0].Contributions.Count, Is.EqualTo(2));
            Assert.That(snapshot.ChangedTargets, Is.Empty);
            Assert.That(CausalTopNProjector.Project(snapshot).Targets, Is.Empty);
        }

        [Test]
        public void GoldenEvidenceIsDeterministicAndByteStableAcrossRuns()
        {
            string expectedPath = Path.Combine(ProjectRoot(), "tests", "causal", "causal_ledger_v1.expected.json");
            string expected = File.ReadAllText(expectedPath, Encoding.UTF8);
            string first = SerializeGoldenEvidence(BuildGoldenPeriod(), CausalTopNProjector.Project(BuildGoldenPeriod()));
            string second = SerializeGoldenEvidence(BuildGoldenPeriod(), CausalTopNProjector.Project(BuildGoldenPeriod()));

            Assert.That(first, Is.EqualTo(expected));
            Assert.That(second, Is.EqualTo(expected));
            Assert.That(Hash(first), Is.EqualTo(Hash(second)));
            Assert.That(Hash(first), Is.EqualTo("sha256:aa8b8b8bd735dbef5c4961f764b9740642351df02b8be3a057aacf4f9fce0b0e"));
        }

        private static VisibleTargetCatalog CreateCatalog(ContentPack pack, GameState state)
        {
            return VisibleTargetCatalog.CreateForMvp(
                state,
                ProjectIds(pack.Regions, delegate(RegionDefinition value) { return value.Id; }),
                ProjectIds(pack.InterestGroups, delegate(InterestGroupDefinition value) { return value.Id; }),
                ProjectIds(pack.Movements, delegate(MovementDefinition value) { return value.Id; }));
        }

        private static ContentPack LoadRealPack()
        {
            ContentLoadResult result = new ContentPackLoader().Load(new DirectoryContentFileSource(ContentRoot()));
            Assert.That(result.IsSuccess, Is.True);
            return result.Pack;
        }

        private static GameState CreateState(ContentPack pack, int seed)
        {
            StateInitializationResult result = new GameStateFactory().CreateInitialState(pack, seed);
            Assert.That(result.Success, Is.True);
            return result.State;
        }

        private static List<string> ProjectIds<T>(IReadOnlyList<T> values, Func<T, string> selector)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                result.Add(selector(values[i]));
            }

            return result;
        }

        private static TickCausalSnapshot BuildTickSnapshot(int tick, params TargetCausalSnapshot[] targets)
        {
            return new TickCausalSnapshot(tick, targets);
        }

        private static PeriodCausalSnapshot BuildGoldenPeriod()
        {
            TickCausalSnapshot tick1 = BuildTickSnapshot(
                1,
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.legitimacy"),
                    9000,
                    10000,
                    new[]
                    {
                        new CausalContribution(DecisionTax, 2000),
                        new CausalContribution(CauseRef.SystemClamp, -1000)
                    }),
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.security"),
                    5000,
                    4500,
                    new[]
                    {
                        new CausalContribution(EventSecurity, -500)
                    }),
                new TargetCausalSnapshot(
                    TargetPath.Parse("igs.ig_ambiental_regionalista.clout"),
                    1111,
                    1200,
                    new[]
                    {
                        new CausalContribution(CauseRef.SystemIgCloutNormalize, 89)
                    }),
                new TargetCausalSnapshot(
                    TargetPath.Parse("igs.ig_empresariado_finanzas.clout"),
                    1111,
                    1000,
                    new[]
                    {
                        new CausalContribution(CauseRef.SystemIgCloutNormalize, -111)
                    }));

            TickCausalSnapshot tick2 = BuildTickSnapshot(
                2,
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.legitimacy"),
                    10000,
                    8851,
                    new[]
                    {
                        new CausalContribution(EventSecurity, -1500),
                        new CausalContribution(ReformRouteA, 250),
                        new CausalContribution(ModifierReform, 100),
                        new CausalContribution(CauseRef.SystemRounding, 1)
                    }),
                new TargetCausalSnapshot(
                    TargetPath.Parse("metrics.security"),
                    4500,
                    4500,
                    Array.Empty<CausalContribution>()),
                new TargetCausalSnapshot(
                    TargetPath.Parse("igs.ig_ambiental_regionalista.clout"),
                    1200,
                    1200,
                    Array.Empty<CausalContribution>()),
                new TargetCausalSnapshot(
                    TargetPath.Parse("igs.ig_empresariado_finanzas.clout"),
                    1000,
                    1000,
                    Array.Empty<CausalContribution>()));

            return CausalPeriodAccumulator.Accumulate(new[] { tick1, tick2 });
        }

        private static string SerializeGoldenEvidence(PeriodCausalSnapshot period, CausalTopNProjection projection)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"tick_start\": ").Append(period.TickStart).Append(",\n");
            builder.Append("  \"tick_end\": ").Append(period.TickEnd).Append(",\n");
            builder.Append("  \"ticks_n\": ").Append(period.TickCount).Append(",\n");
            builder.Append("  \"targets\": [\n");
            for (int i = 0; i < period.ChangedTargets.Count; i++)
            {
                TargetCausalSnapshot target = period.ChangedTargets[i];
                builder.Append("    {\n");
                builder.Append("      \"target\": ").Append(JsonString(target.Target.ToString())).Append(",\n");
                builder.Append("      \"initial_valueS\": ").Append(target.InitialValueS).Append(",\n");
                builder.Append("      \"final_valueS\": ").Append(target.FinalValueS).Append(",\n");
                builder.Append("      \"delta_totalS\": ").Append(target.DeltaTotalS).Append(",\n");
                builder.Append("      \"contributions\": [\n");
                for (int j = 0; j < target.Contributions.Count; j++)
                {
                    CausalContribution contribution = target.Contributions[j];
                    builder.Append("        {\n");
                    builder.Append("          \"cause_key\": ").Append(JsonString(contribution.Cause.CanonicalKey)).Append(",\n");
                    builder.Append("          \"deltaS\": ").Append(contribution.DeltaS).Append('\n');
                    builder.Append("        }");
                    builder.Append(j == target.Contributions.Count - 1 ? "\n" : ",\n");
                }

                builder.Append("      ]\n");
                builder.Append("    }");
                builder.Append(i == period.ChangedTargets.Count - 1 ? "\n" : ",\n");
            }

            builder.Append("  ],\n");
            builder.Append("  \"top_n\": [\n");
            for (int i = 0; i < projection.Targets.Count; i++)
            {
                ProjectedCausalTarget target = projection.Targets[i];
                builder.Append("    {\n");
                builder.Append("      \"target\": ").Append(JsonString(target.Target.ToString())).Append(",\n");
                builder.Append("      \"initial_valueS\": ").Append(target.InitialValueS).Append(",\n");
                builder.Append("      \"final_valueS\": ").Append(target.FinalValueS).Append(",\n");
                builder.Append("      \"delta_totalS\": ").Append(target.DeltaTotalS).Append(",\n");
                builder.Append("      \"top_causes\": [\n");
                for (int j = 0; j < target.TopCauses.Count; j++)
                {
                    ProjectedCausalCause cause = target.TopCauses[j];
                    builder.Append("        {\n");
                    builder.Append("          \"cause_key\": ").Append(JsonString(cause.Cause.CanonicalKey)).Append(",\n");
                    builder.Append("          \"deltaS\": ").Append(cause.DeltaS).Append('\n');
                    builder.Append("        }");
                    builder.Append(j == target.TopCauses.Count - 1 ? "\n" : ",\n");
                }

                builder.Append("      ],\n");
                builder.Append("      \"other_deltaS\": ").Append(target.OtherDeltaS).Append('\n');
                builder.Append("    }");
                builder.Append(i == projection.Targets.Count - 1 ? "\n" : ",\n");
            }

            builder.Append("  ]\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        private static string JsonString(string value)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '\\' || ch == '"')
                {
                    builder.Append('\\').Append(ch);
                }
                else if (ch == '\n')
                {
                    builder.Append("\\n");
                }
                else
                {
                    builder.Append(ch);
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder("sha256:");
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static void AssertCausalCode(TestDelegate action, string expectedCode)
        {
            CausalLedgerException exception = Assert.Throws<CausalLedgerException>(action);
            Assert.That(exception.Code, Is.EqualTo(expectedCode));
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", ".."));
        }

        private static string ContentRoot()
        {
            return Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "content");
        }
    }
}
