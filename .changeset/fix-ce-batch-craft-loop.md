---
"haulers-dream": patch
---

Fix colonists getting stuck forever trying to start a large batch craft when they cannot carry all the ingredients in one trip, most often under Combat Extended.

When a batch crafting job needed more ingredients than a colonist could carry at once (common under Combat Extended, or with strict carry weight and overloading turned off), the colonist would pick up as much as it could, find it still could not make even one item, haul everything back to storage, and immediately start the same job again, looping without ever crafting or resting. It was worst for recipes that mix ingredients (cooked meals, kibble, pemmican, chemfuel, beer), where the plan did not account for bulk at all, so the colonist could gather a full load and still be unable to craft a single item.

Now a colonist gathers only as many whole crafting rounds as actually fit its carry capacity on each trip, picking up every ingredient type in proportion instead of filling up on one, crafts them, then goes back for more until the batch is done. The batch size itself is never reduced because of carry capacity, since other mods can change how much a pawn carries and that is not something to generalize on. If a single round genuinely cannot be carried, the batch quietly steps aside and lets the base game craft one at a time rather than looping. Colonists that can overload as normal are unaffected.
