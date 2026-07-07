---
"haulers-dream": patch
---

Keep colonists moving when a shared storage tile fills up mid haul, instead of dropping the load and pacing.

Haul To Stack lets several colonists pile onto the same storage tile, but the game itself was not built for that: if the tile fills while a colonist is still walking over to it, the game cancels the whole haul and drops the item at their feet, and they pick it up and try again, over and over. Hauler's Dream now steps in the moment that happens and redirects the colonist, still carrying the item, to another stacking tile, so the haul just finishes. Nothing is dropped, nothing is reserved, and stacking stays fully on. If a colonist has to redirect many times in a row because storage is genuinely full, it still falls back to the existing brief pause as a safety net.
