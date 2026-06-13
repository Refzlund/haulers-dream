---
"haulers-dream": patch
---

Pawns now actually put their hauled goods away promptly, instead of carrying materials around for a whole day. Previously the only reliable automatic unload was a slow timer, and a pawn could finish a big deconstruction or harvest, stuff its inventory, then work, eat, sleep and relax all day without ever unloading. Fixes:

- **End of work run:** when a pawn runs out of work while carrying scooped goods, it now makes its unload trip right then — before drifting off to recreation or wandering — rather than holding the load indefinitely.
- **Before meals and recreation:** a pawn that sits down to eat or relax with a full backpack queues an unload that runs the moment it's done (its meal/break is never interrupted).
- **When overweight:** scooping that pushes a pawn over its carrying capacity now triggers an unload at the next job boundary, instead of letting it stay overloaded until it hits the much higher "smart overload" ceiling. A pawn shouldn't lug steel around all day.
- **Heavier loads shed sooner:** a pawn carrying half its capacity or more now diverts to drop the load off on shorter trips and tolerates a slightly bigger detour to storage.
- **Interval backstop** lowered from 6 in-game hours to 1, so even in the rare case every other trigger is missed, a load is never carried for more than about an hour. (Existing saves that changed this setting keep their value.)
- Fixed a desync where a pawn whose meal was momentarily in its hands would silently skip an unload check.

The "automatically unload" setting description now lists exactly when unloading happens, and what turning it off means (manual unload only, via the per-pawn button).
