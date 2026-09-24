# F20 focused native witness

Authored and compiled for root review. **No Prepare or native execution has occurred.** This does not close F20.

The current product is frozen once under `%LOCALAPPDATA%/HaulersDreamQA/inputs/f20-20260924`. Both matched product builds passed with zero warnings/errors. Baseline restores only the three exact `before/` files and omits the new adapter. All other source inputs are identical; assembly bytes differ with compilation paths and debug identities. The two clean runtime packages use identical frozen F07 content with their own four freshly built DLL/PDB files. No build deployed into the game.

`src/RefillGate.cs` reuses the F03 host, native map lifecycle, isolated disposable arena and parked-pawn cleanup. The controller is the accepted F35 whole-package variant, adapted to exactly **five mods / eleven loaded images** with actual Storage Refill Hysteresis 0.2.0. Its whole 12-file package and selected DLL `BB43A267` are pinned in `packages-srh.json`. No provider is faked. No new verifier or input controller was added.

The native VNPERP hopper shelf package is not installed among the available Workshop packages. This witness uses an actual vanilla three-slot `ShelfSmall` and separately the actual vanilla `Hopper`. The retained VNPERP source defines the reported shelf as the same ordinary `Building_Storage` with three slots and `isHopper=true`; this is relevant structural evidence, not a claim to have loaded VNPERP or replayed the 232-mod save.

The scene uses a controlled capable native colonist, one three-slot shelf, one equal-priority accepting fallback stockpile and corn with its real 75-unit stack limit. Only owned starter cargo is cleared. Native capacity, pathing, haul counts and production toils remain unchanged. Nine HD settings are saved/restored: master and stack refinement enabled; bulk/urgent bulk, automatic unload, en-route pickup, storage routing, opportunistic unload and nearby sweep disabled to isolate the reported new-destination decision. The two midway methods are queried directly, as selection evidence only. The real player settings/save are never loaded or edited.

1. At 145/225 shelf fullness, SRH range 30–60 rejects a new refill. Native accurate selection with refinement off must select the fallback. Refinement on must preserve it. A real `WorkGiver_HaulGeneral.JobOnThing` result is ordered through the native job tracker and runs real hauling toils; the expected delivery is exactly 12 corn to fallback and none to the paused shelf. Baseline failure is retained and the scene proceeds.
2. Explicit stock setup resets to 70 corn, waits 101 native ticks past the provider's own default sampling interval, then toggles SRH open→paused→open between 10–90 in one paused tick. Each transition checks native selection, the direct real stack selector with the **same fixed fallback cache key**, and both actual midway selectors. This exercises positive-hit revalidation and reopening a cached miss without editing provider cache fields.
3. Explicit consumption leaves 40 corn. After another 101 ticks the lower boundary opens. A real native scanner job is assigned for 12 corn; an explicitly labelled concurrent-delivery setup then adds 100 corn. After the provider's normal sample refresh, the same job must still be carrying and SRH must be paused. The assigned job must finish with all 152 corn in the shelf. This tests the intentional in-flight overshoot contract rather than imposing a hard count cap.
4. A fresh six-corn source appears while paused. **Native Hauling is assigned priority 1 for an 800-tick observation window**, so absence of new shelf jobs is not inferred from an idle pawn. At least one nonforced haul must start, deliver the six corn to fallback, and leave the shelf at 152. Job provenance and every real haul start/end are recorded.
5. Explicit consumption leaves 25 corn. After sampling expiry, a new real refill must open and deliver 12, yielding 37. Disabling SRH must restore ordinary selection. The actual vanilla hopper worker is queried with an enabled 0–0 range and then disabled: null versus a real haul job to the empty hopper. These returned query jobs are not dispatched.
6. Two native shelves are linked using `NewGroup/InitFrom/SetStorageGroup`. Their real shared settings/controller must be identical. Occupancy is 37/75 corn plus 75/75 wood across six slots, between a 20–30 range. A manual paused latch must reject the group. This distinguishes native aggregate/mixed-definition occupancy from a per-cell approximation.

Only stock setup/consumption stimuli create or destroy corn, with counts explicitly recorded. Each native pawn tick outside atomic transfers asserts physical conservation over ground, hands and inventory; output records only changed material ownership/counts. Real job identities, counts, targets, forced status, giver, source node, queue provenance and endings are retained. Productive hauling is never implemented by the observers. A setup refusal, missing transition, timeout or unexpected gameplay change stays failed; baseline expectations do not convert red assertions into green ones.

Bounds are 240 scenario seconds / 15,000 native ticks, 300 host seconds and 360 native launch seconds. Cleanup removes owned cargo/buildings/walls/colonist/zones, restores parked world ownership/placement, settings and speed. It does not reconstruct the cleared disposable terrain or former Lord/duty membership. No user save is loaded or saved.

The first host compile exposed a fixture type-name typo (`EnRoutePickup` versus the actual `Patch_Pawn_JobTracker_EnRoutePickup`); its failed log is retained. Subsequent source review added actual automatic-job provenance and an explicit fixed-key cache query. Final build is zero warnings/errors; prior successful build logs are retained too. Product code was not changed by fixture work. `selected-source.json`, `products.json`, `host-metadata.json`, `author-checks.json` and `integration.diff` identify the final selection.

## Root execution

After independent source review, consume `selection.json` and each actual returned Prepare GUID:

```powershell
$f20 = Join-Path (Get-Location) 'docs/plans/evidence/f20-refill-20260920/fixture'
$selection = Get-Content -Raw (Join-Path $f20 'selection.json') | ConvertFrom-Json
$role = $selection.roles[0] # baseline first; then candidate with roles[1]
$prepared = & (Join-Path $f20 'controller/scripts/runtime-test.ps1') -Action Prepare `
    -CaseId F20-REFILL -ExpectedBehavior $role.expectedBehavior -NegativeControl None -HdSource Built `
    -BuiltModRoot $role.builtModRoot -HarnessAssembly $selection.harnessAssembly `
    -RobotPackageManifest $selection.packageManifest -PlayerSaveDataRoot $actualPlayerSaveRoot
```

Root alone launches the prepared run through `scripts/run-on-test-desktop.py` and this fixture's `launch.ps1`. Use a fresh receipt directory and the actual prepared run path. Never run the native launcher directly on the Default/input desktop, switch desktops, inject input or fall back to a visible launch. After owned join, preserve complete raw result/events/logs and native/desktop receipts, then run the unchanged separate Verify. Its manual-review requirement is intentional. Root must assess full semantics, logs and protected inputs before acceptance. Provider-absent startup belongs to final integrated validation; the historical 232-mod save remains unavailable.
