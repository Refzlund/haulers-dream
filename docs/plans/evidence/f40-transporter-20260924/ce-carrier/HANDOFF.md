# F40 actual CE, carrier lifecycle and native Grave handoff

Ready for independent source review and then root-owned Prepare. **No native run or compatibility acceptance is claimed.** The author also wrote the preceding F40 persistence fixture and cell-replan correction; a separate reader must assess actual results. This fixture changes no product file.

Read `CONTRACT.md`, `src/TransporterPersistence.cs`, the reused climate helper, and `source.diff`. Source is deliberately limited to one producer and one exact-save restart. The producer measures actual CE-limited inventory intake plus hand overflow from a real pack animal, saves at the explicitly instrumented native `EndJobWith(Succeeded)` boundary, returns normally, and requires productive recovery after the newer queued Wait. The restart compares real saved identity/cargo/driver/queue state before its first tick and repeats recovery, then covers actual massless positive-bulk fallback and four corpse-policy branches through real native Grave container delivery. `BulkUnloadPull.Transfer` observation reads containers after the call; it does not change or read synthetic out arguments.

The carrier checkpoint is an exact native lifecycle-boundary save, not evidence of an ordinary GUI save opportunity between synchronous instant toils. It calls real `SaveGame`, saves the current driver at toil4 with its actual visit references and hand tail, compares state before/after, and lets the intercepted method proceed. No serialization fields, IDs, health, capacities, CE results, job outcomes or tick execution are substituted. The native room cache is warmed by ordinary ticks before creating actors. Explicit setup inputs include two fixture-only resource defs, removal of generated human gear, CE ballast after recovery, real animal deaths to obtain corpses, native Grave filters and the four named policy settings.

## Frozen inputs

- Host SHA256: `71479ACBC617135D6C48FE06B5751090E2569CDFBEBDA2ED8578B70970964E6F`; MVID `f3899367-9fc9-4ee1-b75b-d4c44ffb2488`.
- HD: `9739F033B90CE7722CE6A61FC874EAB46F59E6B7950EE757AA7F37C8949A82BC`; Core: `83EB8EB0E348A3FB6B0066640BAA1278C2B1ED6844EA31A774D3AFE18DAD43C6`. These are the accepted cell-replan candidate images; no recompilation or selection of other product changes.
- Actual CE16.7.3.0, package `CETeam.CombatExtended`, privately copied from installed Workshop2890901044: DLL `3102BC2276C583E51FE85AE340E5B986F80452171DB63EA72D04F7651B96AFE3`. All9,738 package files are retained with original/copy hashes in `provider-provenance.json`; no account, workshop or player-config write.
- `selection.json`: `6AB560921927F1E7CE6B9712FF6D4627204D169305BAD84B21F5F05DBDCFEE8E`.
- `source.diff`: `D8F2EF9C48BA122605AFDA2C816AD4D356DF4E70B1B964E047D25686AEA3B052`.
- Final host build v3: joined0,4.54seconds, zero warnings/errors. `input-audit.json` has9,965 passing copy/source/build/pin checks. `controller-parser-audit.json` parses all21 PowerShell files; no controller action ran. Earlier source/build attempts remain under `before` and their external build folders.

## Execution after review

Use the existing private desktop and short TEMP/TMP discipline (`C:/HDQA/runtime-temp`), restoring the caller's variables afterward. Read the exact canonical product and host paths from `selection.json`; do not use redirected aliases or substitute a working-tree build. The controller is `controller/scripts/runtime-test.ps1` and uses the unchanged owned-process `launch.ps1` wrapper.

1. Prepare `-CaseId F40-CE-PRODUCE -ExpectedBehavior satisfied -HdSource Built -BuiltModRoot <selection.candidate.root> -HarnessAssembly <selection.harness.path>` with no RunDirectory supplied. Root launches, joins, copies complete evidence/manifest/process receipts and runs Verify. Review original native checkpoint XML, all pull/start/end/physical events, selected images and entire logs before admitting a consumer.
2. Prepare `F40-CE-RESTART` with the same selected paths and `-CheckpointSave <original producer>/SaveData/Saves/F40-CECarrierCheckpoint.rws -CheckpointRecord <original producer>/evidence/checkpoint.txt`. The controller requires the actual passing producer, zero Unity errors, all11 original images matching current selection and exact save/record hashes. No copied alternate or reconstructed save is admitted.

Only those two cases are admitted by the scene and the explicit CE Prepare guard; inherited labels/validators do not grant another scenario. The five loaded packages are Harmony, Core, CE, HD and this fixture. The complete provider copy, source, original checkpoint and player trees are protected inputs.

Failures require preserving the exact run before diagnosis. In particular, do not waive a missing carrier field, queue-order failure, original-job failure, floor recovery in place of Grave credit, capacity-masked corpse policy, medical/environment guard or physical loss. Passing this pair supplies only its named CE/carrier/corpse/native-container obligations. Shuttle, Vehicle Framework and network behavior remain separate root-owned obligations.
