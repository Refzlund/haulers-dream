# Robot lifecycle v5: actual recall and cleanup bindings

24 September 2026. **Built and pinned; no v5 Prepare, native launch or product edit.** Exact v4 scene, build, controller, selection and handoff remain in `v4-before-correction`; failed native run `b8128fc9e1d8407e8fcd3958163d6ee9` remains unchanged. Root owns independent review and execution.

The actual v4 OLD-SAVE callback returned with the same pawn already unspawned, inventory7 and no job. The fixture wrongly required an active job. Source inspection additionally found that it expected a giver class name as the JobDef and observed an end method bypassed by native StopAll.

`v4-to-v5.diff` contains all three narrowly evidenced scene corrections:

1. OLD-SAVE alone admits synchronous containment when the pawn was at the station before its actual gizmo callback. It requires the same deep-contained pawn, station.robot cleared, no current/queued job, empty hands, and unchanged exact inventory stack IDs/counts. The next existing `native-contained` assertion still checks total, storage, inventory and disabled automatic respawn before the actual save. There is no fabricated return job or movement.
2. Active return requires the real `AIRobot_GoDespawn` JobDef, that exact station as target A, and an observed actual native start. The current partial-cargo producer must take this active branch; immediate acceptance is restricted to OLD-SAVE. Its existing tagged cargo, Keep7 and actual BulkHaul interruption checks remain unchanged.
3. The ending observer now prefixes native `CleanupCurrentJob` instead of `EndCurrentJob`, capturing actual direct StopAll interruption as well as ordinary job cleanup. It only records state; it does not call cleanup or change the condition.

Native binding evidence is retained beside the prior station source:

- `../AIRobot.X2_JobGiver_Return2BaseDespawn.cs.txt`: the giver returns `new Job(DefDatabase<JobDef>.GetNamed("AIRobot_GoDespawn", true), station)`.
- Actual provider XML `MiscRobots/1.6/Defs/JobDefs/AIRobot_JobDefs.xml` associates `AIRobot_GoDespawn` with `AIRobot.X2_JobDriver_GoDespawning`.
- `../AIRobot.X2_JobDriver_GoDespawning.cs.txt`: `MakeNewToils` yields `GotoThing(TargetA.Cell, Map, PathEndMode.OnCell)` and then `DespawnIntoContainer`. Its latter instant init calls `rechargeStation.AddRobotToContainer(actor)`. Native same-cell path completion can therefore finish containment before the outer StartJob callback returns.
- `../Station.cs.txt:933`: station spawn supplies `base.Position`. Lines1005–1032 call the return giver, then real `robot.jobs.StopAll()` and `StartJob(thinkResult.Job)`. Lines275–299 show AddRobotToContainer stopping jobs/pathing, adding that exact pawn and despawning it while clearing station.robot.
- Native `f07-rimmsqol-20260920/blocked-queue-v13/native-source/Pawn_JobTracker.cs.txt:593` implements StopAll as `CleanupCurrentJob(JobCondition.InterruptForced, releaseReservations: true, cancelBusyStancesSoft: true, canReturnToPool)` followed by ClearQueuedJobs; it does not call EndCurrentJob. The private cleanup begins at line475 and clears the actual current job/driver after its cleanup, reservation and finalizer handling.

An initially guessed `X2_JobDriver_GoDespawn` decompile failed; its empty/tool-notice file is retained. The corrected `GoDespawning` type comes from the actual provider JobDef XML. No claim depends on the failed lookup.

| Changed input | Final identity |
| --- | --- |
| Host | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/builds/f05-f06-lifecycle-20260924/build-v5/Assemblies/HaulersDream.RuntimeHarness.dll` |
| Host SHA256 / MVID | `4668E81EF8164C321A3AAEDF94B34251B71F33DD5EF52D84B75B9C0253E301D4` / `4e49276f-63db-455d-ab6b-0a9d7ff89bfe` |
| Scene SHA256 | `6091DB62C974B39F2529B154855C9F76615BD5B217831A06F4F7564C64AA8BC7` |
| Controller SHA256 | `FF21BA908B4D496E9DBE76A87F408B0416234A5FBE36B9E88B6160F94DD875CE` |
| Selection SHA256 | `FFF2422AE75DE15D7005D796983446CDF6F35D384601E58F3360EDA41A22E194` |

Build succeeded in 6.40 seconds with zero warnings/errors. Bounded metadata read passed and joined with exit0; `metadata-process.json` records ownership. Controller parses with zero errors. All 80 host source files remain unchanged; the only controller changes are two exact host SHA pins. Product, provider, game and payload pins inherit the independently audited v4 selection unchanged. `pin-receipt.json` and `selected-inputs.json` record the updated bindings; do not rerun the one-time `select-v5.py` writer.

Use the same four-case execution sequence, product roots, private TEMP/TMP and explicit player root from the original handoff, replacing only the host with build-v5. OLD-SAVE must still produce a real published checkpoint with zero native HD components and no HD fields, then UPGRADE loads that save, performs actual partial pickup/recall and creates its checkpoint; RESTART loads it and recovers surplus; BIOTECH exercises the actual native menu. The existing checkpoint and private-runtime guards remain. All graphics must use the inactive-desktop runner. Existing failed v4 evidence is a fixture diagnosis, not a product failure or a completed old-save witness.
