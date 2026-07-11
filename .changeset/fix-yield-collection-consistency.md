---
"haulers-dream": patch
---

Collect harvested and drilled yields consistently, as the pawn works.

Under "Drop & haul", a working pawn now pockets its own yield as each item is produced, instead of waiting until its whole current job ends. This fixes two inconsistencies with the same cause. Harvesting a growing zone left the yield piling up on the ground until the entire field was finished and only then swept it up, while a single "Harvest" order collected each plant immediately. Deep-drill output was never collected until the drill was exhausted. Now all harvest modes (growing-zone auto-harvest, a Harvest order, and cutting plants) and deep drilling behave the same way. A pawn still leaves a yield on the ground when it has nowhere to store it or is already carrying too much.
