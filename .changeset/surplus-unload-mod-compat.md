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

- **New "Individual Item Unload Settings" picker (mod options).** A stockpile-style categorized, foldable,
  searchable list where you set how Hauler's Dream treats specific items in a pawn's inventory — per item, choose
  **Never unload** (keep the whole stack), **Keep at most N** (carry up to N and unload the rest), or **Always
  unload** (put it away even if another mod would otherwise keep it). A rule overrides the auto-detected mod
  keeps above for that item. It's fully fallback-safe: choices for items from a mod you later remove won't break
  your save, and they're restored automatically if you reinstall the mod. (Built on the vanilla item tree
  directly, so it also no longer throws errors when opened from the main-menu mod options — the old picker used
  an in-game-only UI that spammed the log when no save was loaded.)

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
