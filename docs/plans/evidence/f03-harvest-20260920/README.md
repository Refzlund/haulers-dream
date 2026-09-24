# F03: one fresh native harvest

Authored and compiled only. **No Prepare or native launch by this author.** This is the first base-path witness recommended in [f03-next-execution-review.md](../f03-next-execution-review.md), not an F03 repair or an H&H compatibility result.

One mature, unblighted native cotton plant occupies one cell of a real growing zone. The zone selects cotton and disables sowing, so it cannot introduce another productive task after harvest. Cotton produces native Cloth, avoiding food consumption as an accounting confounder. An accepting one-cell stockpile is twelve cells away. No existing loose map Cloth is admitted.

A bounded native colonist selection requires a capable grower who is also capable of hauling. Only newly generated owned starter inventory/hand contents are cleared. Plants is set through native `SkillRecord.Level` to 20, with its actual yield stat and capacity recorded. Growing is assigned priority 1; all other work, including Hauling, is unassigned. Food/rest are filled and the timetable set to Work. Physical capacity, body, apparel, crop cost/yield/work and product behavior are not edited. The real expected crop output must fit ordinary inventory headroom without overload. Setup refusal is distinct from a product failure.

The selected product is the frozen F07 HD `D0C7FBE0` / Core `186D8812`. The fixture enables master/automatic unload, selects shipped DropThenHaul, disables nearby sweeping and sets optional pickup delay to zero. It **retains the shipped unload settle window**, which is recorded. This witness requires eventual delivery and makes no claim about F24 field cadence. All seven modified settings are restored. The per-pawn automatic preference must already be enabled on the newly generated pawn.

Native Growing must select `GrowerHarvest` and execute `JobDriver_PlantWork`; the fixture never creates or dispatches a productive job or calls a productive helper/toil. Read-only observers record:

- Actual automatic harvest job identity/provenance and native PlantWork's completed `workDone` at `PlantCollected`.
- The actual newly created Cloth Thing/count at the canonical native nine-argument `GenPlace.TryPlaceThing` call, followed by successful placement of that exact Thing on the ground.
- Actual SelfPickup, full inventory intake, native unload and full accepting-stockpile delivery, followed by their native success endings. An adjacent instant pickup can finish inside `StartJob`; its actual current SelfPickup driver at native `EndCurrentJob` supplies the inventory observation in that case.
- Exact conservation across ground, inventory and hands at completed placement and each native pawn tick. Only changed material states and distinct job/production boundaries are emitted. Produced count is observed, not manufactured or assumed to be the XML yield.

Success requires one productive harvest/collection, one fresh placement, an inventory peak equal to the whole real output, all units stored, empty hands/inventory/pending pickups, successful native job endings and the native `PlantsHarvested` record increment. These observations precede cleanup. A missing transition fails or reaches the finite timeout; cleanup cannot supply success.

The existing F03/F33 host and 21×21 enclosed disposable arena are reused. Original pawns are parked through WorldPawns KeepForever with the accepted F08 native Lord-membership release. Cleanup removes owned yield/plant/walls/zones/pawns, restores original pawn placement/world ownership, settings and speed. It does not reconstruct cleared terrain/buildings, elapsed biology or former Lord/duty state. No user save is loaded or saved. Bounds remain 240 scenario wall seconds / 15,000 native ticks, 300 host seconds, and 360 owned launch seconds.

## Review and actual inputs

`src/HarvestGather.cs` is the new scenario. `integration.diff` shows the limited Bootstrap/project/controller/build/launch changes against the F03 construction harness. The harvest case selects **four mods and ten native images**, without acquiring, copying or loading either robot package. The 78 inherited host support sources and 18 controller helpers are unchanged; native-copy, protected-input, process ownership and separate Verify behavior are reused. Controller/build/launch scripts parse without errors.

`build.ps1` compiled once into `%TEMP%/hd-f03-harvest-20260920/build/`, with zero warnings/errors. `selected-source.json`, `products.json`, `build.log` and the existing read-only `host-metadata.json` are there. `selection.json` records actual source/DLL/PDB/MVID and frozen product paths. The original construction fixture and builds are untouched.

Actual native code supporting the scenario is retained in `%TEMP%/hd-f03-harvest-20260920/{WorkGiver_GrowerHarvest.cs,JobDriver_PlantWork.cs,Zone_Growing.cs,Plant.native.cs}`. Native PlantWork increments real work, constructs the output and places it before `PlantCollected`; the observers do not replace these operations. The failed initial lookup of `Verse.Plant` was a read-only namespace error; `Plant.native.cs` is the successful actual `RimWorld.Plant` read.

## Root execution after independent review

Use the real player-save root solely as the unchanged controller's protected input, and consume its actual returned run GUID. The recipe does not predict one.

```powershell
$harvest = Join-Path (Get-Location) 'docs/plans/evidence/f03-harvest-20260920'
$selection = Get-Content -LiteralPath (Join-Path $harvest 'selection.json') -Raw | ConvertFrom-Json
$prepared = & (Join-Path $harvest 'controller/scripts/runtime-test.ps1') -Action Prepare `
  -CaseId F03-HARVEST -ExpectedBehavior satisfied -NegativeControl None -HdSource Built `
  -BuiltModRoot $selection.builtModRoot -HarnessAssembly $selection.harnessAssembly `
  -PlayerSaveDataRoot $actualPlayerSaveRoot
```

Launch **only through the inactive-desktop wrapper**. Never invoke `launch.ps1` directly on Default/input desktop, switch desktops or inject input. No visible fallback is permitted.

```powershell
& $python -E -B -X utf8 './scripts/run-on-test-desktop.py' `
  --output $freshDesktopReceiptDirectory --cwd (Get-Location).Path --timeout 450 `
  -- 'C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe' -NoProfile -File `
  (Join-Path $harvest 'launch.ps1') -RunDirectory $actualPreparedRun -OutputDirectory $freshNativeOutcomeDirectory
```

After the owned process joins, run the existing separate Verify and preserve its actual returned object plus complete raw events/result/logs/native/desktop outcomes. Its fixed manual-review notice is intentional. Independent semantic/whole-log review must assess the actual outcome. H&H kept-stock/nearby-stock coexistence is the next extension **after this first result**; other modes, fields, robots, settings matrices and save/restart are outside this witness.
