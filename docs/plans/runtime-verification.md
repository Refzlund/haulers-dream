# Isolated RimWorld runtime verification

Status: investigation complete; the bootstrap-only controller and harness are implemented in `scripts/runtime-test.ps1` and `tools/RuntimeHarness/`, pending root build/review/runtime validation. No private runtime was copied or game launched by the implementation agent. Inspected 2026-09-07. This document supplies the runtime part of the September feedback goal; it does not replace the completion matrix or classify any feedback as resolved.

The active goal explicitly requires appropriate in-game verification with disposable saves and isolated data. That current instruction supersedes the older `CLAUDE.md` and August plan instructions saying the user performs all game tests. This investigation did not launch RimWorld, build a mod, alter the installed game, alter the user's settings or saves, or change git state.

## Observed runtime and inputs

| Resource | Observed state |
| --- | --- |
| Game installation | `C:\Steam\steamapps\common\RimWorld` |
| Game version, `Version.txt` | **1.6.4871 rev590** |
| Executable / UnityPlayer product version | **2022.3.35f1 (011206c7a712)** |
| Installed `Assembly-CSharp.dll` file version | **1.6.9676.17735**; this is assembly file metadata, not the player-facing RimWorld build |
| Current repository compile reference | `Source/Directory.Build.props`: **Krafs.Rimworld.Ref 1.6.4518**, .NET Framework 4.8, Harmony compile package 2.3.6 |
| Installed official content directories | Core, Biotech, Odyssey; Royalty, Ideology and Anomaly were not present in `Data/` |
| Installed Common Sense | Workshop item **1561769193**, package `avilmask.CommonSense`, `1.6/Assemblies/CommonSense.dll` |
| Common Sense comparison source | Sibling checkout `../CommonSense`; its 1.6 DLL is byte-identical to the installed Workshop DLL |
| Installed Harmony | Workshop item **2009463077**, package `brrainz.harmony`; `LoadFolders.xml` selects `/` and `Current` for RimWorld 1.6 |
| Harmony runtime DLL metadata | `Current/Assemblies/0Harmony.dll`: **2.4.1.0**; loader `HarmonyMod.dll`: **2.4.2.0**; record both rather than conflating their versions |
| Published HD baseline available locally | Workshop item **3742459652**, `HaulersDream.dll` assembly version **1.24.0.0** |
| User's local installed HD copy | `RimWorld/Mods/HaulersDream`, assembly version **1.23.0.0**; it is a different binary from the Workshop release |
| Decompiler | `C:\Users\Arthur\.dotnet\tools\ilspycmd.exe`, **9.1.0.7988** |
| Build tools found | `C:\Program Files\dotnet\dotnet.exe`; `C:\Users\Arthur\.bun\bin\bun.exe` |
| Isolation storage feasibility | Game `Data/` about 273 MiB; managed/Unity data about 394 MiB; Mono about 9 MiB; executable/UnityPlayer and supporting files add about 31 MiB. Drive C had about 132 GiB free. Full copies are practical. |
| Active game observation | `Get-Process -Name RimWorldWin64` returned no process during this investigation. This must be rechecked immediately before an eventual run. `Get-CimInstance Win32_Process` was denied in the sandbox, so it supplies no process evidence. |

Observed SHA-256 values:

