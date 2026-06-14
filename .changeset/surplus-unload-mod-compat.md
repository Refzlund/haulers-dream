---
"haulers-dream": patch
---

**Fix inventory unload loops with Simple Sidearms / Smart Medicine / Dub's Bad Hygiene, and stop pawns dropping un-haulable items at random spots.**

- **No more unload↔pickup loops with mods that keep items in inventory.** Hauler's Dream used to treat a
  colonist's carried kit as "surplus" and ship it to storage, which mods like Simple Sidearms (remembered
  sidearms), Smart Medicine (stock-up medicine), Dub's Bad Hygiene (carried water), and Combat Extended
  (loadout ammo) would then immediately re-fetch — an endless drop-and-grab loop that could leave pawns walking
  back and forth until they collapse. Those items are now auto-detected (no extra setup, and nothing changes if
  you don't run those mods) and left in the pawn's inventory. Vanilla addiction/chemical-dependency drugs are
  kept too, matching vanilla.

- **New "Items to never unload…" picker (mod options).** A stockpile-style categorized list where you can mark
  any items Hauler's Dream should always leave in a pawn's inventory (e.g. ammo or weapons your pawns carry) —
  on top of the auto-detected mod items above. It's fully fallback-safe: choices for items from a mod you later
  remove won't break your save, and they're restored automatically if you reinstall the mod.

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
