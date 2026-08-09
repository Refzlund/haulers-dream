using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Locks the forbidden rule an HD sweep applies at all three of its checkpoints — choosing a stack, walking
    /// to it, pocketing it (issue #250). Two decisions have to hold at once and they pull in opposite
    /// directions: a pawn must ABANDON a walk the instant the player forbids the target (the player forbids
    /// things that are unsafe, so finishing the trip can get a colonist hurt), yet must NOT abandon the one
    /// stack a player deliberately force-ordered while it was already forbidden (vanilla's own carve-out —
    /// <c>Pawn_JobTracker.StartJob</c> raises <c>ignoreForbidden</c> for a forced job and
    /// <c>ToilFailConditions.FailOnForbidden</c> short-circuits on it). The wrinkle vanilla never had is that
    /// an HD order also sweeps up NEARBY stacks the player never pointed at, so the exemption has to stop at
    /// the anchor.
    ///
    /// <para>BE CLEAR ABOUT WHAT THESE TESTS DO NOT COVER. The policy is two boolean expressions; every part of
    /// the fix that can actually fail lives one layer out, in Verse, where <c>HaulersDream.Tests</c> cannot
    /// reach (it references only <c>HaulersDream.Core</c>, which has no <c>JobDriver</c> in scope). Nothing
    /// here can fail if:</para>
    /// <list type="bullet">
    /// <item><description>the pre-tick action is never REGISTERED — <c>SweepWalk.MakeToil</c> could drop its
    /// <c>AddPreTickAction</c> call and this fixture stays green while every sweep walks the full trip
    /// again;</description></item>
    /// <item><description><c>JumpToToil</c> from inside a pre-tick action turns out to be unsafe — that rests
    /// on decompiled <c>JobDriver.DriverTick</c> re-testing
    /// <c>JobChanged() || CurToil != curToil || wantBeginNextToil</c> after EACH pre-tick action, which is a
    /// claim about the game, not about this code;</description></item>
    /// <item><description>the pather is never actually redirected — abandoning the walk only matters if the
    /// pawn stops walking there;</description></item>
    /// <item><description>cargo already pocketed earlier in the sweep is lost when the chain drops a
    /// stack;</description></item>
    /// <item><description><b>a sibling driver was missed.</b> Eight drivers own a sweep walk. This repo's
    /// documented recurring failure is fixing the one in the bug report and leaving the other seven, and a
    /// green unit suite reads exactly the same either way. That inventory is enforced by
    /// <c>scripts/check-sweep-walk-guard.ts</c>, not here.</description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class SweepForbidPolicyTests
    {
        // ══════════════════ AbandonWalk: the #250 walk gate ══════════════════

        [Test]
        public void AbandonWalk_AutomaticJob_ForbiddenAnchor_Abandons()
        {
            // The ordinary case: a work-scan haul, nobody forced anything. There is no exemption to inherit, so
            // even the stack the job was BUILT around is dropped the moment it turns forbidden. Being the anchor
            // is not itself a licence — only a forced ORDER is.
            Assert.That(
                SweepForbidPolicy.AbandonWalk(forbiddenNow: true, orderIgnoresForbidden: false, isOrderedAnchor: true),
                Is.True);
        }

        [Test]
        public void AbandonWalk_ForcedAnchor_KeepsWalking()
        {
            // The prison-food carve-out (issue #3), and vanilla-exact: a player who right-clicks a forbidden
            // meal on the prison floor and forces the haul MEANT to override the forbid. Vanilla never abandons
            // that job either — StartJob sets ignoreForbidden and FailOnForbidden returns Ongoing. Breaking this
            // would make forcing a forbidden item impossible, which is a worse bug than the one #250 fixes.
            Assert.That(
                SweepForbidPolicy.AbandonWalk(forbiddenNow: true, orderIgnoresForbidden: true, isOrderedAnchor: true),
                Is.False);
        }

        [Test]
        public void AbandonWalk_ForcedJob_SweptExtra_Abandons()
        {
            // The reporter's own case, and the reason this is a policy rather than a plain `!forced` test. The
            // player forced ONE haul; HD swept nine more stacks into the same trip that the player never pointed
            // at. Forbidding one of those extras mid-walk must still stop the pawn — the force covered the
            // anchor, not the sweep.
            Assert.That(
                SweepForbidPolicy.AbandonWalk(forbiddenNow: true, orderIgnoresForbidden: true, isOrderedAnchor: false),
                Is.True);
        }

        [Test]
        public void AbandonWalk_AutomaticJob_SweptExtra_Abandons()
        {
            // The plain automatic sweep — no order, no anchor. Nothing can exempt anything.
            Assert.That(
                SweepForbidPolicy.AbandonWalk(forbiddenNow: true, orderIgnoresForbidden: false, isOrderedAnchor: false),
                Is.True);
        }

        // ── An UNFORBIDDEN stack is never abandoned, whatever the other two flags say. This is the byte-identical
        //    guarantee for every ordinary sweep: the per-tick check added for #250 must be invisible until the
        //    player actually forbids something, or it is a behaviour change dressed as a safety fix. ──────────
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AbandonWalk_NotForbidden_NeverAbandons(bool orderIgnoresForbidden, bool isOrderedAnchor)
        {
            Assert.That(
                SweepForbidPolicy.AbandonWalk(forbiddenNow: false, orderIgnoresForbidden, isOrderedAnchor),
                Is.False);
        }

        // ══════════════════ MayTakeWhileForbidden: the take gate ══════════════════

        [Test]
        public void MayTakeWhileForbidden_OnlyForcedAnchor()
        {
            // The exemption is the AND of both conditions and nothing else: a forced order licenses its own
            // anchor and only that. Spelled out as all four combinations because each near-miss is a real,
            // separately-reported bug — dropping the anchor term leaks the exemption onto every swept extra
            // (the #250 report), dropping the forced term hands every automatic haul a free pass at forbidding.
            Assert.That(SweepForbidPolicy.MayTakeWhileForbidden(orderIgnoresForbidden: true, isOrderedAnchor: true),
                Is.True);
            Assert.That(SweepForbidPolicy.MayTakeWhileForbidden(orderIgnoresForbidden: true, isOrderedAnchor: false),
                Is.False);
            Assert.That(SweepForbidPolicy.MayTakeWhileForbidden(orderIgnoresForbidden: false, isOrderedAnchor: true),
                Is.False);
            Assert.That(SweepForbidPolicy.MayTakeWhileForbidden(orderIgnoresForbidden: false, isOrderedAnchor: false),
                Is.False);
        }

        // ══════════════════ cross-consistency: the two gates cannot drift ══════════════════

        // The invariant that makes this a shared policy instead of three hand-written conditions: over the WHOLE
        // input space, abandoning the walk is exactly "forbidden AND not exempt". If a future edit ever makes the
        // walk stricter than the take, a pawn abandons a stack it would then have been allowed to pocket and the
        // chain livelocks between the two gates; looser, and the walk delivers the pawn to a stack the take
        // refuses — the wasted trip #250 is about, back again.
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void AbandonWalk_IsExactlyTheNotMayTakeCase(bool forbiddenNow, bool orderIgnoresForbidden, bool isOrderedAnchor)
        {
            bool mayTake = SweepForbidPolicy.MayTakeWhileForbidden(orderIgnoresForbidden, isOrderedAnchor);
            Assert.That(SweepForbidPolicy.AbandonWalk(forbiddenNow, orderIgnoresForbidden, isOrderedAnchor),
                Is.EqualTo(forbiddenNow && !mayTake));
        }
    }
}
