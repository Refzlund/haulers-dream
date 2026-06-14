---
"haulers-dream": patch
---

**Fix inventory unload loops with Simple Sidearms / Smart Medicine / Dub's Bad Hygiene, and stop pawns dropping un-haulable items at random spots.**

- **No more unload↔pickup loops with mods that keep items in inventory.** Hauler's Dream used to treat a
  colonist's carried kit as "surplus" and ship it to storage, which mods like Simple Sidearms (remembered
  sidearms), Smart Medicine (stock-up medicine), and Dub's Bad Hygiene (carried water) would then immediately
  re-fetch — an endless drop-and-grab loop. Those items are now auto-detected (no extra setup, and nothing
  changes if you don't run those mods) and left in the pawn's inventory. Vanilla addiction/chemical-dependency
  drugs are kept too, matching vanilla.

- **Pawns no longer carry un-storable items to a random spot.** If a harvested/mined/deconstructed yield (or
  any swept item) has nowhere it can be stored, the pawn now leaves it on the ground where it was produced,
  instead of scooping it into inventory and later dropping it at a random home-area cell. Items are only picked
  up into inventory when there's actually somewhere to deliver them.

- **"Unload foreign surplus" is now off by default.** Out of the box, Hauler's Dream only puts away goods it
  picked up itself — it never touches a colonist's sidearms, carried medicine/water, or traded goods. You can
  still turn this on (mod options) for the convenience of auto-hauling surplus a pawn is carrying for no reason;
  it's now safe with the supported mods. Existing saves keep whatever you had set.

The red "Cannot unload inventory" alert still fires for anything that genuinely has nowhere to go, so nothing
is silently stuck.
