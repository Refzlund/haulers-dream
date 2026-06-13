---
"haulers-dream": patch
---

**"Load nearby items onto pack animal (bulk)" now appears for every pawn vanilla allows.** A colonist who
is incapable of dumb-labor hauling (e.g. a doctor delivering materials to a construction site) was missing
the bulk load option even though vanilla's own one-stack "Load onto pack animal" appeared for them. The bulk
order is a player command — like vanilla, it no longer requires the Hauling work-tag, so any pawn that can
physically pick things up can be ordered to load a pack animal. The same relaxation applies to coalescing
several shift-clicked vanilla load orders into one trip.

This only affects the **player-ordered** pack-animal paths (which deposit onto the animal, never stranding
loot in the pawn's inventory). The automatic bulk-haul still keeps its full eligibility check.
