using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the two STATELESS decisions behind the auto-open-inspect-tab conveniences, one call at a time: WHO the
    /// carrier Gear/Cargo auto-open applies to, and WHETHER a given selection counts as new. The stateful counterpart
    /// — whole click SEQUENCES driven the way vanilla's Selector drives them — lives in
    /// <see cref="SelectionMemoTests"/>.
    /// </summary>
    [TestFixture]
    public class TabAutoOpenPolicyTests
    {
        /// <summary>Shorthand for the carrier-audience decision, named for the question it answers rather than the
        /// method it calls.</summary>
        private static bool GearApplies(bool isHumanlike, bool isPlayerFaction, bool hasInventoryContents)
            => TabAutoOpenPolicy.CarrierGearApplies(isHumanlike, isPlayerFaction, hasInventoryContents);

        /// <summary>Shorthand for the selection-changed decision. Every argument is named at each call site, since
        /// three of the five are ints whose order would otherwise be guesswork for the reader.</summary>
        private static bool IsNew(int selectedId, int lastSelectedId, bool gapPending, int gapFrame, int frame)
            => TabAutoOpenPolicy.IsNewSelection(selectedId, lastSelectedId, gapPending, gapFrame, frame);

        // ---------------- CarrierGearApplies ----------------

        [Test]
        public void Colonist_CarryingSomething_NeverApplies()
        {
            // The #224 regression pin: the old tag-based test fired for COLONISTS (they hold the HD tags) and
            // never for the pack animals the label advertises. A humanlike is excluded no matter what it holds.
            Assert.That(GearApplies(isHumanlike: true, isPlayerFaction: true, hasInventoryContents: true), Is.False);
        }

        [Test]
        public void PlayerPackAnimal_WithCargo_Applies()
        {
            // The one audience the setting names: your own non-humanlike carrier with something in its inventory.
            Assert.That(GearApplies(isHumanlike: false, isPlayerFaction: true, hasInventoryContents: true), Is.True);
        }

        [Test]
        public void PlayerPackAnimal_Empty_DoesNotApply()
        {
            // Nothing to show in the Gear tab, so the pane is left on whatever the player was looking at.
            Assert.That(GearApplies(isHumanlike: false, isPlayerFaction: true, hasInventoryContents: false), Is.False);
        }

        [Test]
        public void TraderPackAnimal_WithCargo_DoesNotApply()
        {
            // A visiting caravan's loaded muffalo is not the player's carrier — it must not hijack the tab.
            Assert.That(GearApplies(isHumanlike: false, isPlayerFaction: false, hasInventoryContents: true), Is.False);
        }

        [Test]
        public void NonPlayerHumanlike_Carrying_DoesNotApply()
        {
            // Both exclusions at once (a guest/raider hauling loot): humanlike AND not player-faction.
            Assert.That(GearApplies(isHumanlike: true, isPlayerFaction: false, hasInventoryContents: true), Is.False);
        }

        // ---------------- IsNewSelection ----------------

        [Test]
        public void DifferentThing_IsNew()
        {
            // Clicking a different carrier is always a fresh selection (the gap here is this click's own
            // ClearSelection, recorded on the SAME frame, so it is correctly ignored).
            Assert.That(IsNew(selectedId: 7, lastSelectedId: 4, gapPending: true, gapFrame: 100, frame: 100), Is.True);
        }

        [Test]
        public void SameThing_SameFrameGap_NotNew()
        {
            // The #224 UX defect: a plain re-click runs ClearSelection(); Select(obj) in ONE frame. That pair is
            // part of this click, so it must not read as a deselect — otherwise a closed tab is forced open again.
            Assert.That(IsNew(selectedId: 7, lastSelectedId: 7, gapPending: true, gapFrame: 100, frame: 100), Is.False);
        }

        [Test]
        public void SameThing_AfterEarlierEmptyGap_IsNew()
        {
            // The player clicked bare ground on an EARLIER frame, then re-selected the same carrier: a genuinely
            // new selection, so the auto-open fires again.
            Assert.That(IsNew(selectedId: 7, lastSelectedId: 7, gapPending: true, gapFrame: 100, frame: 103), Is.True);
        }

        [Test]
        public void SameThing_NoGap_NotNew()
        {
            // No deselect at all (e.g. a keyboard/CameraJumper re-select of the current thing) — still the same
            // selection, so nothing is forced open.
            Assert.That(IsNew(selectedId: 7, lastSelectedId: 7, gapPending: false, gapFrame: -1, frame: 103), Is.False);
        }

        [Test]
        public void FirstEverSelection_IsNew()
        {
            // The -1 sentinel never matches a real thingIDNumber, so the very first selection of a session opens.
            Assert.That(IsNew(selectedId: 7, lastSelectedId: -1, gapPending: false, gapFrame: -1, frame: 42), Is.True);
        }

        [Test]
        public void GapTerm_BothHalvesAreLoadBearing()
        {
            // The SECOND assertion is the one doing the work here — do not delete it as "covered elsewhere". It is
            // the only case that pins the `gapPending` half of `gapPending && gapFrame != frame`: drop that half
            // and this flips true, because 100 != 103. (The `gapFrame != frame` half is pinned by the same-frame
            // case above.) The first assertion is a deliberate duplicate of SameThing_AfterEarlierEmptyGap_IsNew,
            // kept only so the pair reads as a contrast and the mutation being guarded against is obvious.
            Assert.That(IsNew(selectedId: 7, lastSelectedId: 7, gapPending: true, gapFrame: 100, frame: 103), Is.True);
            Assert.That(IsNew(selectedId: 7, lastSelectedId: 7, gapPending: false, gapFrame: 100, frame: 103), Is.False);
        }
    }
}
