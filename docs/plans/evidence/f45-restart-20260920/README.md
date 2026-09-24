# F45 actual checkpoint restart

Source and integration compile successfully (zero warnings/errors, `build.log`). No Prepare, Launch, game, UI or input action was executed while authoring this package. This is a fresh-process continuation of the actual productive Periodic Bills saves, not field restoration or a serializer-only round trip.

`src/PeriodicRestart.cs` is compiled into the reused native harness. Relative to `../f45-production-20260920`, only Bootstrap and its project change among existing host files; 78 other C# files are byte-identical. The controller retains the existing private runtime, metadata, package/image roster, process ownership, protected snapshots and output retention. Its eighteen helper scripts are unchanged. `integration.diff` shows the concrete host/controller/build/entry-point changes. There is no product change.

## Native load path

Actual Assembly-CSharp SHA256 `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A` exposes the existing development Autostart path. `SaveGameFilesUtility.GetAutostartSaveFile` selects `Autostart` only when DevMode is enabled. `Root_Entry.Start` calls native `GameDataSaveLoader.LoadGame`; `Root_Play.Start` invokes `SavedGameLoaderNow.LoadGameFromSaveFileNow`. In `Game.LoadGame`, PauseOnLoad queues an `ExecuteWhenFinished` callback that performs exactly one `DoSingleTick` and then sets Paused; `GameComponentUtility.LoadedGame` is called before that queued callback (actual decompiled lines 638–646). Initial fixture admission therefore requires exactly saved tick +1 and the unchanged saved PB fields/items. This is not a claim of observation before the native load tick, and the fixture never resets time to conceal it.

Read-only native exports remain in `%TEMP%/haulersdream-f45-restart-native-20260920`: `SaveGameFilesUtility.cs`, `Verse.Root_Entry.cs`, `Verse.Root_Play.cs`, `SavedGameLoaderNow.cs`, `Game.cs`. No new startup patch or custom deserializer is needed.

Prepare copies the selected real save byte-for-byte to the new GUID run's private `SaveData/Saves/Autostart.rws`, copies its actual text record to `input/checkpoint.txt`, writes private DevMode/PauseOnLoad preferences, and protects the original save/record/producer result/log. Launch omits `-quicktest`. The host requires its first lifecycle callback to be `loaded-game` and runs this case before its usual five-tick preamble. A wrong startup tick, mode, field, bill/actor ID, or item total fails before fixture advancement or scheduling queries.

## Concrete continuation

Both actual recipes are covered by their existing four checkpoints: simple meal and four-meal recipe, each after the first iteration and at cooldown. Initial checks cover saved amount, interval, produced count, completion tick, PB repeat mode, HD nonbatch state, exact pawn/bench/bill IDs, corn and meal totals. PB lookup is `TryGetData`, never `GetOrCreateData`; no saved field is reseeded.

Partial-cycle checkpoints resume one native iteration and must complete at count two. Cooldown checkpoints observe at least 120 ordinary ticks with neither ordinary nor forced WorkGiver jobs, retain the original deadline, then perform a labelled native tick jump to deadline-minus-one and the exact deadline. At the exact boundary the real PB `ShouldDoNow` must reset the count; two actual native recipe iterations then complete the next cycle. A further 120-tick settled interval rejects extra production. This is not an uninterrupted day-long simulation.

Work is the actual `WorkGiver_DoBill.JobOnThing(..., false)` result dispatched programmatically to the loaded pawn. A read-only native completion observer requires the actual `JobDriver_DoBill`, positive native recipe-work ticks, exact PB counter updates and exact ingredient/product conservation. HD BatchCraft is rejected. Job IDs/drivers, completion ticks and original checkpoint identity are retained. This does not claim physical input or natural priority scheduling. Wall bound is 180 seconds, work bound 10,000 native ticks; host/owned-process bounds remain 300/360 seconds. Cleanup removes only its observer and restores captured paused speed; the loaded scene and real productive changes remain in the disposable process for inspection.

## Selected actual saves

