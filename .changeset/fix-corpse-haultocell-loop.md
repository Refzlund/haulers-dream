---
"haulers-dream": patch
---

Fixed a "started 10 jobs in one tick" error and pawn stall when a colonist hauled a corpse (or any other unstackable item) to a storage cell. Haul To Stack deliberately leaves the destination cell unreserved so several haulers can top up the same tile at once — but an unstackable thing can never share a cell, and removing the reservation also removed the vanilla throttle that stops the same cell from being re-picked every tick. When two haulers contended the same one-capacity corpse cell, the work scan re-issued the identical haul over and over until the engine's safety guard tripped and the pawn froze doing nothing. Unstackable items (corpses, minified buildings, weapons) now keep vanilla's cell reservation; stackable hauling is unchanged.
