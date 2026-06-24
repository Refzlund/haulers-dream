---
"haulers-dream": patch
---

fix: pawns mining/harvesting no longer run home to unload after a single block.

When a pawn mined or harvested, scooped the yield, and the work scan then handed it a nearby non-yield job (often cleaning), Hauler's Dream diverted it all the way to storage to unload after that single item, far short of a full load. So a pawn sent to mine would run out, mine one block, run all the way back, and repeat, instead of accumulating a pack and making one trip.

The "drop your load before unrelated work" divert now waits for the same brief settle the end-of-run unload already uses: while a pawn is still actively scooping (mid mine/harvest run), a quick non-yield detour no longer counts as the run being over, so it keeps its load and keeps working until it is full, the run genuinely winds down, or it heads off to rest or eat. Continuous same-task work and the full-inventory trip are unchanged.
