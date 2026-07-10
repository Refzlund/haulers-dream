---
"haulers-dream": patch
---

Stop colonists looping forever at a full RimIOT interface terminal.

With RimIOT (Logistic Matrix), when a logistic network fills up its interface terminal drops the item a colonist was depositing onto the ground right next to the terminal. Hauler's Dream would then keep scooping that loose stack back up and trying to store it into the still-full network, over and over, so a pair of colonists could get stuck shuffling the same stack (for example a large pile of leather) between "haul all nearby items" and "unload inventory" indefinitely.

Hauler's Dream now leaves loose items in the small area around a powered interface terminal to RimIOT, on every automatic pickup path, so the loop can no longer form. This also closes the same gap on the "grab items on the way to a job" path, which the earlier RimIOT fix did not cover. There is no effect when RimIOT is not installed.
