# F38 fresh consumer1: accepted replay reference

Actual `9e01136ad9544f87a7c022937c286bd6` passes **49/49,U0**,126 events, terminal2019. It is accepted as the reference for a second fresh consumer of the original save. It does not alone prove paired replay. The failed earlier `a120df33098243cc8fdc8f1864a24680` remains failed and is not promoted.

Source review covered `NativeLoadAdmission.cs`, the full current `CapacityReplay.cs`, controller reference admission and the native compressed-map diagnosis. The sole allowed saved-state difference is not normalized away: original nextThingID37542 becomes40725 through exactly3,183 real native map decompression allocations. No counter, ID, reference, reservation, plan or cargo field is written by admission. Native state and complete receipt are retained verbatim.

The actual original E10F save and consumer Autostart are byte-identical, saved tick8. An independent raw Deflate decoder reads125,000 bytes/62,500 ushort cells and verifies each of the3,183 nonzero entries against its receipt's actual sequential ID, shortHash and position, ending40724. The pinned native host guard additionally confirms each actual loaded Thing's def/shortHash/saveCompressible/ID. All saved actors, jobs, queues, cargo/keep, zone, settings and41 other global counters match before the first native tick. Exact actual module SHA5CF1B5… and MVID61e41735… are bound. The complete loaded-state receipt still contains raw40725, not a substituted producer value.

All four mods/ten images match the selected HD207C6A58…/Core815AAE0C…/host44E7C84A… images. Prepared ModsConfig and Prefs are unchanged. Both process receipts show joined exit0: native15816/controller8176; Default stays active, no switch, deadline or cleanup error, no owned process left. Verify retains generic manual/event/log review flags with `protectedChanges: []`.

The eleven emitted boundaries equal the eleven receipt lines, including raw IDs, scoped RNG, every reservation, plan slot/count/cursor, claim row and physical stack:

| Boundary | Actual tick and capacity |
|---|---|
|one-tick-ready/A-before|9, raw83/free83/effective0|
|A-after|9, original job25 admits17+25+41; raw83/free0/effective83|
|first-pickup/Keep-before/Keep-after|196, actual17 in A; original job25/cursor1|
|handoff-ready/B-before|197, kept7 settles one native tick; effective76/free7|
|B-after|197, actual TakeInventory32 reserves25; A's slot1 retires to invalid/0, original25/cursor1 and future41 remain; raw claim83 persists but live effective51/free32|
|C-before/C-after|197, C34 admits exactly32 from distant40; A51+C32 fills effective83/free0|

Original A25 succeeds373 with58 inventory/51 surplus; actual native B32 succeeds558 with25 personal untagged inventory; original C34 succeeds1085 with32. These are immutable native start/end observations, not pooled Job references. A's ordinary follow-up unload40 stores its51 and retains7, then ends Incompletable1156 when only kept cargo remains. It is not a failed original handoff job and contributes no fake successful end to the oracle. C's ordinary unload60 succeeds1721.

Ten complete physical censuses conserve190. At1719, two legal cells hold75+75, A keeps7, B holds25 untagged with UnloadEverything false, and distant ground stack has8. That state remains for300 ticks through2019, effective claims0. Native merges/splits retain actual IDs: final stored Cloth37537×75 and Cloth40726×75; kept Cloth37538×7; personal Cloth37539×25; ground Cloth37541×8.

Whole Player.log1,901 lines and all ten HD debug lines reviewed. No runtime exception, unresolved reference, reservation or cleanup error. The two Mono fallback lines, Header texture mip warning, Direct3D timing notices and allocator output remain preserved; no clean-log fiction. Debug confirms83 initial claim, C32 while51 remains, and productive51/32 unloads.

Root may prepare consumer2 from this exact successful controller-owned run. Controller admission must still rehash all copied/config inputs, actual selected images and three receipts before copying them. Consumer2 must consume the same original E10F save, compare the full raw loaded state/allocation receipt and all eleven boundary strings byte-for-byte, and independently pass physical/job/stability/log/custody oracles. This is local deterministic replay evidence; it is not a real two-client Multiplayer claim.

Authorship: I authored the earlier capacity fixture/product TakeInventory correction, not this new load-admission helper or v3 adaptation. Root also reviews this native result independently. The retained `review-native.py` and `independent-native-audit.json` supply reproducible input/allocation/event checks alongside this semantic review.
