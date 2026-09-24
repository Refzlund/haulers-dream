# F03: actual Harvest and Haul coexistence

Authored and compiled only; no Prepare or native launch by this author. This extends the successful four-mod harvest `4d0a7d7debae4db29586b15bad7b96e2` without changing its source, build or evidence. It is a native reproduction before any compatibility repair, not a claim about the reporter's unknown save.

One mature cotton plant, native automatic GrowerHarvest/PlantWork, the controlled capable human, distant accepting Cloth stockpile, ordinary physical capacity, zero optional pickup delay and disabled nearby sweeping are reused. Plants 20 and clearing only generated starter cargo are explicit setup. Growing alone is assigned. No productive job, toil or helper is called by the fixture.

The extension starts with **10 Cloth in inventory explicitly pinned through HD's public `SetKeptCount`**, and **7 separate Cloth on the ground beside the plant**. It admits the unrelated stack only when that same Thing is still at its original cell within the actual placement wrapper's two-cell search radius, separate from the real drop cell. Actual fresh output count comes from native PlantWork; the previous scene yielded 11 but this scene does not assume or manufacture that count. Exact accounting is always 17 seeded units plus actual production.

The actual unchanged H&H package is selected after HD, with native package selection preserved. Its exposed `minUnloadPercent` is temporarily set from 80 to 1 so this small load exercises its ordinary automatic unload before HD's longer settle window. Its native 180-tick grace and all other H&H fields are retained and recorded. The original threshold and the seven HD settings are restored during cleanup. This tests coexistence at a configured H&H automatic-unload threshold, not its default five-hour cadence or F24's field-work cadence.

Read-only observers record both native placement overloads and their actual patch owners, the canonical ground output before the default-priority H&H postfix, the final canonical and wrapper returns, each actual H&H `NotifyPlaced` argument, actual registered inventory stack and the real `GetNextHarvestStack` result. They also observe actual native job start/end, productive PlantCollected work and changed physical custody. H&H can legitimately collect the fresh output directly from native placement; this extension does not require HD SelfPickup to win intake.

The scenario waits for the actual H&H unload to finish successfully before evaluating the distinct policy assertions: only the real fresh yield is stored, the 10 kept units remain in inventory with their pin, and the unrelated original 7-unit stack is unchanged and was never offered to H&H as new output. A keep or attribution violation does not abort the trace before unloading, and is reported as a failed assertion. Physical conservation failures or structural setup/observer failures still stop the run. Success cannot be produced by cleanup. The trace is compact and emits changed states rather than repeated arrays or per-frame passing assertions.

The inherited disposable arena, WorldPawns/Lord release, cleanup and finite ownership are unchanged. Cleanup restores original pawn placement/world ownership, settings and speed; it does not reconstruct former Lord/duty state or the cleared disposable terrain. No user save is loaded or saved. Scenario bound: 240 wall seconds / 15,000 ticks; inherited host bound: 300 seconds; native ownership bound: 360 seconds. If H&H does not perform the required productive route, the case fails rather than claiming an unobserved boundary passed.

## Exact package and build

The earlier TEMP H&H package was externally lost. A fresh anonymous SteamCMD download to a new task-owned directory returned the **same original manifest `1795465512286430422`**, 87 files / 983,165 bytes, and exact original 1.6 DLL SHA `525309DB8BBA69999ADFAFF5C32B7F5AE0E1D10EF887D7EED9EBB35256DDCB24`, MVID `0b1acae3-8e8c-4f36-bb80-dd3cd29ef816`. The original historical per-file receipt is unavailable, so only these actual original identities and the new complete package inventory are claimed, not an independent historical comparison of every file.

Actual package, acquisition streams/owned exit, ACF and successful metadata read:
`C:/Users/Arthur/AppData/Local/HaulersDreamQA/acquisition/f03-hah-20260920/`.
Its shipped API decompilation is in `decompiled/`. The package is copied whole, with no edited mod bytes or included-source substitution. `hah-package.json` records its new complete inventory. No completed runtime was repopulated or reverified with reconstructed inputs.

The frozen recovered product is `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f07-20260920/Product`, HD `D0C7FBE0` / Core `186D8812`, whose recovery separately matched all 99 original recorded files. The isolated host compiled once with zero warnings/errors into `C:/Users/Arthur/AppData/Local/HaulersDreamQA/builds/f03-harvest-hah-20260920/`. Full build log, selected-source, products and metadata are retained there. `selection.json` contains actual source/DLL/PDB/MVID and package paths.

`source.diff` shows the complete delta against the accepted harvest source, Bootstrap, controller and build recipe. The host supports, other controller helpers, project and launch ownership code are byte-identical. This case selects exactly five mods and eleven images. The controller adds the whole H&H input to its existing protected snapshot, copies it through the existing verified-tree function, preserves order, and adds its actual DLL to the existing closed metadata read. Its separate Verify explicitly requires full semantic/log review. All three shell entry scripts parse without errors.

## Root execution after independent review

Use the actual player-save directory solely as the inherited protected input. Consume the actual returned GUID; none is predicted here.

```powershell
$hah = Join-Path (Get-Location) 'docs/plans/evidence/f03-harvest-hah-20260920'
$selection = Get-Content -LiteralPath (Join-Path $hah 'selection.json') -Raw | ConvertFrom-Json
$prepared = & (Join-Path $hah 'controller/scripts/runtime-test.ps1') -Action Prepare `
  -CaseId F03-HARVEST-HAH -ExpectedBehavior satisfied -NegativeControl None -HdSource Built `
  -BuiltModRoot $selection.builtModRoot -HarnessAssembly $selection.harnessAssembly `
  -HarvestAndHaulRoot $selection.harvestAndHaulRoot -PlayerSaveDataRoot $actualPlayerSaveRoot
```

Launch **only through the existing inactive-desktop wrapper**. Do not invoke `launch.ps1` directly on Default/input desktop, switch desktops or send input. No visible fallback is allowed.

```powershell
& $python -E -B -X utf8 './scripts/run-on-test-desktop.py' `
  --output $freshDesktopReceiptDirectory --cwd (Get-Location).Path --timeout 450 `
  -- 'C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe' -NoProfile -File `
  (Join-Path $hah 'launch.ps1') -RunDirectory $actualPreparedRun -OutputDirectory $freshNativeOutcomeDirectory
```

After the owned process joins, retain full result/events/logs/native/desktop outcomes and the existing separate Verify returned object. A failed current product result stays failed. Independent review must distinguish actual H&H behavior, HD interaction, fixture setup and the unknown historic reporter cause. DirectToInventory, opposite mod order and any repair validation follow only as warranted by this concrete result; they are not bundled into a new broad matrix.
