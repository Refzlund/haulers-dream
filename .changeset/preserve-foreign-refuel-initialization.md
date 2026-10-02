---
"haulers-dream": patch
---

Preserve other mods' refuelling implementations when they replace the native code that Hauler's Dream observes. Previously, an incompatible refuelling patch could throw while Medieval Overhaul was initializing, preventing its saved refuelable buildings from loading. Unsupported implementations now retain their own refuelling behavior, and HD declines its ordinary bulk-refuelling route.

Avoid searching for multiple ground fuel stacks for every building considered during a work scan. Ground pickup planning now runs for the selected job; eligibility checks can still recognize usable fuel already in the pawn's inventory.

Check storage admission before replacing urgent hauling or adding an optional pickup on the way to work. When HD cannot admit the pickup, preserve the original job instead of starting a bulk-haul job that immediately fails its reservations and leaves the pawn standing.
