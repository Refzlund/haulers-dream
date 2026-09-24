# F40 persistence fixture — host v3

Ready for root source review. **Compiled and input-audited only; no Prepare, native launch, staging or product edits.** Root owns the inactive-desktop native slot. All new fixture files are confined to this persistence folder; the separate host build is task-owned input material. Core inputs remain unchanged.

- Host SHA-256: `DBEC63E4272436A886C8DAB323CE49ED96CCA5784487837DBB3FF0670AD76F0E`.
- Selection SHA-256: `DA34D46E013ED2BE2070D480AAFAC92BFC01827A29B0D6CA17B6B9D14A4FEECC`.
- Build v3: 6.41 seconds, zero warnings/errors, joined process receipt. Its `source-before-build.json` binds every host/source input to the current exact bytes. V1/v2 compiler outputs remain retained and were never prepared or run; v2 exact scene/selection/audit/handoff is under `v2-before-start-observer`.
- `input-audit.json`: 355 passing source/product/provider/settings checks. `powershell-parse-audit.json`: all 23 active scripts parse. These are integrity/build results, not behavioral acceptance.
- Product is the unchanged frozen reservation-release input: main `E62F1C1EB45B31031F8AEF705280550D0DF8C3ACEAD6CB48461BCE0D41BDD307`, Core `83EB8EB0E348A3FB6B0066640BAA1278C2B1ED6844EA31A774D3AFE18DAD43C6`. The Core selection snapshot is retained as `product-selection.original.json`.

Read `selection.json` for exact canonical product/host paths. Use `TEMP=TMP=C:/HDQA/runtime-temp` for Prepare, inactive-desktop launch and Verify, with `selection.protectedPlayerRoot`. The short runtime path incorporates the known native Mono XML path-length constraint. The ordinary controller arguments are `HdSource=Built`, `ExpectedBehavior=satisfied`, `NegativeControl=None`, exact `candidate.root` and `harness.path`; no companion/CommonSense/providers. Omit RunDirectory on Prepare.

The only v2→v3 scene change is `v3-start-observer.diff`: exact native `JobDriver.Notify_Starting` PREFIX captures driver.job ID/definition/snapshot before toils can synchronously finish, pool and start a successor. This replaces the outer `Pawn_JobTracker.StartJob` postfix, which can otherwise report a successor twice. The native source shows Notify_Starting precedes SetupToils/ReadyForNextToil; HD's load base calls base.Notify_Starting, and the unload driver inherits it. The existing pre-Cleanup end observer is unchanged. Controller/bootstrap/settings/product/provider bytes are unchanged from v2.

## Root-owned sequence

1. Review `CONTRACT.md`, the three `src/TransporterPersistence*.cs` files and the bootstrap/controller adaptation diffs. Prepare `F40-PRODUCE`. A pass only establishes the real checkpoint's provenance.
2. Inspect the original producer's full events, native XML, physical ownership, complete logs, process/desktop receipts and Verify result. Its originals are `SaveData/Saves/F40-TransporterCheckpoint.rws`, `evidence/checkpoint.txt` and an identical `evidence/checkpoint.original.rws`.
3. Prepare `F40-RESTART` using those original `CheckpointSave` and `CheckpointRecord` paths. The controller requires a passed producer with matching ID/manifest/result/log/hash and preserves those inputs. It loads the copied original as `Autostart.rws`, literal `pauseOnLoad=True`, no `-quicktest`.
4. Separately prepare `F40-DISABLED` from **the same original producer**, using the pinned private disabled settings file. It is not a continuation of the positive consumer. Its disabled jobs may legitimately end and recover; it never replaces the positive delivery proof.

Use `launch.ps1` through the already-proven inactive-desktop wrapper only. No visible/default-desktop launch is authorized by this handoff. Retain native/controller joins, default-desktop/no-switch receipts and whole logs as usual. Verify intentionally still requires independent manual event/log acceptance.

## Exact observations

Five real human couriers share one native checkpoint: inventory unload, hands unload, positive-progress load, explicit Wait with queued load/unload, and a long Wait with a genuinely reserved queued loader for the same active loading group. Original Quicktest pawns use the native world registry; original resources reside in a separate native deep-saved pod. There is no unsaved custom owner.

Actual startup settings select overload Off (`10`) and carry fraction `0.2`. The loader's real command plans at fraction `1.0`, then native pickups run at `0.2`; the initial plan and setting transition are recorded. The custody courier uses the valid `0.05` minimum and actual kept ballast to plan exactly one real reserved unit. The scene returns the live fraction to `0.2` before advancing. None of these controls assigns job counts, driver progress or serialized state. A full kept backpack on the hands courier uses actual vanilla `MassUtility.FreeSpace` to force genuine hands fallback.

The native XML check respects `Scribe_Values` default omission: a missing `hdLtibLoadIndex` means exactly its declared zero default. Before-first-tick comparison still includes the exact live cursor, toil, counters, references, current/queued IDs, manifests, flags/sessions, positions, tags, Keep and physical owners/counts. Typed observer outputs are read by value; no `__args` or native ref/out writes exist.

Positive recovery interrupts the saved inventory visit behind a new explicit Wait, requires ordinary recovery after that work, and rejects any recreated cancelled transporter visit. Hands and loading resume physically; saved queue IDs must start in order and both succeed. ContinuousLoad off leaves the neighboring manifest untouched. Zero-demand custody is observed while the separate native queued loader still exists. **Native `CleanUpLoadingVars` resets groupID and drops contents**: cancellation captures the old group and requires real dropped cargo, removed session and released custody; it does not assume the hold remains full. The subsequent short forbidden, continuous-on and queued-loading-stop phases maintain exact material conservation and finish with a 300-tick interval.

Disabled startup first checks saved raw flags inactive, real native manifest acceptance despite dormant flags, manual toggle-off, saved loading completion and zero-demand queued custody. It then enables the actual setting without changing the HD patch set and performs a real six-unit unload. Provider storage/container paths, CE/carriers/shuttles/VF and actual two-client network replay remain the separate acceptance rows in `../REMAINING-CHECKS.md`.

Native timing/admission is still unexecuted. A refusal at a fixture guard is not a product failure, and an aggregate passed count alone is not final acceptance. No missing provider or final-integration requirement is waived.
