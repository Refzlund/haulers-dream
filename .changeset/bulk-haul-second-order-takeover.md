---
"haulers-dream": patch
---

**Ordering a second nearby haul now starts the area sweep immediately.** With bulk hauling set to "only when a
second item is tasked" (the default), right-clicking to prioritize one haul still hauls just that item — but
ordering a *second* nearby haul now makes the pawn take over with the bulk sweep right away (picking up the
nearby items into its inventory for one storage trip), instead of carrying the first item to storage solo and
only then coming back for the rest.

This works whether the second order is a plain or a shift-queued prioritize, the first item is swept as part
of the one trip, and any unrelated work you had queued is preserved (it runs after the sweep). Ordering a
third or fourth nearby haul folds those into the same trip.
