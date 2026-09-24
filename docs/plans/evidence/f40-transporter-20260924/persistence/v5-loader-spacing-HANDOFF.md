# F40 persistence v5 — native loader admission corrected

Ready for root source review, **not native acceptance**. No product/controller/settings/provider/bootstrap change, Prepare or game launch was performed. The full v4 source, host/controller sources, selection, contract, scripts and metadata are preserved under `v4-before-loader-spacing`; the original host build and failed `native/b373faec5d5946349d38958c9a25a29f` remain unchanged. Every retained failed-capture file is pinned in `v4-before-loader-spacing/failed-native-capture-pins.json` and rechecked by the new input audit.

## Confirmed cause of the v4 failure

The failed producer records **43/46 assertions, U0, 459 events**, with no checkpoint. This is a fixture admission failure; it is not evidence of corrupted save/load behavior.

- Event52, tick24: actual `JobDriver.Notify_Starting` observes original loader **10** admitted with `Uranium39291`, count **35**. L (`Human39251`) is at `(95,0,137)`; that is the same cell as the middle stack created by `Ground("Uranium",200,Cell(-31,12))`.
- Event57, still tick24: the existing `live-carry-setting-input` assertion fails with **planned=0**, cursor1, delivered0, toil7 and tagged `Uranium39296`. Native StartJob has already traversed instant toils and picked up the whole35 before returning from `Order`, while the setting is still1.0. Pausing game ticks does not prevent synchronous toil initialization.
- Event62, tick593: original loader10 succeeds with delivered35. Its following prioritized jobs32/71/etc are genuine successors; they cannot satisfy the fixture's deliberately exact original-loader identity requirement for saved nonzero driver progress. The fixture kept waiting until its medical guard rejected the scene at tick31913.

Source confirms the ordering: `SweepWalk.MakeToil` starts the real pather with `ClosestTouch`; `JobDriver_LoadInBulkBase` uses an instant pickup toil that reads the current carry ceiling, decrements `countQueue` and advances the cursor when exhausted. At the old same-cell input, there was no walk boundary before pickup. The persistent driver's `deliveredUnits` increments only on actual container transfer. Its loop returns to the original remaining plan when the cursor has not exhausted it, otherwise ends successfully and may create a successor. The existing native `JobDriver` excerpt and observed start/end events distinguish this from an observer pooling artefact.

## Exact v5 delta and reachable save boundary

`v5-loader-spacing.diff` changes only `TransporterPersistence.Scene.cs` and `TransporterPersistence.cs`:

1. Start L at **Cell(-35,12)** instead of Cell(-30,12). The original200 uranium still spawns at cells(-31,-30,-29,12); the separate one-unit custody source, all cargo totals, manifests, pawns, capacities, flags and native queue blockers are unchanged.
2. Immediately after the actual command, observe and require its original load job, positive remaining plan, cursor0, delivered0, no Uranium in inventory/hands, and **native `ReachabilityImmediate.CanReachImmediate(...ClosestTouch...) == false`** for every planned source. Failure stops the setup immediately. The original oversized-plan assertion and actual1.0→0.05→0.2 setting sequence remain; any failed setup assertion now terminates before starting the wait.
3. During `producer-loader` and `producer-boundary`, stop immediately if that original loader ends or is replaced. A successor never substitutes for the saved driver.

The source gives a real interval for the unchanged `ReadyCheckpoint` oracle. With the observed35-unit admission and live0.2 ceiling, native pickup should take7 and leave28 in the original plan, then deposit7 into the pod at Cell(8,12). The original cursor remains on its source and the same job must walk back across roughly39 cells before taking the next7. I/H start only then; each is two cells from its own pod and needs just its native touch walk and configured8-tick pull to own positive cargo. They do not need to travel to storage before saving. This fits comfortably within that loader return trip and Q's2200-tick non-idle blocker under the controlled healthy native worker inputs. These timing/count values are source-based expectations for the next run, not fabricated observations or edits to a job.

No added delay, movement-speed/health override, job-count/cursor assignment, substituted loader, disabled observer or weakened serialized/physical oracle exists. `ReadyCheckpoint`, native save/XML checks, before-first-tick equality, positive restart, disabled-start and subsequent policy controls are byte-for-byte unchanged. The first native run must still prove the interval and all acceptance conditions.

## Build and pinned selection

- Host: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f40-persistence-20260924/HarnessBuild-v5/Assemblies/HaulersDream.RuntimeHarness.dll`.
- SHA256 **B9C41DF0E2CFE86E3A7FE8F5A3AFB24230AA90184BE319DD76EC5B988D1C1D8F**; MVID **831b8caa-8db5-450d-a9aa-b29665c8cf6c**.
- Selection SHA256 **B055D47233E9FA45FCFEB3260391D5B3D1F95EBC8B606B00348DEA2D6B255519**.
- Build **15.26s, zero warnings/errors**. Build/metadata workers ran hidden, joined exit0 and have retained receipts. The actual native reference hash is **5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A**. Metadata reading used the existing isolated PS5 reflection-only reader without executing host/game methods.
- `v5-input-audit.json` and current `input-audit.json`: **357 checks passed**. Preserved and current source bytes, unchanged candidate contents/census, providers/controller/bootstrap/settings, native capture, actual compiled source, host metadata and native-reference identity are checked.
- Product stays frozen at HD **E62F1C1EB45B31031F8AEF705280550D0DF8C3ACEAD6CB48461BCE0D41BDD307** and Core **83EB8EB0E348A3FB6B0066640BAA1278C2B1ED6844EA31A774D3AFE18DAD43C6**.

Only selection fields `status`, `scene`, `harness`, `build`, `buildReceipt`, `sourceBeforeBuild`, `buildScript`, and new metadata/metadata-receipt pins changed. Native controller admission is unchanged. Use current `selection.json`; root alone prepares/launches through `scripts/run-on-test-desktop.py` and the retained `launch.ps1`, with the short runtime `TEMP=TMP=C:/HDQA/runtime-temp`. The required sequence remains original producer → independent checkpoint review → fresh RESTART and separate DISABLED consumers of those same originals. No v4 failed run becomes an accepted producer.