| Binary | SHA-256 |
| --- | --- |
| Installed game `Assembly-CSharp.dll` | `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A` |
| Installed game `UnityEngine.CoreModule.dll` | `C5C58EA254834291780A1D6C388C241443D07167B4A4B890A23C9494F626DDBA` |
| Installed game executable | `4C30E2105B49F2D0130F5D861947FBE82B866042299DA48EF1C8CF6979A8564D` |
| Installed `UnityPlayer.dll` | `E0C489F1683609247FEDE45EA049D30BAA4F4542060E308E25C0EC87F6C0FB96` |
| Installed Harmony `0Harmony.dll` | `353DAAFEC180BB8E7BBE4DA78F2A7CDC78067392E3A4E79DC8E7AF295F2371E6` |
| Installed `HarmonyMod.dll` | `408C2CD72B45DA8C70C0C538B2B346C8DCBB5BDED066373F6A3EB8B26615A2E7` |
| Common Sense, installed and sibling checkout | `8FD51F969912CEDBC11CD72E89D40EC713448AFD86CE0BEEF8CE2BDF03A0E2CA` |
| HD Workshop v1.24 assembly | `D55644ACBE064F4F6A37C82205F48EEAF61264100C92AC568BDCB612FE8DCC2D` |
| User local HD v1.23 assembly | `5DA06E7466D23FE8120AF23934224A4709386647E33264158B5A6983BB5C211D` |

A historical fix test must identify the actual baseline binary and installed game build. Running the Workshop v1.24 binary on today's game can establish a current reproduction, but it does not prove behavior on the reporter's older game build. Preserve this distinction in every result.

## What the installed game actually supports

These findings come from decompiling the installed `RimWorldWin64_Data/Managed/Assembly-CSharp.dll`, rather than assuming command-line options from another RimWorld version. Reproducible inspection command:

```powershell
& 'C:\Users\Arthur\.dotnet\tools\ilspycmd.exe' --disable-updatecheck `
  -t Verse.GenFilePaths `
  'C:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll'
```

The inspected type output is retained locally under `%TEMP%\haulersdream-runtime-investigation-20260907`. Do not commit or redistribute the game's decompiled source or copied binaries.

| Runtime seam | Verified behavior and consequence |
| --- | --- |
| `Verse.GenCommandLine.TryGetCommandLineArg` | Accepts `-key=value` as one argument, case-insensitively. It splits on `=` and requires exactly two pieces. Use absolute test paths without `=`. |
| `Verse.GenFilePaths.SaveDataFolderPath` | Reads `savedatafolder`; logs `Save data folder overridden to ...`; creates that directory if necessary. Config, Saves, Scenarios, Screenshots and DevOutput are beneath it. |
| `Verse.GenFilePaths.ModsFolderPath` | Derives `Mods` from the parent of `UnityData.dataPath`. There is no mod-directory command-line override in this implementation. `-savedatafolder` alone does **not** isolate deployed mod files. |
| `Verse.ModLister.RebuildModList` | Scans executable-adjacent `Data`, executable-adjacent `Mods`, then subscribed Workshop content. Active package IDs determine which content loads. A local/Workshop duplicate gives the Workshop instance a suffix. Assert the loaded root paths, not just package names. |
| `Verse.ModsConfig` | Reads `Config/ModsConfig.xml`; missing config can reset the active list. Newly discovered official expansions may activate automatically unless already listed under `knownExpansions`. Seed the exact minimal active list and list installed expansions as known. |
| `Verse.QuickStarter.CheckQuickStart` | `-quicktest` loads the Play scene once. This is a real game startup, not a test-result-producing command. |
| `Verse.Root_Play.SetupForQuickTestPlay` | Builds a Crashlanded game, random world seed, random starting tile, 250-cell map and Cassandra/Rough storyteller. A bare quicktest is nondeterministic and does not reproduce a feedback scenario on its own. |
| `Verse.Game.InitNewGame` / `LoadGame` | Calls `GameComponentUtility.StartedNewGame` or `LoadedGame`. `GameComponentUtility` also dispatches Update and Tick methods. A separate test-only `GameComponent` can set up and observe an actual running game. |
| `Verse.GameComponentUtility` | Catches component exceptions and logs them. An assertion thrown from a test component will not reliably fail the process. The harness must explicitly write a terminal failed result and the controller must validate it. |
| `Verse.TickManager.DoSingleTick` | Advances real map/world/pawn ticking and then game-component ticking. Avoid recursive ticking inside `GameComponentTick`. Use that callback for observations; use Update for bounded control transitions after long events finish. |
| `Verse.GameDataSaveLoader.SaveGame` / `LoadGame` | Real Scribe save and scene/world load are available. Saving catches exceptions and returns void, so validate the resulting file and successful load events; returning from SaveGame is not proof. |
| `Verse.SaveGameFilesUtility.GetAutostartSaveFile` | With `Prefs.DevMode` enabled, a save named `autostart.rws` in the isolated Saves folder is loaded at startup. This supports a genuinely fresh-process reload test without touching real saves. |
| `Verse.Steam.SteamManager.InitIfNeeded` | Initializes the Steam API and may request restart if the application context is missing. There is no `-nosteam` branch in this implementation. The installed `steam_appid.txt` contains `294100`; copy it with the private runtime and verify the started process remains the isolated executable. |
| `HaulersDreamMod` constructor | Calls `HDDebugLog.ConfigureDirectory(Application.consoleLogPath)`. Redirect the Unity player log as well as savedata; otherwise HD's own debug trail can still go to the real user directory. |
| `Verse.Root.Shutdown` | Recursively deletes `GenFilePaths.TempFolderPath`, which is Unity `Application.temporaryCachePath`, outside the savedata override. The harness must flush evidence and use `Application.Quit` directly. Normal `Application.quitting` callbacks still run, including HD's log flush. |

