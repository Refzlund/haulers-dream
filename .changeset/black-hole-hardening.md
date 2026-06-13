---
"haulers-dream": patch
---

Hardened the "no black holes" guarantee so scooped items can never be silently lost or stranded, and the safety-net alert can't be fooled into staying quiet:

- **Items are no longer lost when stacks merge.** When a carried stack is put back and merges into another stack of the same item (which happens with some interrupt mods), the merged-into stack is now re-tracked, instead of the item quietly losing its "needs unloading" mark and being left in the backpack forever.
- **The "cannot unload" alert now catches pawns even when another mod keeps cancelling the unload.** Previously, a mod that interrupted and re-queued the unload job faster than the alert's grace window (e.g. an aggressive autocaster) could keep resetting the alert's timer so it never fired. The alert now times how long the *problem* has persisted, independently of whether an unload happens to be running at that instant — so a genuinely stuck pawn always surfaces.
- **No more phantom "Unload now" churn for personal kit.** Items a pawn legitimately keeps in its inventory (drug-policy stock, packable food, ammo/loadout under Combat Extended) stay tracked so any future surplus is never stranded — but they no longer trigger a no-op unload trip every cycle or a misleading permanent "Unload now" button.
- The unload pass, the unload trigger, and the alert now share one definition of "what counts as surplus vs. the pawn's personal kit", so they can never disagree (which previously could cause either a nag or a missed item).

Also added a `COMPATIBILITY.md` documenting how Hauler's Dream coexists with other mods (item-adders, unload/interrupt mods, Combat Extended), from a code-level review of a real load order.
