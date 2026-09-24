# F45 production: integrated native harness

Latest accepted execution: `0e63f30388344a16ad54559991bdca34` passes 67 assertions and eight actual native crafting iterations. Independent [runtime review](runtime-review.md) accepts its exact accounting, schedule behavior and corrected saved ownership: all four checkpoints deep-save the three parked starting pawns and preserve PB state. There are no prior missing-deep-save warnings. Actual fresh-process restart remains separate. Raw evidence and four saves are retained in `native/0e63f30388344a16ad54559991bdca34/`. Earlier executions below remain historical, including their failures.

Latest execution: `a2f2fabc1bfe4d3e89a8391af302b319` completed eight real native recipe iterations and eight HD gathering jobs, with exact four/sixteen meal totals, cooldown and controlled-boundary resumption. All 67 assertions passed; the owned process joined with exit 0 and zero captured Unity errors. **The four checkpoint saves are not accepted for restart validation:** full-log review found unsaved references to the three despawned starting pawns. Their ownership must be corrected and the saves regenerated. Native work evidence and flawed save evidence remain distinct. Actual records, saves, source and build are retained in `native/a2f2fabc1bfe4d3e89a8391af302b319/`.

The preceding `ba3150e758cd4b619824448f55277463` run stopped at actor eligibility before any productive work. Its source and failed evidence remain in `native/ba3150e758cd4b619824448f55277463/`. The successor used the existing bounded native capable-pawn selection; it did not change pawn incapabilities or product code. The copied-bill settings defect was observed but still needs PB-only comparison; it is not a repaired behavior.

The original build/integration description below is historical. Its initial DLL hash does not identify the later corrected actor-selection build; each executed run retains its actual source and `built-inputs.json`.

Integrated and built successfully on 20 September 2026: **zero warnings, zero errors**. No Prepare, Launch, game or input operation was performed by this integration author. Native production and restart evidence remain pending.

This reuses `../f04-rendered-20260920` directly. Only `host/Bootstrap.cs`, its project file and `controller/scripts/runtime-test.ps1` change among their existing sources; all other 78 host C# files, all 18 other controller scripts and the F04 scenario are byte-identical. One additional source, `src/PeriodicProduction.cs`, is compiled into the host. `host.diff`, `project.diff`, `controller.diff` and the three `*-reuse.diff` files show the entire integration delta. The controller retains its private runtime, protected input snapshots, exact copying, native metadata workers, result reader and owned launch/join mechanism.

The initial exact scenario did not compile because the native game exposes neither `WorkTypeDefOf.Cooking` nor a public `bill.pawnRestriction` field. `scenario-api.diff` contains only the two corrections: resolve the actual Cooking definition and call the actual public `SetPawnRestriction`. `build-first-errors.log` preserves that failure; `build.log` records the successful successor. The original TEMP source remains unchanged. `scenario-notes.md` describes the behavior and limits; its original author-only build status is superseded by this actual integrated build.

The integrated case is `F45-PB-PRODUCTION`, using candidate HD `32D57A64…`, Core `D623F164…`, and actual PB `AAE78E0D…`. The closed package order is Harmony, Core, HD, PB, harness; the existing ten-image metadata roster gains only `PeriodicBills`. The controller checks the full current HD/Core/PB hashes and actual 13-file PB package count, copies PB through the existing verified tree routine, and includes its source tree in protected snapshots. This first integrated profile is candidate-only; no-HD PB baseline still requires its separately selected host/profile context. Do not claim that baseline ran from this candidate profile.

The host waits for genuine PlayerHasControl before Begin, calls Update every frame and Dispose before terminal result. The scenario queues actual menu construction for native Root.OnGUI Layout/Repaint; subsequent native work is programmatically dispatched from the actual unforced WorkGiver result. The 240-second scenario bound sits within the unchanged 300-second host and 360-second owning launch bounds. A passed production result does not turn the separately recorded upstream PB clone defect into a repair, or saved checkpoints into a reload pass.

Measured build output:

- Host DLL SHA256: `F6AC5C07EA49274E9B7963BE96FA446B14FBC9A3E2FE1A7A5CA6C81E41C7A961`
- Host PDB SHA256: `C9FE1107438DBA2BC36674DE20FC188DF888C684B9DA4EB7893F6F4D5EC384A5`
- Host MVID: `cba52956-b74d-4b04-b59d-b16837298edb`
- Scenario SHA256: `F725242ECFB6125D2E3015227A032B9752F4B13272CE0C59A23A00C15EA66A8E`
- Controller SHA256: `8A202CC6ADFEE7B8FA44C8694615DAEDC83D38D3E9049C03BE9BE67D4A08EDEB`

`built-inputs.json` retains actual build source/output hashes. `integration-check.json` records the changed-file comparison, zero PowerShell parser errors and actual compiled public entry signatures. No speculative runtime GUID or future receipt is present.

## Existing operation reuse

From the repository root, the actual completed build command was:

```powershell
& './docs/plans/evidence/f45-production-20260920/build.ps1'
```

It uses the existing .NET executable, offline reference package cache and the already selected native/Harmony references, emits only this evidence folder's obj/ and Assemblies/, and has no deployment target. The copied SDK/NuGet configuration and full arguments are retained in `build.ps1`.

After independent changed-code/controller review, root can use the existing adapted scripts sequentially:

```powershell
& './docs/plans/evidence/f45-production-20260920/prepare.ps1'
# Read the actual newly written prepared.json and private preparation before launch.
& './docs/plans/evidence/f45-production-20260920/launch.ps1'
$production = Get-Content './docs/plans/evidence/f45-production-20260920/prepared.json' -Raw | ConvertFrom-Json
& './docs/plans/evidence/f45-production-20260920/controller/scripts/runtime-test.ps1' -Action Verify -RunDirectory $production.runDirectory
```

Prepare uses the actual existing `content-candidate` with the selected 32D5/D623 pair; PB's root is the retained downloaded Workshop package. It generates the run GUID itself and refuses to overwrite an existing receipt. Launch retains its owned process identity, finite join and outcome. Full actual result/events/logs and all checkpoint saves must be retained and reviewed. Verify deliberately leaves semantic acceptance for that review, matching the reused capture-controller convention. Full saved-game restart is the next focused stage described in `scenario-notes.md`.