The installed `Source/` directory contains only 43 selected example files. It is not a full version-authoritative source tree. Prefer the actual assembly for missing or changed APIs.

## Isolation layout and launch protocol

Recommended root: a new per-run directory under `%TEMP%\haulersdream-runtime-tests\<run-id>`. The controller must resolve and validate its absolute location before writing. Use full file copies, not hard links or junctions back into the live game or user data.

```text
<run-id>/
  runtime/                 private game executable, Unity data, Mono, Data/
    Mods/
      Harmony/             copied complete installed mod
      CommonSense/         copied complete installed mod for the selected case
      HaulersDream/        immutable published baseline OR tested build
      HaulersDreamTests/   separate, non-shipped harness mod
  SaveData/
    Config/                freshly generated test-only settings
    Saves/                 disposable fixture and checkpoint saves
  evidence/
    manifest.json
    Player.log
    HaulersDream-debug.log
    events.jsonl
    result.json
    screenshots/
```

1. Copy the game's executable, UnityPlayer, MonoBleedingEdge, RimWorldWin64_Data, needed Data content, steam_appid.txt and supporting runtime files into `runtime/`. Do not copy the live `Mods/` directory wholesale. Copy selected compatibility mods into this runtime's own Mods directory. No writes to the Workshop directories are required. The exact initial copy set observed on this machine is:

   - Directories, recursively: `Data`, `MonoBleedingEdge`, `RimWorldWin64_Data`.
   - Root files: `RimWorldWin64.exe`, `UnityPlayer.dll`, `UnityCrashHandler64.exe`, `steam_appid.txt`, `Version.txt`, `ScenarioPreview.jpg`, `SteamInputDefaultConfiguration.vdf`, `SteamInputDefaultConfiguration_SteamDeck.vdf`, `SteamInputDefaultConfiguration_SteamFrame.vdf`, `EULA.txt`, `Licenses.txt`, `ModUpdating.txt`, `Readme.txt`.
   - Make a new empty `Mods` directory. Copy the complete selected Harmony and Common Sense directories there, preserving their About, LoadFolders, version folders, patches and resources. Copy HD from one explicitly chosen baseline or build staging directory, never the user-local v1.23 deployment by default.

   Do not include `Source` or the installation's existing `Mods`. Verify file-by-file SHA-256 equality for copied immutable runtime and dependency files before the first launch; inventory unexpected additions rather than silently following reparse points. These files stay local and are not PR artifacts.
