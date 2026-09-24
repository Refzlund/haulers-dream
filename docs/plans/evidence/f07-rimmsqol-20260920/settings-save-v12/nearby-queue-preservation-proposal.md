# Proposed follow-up: preserve identified nearby commands on real saves

Decision proposal only. No product edit or native execution was performed. Keep this separate from the accepted v12 SettingsBlock/savegame correction and from the future F12 source/destination-order implementation.

The exact minimal functional change is to retain the existing cleanup gates and add the established nearby-command identity exception to its current predicate:

```csharp
queue.RemoveAll(pawn, job => job != null && job.def != null && strip.Contains(job.def)
    && !NearbyHaulCommand.IsIdentifiedOrder(job));
```

Within `StripSet`, this preserves only identified forced `HaulersDream_BulkHaul` and `HaulersDream_NearbyDelivery` instances carrying the dedicated WorkGiver. Vanilla HaulToCell/HaulToContainer fallbacks are already outside the custom-driver strip set. Unidentified bulk jobs and other HD jobs retain existing cleanup. Current jobs remain untouched. Update this class's comments to acknowledge preserving explicit nearby intent and that removing other queued jobs releases reservations/cancels work; do not continue calling the operation side-effect-free. The uninstall trade-off must be stated accurately: preserved explicit queued jobs require HD to remain installed, as existing current HD jobs already do.

Use `IsIdentifiedOrder`, not the broad `playerForced` bit and not `CanContinue` during serialization. Permission, map and manifest can be revalidated by the existing native admission hook when the job starts. Saving should not itself become an additional permission/cancellation action. The current predicate does not recognize F12's future dedicated job, so this change does not claim to implement durable point/shelf orders.

Actual native Job serialization already includes `globalTarget`, `targetQueueA/B`, `countQueue`, `playerForced` and `workGiverDef`; Pawn_JobTracker serializes its job queue. A queued nearby sweep carries its plan in Job fields, and a queued scoped delivery carries its manifest there. A fresh driver is expected after queued admission, so tests must validate saved job fields and actual consumption, not a cached driver reference.

## Minimal native acceptance

Reuse the accepted F07 real RIMMS/configuration host and inactive-desktop producer/restart machinery. Keep cleanupOnSave enabled and use the real `GameDataSaveLoader.SaveGame`, never a direct cleanup call. Compare a baseline without the exception to the candidate, preserving the baseline as failed.

The core producer starts a real non-idle Goto and issues the actual nearby command with queue=true while drafted permission is allowed. Save while Goto remains current. Record current identity/target, exact queued command identity/order/targets/counts/WorkGiver/map, floor cargo and keep7 both before and after saving, plus the actual serialized queue. Candidate must preserve the current Goto and the queued command; baseline should show its deletion. An unrelated queued native command may be included to verify ordering without inventing HD jobs.

A fresh process loads that exact saved game and retained RIMMS config. Before fixture mutation, require the same current/queued identities and saved quantities. Let the predecessor finish naturally, observe actual queued admission, then require productive nearby pickup and scoped delivery, exact conservation and keep7. Do not manually restore queue, permission, job fields or cargo.

For complete coverage of the second exempted definition, retain one actual `NearbyDelivery` while it is queued after a successful drafted pickup, before native admission. A native StartJob observer can pause at the ordinary intervening Wait_MaintainPosture once the generated delivery is already queued; save only from the subsequent settled host update, not inside a driver/finish-action callback. No delivery job or manifest may be manufactured. Its fresh-process continuation must retain the saved manifest and deliver exactly that command's cargo. This can share one producer/save with a second actor holding the queued bulk command, giving one bound restart for both definitions; if the natural queue window is not observed, retain a failed precondition rather than weakening it to an in-flight-only test.

The existing v12 RIMMS-permission-revocation scene remains the companion proof that preserved queued commands still undergo live native CanBeginNow checks. Unidentified automatic HD queue cleanup remains source-identical; include one real naturally generated automatic queued unload in the save census if readily available, without adding a synthetic productive job or a broad lifecycle matrix.

This narrowly repairs saving of the existing nearby command. F12 still requires its own durable source/destination intent, multi-trip accounting, cancellation and saved suspended-order lifecycle.
