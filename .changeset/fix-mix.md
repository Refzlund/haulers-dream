---
"haulers-dream": minor
---

Four fixes from player reports:

**Fixed a NullReferenceException flood after migrating a save off Pick Up And Haul.** When you remove Pick Up And Haul (which Hauler's Dream replaces) and load a save where a pawn (often a Lifter mech) was mid-haul, that pawn's old job can no longer be loaded and deserializes broken. RimWorld's own cleanup of such a job crashes on it, so the broken job is never cleared and the pawn throws an error every tick. Hauler's Dream now repairs these orphaned jobs, and the reservations they leave behind, when the save loads, so the affected pawns simply pick new jobs and the errors stop. It is a one-time cleanup per save and does nothing on a clean game.

**Mechanoids now haul in proportion to their carrying capacity, with an optional multiplier.** Hauler's Dream already sizes each pawn's haul by its carrying capacity, so a modded high-capacity lifter already carries more than a vanilla one. A new "Mechanoid carrying capacity" slider (Who can haul, default ×1.0) lets you push your work mechs further, so a dedicated lifter makes fewer, bigger trips. The mech is slowed by the extra load the same way a colonist is, so the smart-overload trade stays balanced. No effect at ×1.0, and Combat Extended keeps managing carry weight when it is installed.

**Added a setting to show the per-pawn auto-haul toggle (Unloading, off by default).** The "Auto-haul yields" toggle on each pawn is now hidden unless you turn this on, keeping the command bar uncluttered. Pawns still auto-haul exactly as before; turn the setting on if you want to stop individual pawns from auto-hauling.

**Fixed vehicle cargo loading being silently off when Vehicle Framework is installed.** Hauler's Dream checks for Vehicle Framework as the game loads, but that check happened slightly too early, before the game had finished setting up Vehicle Framework's stats. Because of the timing it switched the whole integration off for the rest of the session even though it is on by default, so colonists never bulk-loaded a vehicle's cargo in one trip and eating or building from a parked vehicle's cargo did not work. The check now reads the vehicle stat the first time it is actually needed during play, so the integration turns on as intended.
