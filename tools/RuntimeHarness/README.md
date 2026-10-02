# Isolated runtime bootstrap

This test-only mod checks that the intended copied runtime and assemblies load, a real RimWorld map initializes, and at least five actual game ticks run. The default bootstrap does **not** prove any feedback issue fixed. Explicit cooking, storage and delivery cases measure their own bounded scenarios; explicit negative controls validate worker-error capture. No product implementation is replaced by these cases.

The runner is `scripts/runtime-test.ps1`. Its default is preparation only. It never builds, reuses an existing run, kills a process, deletes a runtime, or restarts after a timeout. All copied game/mod files and generated settings/evidence live under `%TEMP%\haulersdream-runtime-tests\<run-id>`. There are no links back to the real installation. The real Config and Saves directories and installed HD assemblies are hashed before and after; their contents are not copied into the fixture.

Build the separate harness explicitly. This project has no HD project reference or post-build deployment target:

```powershell
dotnet build tools/RuntimeHarness/HaulersDream.RuntimeHarness.csproj -c Release `
  '-p:GameManagedDirectory=C:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed'
```

It references the installed game DLLs only for compilation, sets them `Private=false`, and embeds the compiled-against game assembly's SHA-256 in assembly metadata. The runtime compares that value with the copied game assembly. Do not ship the harness with HD or copy game DLLs into its Assemblies directory.

Prepare a new bootstrap using the published Workshop HD binary and actual installed Common Sense:

```powershell
$taskPrepared = & ./scripts/runtime-test.ps1 -CommonSense `
  -PlayerSaveDataRoot 'C:\Users\Arthur\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
$taskPrepared
```

`-PlayerSaveDataRoot` is mandatory for preparation: it identifies the actual player's existing Config/Saves folders. The sandbox account's profile is not necessarily the player profile and is never inferred. Only hashes are read from that directory. Omit `-CommonSense` for Harmony/Core/HD/harness only. To use an already built HD content directory, pass `-HdSource Built -BuiltModRoot '<absolute mod root>'`. This performs no build; when building HD separately, remember that the HD project has an automatic deployment target and must receive an explicit isolated or non-existing `RimWorldModsDir`.

After reviewing `manifest.json`, launch explicitly:

```powershell
$taskLaunched = & ./scripts/runtime-test.ps1 -Action Launch -RunDirectory $taskPrepared.runDirectory
$taskLaunched | Select-Object status, processId, processStartedUtc, statePath, evidenceDirectory
```

Launch returns the live `System.Diagnostics.Process` object as `process`, its PID/start time, and durable `controller-state.json`. Another shell can poll the same recorded identity:

```powershell
& ./scripts/runtime-test.ps1 -Action Status -RunDirectory '<prepared run directory>'
& ./scripts/runtime-test.ps1 -Action Verify -RunDirectory '<prepared run directory>'
```

Select primitive Launch fields before converting its output to JSON: serializing the live Process object can fail after a successful launch. A formatting error is not evidence that launch failed; inspect the recorded process identity before taking further action. For compact Verify output, select `status`, `processStatus`, `problems`, `protectedChanges`, `logReviewCandidates` and `scope`.

`Status` does not interpret elapsed time as process completion. `Verify` requires an exited original process, a complete matching result with every required assertion, required JSON-line events, unchanged protected files and copied runtime inputs, and no whole-log error candidates. It remains `not-verified` while the process runs or required evidence is missing. Whole-log matching is a review aid; inspect Player.log independently as well.

Player.log must be nonempty and contain this run's unique startup and terminal markers plus the exact savedata-override message. Launch also checks empty layout directories (including Saves and evidence) for reparse points, since hashing copied files alone cannot cover empty directories.

The test mod requires both `-hd-test=<explicit case>` and `-hd-harnessmanifest=<absolute manifest path>`. Loading it without the explicit opt-in argument does nothing. It validates executable-adjacent data, save/config directory, mod directory and `Application.consoleLogPath` before running. It records every expected mod's loaded order/root and every important assembly's actual location, SHA-256, version and MVID.

Failures are written to `result.json`; the harness does not rely on throwing assertions because Verse catches component exceptions. `Application.logMessageReceivedThreaded` queues error records with an Interlocked counter; the callback reads no Verse state and performs no file writes. The main thread drains records. Completion closes capture under a shared gate, records the boundary, and revises the earlier error assertion if an error arrived late. Errors before subscription or after that boundary require independent whole-log review; the callback is not a claim of error-free shutdown. On completion it calls `Application.Quit`, preserving normal quitting callbacks. It deliberately avoids `Root.Shutdown`, which recursively clears Unity's shared `temporaryCachePath` outside the savedata override.

