# F05/F06 bounded lifecycle and Biotech witness

**Current execution selection is v5.** See [the v5 handoff](source-review/immediate-recall-v5/HANDOFF.md) for exact host/controller pins and the native synchronous-recall, JobDef and CleanupCurrentJob corrections. The v4 material below is retained historical context; replace its host with build-v5 and follow the v5 recall distinctions. No v5 Prepare or native launch was performed by the fixture author. Exact earlier files and the failed v4 native evidence remain retained.

24 September 2026. Fixture implementation and compilation are complete. **No Prepare or native launch has been performed by this author.** Root owns the native slot and independent acceptance. This does not close either feedback item.

Use `source-review/selected-inputs.json` for the exact 80 host files, scene, 21 controller files, 62 published product files, 99 current product files, and 1,366 actual robot provider files. The published snapshot was rechecked against every entry in `source-review/published-product.json`; all provider bytes were rechecked against `robot-packages.json`.

| Selected input | Identity |
| --- | --- |
| Host | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/builds/f05-f06-lifecycle-20260924/build-v4/Assemblies/HaulersDream.RuntimeHarness.dll` |
| Host SHA256 / MVID | `4BEC754436262543B9AA4B8EC248A1567C08CAE01BCCD08DCAE8C14DC2DB4647` / `70a07e99-c31d-4e26-b94f-f6331528056a` |
| Scene SHA256 | `198997EBA005A9B303AE89BAB2C989FC21D5D25788D577FC4F6EFBCD180FDD91` |
| Controller SHA256 | `A5F5E513F882C440406A5F574A301433AFD489CAD228B2B8427774177FC848D7` |
| Selected-input manifest SHA256 | `6D63B0ADFC08F8634CD4CF20C6CE11C741D99A9914DB9FD4599D28E0BB109716` |
| Published HD / Core | `D55644ACBE064F4F6A37C82205F48EEAF61264100C92AC568BDCB612FE8DCC2D` / `D835EF8E730BDD721B4A6978F85C4E0B61BEBFE8109247CC67CFDF190652F299` |
| Current HD / Core | `A20FBD9FB139668D0BD50DBE9496B75C728AB7023CC7277BA24388FE148E8C0F` / `1A30CF3CBFC96C7BD0980CBE46482C07A58519F4D31FBD9D269A830A6AE9E332` |

Current product is the same frozen F07 v10 product as accepted robot command run `071295e19d9d43b18235d4c89cc87030`; these witnesses add actual lifecycle/menu boundaries to that evidence, not a claim about the later assembled build. Build v4 completed in 3.83 seconds with zero warnings/errors. V1–V3 build products/logs remain retained. Independent review caught one pre-runtime fixture error: native Thing.ExposeData writes `ThingID` into XML `id`, so v4 uses the exact native string instead of `thingIDNumber` in the read-only saved robot query. That is the only scene change from v3; the exact previous scene/controller/selection/handoff are preserved under `source-review/v3-before-thingid-correction`. The final controller parses without errors. `adapt-fixture.py` and `finalize.py` are retained one-time development writers, **not run entry points**; do not rerun them over the frozen selection.

## Four bounded native cases

1. `F05-OLD-SAVE`: exact published HD snapshot. The genuine provider station spawns an Omni I through its returned `Command_Action`. Native component census must be zero. Seed seven personal Plasteel, then invoke the actual station recall gizmo; its native return job stops/despawns/deep-contains that same pawn. Save through `GameDataSaveLoader.SaveGame`. Read-only XML must show that exact robot once inside a station container and no `haulersDream*` fields. Retain the real `.rws` and identity record. No XML editing or component removal occurs.
2. `F05-UPGRADE`: full native restart of that exact published checkpoint, current product. Before fixture settings/jobs/keep changes, require the original station/pawn/inventory/roles, one native HD component, zero original keep/tags, and actual paused-load tick = saved tick + 1. Reactivate the same pawn through the native station gizmo; set Keep7, seed two 10-unit floor stacks, issue the real nearby gizmo/Targeter callback. Pause at the first real ten-unit pickup. Require tagged inventory17, kept7, hands0, floor10, total27, then invoke native station recall. Preserve native interruption, contained identity/tags, no current/queued jobs, and save that real native state. XML must contain the actual HD serialized fields.
3. `F05-RESTART`: full native restart of that exact current checkpoint. Before any fixture mutation require original station/pawn/inventory thing IDs/counts, exact tag roster, Keep7, total27, original roles and exactly one component. Native station reactivation must return the same pawn and cargo. Let real work/unloading recover all twenty surplus units; require stored20, inventory7, hands0, exact total27 and 300 stable native ticks without new cargo work.
4. `F06-BIOTECH`: genuine installed Biotech content enabled, otherwise the same current/provider profile. Native station-spawned Omni I and two 10-unit sources. Query **`FloatMenuMakerMap.GetOptions`**, not the HD provider directly: setting-disabled option is disabled and pure; native Builder III has an unassigned-Hauling disabled option without role/job/cargo mutations. Restore the Omni selection; its actual enabled menu callback must start identified BulkHaul and deliver20 while retaining7, followed by 300 stable ticks. Native specialist creation uses the actual provider factory; a vanilla Wait keeps that control out of the productive scene.

Both lifecycle loads use the explicitly configured **native PauseOnLoad** behavior: the game performs its normal first `DoSingleTick`, then pauses. Observation is saved tick + 1, not pre-first-tick or default-unpaused coverage. No tick suppression, sidecar state restoration, role injection, fabricated manifests or inventory teleporting substitutes for the actual saves/jobs. Test setup and cleanup occur only in the disposable runtime. Original Lords and terrain removed by quicktest arena setup are not reconstructed.

## Root execution

Before every Prepare/Launch/Verify command set process-local `$env:TEMP` and `$env:TMP` to `C:/Users/Arthur/AppData/Local/HaulersDreamQA/runtime-temp`. Keep actual protected player root explicit. All graphic launches must pass through the committed inactive-desktop runner and its owned kill-on-close job, with no desktop switch or physical input. Do not invoke this controller's Launch directly on the input desktop.

Common Prepare parameters:

```powershell
$rlRoot = 'C:/Users/Arthur/Sync/Projects/RimWorld Mods/HaulersDream/docs/plans/evidence/f05-f06-lifecycle-20260924'
$rlHost = 'C:/Users/Arthur/AppData/Local/HaulersDreamQA/builds/f05-f06-lifecycle-20260924/build-v4/Assemblies/HaulersDream.RuntimeHarness.dll'
$env:TEMP = 'C:/Users/Arthur/AppData/Local/HaulersDreamQA/runtime-temp'
$env:TMP = $env:TEMP
$rlCommon = @{
    Action = 'Prepare'
    HdSource = 'Built'
    ExpectedBehavior = 'satisfied'
    HarnessAssembly = $rlHost
    RobotPackageManifest = "$rlRoot/robot-packages.json"
    PlayerSaveDataRoot = 'C:/Users/Arthur/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios'
}
& "$rlRoot/controller/scripts/runtime-test.ps1" @rlCommon -CaseId F05-OLD-SAVE `
    -BuiltModRoot 'C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f05-f06-lifecycle-20260924/PublishedProduct'
```

