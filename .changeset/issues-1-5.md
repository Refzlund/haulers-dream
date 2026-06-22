---
"haulers-dream": minor
---

feat/fix: five player-reported improvements.

Mechanoid carry capacity now tracks the mech's own "carrying capacity" (the value shown on the mech's UI panel) instead of a flat amount. A vanilla lifter and a modded high-capacity loader now haul amounts that match their carrying capacity rather than the same small default. The per-mech haul multiplier still applies on top, and humanlikes, animals, and Combat Extended users are unchanged.

Fixed red errors when right-clicking eggs (or other items held inside a container building, like an egg box) with a colonist selected. Those items are not lying on the floor, so the pickup and haul-nearby orders now skip them instead of throwing.

You can now order a pawn to pick an item straight into its inventory while DRAFTED, and the order works on forbidden items (for example, food dropped in a prison cell that got auto-forbidden). The picked item is carried until the pawn is undrafted, then put away in normal storage, unforbidden.

Fixed pawns getting stuck in an endless "gathering ingredients" loop when crafting or cooking a recipe with many ingredients under the "Do until you have X" bill setting (for example baking pies in a large multi-ingredient oven). Such recipes now use the game's normal ingredient gathering.

Fixed Hauler's Dream interfering with order-based recycling mods (such as Recycle This): an item you have marked for recycling is no longer scooped into a pawn's inventory before the recycling job can carry it to the workbench. Hauler's Dream now leaves items alone when another mod has claimed them with an order.
