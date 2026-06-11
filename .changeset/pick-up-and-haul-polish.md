---
"haulers-dream": patch
---

Pick up and haul polish, from a top-to-bottom review of the feature by three independent reviewers: bulk sweeps no longer pull an item back out of storage when you order two hauls in a row (the first delivery stays delivered); a bulk hauler's end-of-sweep unload now waits behind an order you give mid-sweep instead of finishing its storage trip first; under Combat Extended, the unload keeps a pawn's own loadout reserve (ammo, sidearm stock) instead of shipping it to storage for CE to fetch right back; bulk-saturated CE pawns stop their sweep instead of visiting stacks they can't carry; and a set of robustness and performance improvements to the sweep planner (cheap checks before pathfinding, hardened plan cache, safe behavior if a future CE changes its internals).
