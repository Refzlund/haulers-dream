---
"haulers-dream": patch
---

**Fix colonists occasionally stopping work to unload their own Simple Sidearms sidearm.**

A remembered Simple Sidearms weapon could be hauled off to storage (and immediately re-fetched by Simple
Sidearms) when a colonist happened to be carrying loose weapons that shared a ThingDef with one of its sidearms.
Because weapons don't stack, Hauler's Dream's "same-def" inventory bookkeeping was mistaking the pawn's own
sidearm for hauled loot of the same type and marking it surplus.

Now a genuine remembered sidearm (matched precisely by weapon + material) is never treated as surplus: it is
protected both where Hauler's Dream tags carried items and in the keep check itself, so it always wins over a
mistaken tag. Loose weapons the colonist actually picked up off the ground are still put away normally, and
nothing changes when Simple Sidearms isn't installed.
