---
"haulers-dream": minor
---

Picking items up now takes a moment, with vanilla's pickup progress bar.

Pawns used to vacuum stacks into their inventory instantly, which felt off next to the visible work delay vanilla shows when you order a pawn to pick something up. Now every Hauler's Dream pickup into inventory pauses at the stack with the familiar progress bar first: bulk hauling sweeps, picking things up along the way, scooping up mining and harvest yields, the "Pick up X" and "Keep X in inventory" orders, and the part of bulk loading and bulk refueling where pawns gather stacks off the ground. The default is 120 ticks per stack (about 2 seconds), which is exactly the delay vanilla itself uses for its pick up order, so an ordered pickup feels identical to vanilla and bulk work reads as deliberate effort instead of teleportation.

A new "Pickup delay per stack" slider in the Hauling settings controls it. Slide it to 0 for the old instant pickups, leave it at 120 for the vanilla feel, or pick your own pace. Gathering ingredients for crafting and construction is deliberately not delayed, since vanilla grabs work materials instantly too, and unloading keeps its own separate pacing setting.