## Worker-error controls

Prepare a separate new bootstrap for each control; leave `CaseId` and `ExpectedBehavior` at their bootstrap defaults:

```powershell
$taskControl = & ./scripts/runtime-test.ps1 -CommonSense -NegativeControl WorkerUnityError `
  -PlayerSaveDataRoot 'C:\Users\Arthur\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
```

`WorkerUnityError` calls actual `UnityEngine.Debug.LogError` from a ThreadPool worker after map initialization. `WorkerVerseError` calls actual `Verse.Log.Error` from that worker. `LateWorkerError` records a passing no-errors assertion before injection, then proposes success after the worker finishes; finalization must revise the assertion and downgrade the result.

`TerminalRace` separately exercises the capture boundary: the actual worker callback enters while capture is open and holds the shared gate at a bounded managed barrier. The main thread requests closure and releases the barrier before acquiring that gate. The callback records its sentinel and releases the gate, then finalization closes capture. Recorded ordering must prove callback entry → closure request → capture → closure. This control does not wait for the producer to finish before requesting closure. No Unity/Verse calls or writers occur inside the callback. All controls are explicit in the immutable manifest and only permitted with bootstrap.

A successful control produces a deliberately **failed** runtime result. `Verify` requires the unique configured sentinel in an actual structured `unity-error` event, its callback thread matching the producer and differing from the main thread, and its capture sequence matching the result. An unrelated captured error cannot satisfy the control. `LateWorkerError` and `TerminalRace` require `assertion-revised`; `TerminalRace` also requires the ordered boundary witness. This validates error rejection, not clean startup or feedback completion. Other failed assertions, extra errors, missing evidence or malformed JSON/types invalidate the control. Whole Player.log diagnostics still require review; there is no generic native-loader whitelist.

## L04-O1 storage ownership adapter

Prepare with `-CaseId L04-O1 -ExpectedBehavior baseline-gap` for the published binary, or `-ExpectedBehavior satisfied -HdSource Built` for the changed binary. Common Sense is optional for this case. The fixture creates actual pawn inventories/hand holders, tagged cargo, live production claims and one-cell stockpiles. Ten controls query the actual private HD possession adapter, free-capacity method and patched vanilla storage gate. The controller independently checks the expected numeric answers and fixture preconditions, allowing only the two own-inventory behavior failures on the baseline.

The hand controls install a vanilla current-job object as explicit adapter input; no hauling driver executes. `storage-adapter-verified` is therefore scoped to the adapter, and `baseline-gap-verified` is reproduction evidence only. Automatic pickup/unload convergence remains separate unfinished work. See `docs/plans/storage-ownership-fixture.md` for exact controls and limits.

## BG01 ordinary meal scenario

Use a new run for each expected behavior and binary. Example published-baseline reproduction:

```powershell
$taskBg01 = & ./scripts/runtime-test.ps1 -CommonSense -CaseId BG01 -ExpectedBehavior baseline-gap `
  -PlayerSaveDataRoot 'C:\Users\Arthur\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
```

For the changed build use `-HdSource Built -BuiltModRoot '<absolute built mod root>' -CaseId BG01 -ExpectedBehavior satisfied`. Preparation and launch remain separate. Never edit a prepared run to change settings, assemblies or expected behavior.

The scenario creates a disposable 41×13 room with supported roof, fueled stove, three seeded filth objects, and one seeded ordinary human colonist. Other initially spawned actors and landing objects are despawned only in this generated map. Cooking is priority1 and Cleaning priority4 because CS requires Cleaning to be active. The actor starts fed/rested and receives no forced crafting order. A normal `CookMealSimple` bill requires exactly5 rice and5 potatoes, each runtime-verified at0.05 nutrition, from separate floor stacks over15 cells from the bench. HD gathering/sharing/unloading and the bench switch are on; HD default batching is off. CS advanced cleaning is on and gathering is off; its separate before-work job insertion is disabled to identify cleaning inside the native bill. The actual startup patching mode is checked rather than overwritten.

The observer records actual current jobs/workgivers, item-owner change events and per-tick inventory/carry/floor/product/position/cleaning changes. Owner notifications capture inventory transfers even if two toils advance during one game tick. It does not call HD's builder to manufacture a plan. Both ingredient quantities must coexist in inventory away from the bench before the first return with ingredients. Ordinary native cooking must consume all10 inputs and produce exactly1 meal. All seeded filth must be gone during native cooking **before the first product**, so subsequent ordinary cleaning cannot satisfy the assertion. A300-tick post-product period checks stable counts;9000 scenario ticks without completion yield `inconclusive`.

