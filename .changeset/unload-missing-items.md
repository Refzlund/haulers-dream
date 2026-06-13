---
"haulers-dream": patch
---

Pawns now properly put away items that no stockpile accepts — the most common being rock chunks (vanilla stockpiles exclude the Chunks category by default), but also mod-added materials (e.g. bronze from deconstruction) and mod-added crops whose category isn't in a default stockpile filter. Previously these were correctly picked up but, at unload time, dumped wherever the pawn happened to be standing (a workbench, the dining room) — and with no dumping zone they'd just get re-scooped on the next work run and carried around indefinitely, while everything else unloaded fine.

The unload now mirrors vanilla's own behaviour: if no stockpile accepts an item, the pawn carries it to a dumping zone (if you have one) or a tidy spot near the colony, instead of dropping it underfoot. As a safety net, an item that genuinely can't be placed is no longer left silently stuck in the inventory. The "drop off in passing" trip also no longer gets skipped just because an un-storable item (like a chunk) happens to be in the backpack — it now checks the items it can actually store.

Tip: if rock chunks pile up, add a Dumping Stockpile (and allow chunks on it) so pawns have somewhere to put them.
