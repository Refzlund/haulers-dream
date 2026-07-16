---
"haulers-dream": patch
---

Stop the last way colonists could loop forever at a RimIOT (Logistic Matrix) terminal.

Earlier fixes stopped colonists picking loose items back up around a full terminal, but the loop could still "occasionally" come back. The real, deepest cause was different: RimIOT redirects a colonist who is walking to fetch an item, sending them to its terminal to grab a matching item straight out of the network instead. When Hauler's Dream had upgraded that fetch into a bulk haul, the colonist would pull the item out of the network and immediately carry it back in, moving nothing, forever, with no error to show for it.

Hauler's Dream now leaves delivery into a RimIOT network to RimIOT: when an automatic haul is headed for network storage it keeps the plain vanilla haul (which RimIOT handles correctly) instead of turning it into a bulk haul. As a second safety net, if another mod ever swaps a bulk haul's target out from under Hauler's Dream mid-job, the colonist no longer pockets the substituted network item, and Hauler's Dream backs that item off and prints one clear warning naming what happened, so a future loop of this shape is surfaced instead of silently repeating. None of this has any effect when RimIOT is not installed, and a forced "Haul" order always works as before.
