using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CompositableLoadoutKeepPolicyTests
    {
        // The shim's decision in one line, so every test below exercises the SAME composition the runtime uses:
        // apparel contributes nothing at all; everything else contributes what the loadout wants minus what the
        // pawn already wields. (CompositableLoadoutsCompat.KeepCount reads defIsApparel from vanilla, sums the
        // loadout entries through EntryUnits, then applies ContributedKeep.)
        private static int ClKeep(bool defIsApparel, int wantedUnits, int equippedUnits)
            => CompositableLoadoutKeepPolicy.ShieldsDef(defIsApparel)
                ? CompositableLoadoutKeepPolicy.ContributedKeep(wantedUnits, equippedUnits)
                : 0;

        // ── ShieldsDef: apparel is never shielded, everything else still is ───────────────────────

        [Test]
        public void Apparel_IsNeverShielded()
        {
            // #233: HD's keep must equal what CL would RE-FETCH. CL never re-fetches apparel into inventory — it
            // sends the colonist to WEAR a garment off the map — so an apparel loadout entry may pin nothing.
            Assert.That(CompositableLoadoutKeepPolicy.ShieldsDef(defIsApparel: true), Is.False);
        }

        [Test]
        public void NonApparel_IsStillShielded()
        {
            // The #200 case that the carve-out must NOT regress: medicine (and every other non-apparel loadout
            // item) is genuinely re-fetched into inventory by CL, so it keeps its shield.
            Assert.That(CompositableLoadoutKeepPolicy.ShieldsDef(defIsApparel: false), Is.True);
        }

        [Test]
        public void Issue233_ApparelStrandsNothing_AtAnyLoadoutQuantity()
        {
            // The reported bug, stated as the invariant that actually matters: NO apparel is stranded. It is
            // deliberately NOT phrased as "three dusters are freed" — with CL's default quantity of 1 the pre-fix
            // bug strands exactly ONE unit; stranding three needs quantity >= 3, or the def present in three active
            // tags (Loadout.Items is a SelectMany over the pawn's tags, so duplicate entries SUM). The reporter hit
            // one of those shapes; the fix must hold for all of them, hence the sweep.
            for (int wanted = 0; wanted <= 5; wanted++)
                for (int equipped = 0; equipped <= 2; equipped++)
                    Assert.That(ClKeep(defIsApparel: true, wantedUnits: wanted, equippedUnits: equipped),
                        Is.EqualTo(0),
                        $"apparel must contribute no keep (wanted={wanted}, equipped={equipped})");
        }

        // ── EntryUnits: one loadout entry's units, floored — callers SUM these across duplicate entries ──

        [Test]
        public void EntryUnits_PassesThroughNonNegative()
        {
            Assert.That(CompositableLoadoutKeepPolicy.EntryUnits(0), Is.EqualTo(0));
            Assert.That(CompositableLoadoutKeepPolicy.EntryUnits(1), Is.EqualTo(1));
            Assert.That(CompositableLoadoutKeepPolicy.EntryUnits(7), Is.EqualTo(7));
        }

        [Test]
        public void EntryUnits_SumsAcrossDuplicateTagEntries()
        {
            // A def named by three active tags appears three times in Loadout.Items (SelectMany), and the pawn
            // genuinely wants all three — this is how a "three of them" loadout is expressed at quantity 1 each.
            int wanted = CompositableLoadoutKeepPolicy.EntryUnits(1)
                       + CompositableLoadoutKeepPolicy.EntryUnits(1)
                       + CompositableLoadoutKeepPolicy.EntryUnits(1);
            Assert.That(wanted, Is.EqualTo(3));
        }

        [Test]
        public void EntryUnits_FloorsNegative_SoOneBadEntryCannotCancelAnother()
        {
            // A corrupt/negative desired count must contribute 0, never subtract from a sibling entry's units —
            // otherwise one bad entry silently cancels a real one and surplus leaks into the unload.
            Assert.That(CompositableLoadoutKeepPolicy.EntryUnits(-4), Is.EqualTo(0));
            Assert.That(CompositableLoadoutKeepPolicy.EntryUnits(2) + CompositableLoadoutKeepPolicy.EntryUnits(-4),
                Is.EqualTo(2));
        }

        // ── ContributedKeep: a wielded loadout weapon discharges its own entry ────────────────────

        [Test]
        public void NothingEquipped_KeepsTheWholeWantedAmount()
        {
            // The steady state, and the proof that (b) is a no-op for every def a pawn cannot equip (medicine,
            // ammo, food, components…): equipment count is 0, so the contributed keep is exactly what CL wants.
            Assert.That(CompositableLoadoutKeepPolicy.ContributedKeep(1, 0), Is.EqualTo(1));
        }

        [Test]
        public void WieldedWeaponDischargesItsEntry_HauledSpareIsFreed()
        {
            // The #233 (b) case: the loadout wants one longsword and the colonist is HOLDING one. CL counts gear as
            // inventory PLUS equipment (Utility.InventoryAndEquipment), so that entry is already satisfied — the
            // hauled spare in the pack must not be pinned.
            Assert.That(CompositableLoadoutKeepPolicy.ContributedKeep(1, 1), Is.EqualTo(0));
        }

        [Test]
        public void PartiallySatisfiedEntry_KeepsTheRemainder()
        {
            // Wants two, wields one -> one still has to come from inventory. Not all-or-nothing: the keep stays
            // count-aware, exactly like the Simple Sidearms keep it mirrors.
            Assert.That(CompositableLoadoutKeepPolicy.ContributedKeep(2, 1), Is.EqualTo(1));
        }

        [Test]
        public void OverSatisfiedEntry_KeepsNothing_NeverNegative()
        {
            // More wielded than wanted (a dual-wield mod, or a shrunken loadout) floors at 0. A negative keep would
            // leak out of CL's term and cancel part of ANOTHER keeper's contribution in KeepCountOf's sum.
            Assert.That(CompositableLoadoutKeepPolicy.ContributedKeep(1, 3), Is.EqualTo(0));
        }

        [Test]
        public void NoLoadoutEntry_KeepsNothing()
        {
            // The def isn't in the loadout at all: nothing to keep, whatever the pawn happens to wield.
            Assert.That(CompositableLoadoutKeepPolicy.ContributedKeep(0, 2), Is.EqualTo(0));
        }

        [Test]
        public void NegativeEquippedCount_CannotInflateTheKeep()
        {
            // A corrupt equipment count is floored at 0 BEFORE the subtraction, so it can only ever leave the keep
            // where it was — never raise it. Inflating the keep is the strand-causing direction (#233).
            Assert.That(CompositableLoadoutKeepPolicy.ContributedKeep(1, -5), Is.EqualTo(1));
        }
    }
}
