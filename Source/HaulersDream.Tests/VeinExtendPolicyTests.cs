using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the pure control-flow decisions extracted from the runtime's vein-route "extend as fog clears"
    /// pass (<c>HaulersDreamGameComponent.TryExtend</c>). These tests are the behaviour contract the
    /// extraction must preserve — every branch of the original method maps onto one of these predicates.
    /// </summary>
    [TestFixture]
    public class VeinExtendPolicyTests
    {
        // --- AtCap: the route already holds its chosen Amount of stops -------------------------------------

        [Test]
        public void AtCap_BelowCap_False()
        {
            // Still room for more stops → keep extending.
            Assert.That(VeinExtendPolicy.AtCap(includedCount: 3, cap: 5), Is.False);
        }

        [Test]
        public void AtCap_ExactlyAtCap_True()
        {
            // Reached the chosen Amount → never grow past it.
            Assert.That(VeinExtendPolicy.AtCap(includedCount: 5, cap: 5), Is.True);
        }

        [Test]
        public void AtCap_OverCap_True()
        {
            // Defensive: somehow above the cap → still treated as "at cap" (stop).
            Assert.That(VeinExtendPolicy.AtCap(includedCount: 6, cap: 5), Is.True);
        }

        // --- DecideSupersession: full truth table ---------------------------------------------------------

        [Test]
        public void DecideSupersession_TailStillMatches_Extends_RegardlessOfOtherArgs()
        {
            // When the tail still matches, the other two args are irrelevant (the runtime never even computes
            // them) — every combination must yield a normal Extend.
            Assert.That(VeinExtendPolicy.DecideSupersession(true, lastCellMined: false, nothingElseQueued: false),
                Is.EqualTo(ExtendOutcome.Extend));
            Assert.That(VeinExtendPolicy.DecideSupersession(true, lastCellMined: true, nothingElseQueued: false),
                Is.EqualTo(ExtendOutcome.Extend));
            Assert.That(VeinExtendPolicy.DecideSupersession(true, lastCellMined: false, nothingElseQueued: true),
                Is.EqualTo(ExtendOutcome.Extend));
            Assert.That(VeinExtendPolicy.DecideSupersession(true, lastCellMined: true, nothingElseQueued: true),
                Is.EqualTo(ExtendOutcome.Extend));
        }

        [Test]
        public void DecideSupersession_TailFails_FinalCellMined_NothingElseQueued_FinalAttempt()
        {
            // The flagship 1-2-visible-cell payoff: tail check failed only because the final cell was just
            // mined and no real work follows → make one last extend attempt.
            Assert.That(VeinExtendPolicy.DecideSupersession(false, lastCellMined: true, nothingElseQueued: true),
                Is.EqualTo(ExtendOutcome.FinalAttempt));
        }

        [Test]
        public void DecideSupersession_TailFails_FinalCellMined_ButRealWorkQueued_Drops()
        {
            // The cell was mined, but the player queued other real work after the route → genuinely diverted.
            Assert.That(VeinExtendPolicy.DecideSupersession(false, lastCellMined: true, nothingElseQueued: false),
                Is.EqualTo(ExtendOutcome.Drop));
        }

        [Test]
        public void DecideSupersession_TailFails_FinalCellNotMined_Drops_WhetherOrNotQueued()
        {
            // The tail moved for some reason OTHER than the final cell being mined → superseded; the
            // nothing-else-queued flag does not rescue it.
            Assert.That(VeinExtendPolicy.DecideSupersession(false, lastCellMined: false, nothingElseQueued: true),
                Is.EqualTo(ExtendOutcome.Drop));
            Assert.That(VeinExtendPolicy.DecideSupersession(false, lastCellMined: false, nothingElseQueued: false),
                Is.EqualTo(ExtendOutcome.Drop));
        }

        // --- CanAddStop: stop accumulating once included + new would reach the cap -------------------------

        [Test]
        public void CanAddStop_BelowCap_True()
        {
            // included 3 + 1 new = 4 < cap 5 → there is room for one more.
            Assert.That(VeinExtendPolicy.CanAddStop(includedCount: 3, alreadyAddedNewStops: 1, cap: 5), Is.True);
        }

        [Test]
        public void CanAddStop_WouldReachCap_False()
        {
            // included 4 + 1 new = 5 == cap 5 → no more room (mirrors the runtime's >= break).
            Assert.That(VeinExtendPolicy.CanAddStop(includedCount: 4, alreadyAddedNewStops: 1, cap: 5), Is.False);
        }

        [Test]
        public void CanAddStop_WouldExceedCap_False()
        {
            // Defensive: already over the cap → no more room.
            Assert.That(VeinExtendPolicy.CanAddStop(includedCount: 4, alreadyAddedNewStops: 2, cap: 5), Is.False);
        }

        [Test]
        public void CanAddStop_NoNewStopsYet_RoomLeft_True()
        {
            // Starting fresh with room under the cap.
            Assert.That(VeinExtendPolicy.CanAddStop(includedCount: 0, alreadyAddedNewStops: 0, cap: 5), Is.True);
        }

        // --- KeepAfterNoNewStops: only a non-final pass that still sees fog keeps the tracker --------------

        [Test]
        public void KeepAfterNoNewStops_NotFinalAndStillFog_Keeps()
        {
            // A normal pass found nothing new this time but fog still hides more vein → keep for later.
            Assert.That(VeinExtendPolicy.KeepAfterNoNewStops(finalAttempt: false, stillFog: true), Is.True);
        }

        [Test]
        public void KeepAfterNoNewStops_NotFinalNoFog_Drops()
        {
            // No fog left → the vein is fully revealed; nothing more will ever appear → drop.
            Assert.That(VeinExtendPolicy.KeepAfterNoNewStops(finalAttempt: false, stillFog: false), Is.False);
        }

        [Test]
        public void KeepAfterNoNewStops_FinalAttemptWithFog_Drops()
        {
            // A fruitless FINAL attempt drops the tracker even with fog left — no route jobs remain to mine
            // more, so nothing will ever reveal those fogged cells.
            Assert.That(VeinExtendPolicy.KeepAfterNoNewStops(finalAttempt: true, stillFog: true), Is.False);
        }

        [Test]
        public void KeepAfterNoNewStops_FinalAttemptNoFog_Drops()
        {
            Assert.That(VeinExtendPolicy.KeepAfterNoNewStops(finalAttempt: true, stillFog: false), Is.False);
        }
    }
}
