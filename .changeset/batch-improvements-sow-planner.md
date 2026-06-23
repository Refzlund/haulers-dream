---
"haulers-dream": minor
---

feat: batch crafting that finishes on its own terms, an overshoot option, sowing route planner, and dropdown tooltips.

Batch "Do forever" now keeps the pawn crafting an uninterrupted run instead of one item at a time, and the pawn unloads what it made and stops on its own when it would rather eat, rest, fight, or attend to something more pressing, so it never freezes onto the bench. The pawn fetches a whole batch of ingredients in one trip, makes them, and hauls the results out, then yields to its other needs.

Batch "Do until you have X" now has an optional "overshoot by Y": once a pawn has started a batch (the game starts it while you are still below X), it keeps going up to X+Y so it finishes a useful round number while it is already there, instead of stopping the instant the count crosses X. Set Y to 0 (the default) to keep the exact vanilla behaviour of stopping at X. The "Pause when satisfied" option still ends a normal batch at X; it only steps aside inside an active overshoot window because that is what asking for Y more means.

Added a route planner for the "sow growing area" task, the sowing companion to the existing planners. Right-click a growing zone with a colonist selected and choose "Plan prioritized sowing" to queue an ordered sweep over the zone's empty cells.

Batch dropdown entries ("Batch: Do X", "Do until you have X", "Do forever", "Batch size", and the new overshoot option) now show a short tooltip on hover explaining what each one does.
