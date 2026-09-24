# F40 UI, synchronized commands and localization handoff

Source implementation is ready for independent review and native fixture selection. **F40 is still open** until actual runtime acceptance passes. This slice completes the pending UI/MP/settings/localization work after `foundation/HANDOFF.md` and `operation/V3-REVIEW.md`; it does not replace their lifetime, delivery or recovery requirements. No native run, Prepare, deployment, commit or public action was performed here.

## Behavior and integration

- An always-installed transporter gizmo offers the saved bulk-unload toggle, inserts it before the first loading command, and disables every loading command when any current group member has an active unload flag. The checked state reads raw intent, so an existing flag can still be switched off while the feature is disabled. Unsupported/inactive unflagged targets do not receive an enabled switch. On-state admission and empty-cargo checks are fresh; turning off remains possible after departure.
- The unload provider offers a flag-gated prioritized order and a specific disabled explanation for drafting, no storage, loading conflicts or unavailable MP synchronization. It creates no jobs while constructing the menu.
- `TransporterCommand` synchronizes toggle, ordered loading and ordered unloading. Registration is independent and fail closed for active MP sessions. Commands validate map identity and live state on execution. Load execution skips the menu memo and rebuilds its actual plan. The continuous transporter entry sends an explicit `continuous` argument and rechecks the setting and draft state on execution. Ordinary drafted loading retains its existing single visit; unloading and subsequent trips require an undrafted pawn. Portal/VF dispatch branches are preserved.
- `TransportLoad` now has a read-only ordered menu mode using `LoadAvailableForMenu`; it reaches neither ledger refresh nor JobMaker. The shared selection algorithm remains the source of the offer. The existing per-tick boolean menu memo is ephemeral and does not grant execution authority. The separate purity witness traces both early and normal job-building branches.
- The settings overview and Bulk Loading page expose the feature. Existing free-capacity and unload-delay sliders stay enabled if either carrier or transporter unloading is enabled; the carrier-only reservation setting remains carrier-only. These changes do not touch the root-owned F24 work checkpoint.
- All16 languages receive the feature, setting, toggle, refusal messages, native job report and WorkGiver labels. Continuous-loading wording now requires successful delivery, an undrafted courier and no queued replacement work for further trips. Ordinary loading text describes its selected-manifest continuation and drafted single visit. Unrelated keyed values were checked against the before snapshot and preserved.
- The contributor's exact icon is copied with a sidecar credit. The changeset credits **nullpat / GH267**. There is no blanket proposal-file replacement or cherry-pick.
- Final admission audit found a forced unloading race: the pawn could enter a mental state between the menu and command execution, and native `TryTakeOrderedJob` has no explicit mental-state guard. One targeted addition to `ActorAndTargetBlock` rejects `InMentalState` and a missing job tracker. It also protects later transfers. Root was informed before the correction; the native mental-state race is an acceptance requirement.

## Exact selection

`changed-files.json` lists **63 files** with before/after SHA256 values. `before/` and `after-v3/` retain exact blobs; `implementation.diff` is the textual incremental UI diff. It is based on the current reviewed foundation/operation slice, not the Git base. Select earlier F40 foundation/operation changes too when composing the whole feature.

Source files in this incremental slice:

