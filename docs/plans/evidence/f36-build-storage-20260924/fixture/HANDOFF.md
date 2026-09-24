# F36 Build From Storage paired witness — ready for root runtime

24 September 2026. Source and builds complete. This agent has not Prepared or launched a game, changed the ledger, or accepted runtime behavior. Root owns the one native slot, inactive-desktop launch, process join, raw preservation and independent review.

## Exact product change and frozen pair

Only `Source/HaulersDream/RouteExecutor.cs` changes: exclude `Blueprint_Install` from total raw-material demand, suffix demand and own-stock material fallback. The normal scanner and its install job remain intact. `../product.diff` is the exact diff against `../RouteExecutor.before.cs.txt`.

The once-frozen current working tree contains 464 inputs: 369 source/project/property files and 95 current runtime content files. It includes accumulated working fixes rather than copying the earlier F07 runtime content. Baseline and candidate input hashes differ in exactly RouteExecutor; `source-build-review.json` records the comparison. Its before/after SHA256 values are 074CE88751E77E7F4AB893FC4DF2FDD18BFB1424D1765006AFF22B49F422476C and F56C008A2E4BED53A0177DC052C7F3A619EF70D6A550BE7D8FC87725471F42D3. Current workspace RouteExecutor matches candidate.

Both clean runtime packages contain 99 files. Their 95 content files match exactly; four independently built DLL/PDB files differ. The Core sources are identical; separate absolute compilation roots yield different image/debug identities, which must be pinned per role. Both isolated builds succeeded with zero warnings/errors and an absent deployment-guard path. Complete frozen input hashes and build logs/receipts are retained one directory above; `runtime-products.json` pins every selected runtime file.

- Baseline product: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f36-20260924/baseline-Product`; HD SHA256 4D71CC465D26FB006F00D9B1FDBC137046DB9F089C6F44BDE78B05496E61A285.
- Candidate product: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f36-20260924/candidate-Product`; HD SHA256 4E8CEED968C99A79C504B5BCC4D3EDDB76AB1B87B664B56075D4765D85EF8DC7.
- Actual provider: Workshop 3523011187, package `buildfromstorage.programmerlily.com`, DLL SHA256 47864AFBC11D68D4DB5D54825D0CD430515158AEAF187F004B416A709E216286, MVID 340d662d-9447-4fad-be99-111ab4adfe9b. `packages-build-storage.json` pins all 118 acquired files.

## Smallest native scope

`src/BuildStorage.cs` executes two bounded scenes in a disposable initialized private map:

1. Invoke the real provider-patched `Designator_Build` for two wooden stools with one accepted stored packed stool. Require the first actual install blueprint to reference that exact wrapper/inner building and the second designation to produce an ordinary blueprint, since the packed item is already assigned. Resolve the actual HD construction kind and plan exactly these two stops. Call actual `RouteExecutor.Execute` with haul-and-build enabled during native Root.OnGUI. No replacement install/build jobs are supplied. Observe the original native `HaulToContainer`, count 1, exact targets, normal HD material job, real FinishFrame work/completion, installed original building and exactly one distinct newly constructed stool. Conserve all raw wood across ground, inventory, hands and frame; only the new stool's actual cost may be consumed.
2. Designate another owned packed stool through the provider, then explicitly forbid its source. The actual scanner must return null even for forced work. Call public `BuildJobForStop` and require null with no material-cost query or item change. This is a selection-only check; no fabricated job is dispatched.

A read-only observer counts calls to the actual install `TotalMaterialCost` and allows original execution/error logging. Both roles use identical zero-query assertions. The baseline must retain its failures and native errors; candidate must eliminate them without losing productive installation/construction. Expected source diagnosis is two route queries plus one unavailable-fallback query; actual raw evidence remains decisive. The host deliberately runs productive work to completion despite failed query assertions.

The actor is selected from at most 12 native generated colonists; its measured `StatDefOf.ConstructSuccessChance` must be at least 1 after Construction skill 20. The measured value is emitted and rechecked, so random botches are not assumed absent. The native frame prefix passes its observed position to the existing postfix through Harmony `__state`. Neither observer supplies construction work or materials.

Temporary HD settings are recorded/restored. All owned fixtures, jobs, observers and parked pawn world ownership are cleaned; prior Lord duties and cleared disposable terrain/Home are not reconstructed. No player save is used. Programmatic OnGUI command/designator invocation is not physical mouse/keyboard evidence. No opaque-container, old-save, all-construction-mod or historical recurrence claim is supported.

## Selected fixture and reuse

`selection.json` contains exact machine paths and both roles. Selected host SHA256: **2F5A3672B75E4D3DA1B2ED223D0B2AA06FA382559CB746A46992D1C1C0216039**, MVID **d7da9940-3ebd-4eb1-ad30-64cacb9f698a**. Metadata came from a fresh native PS5.1 reflection-only worker. Final host build has zero warnings/errors; both earlier authoring compile failures remain in `host-build-first-failed.log` and `host-build-statname-failed.log`.

The existing F20 host/controller are reused: 78 host support files and 20 controller support files are byte-identical; only Bootstrap, host project and main controller are adapted. No framework is added. All 21 fixture PowerShell scripts parse without errors. The controller selects exactly five mods/eleven images, validates exact provider package hashes, and retains its existing private-path/hash/metadata/protected-tree/process checks. Its F36 baseline expects actual status `failed`. Failed assertions and native errors are never relabeled as passed; Verify always requires independent semantic review.

## Root execution

For each role in `selection.json`, use `controller/scripts/runtime-test.ps1`:

```powershell
$f36 = Get-Content -LiteralPath '<absolute fixture path>/selection.json' -Raw | ConvertFrom-Json
$f36Role = $f36.roles | Where-Object role -eq 'baseline' # then candidate, separate fresh run
& '<absolute fixture path>/controller/scripts/runtime-test.ps1' -Action Prepare `
    -CaseId $f36.caseId -ExpectedBehavior $f36Role.expectedBehavior -NegativeControl None `
    -HdSource Built -BuiltModRoot $f36Role.builtModRoot -HarnessAssembly $f36.harnessAssembly `
    -RobotPackageManifest $f36.packageManifest
```

Omit RunDirectory during Prepare for a new controller-generated identity. Review generated package/image selection, then launch only through root's existing inactive-desktop wrapper and `launch.ps1` with the actual generated run directory and a fresh evidence output directory. Keep raw baseline failures. The scene has 240-second/15,000-tick bounds; inherited owned-process join is 360 seconds. Preserve raw result/events/Player.log/manifest/process receipts, run Verify, and independently inspect all assertions, native errors, identities/materials/queue and cleanup. No transport-only or build-only acceptance is granted.

## Player guidance once native evidence is accepted

Build From Storage is complementary to Hauler's Dream. It reuses an existing packed building when you place a matching building; HD gathers and delivers materials for new construction. Keep Build From Storage for automatic packed-building reuse. If no matching packed building is available, normal construction proceeds. Existing ordinary blueprints are not retroactively converted when a packed building becomes available later. After the paired run accepts the correction, state that HD construction routes preserve normal installation for these reused buildings.
