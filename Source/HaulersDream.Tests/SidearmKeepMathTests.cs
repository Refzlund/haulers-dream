using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SidearmKeepMathTests
    {
        // ── SurplusForPair: keep the wanted (def,stuff) copies, unload every extra ────────────────
        // #222: a pawn using Simple Sidearms carries a remembered sidearm; after a battle it also carries a
        // same-(def,stuff) LOOTED duplicate (HD strips enemy corpses). The kept copy must stay; only the
        // duplicate is surplus. rememberedCount = SS entries for the pair; primaryMatchesPair = the equipped
        // primary IS this pair (so one remembered entry is satisfied from equipment, not from inventory).
        // Params: (rememberedCount, primaryMatchesPair, pairHave, stackCount).

        [Test]
        public void LoneRememberedSidearm_NoSurplus()
        {
            // The core regression: one remembered sidearm, one held, primary is something else -> keep the one,
            // nothing surplus. This stack must NOT report as surplus, or the pawn unloads its own sidearm.
            Assert.That(SidearmKeepMath.SurplusForPair(1, false, 1, 1), Is.EqualTo(0));
        }

        [Test]
        public void PostBattleDuplicate_UnloadsTheExtra()
        {
            // Remembered sidearm (keep 1) plus a looted same-pair duplicate (have 2) -> unload exactly one copy.
            Assert.That(SidearmKeepMath.SurplusForPair(1, false, 2, 1), Is.EqualTo(1));
        }

        [Test]
        public void PrimaryIsThePair_KeepSubtractsIt_NoSurplus()
        {
            // Two remembered entries for the pair, but the equipped primary IS this pair, so it satisfies one from
            // equipment: inventory keep = 1. One held in inventory (have 1) -> nothing surplus.
            Assert.That(SidearmKeepMath.SurplusForPair(2, true, 1, 1), Is.EqualTo(0));
        }

        [Test]
        public void EquippedPrimaryMatchesHauledDuplicate_UnloadsIt()
        {
            // One remembered entry, and the primary is this pair (keep = 1 - 1 = 0), so a hauled duplicate in
            // inventory (have 1) is fully surplus. This is the "won't put away when the equipped weapon matches the
            // hauled one" case: keep must drop the equipped primary so the inventory copy unloads.
            Assert.That(SidearmKeepMath.SurplusForPair(1, true, 1, 1), Is.EqualTo(1));
        }

        [Test]
        public void KeepNeverNegative_PrimaryWithNoRememberedEntry()
        {
            // remembered 0 but primary flagged as the pair: keep = max(0, 0 - 1) = 0, never negative, so a held
            // copy stays surplus (a negative keep would have under-counted and stripped nothing).
            Assert.That(SidearmKeepMath.SurplusForPair(0, true, 1, 1), Is.EqualTo(1));
        }

        [Test]
        public void KeepAboveHave_NoSurplus()
        {
            // Keep 3 but only 2 held -> nothing surplus (over is negative, surplus floors at 0).
            Assert.That(SidearmKeepMath.SurplusForPair(3, false, 2, 1), Is.EqualTo(0));
        }

        [Test]
        public void ClampsToStack()
        {
            // Keep 0, 5 held total, but this single stack is only 1 unit -> this call contributes at most 1.
            Assert.That(SidearmKeepMath.SurplusForPair(0, false, 5, 1), Is.EqualTo(1));
        }

        [Test]
        public void ClampsToStack_LargerStack()
        {
            // Sanity for a non-weapon stack size: keep 0, have 5, this stack is 3 -> at most 3 from it (min(over, stack)).
            Assert.That(SidearmKeepMath.SurplusForPair(0, false, 5, 3), Is.EqualTo(3));
        }

        [Test]
        public void OverSmallerThanStack_ReturnsOver()
        {
            // keep 1, have 3 -> over 2, this stack is 5 -> contribute the 2 (the true excess), not the whole stack.
            Assert.That(SidearmKeepMath.SurplusForPair(1, false, 3, 5), Is.EqualTo(2));
        }

        // ── KeepForPair: the SS inventory-keep model (remembered minus the equipped primary, floored at 0) ────

        [Test]
        public void Keep_RememberedMinusEquippedPrimary()
        {
            // The primary lives in equipment, so when it is this pair the keep is one less than remembered.
            Assert.That(SidearmKeepMath.KeepForPair(2, true), Is.EqualTo(1));
            Assert.That(SidearmKeepMath.KeepForPair(2, false), Is.EqualTo(2));
        }

        [Test]
        public void Keep_NeverNegative()
        {
            // A primary-only pair (0 remembered, primary flagged) or a corrupt negative floors to 0.
            Assert.That(SidearmKeepMath.KeepForPair(0, true), Is.EqualTo(0));
            Assert.That(SidearmKeepMath.KeepForPair(-3, false), Is.EqualTo(0));
        }

        [Test]
        public void Keep_NoRemembered_NoPrimary_IsZero()
        {
            Assert.That(SidearmKeepMath.KeepForPair(0, false), Is.EqualTo(0));
        }
    }
}