`baseline-gap-verified` requires a valid fixture and successful native recipe/cleaning, but explicitly records `requestedBehaviorSatisfied=false` because the complete inventory sweep was absent. The structured runtime status is `behavior-gap-observed`, not `passed`. `BG01-behavior-verified` requires the full sweep and all execution assertions under the explicitly requested `satisfied` expectation. Neither result verifies BG02–BG12 or the complete original reporter environment.

Evidence stays local: manifest and original test configs, Player.log, HD's debug log, `events.jsonl`, and `result.json`. No user save is opened and no save operation is part of bootstrap. Raw runtime copies and logs are not PR artifacts. The full workflow and planned feedback fixtures are in `docs/plans/runtime-verification.md`.

## BG02 ordinary four-meal recipe

Use `-CommonSense -CaseId BG02 -ExpectedBehavior baseline-gap` for a fresh published comparison, or `-CommonSense -CaseId BG02 -ExpectedBehavior satisfied -HdSource Built -BuiltModRoot '<absolute built mod root>'` for the changed build, always with the explicit real player save-data root. This is ordinary `CookMealSimpleBulk`: twenty rice plus twenty potatoes, one native four-unit meal product, HD batching disabled. It retains BG01's automatic work and actual CS cleaning configuration, with a12,000-tick scenario maximum and300 elapsed ticks of stable completion.

Read-only native consumption observers record the actual destroyed ingredient identities after the native product operation and before successful native cleanup. Complete execution records retain actual jobs, positions, inventory/hand/floor counts, held-transfer receipts and seeded cleaning events. Validation checks those physical and execution boundaries rather than accepting final totals or behavior flags alone. Ingredient conservation is required at enclosing settled boundaries; nested native product/consumption and SplitOff/TryAdd callbacks can expose legitimate intermediate states.

`BG02-behavior-verified` covers this scene only. A published `baseline-gap-verified` result requires genuine completed cooking with the specified gathering gap; arbitrary failure is inconclusive. Full logs still require independent review, and partial carried stock, other recipes/settings, CE, interruption, competition and persistence remain separately specified scenarios.

## CAP01 and first automatic delivery

`-CaseId CAP01` measures seven real native shelf scenes through the published scalar plan/claim adapters. It requires an explicit baseline-gap or satisfied expectation. It does not execute hauling or validate a replacement allocation interface; any replacement requires its own reviewed fixture binding.

`-CaseId L04-O1-DELIVERY` follows actual automatic bulk pickup of two five-unit Steel stacks and the first unload into a Critical stockpile with ten units of room. Both expectations retain1,200 elapsed ticks after the first unload. Published behavior returns the cargo to source and can later recover by native hand hauling; the satisfied expectation requires first-unload delivery to Critical storage and a continuous stable result. This distinguishes one wasted trip from an endless loop and does not claim the original large mod-list configurations were reproduced.

New fixture source files are not available cases until Bootstrap, controller validation, independent source review and an explicit harness build agree on the case contract. Preparing or compiling a case is not runtime acceptance.

## CAP03-B: actual ASF member-budget capture

Use `-CaseId CAP03-B -ExpectedBehavior satisfied -HdSource Built` with the explicit actual player save root. This case adds the installed Adaptive Storage Framework (3033901359) and Neat Storage (3416243474), with their normal load order. It verifies private copies of all content and eleven ASF1.6 assemblies and additionally hashes both full original mod directories before/after. CommonSense and bootstrap negative controls are not supported for this case.

The fixture uses an actual normally spawned wooden six-slot Neat crate with six resident commodity stacks, an outside generated native Novel, and outside Steel7. Three fresh same-tick projector scopes compare a low provider allowance, a sufficient custom-book refusal, and a sufficient ordinary Steel top-up. Each uses the actual CellProjection returned by a generous group page. Evidence distinguishes operation Work from independently read cumulative scope.Used and includes registry/filter/physical identity checks and disposal. These are conservative source-derived reservations, not instrumented callback counts or elapsed-time guarantees.

Capture integration and fixture source have independent review and compile against the actual game. Actual component-B run c77552 passes all120 assertions and complete independent raw/log/loaded-identity review. Corrected helper357C and controllerD2B103 have completed independent review; the bounded component is accepted with exact manual disposition of its two startup log candidates. Verify retains those candidates for review. Other CAP03 controls, independent callback counts, allocation and original hauling reports remain separate acceptance criteria.
