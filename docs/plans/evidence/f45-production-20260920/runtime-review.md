# F45 actual production review — 20 September 2026

**Accept the native work and resource-accounting evidence; reject these saves as clean restart evidence.** Run `a2f2fabc1bfe4d3e89a8391af302b319` reports 67 passing assertions, 127 contiguous events and zero captured Unity errors. Owned native PID 16936 joined with exit 0, without deadline or execution error. The scenario's `passed` result is retained, with the material save limitation below; it is not whole-F45 acceptance. Evidence, exact source/build, process outcome, Verify and four saves are retained in `native/a2f2fabc1bfe4d3e89a8391af302b319/`.

Two actual PB menu actions run in native Root.OnGUI Layout callbacks and clear previously seeded HD batching. The actual unforced WorkGiver then produces eight HD gather jobs. Their eight observed handoffs enter native `Verse.AI.JobDriver_DoBill`, with matching job/bill identities and positive recipe-work ticks (366–522 for simple meals; 1,461 for bulk). Each native notification advances PB once; none enters HD batch crafting. The debug log independently records all eight gather handoffs without rejected ingredient rows.

For CookMealSimple, four completed iterations consume 40 of 50 corn and produce four meals. For CookMealSimpleBulk, four iterations consume 160 of 200 corn and produce sixteen meals. Every settled iteration has exact ground/inventory/carry accounting before scene-goods cleanup. PB counts iterations, so the bulk recipe's two-iteration cycle produces eight meals. Final production is observed before cleanup, not manufactured by destroying ingredients or generating products in the fixture.

The run also records 120 ordinary cooldown ticks for each recipe, ordinary/forced work rejection, suspension at the boundary, then an explicit clock jump. Eligibility resets at completion plus 60,000: ticks 61,514 and 126,161. Two further real native jobs complete each second cycle. This is evidence of those queried boundaries, not an uninterrupted simulated day or general autonomous scheduling.

The actual save XML contains the same map 0, pawn Human11300, stove FueledStove11303 and the expected bill/PB keys. Saved batch data is empty. Counts below are scoped to the fixture map; unrelated world-pawn food is excluded.

| Checkpoint | Tick | Corn / meals | PB produced / completion |
| --- | ---: | ---: | --- |
| Simple partial | 734 | 40 / 1 | 1 / −1 |
| Simple cooldown | 1,514 | 30 / 2 | 2 / 1,514 |
| Bulk partial | 64,443 | 160 / 4 | 1 / −1 |
| Bulk cooldown | 66,162 | 120 / 8 | 2 / 66,161 |

Each target PB entry stores amount 2, with interval 1 and initial completion −1 correctly omitted when equal to the actual PB scribe defaults. These scalar/identity checks confirm what was written; they do not establish a valid reload.

**All four saves contain missing deep-save references.** The full 1,882-line Player.log contains twelve warnings: each save references Thing_Human544, Thing_Human547 and Thing_Human550 without deep-saving them. XML independently locates those references at `game/info/startingAndOptionalPawns/li` and confirms the corresponding deep pawn nodes are absent. The fixture despawned these original quicktest actors and held them only in an in-memory tuple list. Its snapshot therefore loses their serializable ownership. Zero captured Unity errors and Verify's two generic notices do not negate these warnings. Preserve the warning-bearing saves and obtain fresh clean checkpoints after correcting ownership; do not use the existing files as clean restart acceptance.

Actual native `WorldPawns` supports the minimal correction: transfer each recorded, despawned, non-discarded original pawn with `PassToWorld(...KeepForever)`, verify `Contains`, then remove its world membership before respawning the same object during cleanup. The world sets are deep-scribed; RemovePawn clears force-kept membership too. Native tending/notification/mothball behavior may change incidental pawn state, so this approach should promise preserved identity/placement and saved ownership, not full state rollback. This is a recommendation, not an executed correction.

The log also retains two early Mono fallback requests of unknown origin and HD stale-tag pruning diagnostics. No associated recipe/payment exception appears. Scene cleanup returns normally, with no cleanup-failure assertion; Verify reports no protected-file changes. Clean saves, real restart/continuation and a PB-only production comparison remain separate.

Finally, the clone probe observes source amount/interval **7/3 becoming 1/1** on the initialized pasted bill. PB writes clone settings under temporary `Bill_CookMealSimple_-1`, while the real-ID bill gets defaults. This matches the statically identified upstream lifecycle defect, but the actual PB-only comparison remains outstanding. The passing characterization assertion means the defect was reproduced; **nothing repaired cloning in this run**.

## Corrected saved ownership

**Accept successor `0e63f30388344a16ad54559991bdca34` for native production and corrected checkpoint ownership; actual reload remains unproven.** Owned PID 20692 joined with exit 0 and no deadline/error. All 67 assertions pass and have matching raw assertion events; the complete stream has 126 contiguous events, with zero captured Unity errors. Evidence/source/build/outcome/Verify and four new saves are retained in `native/0e63f30388344a16ad54559991bdca34/`. The original warning-bearing run remains qualified above.

In each new XML, the original references `Thing_Human315`, `Thing_Human318` and `Thing_Human321` each resolve to exactly one deep Human node under `game/world/worldPawns/pawnsAlive/li` and one force-kept membership. They are no longer merely unscribed objects referenced by `startingAndOptionalPawns`. Actual cleanup returns without a failure assertion after its guarded removal of world membership and same-object placement restoration. This establishes the specific ownership correction, not complete rollback of incidental pawn state.

Eight HD gather jobs again hand off to eight matching native DoBill jobs: simple work records 366 recipe-work ticks each, bulk 1,461 each. Exact settled accounting remains 50→10 corn/four meals and 200→40 corn/sixteen meals before cleanup. PB advances once per actual iteration, waits through 120 ordinary cooldown ticks, rejects suspended work, then resets at the explicitly queried completion-plus-60,000 boundaries (61,202 and 125,897). Both second cycles finish; the time-jump limitation remains unchanged.

The saved map 0, actor Human37260, stove FueledStove37263 and actual bill/PB keys agree with checkpoint events. HD batch keys/values are empty. Target PB amount is 2; interval 1 and initial completion −1 use native omitted defaults.

| New checkpoint | Tick | Map corn / meals | PB produced / completion |
| --- | ---: | ---: | --- |
| Simple partial | 578 | 40 / 1 | 1 / −1 |
| Simple cooldown | 1,204 | 30 / 2 | 2 / 1,202 |
| Bulk partial | 64,179 | 160 / 4 | 1 / −1 |
| Bulk cooldown | 65,898 | 120 / 8 | 2 / 65,897 |

The full 1,868-line log scan finds no missing-deep-save warnings or corresponding production/save exception. Retained notices include two early Mono fallback requests, reduced mipmaps for Header.png, a force-ended geyser sound, profiler/allocator output and the previously observed debug stale-tag pruning. None supplies a new payment or ownership failure. Verify retains its explicit manual/log review notices and empty protected changes; its absent-PID lookup is consistent with the owned joined process. These new checkpoints are suitable inputs for the separately planned real restart review, not proof it passed. The clone still reproduces **7/3→1/1**; PB-only comparison and repair remain outstanding.
