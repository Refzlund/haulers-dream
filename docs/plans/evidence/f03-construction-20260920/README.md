# F03/F14 automatic construction witness

The first native run, `e2eee2b9ee4c4f0c83d2ef2c19c12ed4`, failed human setup before any construction. Its failure remains recorded. **The corrected build-v2 has not been prepared or run.** Root must independently review its narrow source change before executing. This is the construction witness recommended in [f03-next-execution-review.md](../f03-next-execution-review.md); it does not claim an F03 repair or harvest/H&H compatibility.

One ordinary wooden `Table3x3c` costs 100 wood in the actual Core definition. Each of three sequential scenes starts with an empty actor, two ordinary 50-unit floor stacks and a new native blueprint. The same natively generated human, with the controlled setup below, runs with `inventoryConstructDeliver` enabled and then disabled. The third actor is the acquired packages' real `RPP_Bot_Builder_III`, produced by native `AIRobot.X2_Building_AIRobotCreator.CreateRobot`. Its original Construction 1/Mining 3 roles, skills, needs, timetable and no-station state remain intact; no Hauling role is granted.

The fixture never constructs, dispatches or returns a productive job. Native thinking/Construction must start it. Read-only `Pawn_JobTracker.StartJob`, `Pawn.Tick` and `Frame.CompleteConstruction` observers record real source/forced/queue provenance, physical wood transitions, inventory peak **before the first deposit**, full frame receipts, real FinishFrame work, and the completed table before cleanup. Enabled scenes must exceed measured hand capacity; the disabled scene must use native hand delivery with no inventory phase. All 100 units must be consumed exactly once. Original map wood is included in the physical census; inventory, hands, ground and frame containers are counted distinctly. Material rows are emitted only when those counts change, plus distinct scene/job/completion boundaries; there are no repeated Home/storage arrays or passing assertions every frame.

At most twelve seeded native colonists are considered, stopping at the first capable actor with the required measured capacity. Newly generated, owned starter inventory/hand contents are recorded and destroyed before test wood is seeded; human Construction is set through native `SkillRecord.Level` to 20. Each candidate records the original/new skill and actual success chance, hand limit, inventory ceiling, mass capacity and remaining gear mass. The actual Core construction-success curve reaches a multiplier of 1 at level 8 and 1.13 at level 20, with a final maximum of 1 and native Manipulation/Sight factors. The fixture still requires an actual success chance of 1, hands admitting fewer than 100 wood and the native inventory ceiling admitting all 100. It does not set the success stat, body, apparel, mass, stack limit, construction cost, capacity or overload setting. Lack of a suitable actor or insufficient native Builder capacity remains **fixture setup refusal**, not evidence of a product defect. Human Construction is assigned, its timetable set to Work and its food/rest filled for both scenes; the robot receives none of these edits. The human is reused for the toggle comparison.

The F33 21×21 cleared arena, 80 owned granite perimeter walls and private runtime primitives are reused. Original pawns are parked through native WorldPawns KeepForever, including occupants released when clearing structures. The F08 Lord correction releases actual Lord membership before world transfer and verifies both endpoints. Cleanup restores original pawn placement/world ownership, settings and speed, and destroys only owned scene objects. This disposable quicktest does **not** restore cleared terrain/buildings, biological time or former Lord/duty state. No user save is loaded or saved. The six boolean changes isolate single-site construction from supply relocation/multi-site/tether behavior; all are restored. Native work/AI and normal construction rules remain active.

Scenario limits are 240 wall seconds, 30,000 total ticks and 10,000 ticks per scene, inside the unchanged host 300-second bound and owned launch 360-second bound. Failure remains failure; completed cleanup cannot manufacture construction acceptance.

## Frozen inputs and reuse

