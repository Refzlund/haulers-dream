# F07 queued colony-save witness

Compiled and ready for independent source review and root's native slot. No Prepare, game launch, product edit, ledger edit or completion claim was performed by this continuation author.

The accepted v13 scenario saves a **current** generated delivery. This separate fixture tests actual colony saving with two **queued** identified orders in one save. It does not repeat the full editor/rendering sequence or replace that evidence.

## Native scenario

The private quicktest creates two wall-separated rooms, two actual drafted colonists, two material-specific stockpiles and 7 kept units per pawn. Work priorities are disabled. HD settings are read and required to be their enabled defaults, including cleanupOnSave; the fixture does not change HD settings. The actual registered RIMMS workGiver edit model and QOLMod.WriteSettings enable drafted use. Each room later receives two native stacks of 5 units.

Actor B receives the real IssueSynced command. After successful native Bulk pickup, the observer waits for native StartJob(Wait_MaintainPosture) with exactly one naturally generated, identified NearbyDelivery still queued, then pauses at that boundary. It neither fabricates delivery nor extends the wait. At a subsequent paused real OnGUI repaint, actor A receives a genuine walking Goto followed by a queued IssueSynced Bulk order. Neither queued order has started. One GameDataSaveLoader.SaveGame then writes both queues.

Producer assertions require unchanged current jobs, both queues, physical thing IDs/counts/positions/owners, and two actual serialized queued Job nodes with playerForced and HaulNearby workGiverDef. The baseline retains an honest failed result when cleanup strips queues; it is not converted into a passing expected failure.

Only a passing candidate producer can become restart input. Existing controller copy/hash/path/source-result/source-log checks bind the save, checkpoint and saved RIMMS config. A fresh process loads actual Autostart. Native GameComponent.LoadedGame captures the saved tick and jobs/queues/cargo **before** PauseOnLoad's one native tick. It only reads managed loaded state and installs observation hooks; no Unity calls, time/job/cargo/settings changes or restoration occur there. The main-thread update then checks the actual private Unity runtime, saved tick+1 and native paused state before allowing productive time. Original queued jobs must be natively admitted and succeed, the Goto must succeed, A must generate one delivery, each room must store 10 units, each pawn must retain 7, and no held cargo or HD job may remain.

F07-RIMMS/F07-RESTART and F07-NearbyTransit remain inherited transport identifiers. The generic inherited controller semantics label does not replace this scope description. Producer cleanup restores parked pawns and RIMMS defaults; restart cleanup stops the loaded actors' jobs. Disposable terrain/Home/Lord duties are explicitly not restored; no OS-input claim is made.

## Exact selection

Use `selection.json`. Same host for both products. Baseline is gate-only v12 with ExpectedBehavior=baseline-gap. Candidate and restart use v14/satisfied. Both are Built/None with the same intact actual RIMMS provider.

- Baseline HD: `82198D99C56155B664AD9F4239B0CF4B0EBF8D64852B445667AF12B9D1AE2B3E`; Core: `F24B0569229718941ECFC83EDC1FFA9C664DD64F8EC299B1E4C34C87795CD040`.
- Candidate HD: `A916DA8B6865BFBC16A0842EF4952834504E7EE5CBBC695145964ABDF17A7327`; Core: `F2F5F00B32EE4325931C612ECA4AB7AA68130C0C6D1C7767B7A6A37E75F2771A`.
- Host: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f07-queued-save-v14-20260924/HarnessBuild/Assemblies/HaulersDream.RuntimeHarness.dll`.
- Host SHA256: `F7BBBED9D1C08BF4424201B49531E08CA64A0706AFAB85C8D9CA5FBA71EC0694`; MVID: `b2769aa9-ee16-45fa-8fd1-d9e015d0eaef`.
- Scenario SHA256: `F8C448873C8DC6AE398FB12F1966B50D9F07E609010A7FAA2ADE5A483C430E6C`.
- Controller SHA256: `A7886088B3F60AE9A1B06A5F3E35D080043A6AFBBC2DDCBE1DDB36CEC1BDFD86`; launcher: `72A124BDA577E354E82F68129AFAD2569553B005448CEBC6362494118F985874`.

Root's parent-folder frozen product evidence records 461 source/build inputs, exactly one changed source, all 99 runtime files accounted for, all 95 nonbinary files unchanged, and clean build. This author did not rebuild or mutate either product.

The prior author fixed a missing RimWorld.Planet import; its 10.75s failed build remains first-build-failed.log. Successful compilation took 4.86s with zero warnings/errors. build-inputs.json and unchanged-inputs.json verify 80 host/project inputs, the scenario, 21 controller inputs and both 99-file products stayed unchanged through compilation/initial handoff review. The only subsequent change within that census is replacing the inherited controller/HANDOFF.md's stale F08 description with this F07 scope; all20 executable controller inputs remain unchanged. fixture.diff compares prior F07 source/host/controller. metadata-v2/host.json is a fresh native PS5.1 reflection-only receipt, worker27428 joined exit0. Earlier metadata worker27492 rejected a forward-slash noncanonical path before assembly reading, joined exit1, and is preserved under metadata/. The second attempt canonicalized only that metadata input path. No outstanding worker remains.

## Root sequence

Set process-local TEMP and TMP to `C:/Users/Arthur/AppData/Local/HaulersDreamQA/runtime-temp` for every Prepare/Launch/Verify. Supply selection.json's explicit playerSaveDataRoot. Prepare one F07-RIMMS baseline with baselineProduct/baseline-gap; use the inactive desktop launcher, join, copy raw evidence and retain Verify. Failure must specifically show the colony save stripping both queued identified orders, not setup or observer failure. Then run the identical host with candidateProduct/satisfied. Only after candidate producer review, Prepare F07-RESTART from its original private save and checkpoint record with that same candidate product/host.

Launch is authorized only inside root's scripts/run-on-test-desktop.py inactive desktop wrapper and this folder's launch.ps1. Do not follow inherited nextAction text by launching on Default. Root owns the sole native slot.

This v14 fixture does not reset RIMMS config on restart, so its existing strict copied-config equality remains appropriate. The separate v13 reset/Verify finding is in v13-restart-verifier-finding.md; do not silently transplant a relaxed hash rule into v14. Product correction/full F07 acceptance remain root decisions after actual outcomes and independent review.
