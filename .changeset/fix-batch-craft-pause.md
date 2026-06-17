---
"haulers-dream": patch
---

Batch crafting now respects a bill's "Do until you have X" target, "Pause when satisfied", and "Unpause at" settings. Because HD's batch driver banks freshly-made products in the crafter's inventory (to deliver a whole batch in one trip), and RimWorld's product counter can't see pawn inventory, the batch never noticed the target was reached — colonists kept crafting past it and the bill never paused. The target count now includes the in-flight products colonists are carrying toward storage, so a batch stops at the target (across the whole colony, not just the crafting pawn) and the bill pauses on delivery exactly like a normal one-at-a-time bill. Repeat-count and repeat-forever bills were unaffected and are unchanged.
