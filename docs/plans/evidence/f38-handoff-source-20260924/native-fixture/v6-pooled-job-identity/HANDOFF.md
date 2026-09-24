# F38 native job-pool identity correction

The v5 baseline failure `333f2dd7f3944b14884dfdce4d6ffad1` is a fixture oracle defect. Native reservation displacement worked. No product defect or handoff acceptance is inferred from this pre-handoff control run.

Actual retained events establish the cause:

- Event 45: D (`Human37872`) starts bulk job **14**.
- Event 46: B starts forced Wait job **15**.
- Event 47: D's bulk **14** ends `InterruptForced`.
- Event 48: D immediately starts `GotoWander` **16** at the same paused tick 3.
- The failing assertion records `Reserve=True`. Its saved-ID end-condition term is also true by event 47. Therefore its only false term is `D.CurJob != old`: the old `Job` object was reused for order 16.

Installed `ReservationManager.Reserve` invokes `EndCurrentOrQueuedJob` with native pooling enabled. `Pawn_JobTracker.CleanupCurrentJob` clears reservations, removes the current job and returns it to the pool; `EndCurrentJob` then requests replacement work. The retained exact-image `Verse.JobMaker.native.txt` shows `SimplePool<Job>.Get()` assigning a new unique loadID. The initial lookup of the incorrect `Verse.AI.JobMaker` namespace failed; only the successful `Verse.JobMaker` decompilation is evidence.

Correction is confined to the fixture:

1. Non-haul displacement checks that the captured original ID is absent from all current/queued jobs, its native end was `InterruptForced`, the Wait job owns the target, and D no longer reserves it. Diagnostics show retained object's current ID, reference reuse, current job and reservations.
2. Direct-cancellation and baseline whole-job-loss controls use the same immutable-ID/end-condition approach and target ownership checks.
3. Candidate current/future handoff preservation and pre-handoff admission require both the original reference and original captured ID. Queued preservation already checked captured IDs. Toil order, reservations, product behavior, physical counts and all native arguments remain unchanged.

`before/` preserves v5 scene/selection/build/freeze. The failed raw evidence and both earlier prepared products are untouched. `first-build/` retains the initial v6 source/pins; final v6b additionally applies the same positive-ID guard at handoff entry and future-source preservation. Neither build has been prepared or run.

Final host **v6b**:

- SHA-256 `FA3BB6C344AD70A9396C040811B95AD1E550C4840A6D975C9C77CF7D1091F3D6`.
- Build: 0 warnings, 0 errors, 2.73 seconds.
- `audit.json`: 329 passed evidence/input/build-source checks, zero failed. Product, providers, controller, bootstrap and remaining host sources exactly match v5.
- Root `native-fixture/selection.json` now selects v6b; this folder also retains the same selection and exact scene. `SweepHandoff.cs.diff` and `build.ps1.diff` show the complete change.

Root review and fresh Prepare/native runs remain required. Do not launch old prepared candidate `85017...`; it contains the known false oracle. Continue the same baseline → candidate → bound restart chain and the same inactive-desktop/protected TEMP/TMP discipline. This change does not claim that later handoff or physical/save witnesses pass.
