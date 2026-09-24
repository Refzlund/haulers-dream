# F35 actual finish-off witness

Source and the isolated host are ready; no Prepare or native run has occurred. This reuses the F03/F33 private host, enclosed arena, assembly admission, controller and owned launch. It adds one case, `F35-FINISH`, and selects either actual Allow Tool plus HugsLib, or actual Keyz' Allow Utilities. The providers are tested separately against frozen F07 baseline binaries and the F35 candidate binaries. Failed baseline assertions remain failures.

`src/FinishWild.cs` uses each provider's real designator and `WorkGiver_FinishOff.JobOnThing(forced: true)` to create the order, then native `TryTakeOrderedJob` dispatch. It does not simulate mouse input or claim natural autonomous work selection. A seeded, naturally generated human capable of violence and hauling has Melee set to 10, owned starter inventory emptied and automatic work disabled. Native anesthesia downs a live, factionless deer. Only the real provider toils execute the kill; only native queued hauling moves its exact corpse into the accepting stockpile. No test calls `DoExecution`, `Kill`, hauling utility, carry/drop or productive toil directly.

Each provider/role run contains four sequential scenes:

1. **Wild enabled, existing order preserved.** A native Goto is already behind the finish order. At the actual provider execution postfix the queue must be exactly that Goto followed by one corpse haul. Goto must complete first; precisely that queued haul must then deliver the original corpse and remain settled for 60 ticks. For Allow Tool this scene deliberately kills outside Home with its actual un-forbid setting enabled, proving HD sees the provider's post-execution forbidden state. Keyz uses an inside-Home target because it does not un-forbid the new corpse.
2. **Wild disabled, tamed enabled.** The same wild finishing succeeds, but neither the queue nor subsequent actual jobs may contain a corpse haul. This distinguishes the reported wild path from the separate tamed toggle.
3. **Forbidden outside Home.** Both toggles are enabled; the provider is allowed to kill normally outside Home. Allow Tool's own un-forbid option is temporarily disabled. The native corpse must remain forbidden and unhauled. The test never manually forbids the corpse after death.
4. **Cancelled before execution.** The actual provider job is replaced with a native Goto before any killing toil. The victim must remain alive with no corpse, execution callback or haul.

The real provider `DoExecution` postfix, `Pawn_JobTracker.StartJob` and `Pawn.Tick` observers record changed state, exact job/target IDs, queue order, native completion condition, corpse ownership/storage and forbidden state. They never return a replacement job or modify production. The post-execution queue check runs after the normal-priority HD hook. Negative scenes settle for 240 ticks; cancellation for 120. Scene, aggregate tick and wall bounds are 3,500 ticks, 16,000 ticks and 240 seconds. The inherited host and owned launch retain their 300/360-second bounds.

Neither provider supplies an unshifted player-owned animal order under this no-input test: Allow Tool requires actual Shift for its friendly target permission, and Keyz rejects player-owned/hosted targets. The fixture reports that limitation instead of manufacturing a tamed order. Keyz's strip subclass reaches the same base execution hook according to the retained provider source; this fixture uses its ordinary native finish designation, not a synthesized Shift/strip order. These limitations must remain distinct from actual tested provider support.

Cleanup removes only fixture-owned objects/observers/zone, restores the HD booleans, Allow Tool's actual setting, game speed and parked original pawn placement/world ownership. It uses the already corrected native Lord release and WorldPawns KeepForever parking. The disposable quicktest does not restore cleared terrain, Home edits, biological time or former Lord duty. No user save is loaded or written.

## Actual sources and build

