# F24 v8 native stump lineage handoff

Ready for independent source review and the root-owned Prepare/native run. No native process or Prepare was invoked by this author. Candidate-v5 product bytes, scene pawn/environment inputs, controller, launcher and existing v7 diagnostics are unchanged.

## Why this host changed

Run `177fd659dd214dd5b39702845fb6c04c` genuinely completed phase 0: all 17 cotton targets, 173 fresh cloth stored, normal end-of-work unload 45. All 40 gate observations remained medically eligible. Phase 1 then used actual `GrowerSow` CutPlant 76 and failed exact conservation after the original tree's 22 wood became 25. The v7 observer omitted the native stump returned/queued by PlantCollected. See that run's `independent-runtime-review.md` and reproducible `audit.py`; it remains a failed 47/49 run, with phase 0 passing and no claim that the older v6 stranded result has been diagnosed.

## Small change and unchanged oracles

`fixture-v7-to-v8.diff` contains only WorkCadence observation/accounting and the build-output version change:

- Observe the real `Plant.TrySpawnStump` return. Require the original phase-1 target, same ordinary unforced GrowerSow productive job, exact chopped-stump definition, map/cell, growth and fresh object identity. Record original-tree/stump/job lineage.
- At original PlantCollected return, require the actual stump to be in that same native job's targetQueueA. The observer neither inserts the stump nor changes the queue.
- Include the returned stump in the observer's target/cleanup collection so the existing fresh-placement and completed-work checks count its actual wood and retirement. Require its retirement to belong to the original native job. Exact physical conservation is unchanged; no inferred yield is added to actual production.
- Add a conservative full-stump yield bound, including native rounding/harvest stat and applicable difficulty, to the existing initial capacity guard. It affects only scene admission. Runtime production still comes solely from observed native placement.

Existing ordinary productive work-giver checks, complete-work-before-storage, successful delivery, native sow count, section limits, no emergency-empty substitution, default cadence, finite budget and protected cleanup are unchanged. No product, health, climate or eligibility decision changed. No productive job was assigned and no native resource was manufactured by the new observer.

## Frozen inputs and validation

Build completed successfully in 7.90 seconds with zero warnings/errors. Metadata reader PID 23112 joined with exit 0. PowerShell build script parses without errors. `pin-audit.json` records 1,252 successful checks, including unchanged product/controller/launcher inputs, retained v7 host, exact built v8 sources and preserved copies. The nine previous source/build/selection records are under `v7-before-lineage`; the original host remains in HarnessBuild-v7. The failed run and complete raw/log/Verify evidence are indexed in `retained-v7-native-failure.json`.

| Input | Value |
|---|---|
| Host | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/HarnessBuild-v8/Assemblies/HaulersDream.RuntimeHarness.dll` |
| Host SHA-256 | `55CE39F1897CAC9F25E8A24B4F3B6635654D9C06C2F95AA2BF587D7E31102799` |
| Host MVID | `280a1d02-7671-4712-9795-1633fc7d887f` |
| Scene SHA-256 | `08DFD841F84FA231BFDEFC1666A664A0B25F21EE9E0882C028B28DC571C30C86` |
| selected-inputs.json SHA-256 | `C65E38DD7684B76E786974F82B269C305D1586EDBC6BD0256F39D7E31A5BA955` |
| Product root | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/candidate-v5-Product` |
| HaulersDream SHA-256 | `93A65549BC110B61DB3FE6D89C67B654C9B59AE9281C2FB817E273CA0806E94F` |
| Core SHA-256 | `D8B9A6AC73860480F337E276CD597498CEF1B2A86D1B931B763E0917375D45A1` |

## Bounded next acceptance

Use the existing four-mod F24 case and protected TEMP/TMP discipline, with this selected host and unchanged candidate-v5 product. Phase 1 should now emit two actual `f24-native-stump` and two `f24-native-stump-queued` receipts, corresponding native fresh-yield and completed-work records, and require both original trees plus both native stumps retired before any storage trip. Independent review must follow the actual object IDs and merged stacks, rather than assume a fixed stump yield. A complete candidate still requires all four original phases and full cleanup/log review. If the earlier stranded behavior recurs, retain its new gate observations and diagnose that actual event; do not bypass any gate or replace it with an unproved medical explanation.