2. Create a fresh ModsConfig containing Harmony, Core, the selected DLCs, the actual compatibility mod(s), HD and the test harness in the intended order. For minimal #258 testing, no DLC is required. Seed `knownExpansions` with Biotech and Odyssey so the game does not silently expand that case. Record exact loaded order and root directories after startup; fail if it differs from the requested case.
3. Build HD with `-p:RimWorldModsDir=<isolated-runtime>\Mods`, or disable deployment by passing a non-existing sentinel directory. **The current HD project automatically deploys after every build when that property names an existing directory and otherwise defaults to the real installation's Mods directory.** Do not use an unqualified build or deployment script. The harness project must have its own output and no inherited deploy target.
4. Keep a published baseline run and a changed-code run separate. Never overwrite a running process's assemblies. Record the git revision, working-tree diff hash, file hashes for HD and Core, compiler reference version, loaded assembly versions/MVIDs, game version, load order and settings in the manifest.
5. Before launch, collect read-only hashes of real Config **and Saves** files and installed HD binaries. Recheck the live process list. Do not kill or reuse a process that was not created by this test controller. Hashing a real save is the only bootstrap interaction with it; never load or copy its contents into the test fixture.
6. Launch the isolated executable directly with explicit isolated savedata and log destinations. First validate a minimal bootstrap case. The following is a **proposed command shape**, not a command already executed:

```powershell
$taskRunRoot = '<resolved absolute per-run test directory>'
$taskExe = Join-Path $taskRunRoot 'runtime\RimWorldWin64.exe'
$taskSavedata = Join-Path $taskRunRoot 'SaveData'
$taskLog = Join-Path $taskRunRoot 'evidence\Player.log'
$taskArgs = @(
  ('"-savedatafolder={0}"' -f $taskSavedata),
  '-logFile', ('"{0}"' -f $taskLog),
  '-screen-fullscreen', '0',
  '-screen-width', '1024', '-screen-height', '768',
  '-quicktest',
  '-hd-test=cs-clean-only-nonmix'
)
$taskProcess = Start-Process -FilePath $taskExe `
  -WorkingDirectory (Split-Path -Parent $taskExe) `
  -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
```

`-hd-test` is a new test-harness argument to implement, not a vanilla option. The explicit Unity log/window options must be verified in the bootstrap result, including `Application.consoleLogPath`. Do not assume `-batchmode` or `-nographics` works with RimWorld's UI/shader initialization; a normal graphical Unity process is the initial target. A hidden start request may not keep every Unity window hidden; observe the actual process behavior without making unsupported claims.

7. The harness's very first action must validate `GenFilePaths.SaveDataFolderPath`, `GenFilePaths.ModsFolderPath`, `Application.consoleLogPath`, active package order, and actual loaded assembly locations against the manifest. Refuse to generate or save any fixture if isolation checks fail. An in-process guard cannot prevent earlier startup writes if command-line isolation was wrong, so the external controller's preflight validation is also mandatory.
8. Poll the returned process handle and durable evidence. A timeout is an inconclusive run until the actual handle and logs are inspected. Give startup, generation, scenario execution and shutdown separate timeouts. Record a terminal failure before requesting a controlled shutdown; if cleanup requires terminating the process, target only the PID created by this run after rechecking its executable path.
9. Validate terminal result, required events, expected case count and artifact hashes. A zero exit code, surviving process, quiet log, or one `PASS` line is insufficient. Check the real Config and mod hashes after the run. Do not sync, upload or commit saves, logs or game binaries automatically.

## Test-only harness design

Use a small separate .NET Framework 4.8 mod loaded only in the isolated runtime. Compile against the same declared RimWorld references as HD, then verify actual-runtime binding. It should not be shipped inside HD's assembly or become a required compatibility dependency.

