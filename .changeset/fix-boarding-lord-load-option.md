---
"haulers-dream": patch
---

Fixed colonists being left with no way to load a transporter, shuttle, drop pod or map portal once they were already assigned to board it. While a pawn was under the caravan/portal boarding lord, Hauler's Dream suppressed vanilla's "Load X" right-click option, but its own bulk-load option also intentionally stands aside there (to let the vanilla gather-and-board flow run) — so right-clicking the transporter offered nothing and the pawn could not be hand-directed to load. Hauler's Dream now keeps vanilla's load option whenever its own bulk option declines, so there is always a way to order the load.
