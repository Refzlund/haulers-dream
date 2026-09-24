# F07 v13: native blocked-queue oracle

The unchanged v11 host was used in v12 native run `4728a42fbd8c454387898dc1aceae8de`. Actual settings saving now retains identified Bulk job58 behind native Goto56 (events159–161, tick2417). Goto56 succeeds naturally at tick3468, and actual CanBeginNow rejects job58 twice (163–164). Native Wait_Combat69 then starts while job58 remains queued (165). The overall run remains failed because the fixture incorrectly required deletion (166). Its raw events and exact source are preserved here; the original host and prior evidence remain untouched.

Actual `ThinkNode_QueuedJob.TryIssueJobPackage` only discards blocked leading entries when the pawn is downed or some queued job can begin. A healthy pawn with a single blocked order retains it and receives NoJob from that node. `JobQueue.AnyCanBeginNow` only queries and never removes entries. Actual decompiles of those classes and Pawn_JobTracker are retained in `native-source`; their trailing ILSpy update notices are tool output, not native source.

The fixture-only diff now requires the exact pending job reference to survive settings saving and native admission rejection, with no start and unchanged physical cargo identities, counts, custody, ground positions, carry identity and keep quantity. The unrelated Goto must still complete successfully without a forced handoff. This strengthens the settings-save check while correcting the native queue expectation.

Before the next scene, the fixture pauses, restores permission through the actual RIMMS editor, then invokes the next ordinary offered nonqueued command. Native TryTakeOrderedJob clears the blocked queue as part of this explicit replacement. A new assertion requires a distinct command ID, no old-order start and no surviving old-order queue entry. The fixture never calls ClearQueuedJobs, Dequeue or RemoveAll to manufacture that transition. Interruption, saved current generated delivery and bound restart assertions are unchanged and remain native obligations.

## Selection

Fresh host compile passed once, zero warnings/errors, 5.11 seconds. Host: `%TEMP%/hd-f07-20260920/build-v13/Assemblies/HaulersDream.RuntimeHarness.dll`.

- SHA-256: `9A710D4F57FDDD7C944139B873C4A8998E2F62AD7CDE32B923DEF9C2BDD428AA`
- MVID: `033dd8f0-5f71-4653-92dc-8440c08f41e8`
- Scenario SHA-256: `8F8A463DF91089589CB5A25062704BC26FAF1B98E933E3CCA597693BCBD93FA6`

`selection.json` retains the v12 product/controller/launcher and selects only this new host. Build references use the unchanged v12 product (`f07-settings-save-v12-20260924/Product`); no new product was built. `build-inputs.json` and `unchanged-inputs.json` verify all 80 host/project files, 99 product files, 21 controller files, launcher, prior v11 host and selected v13 scenario remained unchanged during compilation. Reflection-only metadata extraction passed in a joined native PS5.1 worker. Both build and metadata commands exited0; no outstanding child/process handle.

Root owns review, Prepare, inactive-desktop launch, join, evidence copy, Verify and producer/restart acceptance. No such runtime actions, product edits, ledger edits or resolution claims were performed by this author. True-game-save preservation of queued Bulk/generatedDelivery remains a separately proposed correction; this fixture still reaches a current delivery save and does not cover those pending-queue saves.
