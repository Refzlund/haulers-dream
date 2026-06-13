---
"haulers-dream": minor
---

**Caravan pawns now offload onto pack animals on their own.** On a caravan or other away map there's no
stockpile, so pawns used to just carry their scooped loot around in their own inventory — and could end up
idle or asleep with a full pack while the red "Cannot unload inventory" alert fired. Now they offload onto a
nearby owned pack animal at the same moments they'd unload to a stockpile at home: when their work run ends,
before they rest / eat / relax at camp, on the periodic backstop, and immediately when over-encumbered. When
no usable pack animal is reachable the loot still rides home in inventory as before.

The "Offload onto pack animals (caravans)" setting (formerly "Auto-load pack animals when heavy") now governs
all of this; it still requires automatic unloading to be on, and pawns keep accumulating their whole work run
before making the trip (no change to that). The red alert no longer false-fires on caravans where loot is
correctly riding home — it only flags a pawn that genuinely can't offload a reachable pack animal for hours.
