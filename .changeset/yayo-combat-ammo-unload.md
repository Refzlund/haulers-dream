---
"haulers-dream": patch
---

**Fix pawns freezing in the "unloading inventory" job over Yayo's Combat 3 ammo (and harden the unload against any item it can't move).**

A colonist returning from a caravan could get stuck standing in the "unloading inventory" state; manually
dropping their Yayo's Combat 3 ammunition fixed it. Cause: Hauler's Dream only recognised Combat Extended
ammo as "keep in inventory", so it treated YC3 ammo as surplus and kept trying to haul it off — fighting
YC3 (which re-stocks the pawn's ammo), and churning the unload job.

- **Yayo's Combat 3 ammo is now kept in inventory** (auto-detected, no setup, nothing changes if you don't
  run YC3), the same way Combat Extended ammo already was. A pawn's own ammo is never hauled to storage; HD
  only ever moves *loose* ammo it scooped off the ground. If you actually want a pawn's ammo put away, the
  per-item "always unload" rule in mod options still overrides this.

- **The unload job can no longer get stuck on a single item it can't move.** If something can't be taken out
  of a pawn's inventory (another mod is holding it, or the pawn's hands are momentarily full), the pawn now
  skips it and unloads the rest, instead of standing in place retrying the same item. The skipped item keeps
  its place in the queue and is retried later — and still raises the "cannot unload" alert if it's genuinely
  stuck — so nothing is silently abandoned. This also covers carried grenades (More Useful Grenade) and any
  other mod that keeps combat consumables in a pawn's inventory.

If you have an existing save where ammo got dropped during this bug, it will be picked back up by YC3 as
normal.