Implemented project: `tools/RuntimeHarness/HaulersDream.RuntimeHarness.csproj`, with explicit `TargetFramework=net48`, `LangVersion=10.0`, `IsPackable=false`, `IsPublishable=false`, and output under its own ignored `bin/Release/` directory. This location avoids inheriting an HD deployment target. It uses compile-only direct references to the actual installed game and Unity CoreModule, plus the .NET 4.8 reference-assembly package; it embeds the compile-time `Assembly-CSharp.dll` SHA-256 for runtime checking. No HD project reference is necessary for black-box runtime assertions: inspect the actually loaded HD assembly and settings through a small reflected adapter, which also permits testing the unmodified published baseline. If observational Harmony hooks or internal Verse access become necessary, add the respective compile-only dependencies explicitly and document the accessed seams.

An alternative when a needed API differs from the pinned reference package is compile-only direct references to the private runtime's `RimWorldWin64_Data/Managed/Assembly-CSharp.dll`, `UnityEngine.CoreModule.dll`, and, only when actually used, `UnityEngine.JSONSerializeModule.dll` / `UnityEngine.IMGUIModule.dll`. They are all present in the installed runtime. Set `Private=false`; never ship game DLLs or a second Harmony DLL in the harness. Do not mix both complete RimWorld reference sources in one compile. Record whichever reference source was selected. Framework `System.Runtime.Serialization` or Unity's `JsonUtility` can serialize the small evidence schema without adding a mod dependency; choose one schema and test its failure-output path.

The harness needs no NUnit runtime inside RimWorld: use explicit case/assertion records whose failed state reaches `result.json`. Independent unit tests may test the controller's manifest/path/result handling outside the game, but they do not replace the in-game case execution.

The repository's existing `HaulersDream.Tests` project references only `HaulersDream.Core`. Those tests remain useful but cannot instantiate and run the patched Verse job machinery. HD's existing “Make colony (stress test)” dev action and vanilla `RimWorld.Autotests_ColonyMaker` provide scenario-generation examples, but neither supplies focused acceptance assertions. Sibling Vehicle Framework has real in-game `DevTools.Testing` tests and design examples; its referenced DevTools/SmashTools/CoreLib binaries were not found in the inspected `1.6/Assemblies` path. Do not add that entire framework to the minimal #258 environment just to obtain a runner.

Recommended components:

- A `Mod` entry point validates the opt-in argument and installs observational Harmony patches on relevant boundaries. Test hooks must not replace the job builders or suppress errors in the implementation under test.
- A `GameComponent` receives `StartedNewGame`, `LoadedGame`, Tick and Update. Defer map mutation until `LongEventHandler.ExecuteWhenFinished`, the map and region grids exist, and `ProgramState.Playing` is reached. Keep setup, running, checkpointing and completion explicit states.
- A deterministic fixture defines exact cell positions, floor types, reachable routes, benches, bills, stack counts, pawn generation seed, pawn skills/capabilities/work priorities, allowed area, home area, settings and expected products. Initial world quicktest randomness must not determine the test layout. Prefer a fixture save generated once per baseline/runtime environment for repeat runs; record its hash. Never use the user's active colony as the fixture.
- Let automatic work selection and ordinary pawns execute the jobs. Use the normal player ordering path for forced-order cases. Do not directly call HD's replacement builder and treat the returned Job object as runtime success.
- Observe selection, StartJob, driver type/toil changes, reservations, carried/inventory contents, map stack identity/count, bill completion and EndCurrentJob/cleanup. Record both successful and failed job endings. Deduplicate repeated scan-only trace output while preserving chosen-job execution evidence.
- Emit JSON lines with run/case identity, tick, pawn ID, job identity, phase and measured values. Emit an explicit final structured result with every acceptance assertion and a separate inconclusive/failure state. Persist before calling the game's normal shutdown path. Exceptions must mark the case failed, because Verse catches many exceptions and continues.
- Keep assertions outside HD's own policy helpers where possible. Use physical item counts, consumed inputs, produced outputs, actual completion and cleanup as the oracle. Instrument helper decisions for diagnosis, not as the sole proof.

