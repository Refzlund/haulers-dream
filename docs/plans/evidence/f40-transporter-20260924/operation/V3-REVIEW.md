# F40 operation v3 — reviewed corrections and current pins

This supersedes the final pins and queued-save status in `HANDOFF.md`; all v1/v2 sources, receipts and diffs remain preserved. v3 adds one root-authored save-hook integration and corrects the drafted recovery finding from root's independent review. No native run/deployment/Prepare/commit occurred.

**Draft boundary:** actual `Pawn_DraftController.Drafted` first updates `draftedInt`, clears the queue, then ends the old job. Actual `Pawn_JobTracker.CleanupCurrentJob` runs driver cleanup before applying `carryThingAfterJob`. Both unload JobDefs leave that flag false. `BulkUnloadRecovery` therefore now refuses to construct a hands haul while drafted, just as its inventory branch already did. Recorded inventory remains tagged; native cleanup may drop the actual hands stack near the pawn, preserving world cargo for ordinary later hauling. A failed native drop remains physically held. Neither case is recorded as storage delivery or grants another trip. This deliberately does not move overflow into an over-capacity backpack, invent a persistent recovery scheduler, or create autonomous drafted work. The actual draft decompilation is retained as `Pawn_DraftController.actual.cs`; this source trace still needs its native acceptance witness.

**Queued save:** root committed F07 as `ee6f84028ebefd7b7d915e9e74aed02fb60628fb` and separately added `!TransporterOperation.IsExplicitOrder(job)` to the working save hook. v3 includes that exact root-authored hunk, with the file's baseline taken from that F07 commit. It preserves explicit queued transporter load/unload orders while current jobs continue to be left intact by the existing hook. No live admission test runs while saving.

**Validation:** full frozen Release build succeeded with zero warnings/errors in 25.87s; PID2512 joined exit0, deployment guard absent. F40 pure progress/state/planner source still matches the earlier 33/33 passing test run. `capture-v3.py` checked every one of the changed Source files against the final v3 frozen build input. Storage guard and XML validation remain valid; those sites did not change after their recorded checks. No native acceptance is inferred.

Current review inputs:

* `changed-files-v3.json`: **22** exact files, including root's save-hook hunk.
* `after-v3/`: frozen full contents of each changed file.
* `implementation-v3.diff`: focused slice, SHA256 **2B6C6E362958BDEA4906EDE283F19374C9B9E79C2E76B601F7DE2D113DC597FD**.
* `final-v3/build-inputs.json`, `build.log`, `build-process.json`: complete frozen product build provenance.
* Build root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f40-operation-v3-20260924`.
* HaulersDream.dll SHA256 **194F137EF89D3D18759555988DAE34261EDB364EF8B859530AC24447AD7C9A87**.
* HaulersDream.Core.dll SHA256 **07F1239DABFBE8747F6E7941F3D4DAC7A5668E80719192696B3144DC1C4400CA**.

Root granted the concrete next UI/MP/settings/16-locale slice listed in HANDOFF. That work will use separate evidence/pins and preserve this reviewable v3 implementation. F40 remains open for that integration and all original native acceptance obligations.
