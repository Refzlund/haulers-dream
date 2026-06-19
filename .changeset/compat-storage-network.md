---
"haulers-dream": patch
---

Fixed bulk-loading transporters, shuttles, drop pods, portals and vehicles when the goods live in a storage building — including Storage Network servers, but also shelves, deep storage and ordinary stockpiles. The bulk-load sweep only ever looked at loose items lying on the ground (the haulables list excludes anything already in valid storage), so when everything was stored the sweep found nothing and the pawn fell back to the vanilla one-stack-per-trip behaviour — taking a single pack instead of everything the manifest needed. Hauler's Dream now also sweeps the stacks held in storage for the items being loaded, so the whole load is gathered in one trip as intended. The amount taken is still bound to the manifest and the pawn's carry capacity, and anything a storage keeps off-map (rather than as a normal stack) is simply left to vanilla, so nothing is over-pulled or stranded.