Use corrected production run `0e63f30388344a16ad54559991bdca34`, whose actual producer has joined and whose world-owned parked pawns were independently inspected. Do not use the retained `a2f2fabc1bfe4d3e89a8391af302b319` saves: they contain unresolved deep-save references. The generic Prepare rejects that known warning. Full actual restart log review is still required.

All selected records use map 0, pawn 37260, bench 37263, amount 2 and interval 1.

| Checkpoint | Saved tick | Corn / meals | Produced / completion | Save SHA256 |
|---|---:|---:|---:|---|
| F45-PB-0-partial | 578 | 40 / 1 | 1 / -1 | DCF8897C1FEF414D971009555AA10DFE6329381BAA62844774F1F7F4ACD9F1D8 |
| F45-PB-0-cooldown | 1204 | 30 / 2 | 2 / 1202 | 1BA00C88D50FCBBB95061115BB1D480E846460B73D33A69809307C266B3E50AC |
| F45-PB-1-partial | 64179 | 160 / 4 | 1 / -1 | D0A40AF54994BFBC3BF034B6440A5283D7041D4F9106B4A1364C38024052508A |
| F45-PB-1-cooldown | 65898 | 120 / 8 | 2 / 65897 | 8D2445A56B268CBC38B9175D80DB50F8A7E8F4BF4BFD1CAC0941F07262DA9B5B |

## Existing operation commands

After source/controller review, use one checkpoint at a time. The entry points retain a separate preparation receipt and owned process outcome for each checkpoint. They refuse to overwrite existing receipts/outcomes. Review the actual prepared manifest before its explicit Launch. No future run ID or runtime result is presumed here.

```powershell
$restart = Join-Path (Get-Location) 'docs/plans/evidence/f45-restart-20260920'
$producer = Join-Path $env:TEMP 'haulersdream-runtime-tests/0e63f30388344a16ad54559991bdca34'
$checkpoint = 'F45-PB-0-partial' # subsequently 0-cooldown, 1-partial, 1-cooldown
& (Join-Path $restart 'prepare.ps1') -CheckpointSave (Join-Path $producer ('SaveData/Saves/' + $checkpoint + '.rws')) -CheckpointRecord (Join-Path $producer ('evidence/' + $checkpoint + '.txt'))
# Inspect that actual prepared state, then:
& (Join-Path $restart 'launch.ps1') -Checkpoint $checkpoint
$prepared = Get-Content (Join-Path $restart ('prepared-' + $checkpoint + '.json')) -Raw | ConvertFrom-Json
& (Join-Path $restart 'controller/scripts/runtime-test.ps1') -Action Verify -RunDirectory $prepared.runDirectory
```

Verify deliberately retains the existing manual-review finding. Inspect whole logs (including save/load reference warnings), all assertions/events, native work/conservation and process/protected outcomes. Only actual successful executions establish reload support; this source/build package does not close F45 or the separately observed upstream clone defect.

## Built checkpoint

- Scenario SHA256: `CFF69BFA8B2BB1614B819E8D1563BEA62FCBDD1E2851EAB03B9A1688FBE3365D`.
- Bootstrap SHA256: `2797752A97B65760118D58AAA960F010E9C5E00EFCAA66DF2C8092E6684A19A7`.
- Controller SHA256: `3267CC6454B42F964B2AC1CF8B9AEADCCD46A84206A33CEE008E28DB2D71695F`.
- Built host SHA256: `D424821551252D04A4A17317A2502D6FF41DB79A73D5A6D6568A1470D8BAAB8B`.
- PDB SHA256: `F4ABA048F5D8B8B514CAA83CCEA021497D2EBCE93C16906C62A38EFE93A60DFB`.

The unchanged build command is `./docs/plans/evidence/f45-restart-20260920/build.ps1`, using the existing local SDK/cache/references and source-matched HD32D57A64/CoreD623F164. The actual PB package remains AAE78E0D/MVID c3971e5a-4c8e-4137-8147-593b90bd0e0e. No deployment occurs.
