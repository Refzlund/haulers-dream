---
"haulers-dream": minor
---

**Disabling Hauler's Dream from an existing save no longer risks breaking that save — automatically.**

Removing any mod that adds custom jobs from an *active* save is an unsupported RimWorld operation: if a
colonist is mid-task when you save, the job's now-missing definition can't load, vanilla's own cleanup of that
broken job can fail, and a pawn left in that state can make other UI mods (e.g. Color Coded Mood Bar) blank the
in-game HUD. A disabled mod's code never runs, so it can only protect you *before* removal.

Hauler's Dream now does that protection **automatically, with no action required**:

- **Automatic on-save protection (default on):** whenever the game is saved, Hauler's Dream rewrites its own
  in-progress jobs to a harmless placeholder *in the written save file only* — your colonists are never
  interrupted during play. The result is that every save is always safe to disable the mod from. (When you load
  such a save, RimWorld may log a harmless "cleaning up job state" notice for any colonist that was mid-task;
  they simply re-pick their work.) There's a toggle to turn it off if it ever conflicts with another save mod.
- **Manual one-shot button + Dev action** (*Mod Settings → "Prepare to safely remove this mod now"*): stops
  in-progress Hauler's Dream tasks on the loaded game and returns scooped items to the ground. Mainly useful for
  a save first made with an older version of the mod.
- The mod description now states it's safe to remove from existing saves.

This is safe by construction: RimWorld serializes synchronously on the main thread (the game is frozen during a
save), so the rewrite-then-restore happens entirely within the save and is never observed by the running game.
It protects every save written by this version onward.
