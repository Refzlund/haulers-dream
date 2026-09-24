# F07 actual RIMMSqol editor and nearby-command fixture

Ready for root's focused source review and isolated native producer, followed by one fresh restart of that producer's actual in-transit save. No Prepare or native execution was performed by the fixture author. Build success is a prerequisite, not editor or feedback acceptance.

The final host is `%TEMP%/hd-f07-20260920/build-v3/Assemblies/HaulersDream.RuntimeHarness.dll`: SHA256 `03FB0B5CCB9B5255454CCC70022900399135CE8C827BA803912C4BD62D27FF2F`, actual MVID `4533aefb-eba0-47b6-b960-253b857a7b04`. PDB SHA256 `BE3727FD26CADF1740B7B920E680027F765404DE78477472D75AFC3267A9D05C`. The real compile completed with zero warnings and errors. Complete build logs and source/output pins are in that build directory. The earlier clean initial build and the intervening failed build-v2 (a nonexistent native DefOf field, corrected to the actual DefDatabase lookup) remain retained.

Frozen scenario `src/RimmsCommand.cs` is `13E360F5E35C982925813DE6524FF5EDE9F4F1C57B9FEAFE4DB668D36170F567`; host Bootstrap `2533E3F6CA3056467421218D0EA43E6601188E20F1CC9FB734689A93E3F8FFBB`; controller `064F966BC19818D84AEB45FDFE7B94E947A0DCB74606092BDB1B859F415B91D2`. The final controller parses without errors in PowerShell 7.6.5. `fixture-sources.json` records all selected integration identities, and `fixture-integration.diff` is the exact delta from the accepted F08 host/controller/owning-launch basis. `adapt-fixture.py` records the initial authoring transformation; do not rerun it over the adapted sources.

The 99-file runtime product at `%TEMP%/hd-f07-20260920/Product` contains only the approved build's actual selected content and four DLL/PDB outputs. Every copy is recorded in `runtime-product-copy.json`. HD `D0C7FBE0` / Core `186D8812` are the source-reviewed current F07 product. The full acquired 449-file RIMMS package is preserved at `%TEMP%/hd-f07-20260920/RIMMS`, including its root legacy Harmony image; `rimms-package-copy.json` retains every original/copy identity.

Native selection has three RIMMS candidates: current `RIMMSqol.dll`, `Priority Queue.dll`, and root `0Harmony.dll` 1.2.0.1. Prepare measures all thirteen input images in separate metadata-reader processes. Twelve are required loaded identities; the legacy candidate has its own retained receipt. Native host admission records the actual RIMMS assembly roster and accepts the legacy candidate only as its exact native-loaded image or an actual binding to the already admitted modern Harmony identity. It neither deletes a DLL nor forces a second assembly load. Other cases retain their simple-name uniqueness checks. The actual game `ModAssemblyHandler` loads each selected file through `Assembly.LoadFrom`; its native loader and file-selection decompiles were read, not invoked.

The producer uses actual RIMMS selection and edit page renderers, edits their real settings model, and closes the native settings dialog to apply it. It captures three PNGs. Actual visual review must confirm the discovered entry, `Allow Drafted` control at the selected scroll position, and offered drafted menu; a nonempty PNG is only capture readiness. Programmatic navigation/model editing is disclosed and is not a physical-click claim.

Within that same finite scene it checks default/reset/live permission and direct-order fields, native auto-pick consumer behavior, HD query purity, overlapping usable/unusable clicked materials, and an enabled ordinary native hauling option beside the single HD option. Native undrafted pickup, drafted pickup-to-delivery with pause enabled, and small-job fallback must finish and conserve goods. A real non-idle Goto precedes a queued HD order; permission is revoked and native admission must reject that queued job after Goto finishes naturally. Actual interruption must not manufacture a delivery. The final productive order saves its real generated delivery while carrying cargo in transit; the restart must recover the same pawn, job, work giver, map, carry object, manifest, receipt, quantities, keep stock, and actual saved RIMMS configuration without restoring those loaded fields by hand.

Assertions are emitted once per named evaluation/stage. Job events retain actual starts, endings and admission. Reservation query snapshots use actual claimant/job/thing/cell/layer/max-pawn/count fields. The native reservation type also has a concrete ToString override; the fixture does not rely on a default type-name string.

The owned arena retains normal native AI. Before parking any map pawn it removes and verifies its Lord membership, then verifies map/world endpoints; disposable map duties and cleared terrain/Home are explicitly not restored. Existing windows are preserved across each owned operation; ephemeral native immediate windows may be recreated between frames. Own windows, observers, selection, actor/jobs/goods, settings and parked pawn ownership are cleaned up. Any cleanup failure remains a failed assertion. Private-runtime protections and finite host/owning-launch process handling are reused.

Use the existing controller directly for data-only Prepare (do not invent a GUID):

```powershell
$f07 = Join-Path (Get-Location) 'docs/plans/evidence/f07-rimmsqol-20260920'
$f07Controller = Join-Path $f07 'controller/scripts/runtime-test.ps1'
$f07Arguments = @{
    HdSource='Built'; ExpectedBehavior='satisfied'; NegativeControl='None'
    BuiltModRoot=(Join-Path $env:TEMP 'hd-f07-20260920/Product')
    HarnessAssembly=(Join-Path $env:TEMP 'hd-f07-20260920/build-v3/Assemblies/HaulersDream.RuntimeHarness.dll')
    RimmsRoot=(Join-Path $env:TEMP 'hd-f07-20260920/RIMMS')
}
& $f07Controller -Action Prepare -CaseId F07-RIMMS @f07Arguments
```

Root must launch the returned actual run through the already proven inactive-desktop helper around this folder's `launch.ps1`, with its actual run directory and a fresh output directory. Use native owner 360 seconds and private-desktop outer timeout 480 seconds. Do not invoke Launch directly on the user's desktop. After the actual owned process joins, run this controller's `Verify` against that run and review all raw events, result, screenshots and Player.log. The reader explicitly retains manual F07 semantic review; it does not automatically close the feedback.

Only after the producer's real outcome is accepted, reuse the same arguments for one fresh restart:

```powershell
& $f07Controller -Action Prepare -CaseId F07-RESTART @f07Arguments `
    -CheckpointSave (Join-Path $actualProducerRun 'SaveData/Saves/F07-NearbyTransit.rws') `
    -CheckpointRecord (Join-Path $actualProducerRun 'evidence/checkpoint.txt')
```

The restart admission binds the original successful producer's actual save, checkpoint, retained RIMMS config and whole-log save warning checks. It copies them to fresh private Autostart/config paths, omits quicktest and enables native PauseOnLoad. No repeated delivery matrix, broad mod matrix, user-game input or new wrapper is introduced. Final assembled Multiplayer interaction remains the separately planned integration check.
