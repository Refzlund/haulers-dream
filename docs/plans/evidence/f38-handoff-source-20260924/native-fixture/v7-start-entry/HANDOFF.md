# F38 v7: capture admitted job identity before instant execution

Ready for root review and one fresh restart from the already accepted **c19dba9d9f0b4b79a1710bec0b73f725** checkpoint. No new baseline or producer is needed. No Prepare, native launch, product change, save alteration, or controller relaxation was performed here.

The previous restart **251d5679d9a64274b0cf953c44c121f0** remains failed (40/42, zero captured Unity errors). Its complete 136-event sequence is retained. At tick 1808, event 129 records original empty queued bulk job **17** ending `Succeeded`; event 130 records replacement `Wait_MaintainPosture` **63**. The existing `Pawn_JobTracker.StartJob` **postfix** then tries to serialize the already pooled job and throws at `JobState` (`def` is null). This is an observer failure, not successful completion of the remaining physical/restart checks.

Native source establishes the ordering:

- `f07-rimmsqol-20260920/blocked-queue-v13/native-source/Pawn_JobTracker.cs.txt:334–371`: after successful pre-toil reservations and opportunistic admission, `StartJob` calls `Notify_Starting`, `SetupToils`, and `ReadyForNextToil` before returning.
- `f41-unfinished-review-20260924/Verse.AI.JobDriver.cs.txt:469–550`: native instant toils execute recursively; exhausting toils invokes `EndJobWith(Succeeded)`. The base `Notify_Starting` at line 630 only sets its start tick.
- The tracker at lines 419–521 cleans and pools completed jobs, then can start the one-tick posture job. Retained `v6-pooled-job-identity/Verse.JobMaker.native.txt` calls `job.Clear()` before returning the job to its pool; `f07-rimmsqol-20260920/queued-save-v14/Verse.AI.Job.cs.txt:201` clears `def`.

The complete fixture diff is `SweepHandoff.cs.diff`. Only the start observer changes: a prefix on actual `JobDriver.Notify_Starting` verifies that this driver owns the pawn's admitted current job and immediately captures its integer ID, textual plan and driver type. It emits `boundary=Notify_Starting-prefix`. It retains no job reference for later inspection. `fromQueue` is no longer reported because that is an argument of the earlier tracker request, not this boundary. The native end observer and every load, continuation, queue, cargo, keep, conservation, delivery and 300-tick stability oracle are unchanged. No nullable fallback conceals a lost identity.

`before/` preserves the exact v6b scene, build script, selection and controller. Existing failed raw evidence and original host remain unchanged. The new host builds into a distinct directory; build completed in 9.74 seconds with zero warnings/errors.

Current selection `../selection.json`:

- Selection SHA-256: `1E8B27BE75930157D719F88249BFE6A9BD628BE48BDA3487972E5D9515EE9E8E`.
- Host: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f38-native-20260924/HarnessBuild-v7/Assemblies/HaulersDream.RuntimeHarness.dll`.
- Host SHA-256: `CCF3C0E99AE9D0E3D7F4FA5A6E28CC543F1C0BFB3897CDF75375796F335A0EA4`.
- Host MVID: `8ca4c036-40be-4a8c-988a-d5a00bbdb62c`; version `0.1.0.0`.

The explicit `acceptedProducer` selection entry pins the original save, record, manifest, passing result, whole log, independent review, and all **ten actual observed producer images**. `audit.json` confirms every original observed image matches its producer manifest and the **nine non-host binaries remain identical** to the newly selected sources. It also verifies 318 unchanged content/provider/host/controller pins and the three compiled source pins. Native PS5 reflection-only host metadata is retained separately.

Use only this original source pair:

- `C:/Users/Arthur/AppData/Local/HaulersDreamQA/runtime-temp/haulersdream-runtime-tests/c19dba9d9f0b4b79a1710bec0b73f725/SaveData/Saves/F38-SweepHandoff.rws`, SHA-256 `B65EFC6ECE626502785893516A13969395D1345FB23539AB7131A0D762466A2F`.
- Same run's `evidence/checkpoint.txt`, SHA-256 `406E693B5BC4B02136A2DF0FA68A33A348D35C8098D40C0550D5779F853D2CA3`.

The existing controller already verifies the joined native producer, passing result, original record/save hash, and exact copied checkpoint hash; it never required equal old/new host identities. Root must compare the prepared source pair to `acceptedProducer` and verify nine unchanged images plus the exact reviewed v7 host before Launch. This is an explicit one-checkpoint review binding, not a general compatibility exemption. Full fresh restart acceptance remains outstanding.