Upgrade/restart use `-BuiltModRoot 'C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f07-authority-20260924/Product'`, their respective case, and `-CheckpointSave '<actual prior run>/SaveData/Saves/F05RobotCheckpoint.rws' -CheckpointRecord '<same prior run>/evidence/checkpoint.txt'`. Controller requires a passing correctly identified producer, the original private run root, exact phase record, native save XML, matching retained evidence copy and no known unresolved save-reference warnings; it protects all original checkpoint inputs and copies them read-only into the fresh run. Load uses actual `Saves/Autostart.rws` with no quicktest argument. Biotech uses the current product and `F06-BIOTECH` without checkpoint parameters.

Launch wrapper is `launch.ps1 -RunDirectory <prepared run> -OutputDirectory <fresh evidence directory>`, passed as the child of `scripts/run-on-test-desktop.py` exactly as previous accepted runs. Controller returns its owned process; wrapper bounds and joins it. After join, preserve raw evidence, manifest and desktop/process receipts, then invoke Verify. Independent reviewer must read complete physical/identity assertions, job events, exact checkpoint chain, whole logs, owned cleanup and protected-tree receipt before accepting the result. Preserve setup refusals or failed runs without relabeling them product bugs.

## Review map

`src/RobotLifecycle.cs` is the only new scene implementation. `source-review/Bootstrap-lifecycle.diff` shows the host substitution and moving lifecycle admission ahead of the five-tick gate so paused native loads can be observed. `source-review/runtime-test-lifecycle.diff` shows case registration, exact published/current/host pins, source-bound checkpoint admission/copy/protection, Biotech package registration and actual Autostart launch. Existing controller process-ownership/metadata/payload/protected-player guards remain active. Native station source is retained in `source-review/Station.cs.txt`; native menu dispatch is retained in `source-review/FloatMenuMakerMap.cs.txt`. No product source was changed.
