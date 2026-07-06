---
"haulers-dream": patch
---

Colonists ordered to leave an underground map with loot now share the gathering instead of one pawn claiming everything while the rest wander.

When several colonists were sent through an exit portal (a pit gate or cave exit) together with loot, the first pawn to plan its load claimed the whole manifest up to its smart overload ceiling, which routinely covers an entire dungeon haul. The other ordered pawns then found nothing left to claim, and the board gate held them back from entering while goods remained, so they idled and wandered until the one loaded pawn finished. Bulk load plans are now clamped to an even mass share of the claimable loot across every ready co loader (the pawns ordered to board that portal or transporter group), with a floor so every share stays big enough to hold any single remaining item (a heavy sculpture never becomes unclaimable just because the even split runs smaller than it). A lone loader, a player ordered load, and vehicle loading keep their full plan exactly as before.

A pawn that has nothing left to claim (every remaining item is another loader's in flight slice) now boards the portal instead of pacing next to it, matching how the base game behaves. The pawns still gathering finish the manifest.

Also fixes a silent failure where bulk portal loading at overload level "Free" (level 0) built empty plans: the unlimited trip budget overflowed an integer conversion and every stack read as unaffordable, so those pawns quietly fell back to loading one stack at a time.
