---
"haulers-dream": patch
---

Fixed "Do until you have X" bills ignoring **Pause when satisfied** and **Unpause at** — pawns kept crafting until the target was full and the bill never paused. Hauler's Dream banks freshly-made products in a pawn's inventory (to deliver a whole batch in one trip), but the vanilla product count that drives the pause/target/unpause decision only counts items in storage and in the hands, never in inventory. So the banked products were invisible, the bill never saw its target met, the paused state never latched, and pawns overproduced. The product count now includes the in-flight banked products colony-wide, so the bill's own pause-when-satisfied and unpause-at hysteresis work exactly as they do for ordinary one-at-a-time crafting.
