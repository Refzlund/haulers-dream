# Independent F24 v7 review

The complete run is **failed**, with 47/49 assertions passing, 266 contiguous events and zero captured Unity errors. Phase 0 genuinely passes its four assertions. Phase 1 exposes a fixture accounting gap; it does not establish product duplication. The old v6 stranded inventory cause remains unproved.

`audit.py` independently checks retained raw bytes against the original private run, the manifest, all ten loaded assembly hashes/MVIDs, all assertions, event sequence/tick ordering, all 40 gate observations, fresh phase-0 production, and the relevant native work boundaries. Its output is `independent-runtime-audit.json`. Actual native PID 13220 and controller 11164 joined with exit 0. Retained desktop receipts report no desktop switch or controller error. Verify completed with the protected TEMP/TMP root and `protectedChanges: []`; its failed-result, manual-review, missing generic scenario event and whole-log flags are preserved. `verify-console.txt` preserves an earlier successful Verify invocation whose external PowerShell output was formatted as text; `verify.json` is the subsequently serialized structured Verify result.

## Actual timeline

- Tick 6: generated pawn 10318 admitted with harvest yield 1.0283 and measured headroom 34.2. Seventeen mature cotton plants are seeded. Five existing wood stacks are parked with original identities/counts/positions preserved.
- Ticks 9–2490: ordinary, unforced `GrowerHarvest` jobs 12, 29 and 39 complete all seventeen targets in native sections of 8, 6 and 3. Seventeen separately observed native yield placements total 173 cloth. Job 16 performs actual SelfPickup between sections, initially gathering 81 cloth. No unload starts while productive targets remain.
- Tick 2493: the emergency work scan is empty without substitution; the following ordinary scan is also empty and returns unload 45. Native en-route BulkHaul jobs 46/49/52/54/56 gather the remaining five ground stacks. This explains the continuing inventory growth; no fixture job is injected.
- Ticks 2751–3082: queued unload 45 starts and succeeds, delivering all 173 cloth as storage stacks 10338:75, 10346:75 and 10352:23. Pending yield references left by the en-route pickups are later consumed by native SelfPickup 65, which succeeds at 4020 without moving stored output. Phase 0 completes at 4020 with empty hands/inventory/pending list, all output stored, and all four assertions passing.
- Tick 4020: phase 1 seeds yielding but immature poplars 10357/10358 at growth .75. Their observed life stage is Growing; both are harvestable sow blockers.
- Tick 4023: ordinary `GrowerSow` actually selects unforced CutPlant 76 on tree 10358. This validates the previous mature-tree fixture correction without relaxing the giver oracle.
- Tick 4542: native tree work exceeds its 800-work requirement. Native placement produces 22 wood, merged as ground stack 10359. `PlantCollected(Cut)` retires the original tree.
- Tick 4578: the same CutPlant 76 succeeds. Its pending pickup reference now points to 10359:25. Tick 4579 correctly fails strict conservation because the v7 observer has recorded only 22. Phase 1 is incomplete; phases 2/3 have not run. Cleanup restores all five parked wood stacks exactly and leaves the holder empty.

## Gate findings and source diagnosis

All 40 emitted observations show medical rest, bed, resting-patient and drafted gates false; no mental state/Lord/duty; eligible/candidate true; hauling enabled; bleed rate zero. Temperature ranges 10.7–12.6 C. The actor has Frail, two HearingLoss entries and a nonbleeding torso Scratch, but none activates the measured gates. There is no evidence here to justify medical, environment or product gate changes. This differently generated run cannot diagnose the earlier v6 failure retrospectively.

The exact native game assembly (`5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`) supplies the missing path:

1. `Plant.PlantCollected` calls `TrySpawnStump`, destroys the original tree, and appends the returned chopped stump to the same player's CutPlant `targetQueueA`.
2. `TrySpawnStump` creates the configured `ChoppedStump` and copies the tree's growth. Retained Core XML gives StumpBase a WoodLog yield of 4 and work requirement 180. Native `YieldNow` scales yield by growth, hitpoints and applicable difficulty, then rounds; JobDriver_PlantWork may additionally scale by the actor's harvest stat.
3. `JobDriver_PlantWork` loops the native queue, works each plant/stump, places fresh output, and calls `PlantCollected`. Thus an additional small wood yield during the same job is expected.
4. The v7 `Placing` and `Collecting` observers only recognize the original target list. They miss the native descendant stump entirely. The observed 22→25 increase is consistent with this exact path. V7 did not record the actual stump ID, so it cannot independently bind the three extra units to that object; the next host must observe the native return and queue admission to establish that binding directly.

Recommended and root-authorized next action: a host-only postfix records the actual returned stump, binds it to its original target/job, verifies native queue admission, and includes its observed productive output and retirement in the same strict accounting. Preserve giver, work-completion, no-early-storage and exact-conservation oracles. Include the stump's conservative yield bound in the initial capacity check. Do not assign productive jobs, alter health/environment, synthesize wood, infer output from balance, or declare this complete run passed.

## Whole log

Reviewed startup through terminal/shutdown, including the native debug log. Player.log retains two dynamic Mono fallback-library messages, Direct3D refresh/vsync timing notices, Header.png mipmap notice, profiler timing tables and allocator statistics. There are no runtime exception stacks, cleanup errors or additional game warnings attributable to this scene. The explicit harness terminal remains failed and is not hidden. The log enumerates installed Workshop entries during discovery, while the actual active mod list and all loaded images are the four private fixture mods; discovery is not proof those other mods were active. Installed version text is rev590, running game reports rev591; the exact game image hash is bound in the manifest.