Test fixture changes can freeze incidental hunger/storyteller hazards when those are outside the scenario, but must record that intervention. Interruption/needs/priority feedback requires separate cases with those systems active. Do not permanently disable competing vanilla systems merely to make a broad scenario pass.

## First suite: Common Sense gathering (#258 and related stove request)

The parallel focused investigation established from the actual Common Sense DLL that `Settings.adv_cleaning` and `Settings.adv_haul_all_ings` are the operative static fields. Its `JobDriver_DoBill.MakeNewToils` prefix takes over when either is enabled; cleaning-only still invokes vanilla ingredient-collection toils. These settings and the chosen recipe/bench must appear in each runtime result.

The current HD inventory-gather route excludes mixed-ingredient recipes, single-item stack limits, forced/player-forced jobs, nonordinary/autonomous benches, mechs, usable tagged stock and fewer than two eligible floor stacks. This creates an important coverage trap: an ordinary nonmixed recipe can pass while the related stove/meal request remains unmet. Batch-only gathering does not satisfy a request for ordinary bill gathering.

| Case | Required distinction and observations |
| --- | --- |
| Eligible nonmixed recipe, two separated stacks | One ordinary human colonist, ordinary bench, automatic DoBill, HD gathering enabled globally and per bench. CS cleaning on / haul-all off. Verify the selected runtime job gathers both required stacks in the intended inventory trip, returns to the bench, hands off correctly to actual DoBill, cleans when configured, consumes the correct inputs and completes the product. |
| Meal stove, `allowMixingIngredients=true` | Run an ordinary meal bill, not an HD batch. Use two separated ingredients required by the selected recipe. Apply the same cleaning-on / haul-all-off settings. This must cover the requested meal behavior rather than quietly inheriting the current exclusion as a passing expectation. First establish exact intended mixed-ingredient semantics with the focused implementation plan. |
| Concrete ordinary simple-meal fixture | `CookMealSimple`, one production repeat, fueled stove, 5 rice and 5 potatoes in two separated floor stacks, each runtime nutrition verified as 0.05. Together they supply 0.5 nutrition. Observe gathering all 10 required units before the ingredient-carrying return to the bench, then exactly 1 `MealSimple`, correct consumption and no repeat gather cycle. Allow any pre-work cleaning trip explicitly in the trace; distinguish it from returning with only the first ingredient. |
| Concrete vanilla four-meal fixture | `CookMealSimpleBulk`, one production repeat, 20 rice and 20 potatoes supplying 2 nutrition, producing exactly 4 `MealSimple`. This is a vanilla x4 bill, not HD's batch mode. The #258 log contains scan-time `CookMealSimpleBulk` evidence; that does not establish final selection, so the harness must demonstrate actual execution. Also test 12 already-carried milk plus enough floor food to meet the same 2-nutrition requirement, using runtime nutrition and counts rather than assuming all foods have identical values. |
| Settings ownership matrix | Actual Common Sense absent; present with both settings off; cleaning on / gathering off; cleaning off / gathering on; both on. HD global gathering off/on and bench off/on must each be covered where behavior differs. Record the complete actual settings rather than relying on defaults. |
| Changed load order | Run CS before HD and HD before CS for the relevant cases, recording actual Harmony patches on DoBill. Both orderings must retain the intended gathering and cleaning behavior. |
| Partial inventory and floor stock | Give the pawn some required materials, leave the remainder in separate floor stacks. Verify no double-counting, over-consumption, abandoned tagged cargo, extra products or repeated net-zero trips. |
| Interruption and retest | Interrupt during gathering and during handoff, then allow normal work to resume. Verify claims release appropriately, subsequent work succeeds, and items are conserved. |
| Save/load and fresh-process load | Save during a gathering job and after handoff. Load through real Scribe; separately restart the entire runtime on the disposable saved fixture. Verify continuation, final product counts and cleanup without relying on preserved process caches. |
| Ineligible or alternative worker path | Forced work, one available stack, mech and autonomous bench require explicit intended behavior, then evidence that they either use the supported alternative or preserve vanilla behavior without breaking the job. Do not treat a silent exclusion as adequate support when feedback requests that path. |

