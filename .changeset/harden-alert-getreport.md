---
"haulers-dream": patch
---

**Harden the "cannot unload inventory" alert so a bug in it can never blank the game's UI.**

The black-hole safety-net alert (`Alert_CannotUnloadInventory`) recomputes its report on the UI render
path — RimWorld calls it when you hover or click the alert, and the vanilla alerts readout does *not*
wrap that call in a try/catch. So if that recompute ever threw an exception, it would abort the rest of
the frame's UI drawing before the window layer, leaving the whole HUD invisible-but-still-clickable until
you moved off the alert. Its report code is now guarded: on any unexpected error it logs the problem
loudly (so the bug is still reported, never silently swallowed) and falls back to its last good report,
keeping the HUD alive.

This is defensive hardening — no behaviour change in normal play. (Investigated alongside player reports
of disappearing UI; the most likely causes of that symptom are an unrelated mod throwing on the UI layer
or save corruption from swapping inventory mods mid-save, which a Player.log will pinpoint.)
