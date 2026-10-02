# October 2026 release regression replay

Test-only native RimWorld fixture, not shipped with the mod. It does not reference or deploy the HD project. The product DLLs are selected explicitly when preparing each private runtime. `prepare.py` never launches, overwrites a run, deletes a directory, or changes player saves/configuration.

## Findings and evidence limits

The baseline is the downloaded **v1.25.0** release asset, based on `507685246a9ce29476dda52a7c14e0af7d94d96e`. Its HD DLL SHA256 is `4864BB0809008442A451CD6576473BAF416E1102B93532C7C869E20707932655`. The incident candidate DLL is `B7EF8A1EC643697E6B0B10FDB4523CF68CAE60C0AA815F2FD195E3BF5F501D25`. Runtime: RimWorld **1.6.4871 rev591**, actual game assembly MVID `61e41735-6189-4da4-9d21-0260257b5097`.

| Case | Released behavior | Corrected behavior | Scope |
|---|---|---|---|
| Medieval Overhaul old-save load | HD's refuel transpiler throws during MO initialization; only **6/30** saved building identities survive | **30/30** retained; **18** native refuel consumers consume fuel and gain fuel | Genuine MO 1.6.2.2 + installed VEF/Processor; both load orders; corrected save loaded again in another process |
| 100 native refuel eligibility checks | **100** multi-stack ground searches, plus one for the selected job | **0** during eligibility, **1** for the selected job; held-only fuel still eligible | Actual native `WorkGiver_Refuel`; counts of `FindEnoughReservableThings`, not an FPS measurement |
| Allow Tool urgent haul with unclassified quantity hook | Bulk job rejects admission after native StartJob; exact reported reservation error and `Wait` | Original `HaulToCell` preserved; **120/120** items delivered | Actual Allow Tool + HugsLib, plus a test-only no-op foreign quantity patch |
| Supported urgent haul | Native release control delivered 120/120 | Bulk job retained; **120/120** delivered | Native storage, no foreign quantity patch |
| En-route producer with unclassified quantity hook | Produces a bulk job that native StartJob rejects into `Wait` | Declines the optional job | Tests actual producer via reflection; does **not** establish route-selection or resumed-work coverage |
| Supported en-route producer | — | Produces bulk job; native StartJob accepts it | Same narrow producer test |

The no-op foreign patch targets `StoreUtility.NoStorageBlockersIn`. It preserves native acceptance while exercising HD's unclassified quantitative-provider policy. It is **not** a claim to have reproduced a commenter's exact storage-mod combination. An earlier foreign Boolean-predicate test did not reproduce the bug and is not used as evidence of correction.

Most native replays used `-batchmode -nographics`. RimWorld logs graphics/atlas exceptions in that mode. These are functional assertions, **not** clean rendered-game smoke tests or performance benchmarks. Rendered MO checks timed out while loading textures; they do not pass. The native refuel counter test also passed with graphics enabled on an inactive desktop. The existing suite passes 3,141 managed tests and 13 static guards, but those checks alone previously missed these regressions.

**Still open:** developed-colony Haul-to-stack stutter/TPS loss, exact affected storage combinations, rendered MO loading, whole-path en-route continuation, and player-save confirmation. The fixes cannot reconstruct buildings already omitted from an overwritten save; use an intact pre-update save for recovery.

## Run the fixture

