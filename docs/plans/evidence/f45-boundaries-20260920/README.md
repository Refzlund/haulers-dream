# F45 composition and selection boundaries

Built and ready for changed-source review and root-controlled runtime. No Prepare, Launch or Verify has been executed. `source.diff` is against the preserved successful core `src-v3`; all changes live in this new subtree. The native fixture uses reviewed F45 HD `6828B52A` / Core `0C46DD59`. Product sources are untouched. The four relevant workspace files (`BillRepeatMenuComposition.cs`, `BillRepeatMenuActions.cs`, `BillRepeatMenuProviders.cs`, `Patch_BillRepeatMode_Batch.cs`) were also compared with the actual selected product build source under `TEMP/haulersdream-f45-product-root-build-20260920/Source/HaulersDream`; all four are byte-identical.

The only selected profile is `candidate-boundaries`, with 11 cells. Its first cell primes the real menu and harvests original native options for the two real bills; it does not fabricate known provider closures. The eight composition stimuli run in a temporary synthetic creator prefix after HD's actual Enter prefix. They use actual FloatMenu constructors and product finalizers, while skipping only the controlled original menu creator body. No action is selected in these eight cells.

| Control | Discriminating observation |
| --- | --- |
| Unrelated and other-bill lists | Both retain the exact incoming list and option identities; the same-bill list receives three batch options. |
| Zero constructor output | Real entered scope closes although the controlled creator produces no menu. |
| Two constructor outputs | Both constructors compose independently, both actual Add calls occur, and only owned windows are cleaned up. |
| Nested normal call | Distinct inner frame links to outer, closes normally, and restores outer before its next constructor. |
| Nested exception | Exact injected exception survives the real inner finalizer; outer resumes and composes its own menu. |
| Returned and copied outputs | Returned list bypasses augmentation by identity; copying its native-sorted options does not duplicate batch controls. |
| Composition exception/reentrancy | At actual AddBatchOptions, reentrant augmentation sees Composing=true and returns the input; an intentional throw is contained by the product's constructor adapter; the flag clears and a second constructor succeeds. |
| Outer exception | Exact exception escapes the real finalizer with the frame closed and current scope cleared. |
| Action throw after store | Tiny temporary foreign transpiler runs before actual HD Observe; the original offered native delegate stores mode then throws. Batch/size remain and completion count stays zero. Removing only that stimulus restores normal completion by the same delegate. |
| Incoming return labels | Foreign IL supplies an incoming branch to Ret before the store and another after the store. Actual Observe must move labels onto its guard: no-store return completes zero times and preserves state; accepted return completes once. |

The action controls do not replace offered delegates or call an imitation completion helper. They patch one known actual native action temporarily, require a one-store/one-return/no-EH stream without HD's completion already inserted, and fail if the synthetic transpiler ordering differs. Runtime observers retain exact action entry/return/exception/completion counts. Every temporary patch is removed by its own owner; original product and observation patches stay installed. Existing baseline window objects/order/flags/delegates and other-bill state are checked after each control. Full scene, GUI and process cleanup remain independently required.

One explicit warning is expected from `boundary-composition-exception`: HD's original `Bill repeat menu composition failed; preserving the original menu` warning containing `F45 intentional boundary stimulus`. It records a deliberately injected contained exception, not an unexplained failure. Its full log and event evidence must remain. No native outcome is claimed by this source document.

A **contract-mismatch initialization check remains separate**. `BillRepeatMenuProviders.EnsureInitialized` permanently sets `initialized` at its first call. Resetting private initialized/Ready/provider state in this already-primed process would not test startup behavior. The minimal justified check is one fresh candidate-native profile: install an explicitly unsupported foreign action transpiler (e.g. a second repeat-mode store) before the first actual menu call, let actual Install/ValidateBody reject it, observe the visible binding warning and Ready=false, verify only the attempted HD observer rolls back while the foreign patch remains, and retain the original menu/actions without HD additions. This does not require another provider matrix or controller framework. It has not been implemented or claimed by the present 11-cell profile.

Build: exit 0, 39 existing JSON DTO CS0649 warnings, zero errors; complete `build.log` and `build-result.json`. DLL `400D2ABC0B91D2CD0565E0F79DC6C891E7A3D72C6E81224CC1A0DD2663528F92`; actual reflection-only MVID `06079059-9588-4e22-9fc7-a063e2254895` in `compiled-metadata.json`. Six executable controller files and action contracts are unchanged copies. Only actual changed source/DLL/PDB pins, additional partial source, profile ID/roster and fresh paths differ. The 19 inherited pending requirements and whole-F45=false remain.

Use the existing process controller with a real root review file/hash after source review:

```powershell
$python = 'C:/Users/Arthur/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$controller = 'C:/Users/Arthur/Sync/Projects/RimWorld Mods/HaulersDream/docs/plans/evidence/f45-boundaries-20260920/controller/run.py'
& $python -E -B -X utf8 $controller Prepare --profile candidate-boundaries --review $reviewPath --review-sha256 $reviewHash
# Only after inspecting the actual prepared result and when the native slot is free:
& $python -E -B -X utf8 $controller Launch --run $actualRunDirectory --review $reviewPath --review-sha256 $reviewHash
# After the actual owned native process joins:
& $python -E -B -X utf8 $controller Verify --run $actualRunDirectory --review $reviewPath --review-sha256 $reviewHash
```

The reused reader checks structure and retains full evidence; its output still needs semantic and full-log review. No new provenance wrapper or controller framework was added.
