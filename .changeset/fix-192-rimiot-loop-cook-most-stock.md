---
"haulers-dream": patch
---

Fix the RimIOT terminal haul loop and make "cook with the most-stocked ingredient first" work under Common Sense (issue #192).

When a RimIOT (Logistic Matrix) network is full it drops the carried stack on the ground by the interaction terminal, which colonists could keep re-collecting and re-unloading forever. The gate that leaves those drops to RimIOT now recognises every interface-terminal type by its building class (so a renamed or extra terminal variant is still covered) and reaches a bit farther from the terminal, since the overflow drop can scatter several tiles when the space around a full terminal is crowded. Items are only ever left for RimIOT and normal hauling to collect, never lost.

"Cook with the most-stocked ingredient first" now also works when Common Sense is installed. Common Sense takes over the cooking-ingredient order, and this option had no effect on its own before; it now layers on top, so cooks reach for the ingredient the colony has the most of while Common Sense's freshness order is kept within each ingredient. It stays off by default and does nothing to non-cooking bills.