1. Build the product with `dotnet build Source/HaulersDream.sln -c Release -p:SkipRimWorldDeploy=true`. The flag prevents the normal build's live-game deployment.
2. Build `tools/ReleaseRegressionProbe/Probe.csproj -c Release`. Override `GameManagedDirectory` and `RuntimeHarmonyAssembly` if necessary. Use the actual game assemblies and matching installed Harmony; do not substitute compile-only reference assemblies.
3. Prepare a fresh named run with `prepare.py`. It requires a frozen manifest from `scripts/runtime-test.ps1`, a seed save, and an extracted release mod. The manifest's game/Harmony files are hash-checked and hardlinked; provider mods and product files are copied privately and all inputs are pinned in `pins.json`. Do not modify the frozen reference runtime, which shares hardlinks. Check provider version/hash differences before comparing runs prepared from Workshop at different times.
4. Launch its generated `args.json` through `scripts/run-on-test-desktop.py` (see example). The launcher uses an inactive private desktop, never switches to it, and only cleans up the process it owns. Never install this probe into a player's mod list.
5. Inspect `output/result.txt` **and** `failure.txt`, `observations.txt`, `trace.txt`, `Player.log`, the pinned product hash, and `desktop/desktop-launch.json`. An exit code of zero does not mean a case passed. A timeout, missing result, producer failure, unexpected native error, or incomplete delivery is a failed/inconclusive test. Preserve negative-control evidence too.

The fixture intentionally enforces `C:/HDQA/release-regressions-20261002/` as its private savedata root. A seed needs a loaded player colony with at least one free colonist and clear fixture coordinates in a map larger than 120×120. Incident tests used the private 20-pawn `Benchmark20.rws`; it contains a removed benchmark component, producing a known seed-load warning. Do not use player saves for the destructive scene-setup modes.

```powershell
python tools/ReleaseRegressionProbe/prepare.py example-urgent-fixed `
  --reference-manifest C:/HDQA/runtime-temp/haulersdream-runtime-tests/pr272-default-smoke-20261002/manifest.json `
  --seed-save C:/HDQA/hauling-bench-20261002/bases/Benchmark20.rws `
  --release-mod C:/HDQA/release-regressions-20261002/release/extracted/HaulersDream `
  --product fixed --mode haul-urgent

$casePath = 'C:/HDQA/release-regressions-20261002/runs/example-urgent-fixed'
$caseArgs = @(Get-Content "$casePath/args.json" -Raw | ConvertFrom-Json)
python scripts/run-on-test-desktop.py --output "$casePath/desktop" `
  --cwd "$casePath/runtime" --timeout 240 -- @caseArgs
```

For a graphics-disabled functional replay append `@('-batchmode','-nographics')` to `$caseArgs`, with the limitations above. Visible, timed FPS runs remain a separate campaign requiring the user's idle-PC confirmation.

Modes:

- `produce --product none --medieval`: create/refuel 30 building definitions, save `RegressionBeforeUpdate.rws`, write exact IDs/defs to `expected.txt`.
- `verify --medieval`: set `--seed-save` to that saved fixture and `--expected` to its identity list; select `--product release` or `fixed`. Repeat with/without `--before` (HD before/after providers). Verify a second process using `RegressionAfterUpdate.rws` for the cold round trip. Never use the failed release's resave as the corrected input.
- `refuel`: 100 native eligibility checks, selected bulk job, then held-only eligibility/job. Released `FAIL` is the expected negative control for redundant search counts.
- `haul-urgent-foreign`: actual Allow Tool worker with the test quantity hook. Release must show rejection; fixed must preserve native work and deliver all 120 items.
- `haul-urgent`: supported-storage control; bulk behavior and complete delivery must remain.
- `enroute-foreign`, `enroute`: narrow producer/admission cases described above.
- `haul`: ordinary hauling control.

Incident raw runs remain local under the enforced root; `docs/` stays ignored. Relevant run names: `mo-exact-release`, `fixed-before-headless`, `fixed-after-headless`, `mo-fixed-roundtrip`, `refuel-released`, `refuel-fixed-v2`, `urgent-foreign-released-v3`, `urgent-foreign-fixed-v2`, `urgent-supported-fixed-v2`, and the three `enroute-*-v2` runs. Earlier invalid probes/timeouts are retained and excluded from acceptance. `tracked-urgent-fixed` verifies the committed fixture's stricter full-delivery assertion and preparation helper.