1. `Source/HaulersDream/TransporterCommand.cs` — new local probes and synchronized mutation handlers.
2. `Source/HaulersDream/FloatMenuOptionProvider_BulkUnloadTransporter.cs` — new unload provider.
3. `Source/HaulersDream/Patch_CompTransporter_Gizmos_BulkUnloadAll.cs` — new always-installed toggle patch.
4. `Source/HaulersDream/HaulersDreamGameComponent.LoadProbe.cs` — new read-only projection, no tick/ExposeData/FinalizeInit change.
5. `Source/HaulersDream/TransportLoad.cs` — menu-only planner branch and existing shared planner wrapper.
6. `Source/HaulersDream/FloatMenuOptionProvider_BulkLoadTransporter.cs` — pure offer and synchronized dispatch.
7. `Source/HaulersDream/FloatMenuOptionProvider_ContinuousLoad.cs` — transporter-only synchronized branch and corrected comments.
8. `Source/HaulersDream/MultiplayerCompat.cs` — separate registration/readiness for the two transporter handlers; other handlers preserved.
9. `Source/HaulersDream/HaulersDreamSettings.Window.cs` — feature card, checkbox and two shared slider gates.
10. `Source/HaulersDream/Patch_SuppressVanillaLoadFloatMenu.cs` — obsolete purity explanation corrected; behavior unchanged.
11. `Source/HaulersDream/BulkUnloadTransporterGate.cs` — final mental-state/null-job-tracker admission check only.
12. `Source/HaulersDream.Tests/LoadMenuProjectionTests.cs` — two focused production projection tests.

The other51 files are 16 keyed files, 16 existing JobDef translation files, 16 new WorkGiverDef translation files, the icon and its credit, and `.changeset/bulk-unload-transporters.md`. `SettingsUI.cs` was inspected/snapshotted but needed no edit. Earlier shared DefOf/Scribe/storage evidence edits are described in the operation handoff and preserved.

## Validation receipts

Current isolated build: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f40-ui-v3-20260924`. `final-v3/build-inputs.json` pins **all** compiled inputs, including unrelated shared working-tree changes. Every F40 source file in this incremental snapshot matches that frozen build. This output is a compilation witness; the root must still compose and pin the independently reviewed native source/content selection.

- Release build: exit0, **0 warnings / 0 errors**,28.72 seconds. Process24828 joined; deployment guard absent.
- Focused production tests: **57 passed / 0 failed / 0 skipped**,256ms; test process21880 joined, exit0. Includes carrier policy, transporter state/progress, ledger arithmetic and both new projection tests. TRX and full logs retained under `final-v3/`.
- `validation.json`:16 locales,17 relevant keyed entries each, placeholder parity and duplicate checks, job/workgiver translation shape, XML parsing, exact contributor icon bytes, preservation of unrelated keyed values, and source/build identity all pass.
- `storage-guard.log`:256 files scanned,9 allowlisted commit sites across5 reviewed files, pass.
- `final-v1/` retains the failed compile caused by the missing `RimWorld.Planet` using; fixed before v2. `final-v2/` retains the passing pre-mental-state-guard build/test and then-current diff/manifest/validation. No failed evidence was replaced with a success.

Pins:

| Artifact | SHA256 |
| --- | --- |
| `HaulersDream.dll` | `CBFE816FE991B53B97803C04171E98C2D8FF92411D11844F7B7635DE76390F10` |
| `HaulersDream.Core.dll` | `B5763AF1B791E8EEB5484CB28F8CD358F29186CE39A11D7825A9327871E2D050` |
| `implementation.diff` | `8082FDB66CE1108F5A74324A6173E0E740885410A013FBAE96392D0FA9BBE472` |

The reference package remains1.6.4518; compiling against it does not establish behavior in installed1.6.4871. No real MP session or UI rendering is claimed by these checks.

## Next acceptance step

Review this source selection, then build one bounded native fixture using the root-owned runtime slot. Follow `PURITY-WITNESS.md` for actual menu/job-ID/RNG/ledger/cargo/queue equality and synchronized stale-state cases. Retain all operation acceptance cases: two haulers and both cargo paths; ordered multi-trip load and unload; exact kept stock/passengers/conservation; storage filling and no-progress termination; accepted/top-up/rejected/cancelled loads and non-primary flags; runtime feature toggles; cargo ownership changing during the delay; save/restart with queued explicit work and partial delivery; interruption/drafting recovery; CE hands fallback/corpse policy/carrier regression; supported shuttle ownership; and actual multiplayer replay. Finish source/fixture review before any native launch, and do not close F40 on the source receipts alone.