- Product: `%TEMP%/hd-f07-20260920/Product`; HD `D0C7FBE00E0C194E640331D98ACDFA507FA191CFBFCD04F623BC68C16029CBC6`, Core `186D88120BFC3ABAE7D37E55FE13C29A790D207AFC0448B40AE8B0DD8899B12A`. No F07 product bytes changed.
- Robot package roots: `%TEMP%/haulersdream-runtime-tests/da718daf61ff401b945a32259c881b60/runtime/Mods/{MiscRobots,MiscRobotsPlusPlus}`. `robot-packages.json` inventories all 1,006/402 original files; default/native load selection and every package byte are preserved.
- Corrected host: `%TEMP%/hd-f03-20260920/build-v2/Assemblies/HaulersDream.RuntimeHarness.dll`; exact identities are in `selection-v2.json` and that build's `products.json`. `build-v2.ps1` changes only the output directory from the original build recipe, retaining the actual native/Harmony assemblies and frozen F07 product. It builds only this isolated test host, never HD or the game.
- The first compile omitted inherited F33 support files; `build-first-missing-support.log` retains its 25 missing-type errors. Byte-exact support files were copied and the host then compiled successfully. A following ownership-guard refinement is recorded by the retained earlier successful log. Current `build.log` is zero warnings/errors. The added scenario, Bootstrap/project integration and controller delta are the review scope; unchanged support and controller helper files are reused.
- `integration.diff` shows the bounded host/project/controller/build/launch changes from F33. The controller adds F03 to its existing robot-copy/12-image metadata path, binds the actual package inventory, requires Built/satisfied/None, and retains all private-copy/process/protected-input behavior. Its generic Verify still requests independent semantic/log review; it is not an automatic closure decision.
- `human-setup-correction.diff` is the complete scenario delta after the first native failure: owned starter cargo removal, controlled human skill and measured candidate reporting. The first run reported 45 assertions, two failed, zero captured Unity errors and zero completed scenes at tick 6. Its aggregate guard did not identify which condition rejected each candidate; the correction makes that observable. The exact first source, full build, documentation/selection and raw evidence are retained in `%TEMP%/hd-f03-20260920/pre-human-setup-fix-e2eee`; original `build/`, `selection.json`, run and evidence also remain unchanged. `build-v2/` compiled once with zero warnings/errors. No controller, host integration, robot package, product or behavior acceptance criteria changed.

## Root execution after independent review

Use an explicit actual player-save root and no `RunDirectory` for Prepare; consume the GUID actually returned by the unchanged preparation mechanism. `selection-v2.json` contains the corrected concrete product/harness/package-manifest paths.

```powershell
$f03 = Join-Path (Get-Location) 'docs/plans/evidence/f03-construction-20260920'
$selection = Get-Content -LiteralPath (Join-Path $f03 'selection-v2.json') -Raw | ConvertFrom-Json
$prepared = & (Join-Path $f03 'controller/scripts/runtime-test.ps1') -Action Prepare `
  -CaseId F03-CONSTRUCTION -ExpectedBehavior satisfied -NegativeControl None -HdSource Built `
  -BuiltModRoot $selection.builtModRoot -HarnessAssembly $selection.harnessAssembly `
  -RobotPackageManifest $selection.robotPackageManifest -PlayerSaveDataRoot $actualPlayerSaveRoot
```

Launch **only through the established inactive-desktop wrapper**, with the actual returned run and fresh output directory. Never invoke `launch.ps1` directly on the Default/input desktop. No visible fallback, desktop switch or input injection is permitted.

```powershell
& $python -E -B -X utf8 './scripts/run-on-test-desktop.py' `
  --output $freshDesktopReceiptDirectory --cwd (Get-Location).Path --timeout 450 `
  -- 'C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe' -NoProfile -File `
  (Join-Path $f03 'launch.ps1') -RunDirectory $actualPreparedRun -OutputDirectory $freshNativeOutcomeDirectory
```

After the owned game joins, invoke the existing separate Verify and retain its returned object along with complete raw result/events/logs and native/desktop outcomes. Independently read those observations before deciding F03 construction support or representative F14 acceptance. Harvest intake/H&H is the next distinct investigation; it is not part of this fixture. Final combined-build checks remain separate.
