---
"haulers-dream": minor
---

**Add a "Prepare to safely remove this mod" action so disabling Hauler's Dream no longer risks breaking a save.**

Removing any mod that adds custom jobs (Hauler's Dream, Pick Up And Haul, etc.) from an *active* save is an
unsupported RimWorld operation: if a colonist is mid-task when you save, the job's now-missing definition can't
load, and vanilla's own cleanup of that broken job can fail — leaving a pawn in a bad state that other UI mods
(e.g. Color Coded Mood Bar) can choke on, blanking the in-game HUD. Once a mod is disabled none of its code runs,
so it can only protect you *before* removal.

Hauler's Dream now ships that protection:

- **Mod Settings → "Prepare to safely remove this mod"** stops every in-progress and queued Hauler's Dream task
  on all colonists/caravans and returns any items they had scooped into their packs to the ground. Click it,
  save your game, then it's safe to disable the mod — the save will load cleanly with Hauler's Dream gone.
- A matching **Dev Mode debug action** ("Hauler's Dream → Prepare for safe removal") does the same.
- The mod description and settings now document the recommended removal procedure.

This is intentionally a manual action (not run automatically on every save), so it never interrupts your
colonists during normal play. It protects any save made after clicking it; a save already created with an
in-progress Hauler's Dream job can be fixed by re-enabling the mod, loading, clicking the button, saving, then
disabling.