Each run must capture `recipe.defName`, `allowMixingIngredients`, bench type, worker type/faction, all selected ingredient stack IDs/counts, relevant HD gates and the final selected job/driver. Existing `[BillPrep]` and `[Batch]` diagnostics show plan construction, which is insufficient to establish that the pawn performed that plan. Bound the scenario by game ticks and progress, not just elapsed wall-clock time or absence of red errors.

For before/after evidence, run the same fixture against the local published Workshop v1.24 baseline and the changed build. Reconfirm expected baseline failure rather than requiring every matrix case to fail historically. Positive controls must succeed without invoking the defect, and a fixture precondition failure must be reported as inconclusive instead of blaming the mod.

## Extending the harness without weakening coverage

After the first suite is executable, reuse the runner while giving each feedback cluster its own fixtures and oracles:

- Storage/concurrency: one constrained destination and 2–8 real haulers, existing incoming claims, full/partially filled cells, forced and automatic orders, storage filters, actual mod storage components. Verify completed deliveries, simultaneous reservations, physical capacity and eventual quiescence; a Core concurrency simulation is supplementary.
- Repeating haul/drop cycles: count completed trips, target changes and physical net progress. Include successful jobs that reverse the previous job. Do not make “warning no longer emitted” the criterion.
- Reservation cleanup: interrupt, cancel, sleep, despawn/map exit and save/reload while work owns cargo or destinations. Assert another eligible pawn can obtain the necessary reservation afterward.
- Save-derived compatibility state: create the actual converted/sentient pawn or modded unfinished bill, serialize and load it, then restart the application. Assert actual capability and job execution, not just a manually changed in-memory flag.
- Startup/UI compatibility: real startup and loaded-game UI paths with the named mods; use actual graphics and collect OnGUI/update failures and visual evidence. The job harness alone cannot prove these issues resolved.
- Transporters/portals: use actual relevant carrier classes and both directions; preserve the distinction between pack animals, ordinary pods, quest shuttles, portals and Vehicle Framework vehicles. Include multiple pawns, loading versus unloading mutual exclusion and post-trip cargo conservation.
- Performance: use representative pawn/item/mod counts and record tick-time distributions and route/scan counts with and without the change. Small deterministic correctness fixtures alone do not establish large-colony performance.

Each expansion must carry its own source references, exact dependency/version manifest, acceptance assertions and independent QA. A new test framework should reduce setup duplication without flattening the evidence required by different reports.

## Remaining implementation work and gates

- [x] Implement a separate non-shipped bootstrap harness project and controller with isolation preflight and explicit structured outcomes. Build, independent review and real execution remain separate unfinished gates.
- [ ] Make ordinary build/test commands safe from accidental real-game deployment, or consistently use validated isolated build parameters until that is implemented.
- [ ] Create the private runtime copy and minimal mod/config fixture; verify loaded locations and all redirected logs in a first bootstrap run.
- [ ] Reconcile the 1.6.4518 compile reference against actual 1.6.4871 APIs; record changes instead of silently assuming the versions are equivalent.
- [ ] Implement and independently review #258 ordinary and mixed-ingredient scenarios and their external behavioral assertions.
- [ ] Execute baseline and changed-code cases and retain evidence with immutable binary/config/fixture hashes.
- [ ] Execute real save/load and fresh-process variants.
- [ ] Add other cluster-specific runtime fixtures as their plans mature; acquire unavailable actual compatibility mods or document an explicit unresolved dependency.
- [ ] Independently audit that no test fixture bypasses the trigger or substitutes helper-policy behavior for executed jobs.

Until these gates are met, this plan is evidence about feasibility and runtime interfaces only. It is not gameplay verification of Hauler's Dream.
