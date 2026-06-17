---
"haulers-dream": patch
---

Internal hardening (no behavior change): the per-tick caches Hauler's Dream clears on game load are now tracked by a self-registering registry instead of a hand-maintained list, so a future cache can't be forgotten and leak stale data across a save/load. This also closes two pre-existing gaps where the route-claim cache (cleared only indirectly) and the Common Sense compat cache (never cleared) could carry a stale value into a freshly loaded game.
