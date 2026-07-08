---
"haulers-dream": patch
---

Fix a much bigger cause of the same colonists-freezing-in-place bug (#160): giving a bulk-haul order while the game was paused could rebuild the whole sweep plan hundreds of times in a couple of real seconds for nothing.

Bulk-haul orders remember their plan for the rest of the tick so opening a menu or clicking doesn't redo the same expensive search over and over, but that memory was deliberately skipped whenever the game was paused, to make sure queuing a second nearby order was noticed right away. The debug logs you sent showed exactly what that costs in practice: a single paused ordering session rebuilding the same plan for the same colonist several hundred times in a few seconds, worse with more colonists or orders queued at once. The fix keeps that memory turned on while paused too, but only reuses it as long as nothing about that colonist's current job or queue has actually changed since, so a genuinely new order still gets noticed immediately while a repeated click or hover no longer pays for a full rebuild.