- Scenario SHA256: `D78CA285DCA9D07DE8CF9086106AA01C45DECEF0607D9DA45A6C8892BB6169AD`.
- One successful isolated host compile, zero warnings/errors: `%TEMP%/hd-f35-20260920/build/build.log`. DLL `E990E7EF3EEC7F5CED6B2D99CECB2D7B855B1C03D67545AC3A74D2A886CA36BD`; PDB `8B204D7A82D9112580660A22A0872A179206C9C20BB3FC77E518FCFDE068D2D0`. `build.ps1` is the existing F03 operation with F35 source/product/output paths. It uses assembly references only, isolated intermediates and no deployment.
- `packages-allowtool.json` and `packages-keyz.json` retain complete fresh copies of the installed Workshop packages, including their native load folders and older DLLs. Allow Tool selected v1.6 DLL: `471F01CBC116269CE5CCD2588E6748C704CB099F512B91531847D6944147DA5C`; HugsLib selected v1.6 DLL: `8BAD9C2EB1CB7D2735AEF9DDFB209F422C257DA14A4740A0F99F9EF7363386D7`; Keyz selected 1.6 DLL: `F789A178E09093DC7CED1F3BE1D87AEA59B1A91E750F33E662868ABC65CA6985`. Older root assemblies are not deleted or silently treated as current; the actual native binding must match the selected required assembly identities. Allow Tool is six packages/twelve required images; Keyz five/eleven.
- `integration.diff` contains the host/project/controller/build/launch differences from F03. All unrelated helpers remain reused. Controller parsing succeeds; parsing is not native execution evidence. Metadata workers and strict copied/protected input checks remain active, and Verify still requires complete independent semantic/log review.

The older temporary F07/F35 content suffered missing files while this fixture was being authored; no old runtime or prepared state was restored or rebased. Root supplied the recovered byte-exact F07 package at `%LOCALAPPDATA%/HaulersDreamQA/inputs/f07-20260920/Product`, independently joined to all 99 original copy hashes. `product-copy.json` verifies those 99 again and records the fresh F35 package: 95 content files are identical, and only the four HD/Core DLL/PDB files differ. The F35 clean package, complete provider copies and byte-exact built host copy are under `%LOCALAPPDATA%/HaulersDreamQA/inputs/f35-20260920`. These fresh inputs avoid depending on the damaged historical temporary trees; actual build outputs/logs and their history remain retained.

## Root execution

First independently review the scenario and bounded integration. `selection.json` now has concrete complete package/host selections and `readyForPrepare: true`; this means the inputs exist, not that review or native acceptance has occurred. No run GUID is invented in advance.

```powershell
$f35 = Join-Path (Get-Location) 'docs/plans/evidence/f35-finish-wild-20260920/fixture'
$selection = Get-Content -LiteralPath (Join-Path $f35 'selection.json') -Raw | ConvertFrom-Json
# Select the actual baseline or candidate row and one provider row from selection.json.
$prepared = & (Join-Path $f35 'controller/scripts/runtime-test.ps1') -Action Prepare `
  -CaseId F35-FINISH -ExpectedBehavior $role.expectedBehavior -NegativeControl None -HdSource Built `
  -BuiltModRoot $role.builtModRoot -HarnessAssembly $selection.harnessAssembly `
  -RobotPackageManifest $provider.packageManifest -PlayerSaveDataRoot $actualPlayerSaveRoot
```

After reviewing the actual prepared result, root launches only through the existing inactive-desktop wrapper. Never invoke `launch.ps1` directly on the Default/input desktop or switch the user's desktop.

```powershell
& $python -E -B -X utf8 './scripts/run-on-test-desktop.py' `
  --output $freshDesktopReceiptDirectory --cwd (Get-Location).Path --timeout 450 `
  -- 'C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe' -NoProfile -File `
  (Join-Path $f35 'launch.ps1') -RunDirectory $actualPreparedRun -OutputDirectory $freshNativeOutcomeDirectory
```

Join the owned process, run the separate existing Verify, retain complete result/events/logs and review all four scene outcomes and cleanup. Admission or setup failure is not a product diagnosis. Product source/build alone does not close F35; player guidance and localized wording follow accepted native results.
