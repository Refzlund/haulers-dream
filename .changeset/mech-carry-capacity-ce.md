---
"haulers-dream": patch
---

fix: under Combat Extended, mechanoids now haul according to their carrying capacity.

With Combat Extended installed, a hauler mechanoid was limited by Combat Extended's carry weight (a flat body-size value, around 42 kg for most lifters) regardless of its carrying capacity, so a vanilla lifter (carrying capacity 52) and a modded advanced loader (158) both hauled about the same small amount, and a fuller mech crawled once it passed that tiny limit. Hauler's Dream now sets a player mechanoid's Combat Extended carry weight to its carrying capacity, so it loads up to that capacity without hitting Combat Extended's over-capacity slowdown (Combat Extended's encumbrance and fit checks now follow the carrying-capacity number). The mech-haul multiplier in the settings now also applies under Combat Extended. Pawns other than your own mechanoids, and games without Combat Extended, are unaffected.
