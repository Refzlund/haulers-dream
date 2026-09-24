# F03 H&H failed baseline and compatibility handoff

The actual run `2afd7b3dd62b43cfb3d79a6eb2dea935` demonstrates two integration defects in the configured scene. It does not establish that either defect caused the historical reporter's missing harvest. All failed raw evidence is retained under `native/2afd7b3dd62b43cfb3d79a6eb2dea935/evidence`; the baseline remains failed.

## Observed behavior and cause

The capable human pawn performs a real automatic `GrowerHarvest` job with 10 Cloth explicitly kept in inventory and a separate 7-Cloth floor stack nearby. Hauling is unassigned; Growing is assigned. H&H's public load threshold is configured from 80% to 1%, with its native 180-tick grace unchanged. HD uses DropThenHaul, zero pickup delay and no nearby sweeping. The fixture supplies neither a productive job nor a replacement toil.

At tick 153 the native plant work produces 11 Cloth (`Thing39478`) and canonically places it on the ground. H&H's first placement notification collects those 11 and merges them into the kept inventory stack (`Thing39475`), giving 21 Cloth while the keep pin remains 10. That destroys the original fresh Thing. H&H also patches the wrapper placement overload: its later postfix sees the destroyed original and searches a radius for another same-def stack. It incorrectly attributes the unrelated 7 Cloth (`Thing39476`) as another harvest, collects it and leaves 28 Cloth in the tracked inventory stack. This is visible in the canonical and wrapper placement events, not inferred from the final inventory alone.

Harvest succeeds and HD's queued self-pickup completes. At tick 426 H&H's own automatic unload selects the full tracked stack. Its native drop stores all 28 Cloth by tick 693. The final observation at tick 699 has no inventory Cloth or keep pin and 28 stored Cloth. The physical accounting passes: 17 pre-existing plus 11 produced equals 28. The defect is misattribution and an ignored keep boundary, not item loss or duplication.

The run records 96 contiguous events and 49 assertions: 45 pass, with `f03hh/nearby-stock-isolation`, `f03hh/kept-stock-retained`, `f03hh/only-fresh-output-stored` and the aggregate host assertion failing. Native PID 22488 and controller 13816 joined with exit 0, no deadline or cleanup error. The inactive desktop receipt records no foreground switch and no remaining owned process. Verify reports `protectedChanges: []`; its not-verified result correctly retains the failed assertions and manual/generic-marker/log review requirements.

The complete 1,871-line Player log was inspected. Its two early Mono fallback notices, texture-size warning and Direct3D timing notice do not identify a gameplay exception. There is no runtime exception, failed cleanup or unexpected Unity error; Unity error count is zero. The actual package/image records match the manifest, including the original H&H DLL `525309DB8BBA69999ADFAFF5C32B7F5AE0E1D10EF887D7EED9EBB35256DDCB24`, MVID `0b1acae3-8e8c-4f36-bb80-dd3cd29ef816`.

Raw result SHA-256: `0320C9C3793E0DBA3642B0BCC6B932424606546DF0845BECC514AB6E51DAA5C4`; events: `7D8A894B08DD2B55D1BD6F60F6EAB87E27A7F265572CC904A167050A689B49A0`; Player log: `06B5645BF7CC13C0B748A2DF7C2C182F2C367D63382C65E5A02D232645950C19`.

## Narrow candidate

Only `Source/HaulersDream/HarvestAndHaulCompat.cs` was added as product code. Its SHA-256 is `8211C4A396163B6901B9DC84192B56F9C54E5404D93C212B2B8A5E7662C2F5D1`. The actual provider source, complete unload IL and native ThingOwner count-drop implementation were reviewed; independent review is retained in `compat-source-review.md`.

The candidate gives H&H's placement postfix the exact native canonical out-result from an invocation-local frame, preventing the duplicate wrapper notification from guessing unrelated floor stock. Nested calls and exceptions restore the parent frame. Failed placements and nonspawned direct-to-inventory results are excluded. H&H retains its producer attribution, filters, intake and tracking. A genuine ground merge retains H&H's existing whole-stack policy; this is not a new fresh-only ground-stack policy.

The candidate also replaces the six whole-drop call sites in the recognized H&H unload callback with a guarded adapter. On the actual contained current inventory item, it recalculates HD's live surplus and uses native count-drop for a partial surplus. Native splitting, rollback, callbacks and H&H storage selection, walking, retries and deregistration remain intact. Explicit keeps remain meaningful with HD automatic intake disabled. An absent provider is inert, and an unrecognized unload shape preserves upstream behavior with a warning.

The three CE pre-split call sites deliberately fall through unchanged because their detached item differs from the current inventory item and is not owner-contained. This candidate neither repairs nor tests that upstream CE branch. No claim is made for opposite load order, default long-duration H&H cadence, robots, saves or provider versions with a changed callback shape.

## Build and runtime handoff

The isolated build contains the 461 byte-exact selected F07 inputs plus this one file. `compat-product-inputs.json` records all 462 inputs; they were rehashed unchanged after compilation. The build joined owned PID 22720 with exit 0, zero warnings/errors and no live deployment. The selected clean package contains the original 95 nonassembly runtime files and four newly built DLL/PDB files, recorded in `compat-product-copy.json`.

- Built mod root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f03-hah-compat-20260924/Product`.
- Isolated build root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/builds/f03-hah-compat-20260924`.
- HD DLL: `67BF59CC8F33CF40B4F2E9E77DF5C8985707C1195CD28220C04FE74F64E75998`.
- Core DLL: `8C83A9BEECF64FACE2CEBB17D3E9433F9C3EDB5A3E820D31E4A4BC197A926F49`.
- Build receipt and logs: `compat-build.json`, `compat-build.stdout.log`, `compat-build.stderr.log`; selected package receipt: `compat-selection.json`.

The unchanged existing host can run the paired candidate scene. Acceptance still requires actual productive harvest and H&H unload, storage of exactly 11 fresh Cloth, retention of the kept 10 and unrelated 7, exact conservation, full-log inspection and cleanup/protected-state acceptance. Source/build acceptance is not runtime acceptance. Root owns Prepare, execution, ledger integration and any closure decision.
