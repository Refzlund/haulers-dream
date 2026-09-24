using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    public sealed class HarnessMod : Mod
    {
        public HarnessMod(ModContentPack content) : base(content)
        {
            HarnessSession.Initialize();
        }
    }

    public sealed class HarnessGameComponent : GameComponent
    {
        // Game.FillComponents uses Activator.CreateInstance(type, Current.Game).
        public HarnessGameComponent(Game game) { }

        public override void StartedNewGame() => HarnessSession.GameStarted("new-game");
        public override void LoadedGame() => HarnessSession.GameStarted("loaded-game");
        public override void GameComponentUpdate() => HarnessSession.Update();
        public override void GameComponentTick() => HarnessSession.Tick();
    }

    internal static class HarnessSession
    {
        private static RunManifest manifest;
        private static RunResult result;
        private static StreamWriter events;
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static bool optedIn;
        private static bool canQuitOwnRuntime;
        private static bool terminal;
        private static bool gameStarted;
        private static bool mapObserved;
        private static int firstTick;
        private static int unityErrors;
        private static readonly ConcurrentQueue<UnityErrorRecord> unityLogQueue = new ConcurrentQueue<UnityErrorRecord>();
        private static readonly List<UnityErrorRecord> capturedControlErrors = new List<UnityErrorRecord>();
        private static readonly object unityLogGate = new object();
        private static bool captureUnityLogs;
        private static int captureSequence;
        private static int sequence;
        private static string initializationFailure;
        private static Bg01Scenario scenario;
        private static Bg03Scenario partialInventoryBill;
        private static L04B1Scenario candidateRecurrence;
        private static L04B1Result candidateRecurrenceObservation;
        private static B1InFlightScenario inFlightRecurrence;
        private static B1InFlightResult inFlightRecurrenceObservation;
        private static StorageDeliveryScenario storageDelivery;
        private static QuantityUiBridgeScenario quantityUiBridge;
        private static bool scenarioAttempted;
        private static bool controlStarted;
        private static int controlDone;
        private static int controlWorkerThread;
        private static int controlMainThread;
        private static string controlException;
        private static string controlMarker;
        private static readonly ManualResetEventSlim controlCallbackEntered = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim controlTerminalRequested = new ManualResetEventSlim(false);
        private static int controlOrder;
        private static int controlCallbackEntryOrder;
        private static int controlCloseRequestedOrder;
        private static int controlCapturedOrder;
        private static int controlCaptureClosedOrder;

        internal static void Initialize()
        {
            if (!GenCommandLine.TryGetCommandLineArg("hd-test", out var caseId)) return;
            optedIn = true;
            try
            {
                if (!GenCommandLine.TryGetCommandLineArg("hd-harnessmanifest", out var manifestPath))
                    throw new InvalidOperationException("Explicit hd-harnessmanifest is required.");
                ValidateControllerPath(manifestPath);
                manifest = Json.Read<RunManifest>(manifestPath);
                ValidateManifest(manifest, manifestPath, caseId);
                result = new RunResult
                {
                    schemaVersion = 1, runId = manifest.runId, caseId = manifest.caseId,
                    status = "running", startedUtc = DateTime.UtcNow.ToString("O"),
                    processId = Process.GetCurrentProcess().Id,
                    assertions = new List<AssertionRecord>(), assemblies = new List<AssemblyRecord>(),
                    mods = new List<ModRecord>()
                };
                events = new StreamWriter(new FileStream(Path.Combine(manifest.evidenceDirectory, "events.jsonl"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                lock (unityLogGate) captureUnityLogs = true;
                Application.logMessageReceivedThreaded += OnUnityLogThreaded;
                Event("harness-start", "case=" + manifest.caseId + "; expectedBehavior=" + manifest.expectedBehavior);
                UnityEngine.Debug.Log("[HD Runtime Harness] run=" + manifest.runId + " case=" + manifest.caseId + " startup");

                // Do not quit another game if an incorrectly launched process receives our manifest.
                canQuitOwnRuntime = SamePath(Application.dataPath,
                    Path.Combine(manifest.runtimeDirectory, "RimWorldWin64_Data"));
                Check("private-runtime-data-path", canQuitOwnRuntime, Application.dataPath);
                Check("private-save-data-path", SamePath(GenFilePaths.SaveDataFolderPath, manifest.saveDataDirectory),
                    GenFilePaths.SaveDataFolderPath);
                Check("private-mod-directory", SamePath(GenFilePaths.ModsFolderPath, Path.Combine(manifest.runtimeDirectory, "Mods")),
                    GenFilePaths.ModsFolderPath);
                Check("private-player-log", SamePath(Application.consoleLogPath, manifest.logPath), Application.consoleLogPath);
                Check("case-supported", new[] { "bootstrap", "BG01", "BG02", "BG03-P1", "L04-O1", "CAP01", "CAP02", "CAP03-B", "CAP03-A", "CAP03-C", "L04-O1-DELIVERY", "L04-B1", "L04-B1-D2", "L40-Q1", "L40-UI-P0" }.Contains(manifest.caseId), manifest.caseId);
                Check("negative-control-supported", string.IsNullOrEmpty(manifest.negativeControl) || manifest.negativeControl == "None"
                    || (manifest.caseId == "bootstrap" && new[] { "WorkerUnityError", "WorkerVerseError", "LateWorkerError", "TerminalRace" }.Contains(manifest.negativeControl)),
                    manifest.negativeControl ?? "None");
                result.installedVersionFile = manifest.gameVersion;
                result.executingGameVersion = RimWorld.VersionControl.CurrentVersionStringWithRev;
                Event("game-version-provenance", "Version.txt=" + result.installedVersionFile + "; executing assembly reports=" + result.executingGameVersion);
                Check("installed-version-file-matches-manifest", File.ReadAllText(Path.Combine(manifest.runtimeDirectory, "Version.txt")).Trim()
                    == manifest.gameVersion, manifest.gameVersion);

                string compiledGameHash = typeof(HarnessSession).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(x => x.Key == "GameReferenceSha256")?.Value;
                var expectedGame = manifest.expectedAssemblies.Single(x => x.name == "Assembly-CSharp");
                Check("harness-compiled-against-running-game", string.Equals(compiledGameHash,
                    expectedGame.sha256, StringComparison.OrdinalIgnoreCase), compiledGameHash ?? "missing build metadata");

                if (result.assertions.Any(x => !x.passed)) Finish("failed", "Initial isolation or build checks failed.");
            }
            catch (Exception error)
            {
                initializationFailure = error.ToString();
                Fail("initialization-exception", initializationFailure);
            }
        }

        internal static void GameStarted(string kind)
        {
            if (!optedIn || terminal || manifest == null) return;
            gameStarted = true;
            Event(kind, "GameComponent lifecycle callback received.");
        }

        internal static void Tick()
        {
            if (!optedIn || terminal) return;
            try
            {
                scenario?.Observe("tick");
                partialInventoryBill?.Observe("tick");
                // This coordinator performs six adjacent-tick query controls.
                // Advance only here and retain its terminal result for Update.
                if (candidateRecurrence != null && candidateRecurrenceObservation == null)
                    candidateRecurrenceObservation = candidateRecurrence.Tick();
                if (inFlightRecurrence != null && inFlightRecurrenceObservation == null)
                    inFlightRecurrenceObservation = inFlightRecurrence.Tick();
                // A frame can span several game ticks. Retain every actual delivery
                // tick so its bounded stable window is independently auditable.
                storageDelivery?.ObserveSettled("game-tick");
            }
            catch (Exception error) { Fail("scenario-observation-exception", error.ToString()); }
        }

        internal static void Update()
        {
            if (!optedIn || terminal) return;
            try
            {
                DrainUnityLogs();
                if (manifest == null) return; // No authorized output location; controller diagnoses missing evidence.
                if (clock.Elapsed.TotalSeconds > 300)
                {
                    Check("harness-wall-timeout", false, "Harness elapsed time exceeded 300 seconds.");
                    Finish("inconclusive", "The configured case did not finish within the harness wall-time budget; required evidence remains unfinished.");
                    return;
                }
                if (!gameStarted || LongEventHandler.ShouldWaitForEvent || Current.ProgramState != ProgramState.Playing)
                    return;
                var map = Find.CurrentMap;
                if (map == null || Find.TickManager == null) return;
                if (!mapObserved)
                {
                    mapObserved = true;
                    firstTick = Find.TickManager.TicksGame;
                    Check("real-map-initialized", map.Size.x > 0 && map.Size.z > 0,
                        "size=" + map.Size + "; maps=" + Find.Maps.Count);
                    CaptureLoadedEnvironment();
                    Event("map-initialized", "tick=" + firstTick + "; size=" + map.Size);
                    if (result.assertions.Any(x => !x.passed))
                    {
                        Finish("failed", "Runtime environment differed from the prepared manifest.");
                        return;
                    }
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    if (manifest.negativeControl == "WorkerUnityError" || manifest.negativeControl == "WorkerVerseError") StartErrorControl();
                }
                int elapsedTicks = Find.TickManager.TicksGame - firstTick;
                if (elapsedTicks < 5) return;
                if (!result.assertions.Any(x => x.id == "real-game-ticks-advanced"))
                    Check("real-game-ticks-advanced", true, "elapsedTicks=" + elapsedTicks);
                if (manifest.caseId == "L40-Q1")
                {
                    bool passed = InventoryQuantityScenario.Run(map);
                    Check("quantity-command-fixture-complete", passed, "All19 command-body scenes and cleanup; UI/network/compatibility/lifecycle remain separate.");
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("quantity-result", "completed=" + passed + "; expectedScenes=19; scope=command-body-only");
                    Event("scenario-observed", "case=L40-Q1; expected=" + manifest.expectedBehavior + "; completed=" + passed);
                    Finish(passed && unityErrors == 0 ? "passed" : "failed", "Native inventory quantity command-body fixture only; user-interface and wider feature acceptance remain unfinished.");
                    return;
                }
                if (manifest.caseId == "L40-UI-P0")
                {
                    if (quantityUiBridge == null)
                    {
                        string capturePath = Path.Combine(manifest.evidenceDirectory, "quantity-ui-bridge.png");
                        ValidateControllerPath(capturePath);
                        quantityUiBridge = new QuantityUiBridgeScenario(capturePath);
                    }
                    string outcome = quantityUiBridge.Update();
                    if (outcome == null) return;
                    Check("quantity-ui-native-bridge", outcome == "passed", outcome + "; native test window only; decoded visual review pending.");
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=L40-UI-P0; expected=" + manifest.expectedBehavior + "; outcome=" + outcome);
                    Finish(unityErrors == 0 ? outcome : "failed", "Native queued-mouse delivery preflight only; actual product row/dialog and visual acceptance remain unfinished.");
                    return;
                }
                if (manifest.caseId == "CAP03-C")
                {
                    result.storageProjectionWrappers = Cap03WrapperScenario.Run(map, manifest.expectedBehavior);
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=CAP03-C; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + result.storageProjectionWrappers.requestedBehaviorSatisfied
                        + "; expectationMatched=" + result.storageProjectionWrappers.expectationMatched);
                    Finish(unityErrors == 0 ? result.storageProjectionWrappers.status : "failed",
                        "Actual incoming/resident native minified wrapper containment and inner callback controls only; reviewed minified support and hauling remain unfinished.");
                    return;
                }
                if (manifest.caseId == "CAP03-A")
                {
                    result.storageProjectionFilters = Cap03FilterScenario.Run(map, manifest.expectedBehavior);
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=CAP03-A; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + result.storageProjectionFilters.requestedBehaviorSatisfied
                        + "; expectationMatched=" + result.storageProjectionFilters.expectationMatched);
                    Finish(unityErrors == 0 ? result.storageProjectionFilters.status : "failed",
                        "Actual linked fixed-filter dispatch and custom-worker containment only; remaining CAP03 and hauling convergence are unfinished.");
                    return;
                }
                if (manifest.caseId == "CAP03-B")
                {
                    result.storageProjectionBudget = Cap03BudgetScenario.Run(map, manifest.expectedBehavior);
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=CAP03-B; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + result.storageProjectionBudget.requestedBehaviorSatisfied
                        + "; expectationMatched=" + result.storageProjectionBudget.expectationMatched);
                    Finish(unityErrors == 0 ? result.storageProjectionBudget.status : "failed",
                        "Actual ASF full-member budget component B only; other CAP03 components, native-call instrumentation and hauling convergence remain unfinished.");
                    return;
                }
                if (manifest.caseId == "CAP02")
                {
                    result.storageProjection = StorageProjectionScenario.Run(map, manifest.expectedBehavior);
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=CAP02; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + result.storageProjection.requestedBehaviorSatisfied
                        + "; expectationMatched=" + result.storageProjection.expectationMatched);
                    Finish(unityErrors == 0 ? result.storageProjection.status : "failed",
                        "Native physical/eligibility adapter and zone-index recovery only; allocation, ASF and reported hauling outcomes remain unverified.");
                    return;
                }
                if (manifest.caseId == "CAP01")
                {
                    result.storageSlots = StorageSlotsScenario.Run(map, manifest.expectedBehavior);
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=CAP01; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + result.storageSlots.requestedBehaviorSatisfied
                        + "; expectationMatched=" + result.storageSlots.expectationMatched);
                    Finish(unityErrors == 0 ? result.storageSlots.status : "failed",
                        "Actual shelf-slot measurements, plan budgets and live claim controls only; no executed hauling or original report verified.");
                    return;
                }
                if (manifest.caseId == "L04-O1-DELIVERY")
                {
                    if (!scenarioAttempted)
                    {
                        scenarioAttempted = true;
                        try { storageDelivery = new StorageDeliveryScenario(map, manifest.expectedBehavior); }
                        catch (Exception error)
                        {
                            Check("fixture-storage-delivery-setup", false, error.ToString());
                            Finish("inconclusive", "Automatic storage delivery fixture setup failed; no behavior conclusion.");
                            return;
                        }
                        Check("fixture-storage-delivery-setup", true, "Disposable automatic pickup and delivery scene initialized.");
                    }
                    var observation = storageDelivery.TryFinish();
                    if (observation == null) return;
                    result.storageDelivery = observation;
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=L04-O1-DELIVERY; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + observation.requestedBehaviorSatisfied
                        + "; expectationMatched=" + observation.expectationMatched);
                    Finish(unityErrors == 0 ? observation.status : "failed",
                        "Actual first automatic bulk delivery and bounded follow-up only; original reported loops and other storage paths remain unverified.");
                    return;
                }
                if (manifest.caseId == "L04-O1")
                {
                    result.storageOwnership = StorageOwnershipScenario.Run(map, manifest.expectedBehavior);
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=L04-O1; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + result.storageOwnership.requestedBehaviorSatisfied
                        + "; expectationMatched=" + result.storageOwnership.expectationMatched);
                    Finish(unityErrors == 0 ? result.storageOwnership.status : "failed",
                        "Storage custody and capacity adapter controls only. No hauling driver, automatic convergence or original report is considered verified.");
                    return;
                }
                if (manifest.caseId == "L04-B1-D2")
                {
                    if (!scenarioAttempted)
                    {
                        scenarioAttempted = true;
                        try { inFlightRecurrence = new B1InFlightScenario(map, manifest.expectedBehavior); }
                        catch (Exception error)
                        {
                            Check("fixture-l04-b1-d2-setup-completed", false, error.ToString());
                            Finish("inconclusive", "L04-B1-D2 in-flight query setup failed; no behavior conclusion.");
                            return;
                        }
                        Check("fixture-l04-b1-d2-setup-completed", true,
                            "Fresh automatic delivery scene; only GameComponentTick advances the in-flight query coordinator.");
                    }
                    if (inFlightRecurrenceObservation == null) return;
                    var observation = inFlightRecurrenceObservation;
                    result.inFlightRecurrence = observation;
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=L04-B1-D2; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + observation.requestedBehaviorSatisfied
                        + "; expectationMatched=" + observation.expectationMatched);
                    Finish(unityErrors == 0 ? observation.status : "failed",
                        "Actual progressing first-source query window and its delivery witness only; full recurrence repair and original reports remain unverified.");
                    return;
                }
                if (manifest.caseId == "L04-B1")
                {
                    if (!scenarioAttempted)
                    {
                        scenarioAttempted = true;
                        try { candidateRecurrence = new L04B1Scenario(map, manifest.expectedBehavior); }
                        catch (Exception error)
                        {
                            Check("fixture-l04-b1-setup-completed", false, error.ToString());
                            Finish("inconclusive", "L04-B1 candidate recurrence setup failed; no behavior conclusion.");
                            return;
                        }
                        Check("fixture-l04-b1-setup-completed", true, "Isolated Q1-Q4/M0 query scenes; native tick coordinator owns subsequent phases.");
                    }
                    if (candidateRecurrenceObservation == null) return;
                    var observation = candidateRecurrenceObservation;
                    result.candidateRecurrence = observation;
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=L04-B1; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + observation.requestedBehaviorSatisfied
                        + "; expectationMatched=" + observation.expectationMatched);
                    Finish(unityErrors == 0 ? observation.status : "failed",
                        "Candidate-query controls and separate healthy automatic delivery only; D2 in-flight queries and full recurrence resolution remain unfinished.");
                    return;
                }
                if (manifest.caseId == "BG03-P1")
                {
                    if (!scenarioAttempted)
                    {
                        scenarioAttempted = true;
                        try { partialInventoryBill = new Bg03Scenario(map, manifest.expectedBehavior); }
                        catch (Exception error)
                        {
                            Check("fixture-bg03p1-setup-completed", false, error.ToString());
                            Finish("inconclusive", "BG03-P1 partial-inventory bill fixture setup failed; no behavior conclusion.");
                            return;
                        }
                        Check("fixture-bg03p1-setup-completed", true, "Disposable ordinary bill with twelve initially held tagged milk units and two fourteen-unit floor sources.");
                    }
                    var observation = partialInventoryBill.TryFinish();
                    if (observation == null) return;
                    result.partialInventoryBill = observation;
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=BG03-P1; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + observation.requestedBehaviorSatisfied
                        + "; expectationMatched=" + observation.expectationMatched);
                    Finish(unityErrors == 0 ? observation.status : "failed",
                        "Partial initially tagged inventory plus two remaining floor sources, native cooking and cleaning, and bounded follow-up only; other inventory cases remain unverified.");
                    return;
                }
                if (manifest.caseId == "BG01" || manifest.caseId == "BG02")
                {
                    if (!scenarioAttempted)
                    {
                        scenarioAttempted = true;
                        try { scenario = new Bg01Scenario(map, manifest.expectedBehavior, manifest.caseId); }
                        catch (Exception error)
                        {
                            Check("fixture-setup-completed", false, error.ToString());
                            Finish("inconclusive", manifest.caseId + " fixture setup failed; no behavior conclusion is supported.");
                            return;
                        }
                        Check("fixture-setup-completed", true, "Disposable " + manifest.caseId + " room, actor, bill and ingredients created.");
                    }
                    var observation = scenario.TryFinish();
                    if (observation == null) return;
                    result.scenario = observation;
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                    Event("scenario-observed", "case=" + manifest.caseId + "; expected=" + manifest.expectedBehavior
                        + "; requestedBehaviorSatisfied=" + observation.requestedBehaviorSatisfied
                        + "; expectationMatched=" + observation.expectationMatched);
                    Finish(unityErrors == 0 ? observation.status : "failed",
                        observation.requestedBehaviorSatisfied
                            ? manifest.caseId + " requested behavior satisfied in this isolated scenario; other acceptance rows remain unverified."
                            : manifest.caseId + " requested behavior is not satisfied. An expected baseline gap is reproduction evidence, not feedback completion.");
                    return;
                }
                if ((manifest.negativeControl == "TerminalRace" || manifest.negativeControl == "LateWorkerError") && !controlStarted)
                {
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "initial observedErrors=" + unityErrors);
                    StartErrorControl();
                    return;
                }
                if (manifest.negativeControl == "TerminalRace")
                {
                    // The worker callback holds the capture gate until Finish requests closure.
                    // Waiting for the producer to finish here would remove the race (or deadlock).
                    if (!controlCallbackEntered.IsSet)
                    {
                        if (Volatile.Read(ref controlDone) != 0) Fail("negative-control-callback-missing", controlException ?? "Worker returned without entering capture.");
                        return;
                    }
                    Event("bootstrap-observed", "Actual map initialized and at least five real game ticks executed.");
                    Finish("passed", "Intentional terminal-boundary control; capture must reject the proposed success.");
                    return;
                }
                if (controlStarted && Volatile.Read(ref controlDone) == 0) return;
                if (!result.assertions.Any(x => x.id == "no-unity-errors-after-harness-start"))
                    Check("no-unity-errors-after-harness-start", unityErrors == 0, "observedErrors=" + unityErrors);
                Event("bootstrap-observed", "Actual map initialized and at least five real game ticks executed.");
                // LateWorkerError deliberately proposes success using the pre-injection assertion.
                Finish((manifest.negativeControl == "LateWorkerError" || result.assertions.All(x => x.passed)) ? "passed" : "failed",
                    "Bootstrap proves isolated startup only. No Hauler's Dream feedback acceptance criteria were tested.");
            }
            catch (Exception error)
            {
                Fail("bootstrap-exception", error.ToString());
            }
        }

        private static void StartErrorControl()
        {
            if (controlStarted) return;
            controlStarted = true;
            controlMainThread = Thread.CurrentThread.ManagedThreadId;
            string kind = manifest.negativeControl;
            controlMarker = "[HD Runtime Harness NegativeControl] run=" + manifest.runId + " control=" + kind + " intentional worker error";
            Event("negative-control-start", kind + "; mainThread=" + controlMainThread);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    controlWorkerThread = Thread.CurrentThread.ManagedThreadId;
                    if (kind == "WorkerVerseError") Verse.Log.Error(controlMarker);
                    else UnityEngine.Debug.LogError(controlMarker);
                }
                catch (Exception error) { controlException = error.ToString(); }
                finally { Interlocked.Exchange(ref controlDone, 1); }
            });
        }

        private static void CaptureLoadedEnvironment()
        {
            var mods = LoadedModManager.RunningMods.ToList();
            result.mods = mods.Select(x => new ModRecord { packageId = x.PackageId, rootPath = x.RootDir }).ToList();
            Check("exact-active-mod-count", mods.Count == manifest.expectedMods.Count,
                "actual=" + mods.Count + "; expected=" + manifest.expectedMods.Count);
            for (int i = 0; i < manifest.expectedMods.Count; i++)
            {
                var expected = manifest.expectedMods[i];
                var actual = i < mods.Count ? mods[i] : null;
                Check("mod-order-root-" + i, actual != null
                    && string.Equals(actual.PackageId, expected.packageId, StringComparison.OrdinalIgnoreCase)
                    && SamePath(actual.RootDir, expected.rootPath),
                    actual == null ? "missing " + expected.packageId : actual.PackageId + " @ " + actual.RootDir);
            }
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var expected in manifest.expectedAssemblies)
            {
                var matching = assemblies.Where(x => x.GetName().Name == expected.name).ToList();
                Check("single-assembly-" + expected.name, matching.Count == 1, "count=" + matching.Count);
                if (matching.Count != 1) continue;
                var loaded = matching[0];
                string actualHash = Hash(loaded.Location);
                var record = new AssemblyRecord
                {
                    name = expected.name, path = loaded.Location, sha256 = actualHash,
                    assemblyVersion = loaded.GetName().Version.ToString(),
                    moduleVersionId = loaded.ManifestModule.ModuleVersionId.ToString()
                };
                result.assemblies.Add(record);
                Check("assembly-identity-" + expected.name, SamePath(record.path, expected.path)
                    && string.Equals(record.sha256, expected.sha256, StringComparison.OrdinalIgnoreCase)
                    && record.assemblyVersion == expected.assemblyVersion,
                    record.path + "; version=" + record.assemblyVersion + "; sha256=" + record.sha256);
            }
        }

        private static void OnUnityLogThreaded(string message, string stackTrace, LogType kind)
        {
            if (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert) return;
            // Unity invokes this on worker threads too. Never call Verse, Unity properties or writers here.
            lock (unityLogGate)
            {
                if (!captureUnityLogs) return;
                bool isControl = controlMarker != null && message == controlMarker;
                if (isControl && manifest.negativeControl == "TerminalRace")
                {
                    controlCallbackEntryOrder = Interlocked.Increment(ref controlOrder);
                    controlCallbackEntered.Set();
                    // A bounded managed barrier only; no Unity/Verse calls or writers in this callback.
                    if (!controlTerminalRequested.Wait(5000))
                        controlException = "Terminal boundary did not arrive within the control's five-second barrier.";
                }
                Interlocked.Increment(ref unityErrors);
                var record = new UnityErrorRecord
                {
                    captureSequence = ++captureSequence, callbackThread = Thread.CurrentThread.ManagedThreadId,
                    kind = kind.ToString(), message = message, stackTrace = stackTrace
                };
                if (isControl) controlCapturedOrder = Interlocked.Increment(ref controlOrder);
                unityLogQueue.Enqueue(record);
            }
        }

        private static void DrainUnityLogs()
        {
            while (unityLogQueue.TryDequeue(out var record))
            {
                if (controlMarker != null && record.message == controlMarker) capturedControlErrors.Add(record);
                Event("unity-error", Json.Stringify(record));
            }
        }

        private static void CaptureControlOutcome()
        {
            if (!controlStarted) return;
            bool workerFinished = SpinWait.SpinUntil(() => Volatile.Read(ref controlDone) != 0, 2000);
            var error = capturedControlErrors.Count == 1 ? capturedControlErrors[0] : null;
            bool boundary = manifest.negativeControl != "TerminalRace"
                || (controlCallbackEntryOrder > 0 && controlCallbackEntryOrder < controlCloseRequestedOrder
                    && controlCloseRequestedOrder < controlCapturedOrder && controlCapturedOrder < controlCaptureClosedOrder);
            bool observed = workerFinished && controlException == null && unityErrors == 1 && error != null
                && controlWorkerThread > 0 && error.callbackThread == controlWorkerThread
                && error.callbackThread != controlMainThread && error.captureSequence > 0
                && error.captureSequence <= captureSequence && boundary;
            result.negativeControl = new NegativeControlResult
            {
                name = manifest.negativeControl, expectationMatched = observed, marker = controlMarker,
                workerThread = controlWorkerThread, mainThread = controlMainThread,
                callbackThread = error?.callbackThread ?? 0, markerCaptureSequence = error?.captureSequence ?? 0,
                finalCaptureSequence = captureSequence, expectedCapturedErrors = 1,
                matchingCapturedErrors = capturedControlErrors.Count, workerFinished = workerFinished,
                callbackEntryOrder = controlCallbackEntryOrder, closeRequestedOrder = controlCloseRequestedOrder,
                capturedOrder = controlCapturedOrder, captureClosedOrder = controlCaptureClosedOrder,
                boundaryOrderingMatched = boundary, exception = controlException
            };
            Check("negative-control-worker-error-observed", observed, Json.Stringify(result.negativeControl));
            Event("negative-control-observed", Json.Stringify(result.negativeControl));
            if (manifest.negativeControl == "TerminalRace")
                Event("terminal-race-witness", Json.Stringify(result.negativeControl));
        }

        internal static void Check(string id, bool passed, string observed)
        {
            result?.assertions.Add(new AssertionRecord { id = id, passed = passed, observed = observed });
            Event("assertion", id + ": " + (passed ? "passed" : "failed") + "; " + observed);
        }

        private static void Fail(string id, string detail)
        {
            Check(id, false, detail);
            Finish("failed", detail);
        }

        private static void Finish(string status, string detail)
        {
            if (terminal) return;
            terminal = true;
            // Remove only this test's observers while error capture is still open.
            try { scenario?.Dispose(); }
            catch (Exception error) { Check("bill-scenario-dispose", false, error.ToString()); status = "failed"; }
            try { partialInventoryBill?.Dispose(); }
            catch (Exception error) { Check("partial-inventory-bill-dispose", false, error.ToString()); status = "failed"; }
            try { candidateRecurrence?.Dispose(); }
            catch (Exception error) { Check("candidate-recurrence-dispose", false, error.ToString()); status = "failed"; }
            try { inFlightRecurrence?.Dispose(); }
            catch (Exception error) { Check("in-flight-recurrence-dispose", false, error.ToString()); status = "failed"; }
            try { storageDelivery?.Dispose(); }
            catch (Exception error) { Check("storage-delivery-dispose", false, error.ToString()); status = "failed"; }
            try { quantityUiBridge?.Dispose(); }
            catch (Exception error) { Check("quantity-ui-bridge-dispose", false, error.ToString()); status = "failed"; }
            // Freeze the capture boundary under the same gate used by callbacks, then drain on this thread.
            // Errors before subscription or during shutdown remain the whole Player.log review's responsibility.
            if (controlStarted)
            {
                controlCloseRequestedOrder = Interlocked.Increment(ref controlOrder);
                controlTerminalRequested.Set();
            }
            lock (unityLogGate)
            {
                captureUnityLogs = false;
                if (controlStarted) controlCaptureClosedOrder = Interlocked.Increment(ref controlOrder);
            }
            Application.logMessageReceivedThreaded -= OnUnityLogThreaded;
            try
            {
                DrainUnityLogs();
                if (result != null && manifest != null)
                {
                    CaptureControlOutcome();
                    int finalErrors = Volatile.Read(ref unityErrors);
                    var errorAssertion = result.assertions.FirstOrDefault(x => x.id == "no-unity-errors-after-harness-start");
                    if (errorAssertion != null)
                    {
                        bool previousPassed = errorAssertion.passed;
                        errorAssertion.passed = finalErrors == 0;
                        errorAssertion.observed = "observedErrors=" + finalErrors + "; threaded capture through terminal-result boundary";
                        if (previousPassed != errorAssertion.passed)
                            Event("assertion-revised", "no-unity-errors-after-harness-start: " + previousPassed + " -> "
                                + errorAssertion.passed + "; final captured errors=" + finalErrors);
                    }
                    if (finalErrors > 0 && (status == "passed" || status == "behavior-gap-observed" || status == "partial")) status = "failed";
                    if (result.negativeControl != null && !result.negativeControl.expectationMatched) status = "failed";
                    result.status = status;
                    result.detail = detail;
                    result.finishedUtc = DateTime.UtcNow.ToString("O");
                    result.unityErrorsObserved = finalErrors;
                    Event("error-capture-boundary", "Threaded capture closed before terminal result; earlier startup and shutdown require whole Player.log review.");
                    Event("terminal-result", status + ": " + detail);
                    UnityEngine.Debug.Log("[HD Runtime Harness] run=" + manifest.runId + " case=" + manifest.caseId
                        + " terminal=" + status + "; captured-errors=" + finalErrors);
                    Json.WriteNew(Path.Combine(manifest.evidenceDirectory, "result.json"), result);
                }
                else
                {
                    // No validated output path exists. Avoid writes outside the controller's root.
                    UnityEngine.Debug.LogError("[HD Runtime Harness] Initialization failed before safe output was established: "
                        + (initializationFailure ?? detail));
                }
            }
            finally
            {
                events?.Dispose();
                events = null;
                // Root.Shutdown recursively deletes shared Unity temporaryCachePath. Do not call it.
                // Application.Quit still fires Application.quitting (including HD debug-log flushing).
                if (canQuitOwnRuntime) Application.Quit();
            }
        }

        internal static void Event(string phase, string detail)
        {
            if (events == null) return;
            events.WriteLine(Json.Stringify(new EventRecord
            {
                sequence = ++sequence, runId = manifest.runId, caseId = manifest.caseId,
                utc = DateTime.UtcNow.ToString("O"), phase = phase, detail = detail,
                tick = Current.Game?.tickManager?.TicksGame ?? -1
            }));
        }

        private static void ValidateManifest(RunManifest value, string manifestPath, string caseId)
        {
            if (value == null || value.schemaVersion != 1 || value.caseId != caseId || string.IsNullOrWhiteSpace(value.runId))
                throw new InvalidDataException("Unsupported or mismatched run manifest.");
            string run = Canonical(value.runDirectory);
            ValidateControllerPath(run);
            if (!SamePath(manifestPath, Path.Combine(run, "manifest.json"))
                || !SamePath(value.runtimeDirectory, Path.Combine(run, "runtime"))
                || !SamePath(value.saveDataDirectory, Path.Combine(run, "SaveData"))
                || !SamePath(value.evidenceDirectory, Path.Combine(run, "evidence"))
                || !SamePath(value.logPath, Path.Combine(run, "evidence", "Player.log")))
                throw new InvalidDataException("Manifest paths do not match the isolated directory layout.");
            if (value.expectedMods == null || value.expectedAssemblies == null)
                throw new InvalidDataException("Manifest lacks an expected runtime identity.");
            foreach (var mod in value.expectedMods) RequireChild(mod.rootPath, value.runtimeDirectory);
            foreach (var assembly in value.expectedAssemblies) RequireChild(assembly.path, value.runtimeDirectory);
            foreach (string directory in new[] { value.runtimeDirectory, value.saveDataDirectory, value.evidenceDirectory,
                Path.Combine(value.saveDataDirectory, "Config"), Path.Combine(value.saveDataDirectory, "Saves"),
                Path.Combine(value.runtimeDirectory, "Mods") })
            {
                ValidateControllerPath(directory);
                if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            }
            ValidateControllerPath(value.logPath);
        }

        private static void ValidateControllerPath(string path)
        {
            RequireChild(path, Path.Combine(Path.GetTempPath(), "haulersdream-runtime-tests"));
            string current = Canonical(path);
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse points are forbidden in harness paths: " + current);
                current = Path.GetDirectoryName(current);
            }
        }

        private static void RequireChild(string path, string parent)
        {
            if (!Canonical(path).StartsWith(Canonical(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Path escapes its expected test root: " + path);
        }

        private static string Canonical(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || path.IndexOf('=') >= 0)
                throw new IOException("An absolute path without '=' is required.");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool SamePath(string left, string right) => string.Equals(Canonical(left), Canonical(right), StringComparison.OrdinalIgnoreCase);

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
        }
    }

    internal static class Json
    {
        public static T Read<T>(string path)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Manifest exceeds 16 MiB.");
            using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        public static string Stringify<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static void WriteNew<T>(string path, T value)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                stream.Flush(true);
            }
        }
    }

    [DataContract]
    internal sealed class RunManifest
    {
        [DataMember] public int schemaVersion { get; set; }
        [DataMember] public string runId { get; set; }
        [DataMember] public string caseId { get; set; }
        [DataMember] public string expectedBehavior { get; set; }
        [DataMember] public string negativeControl { get; set; }
        [DataMember] public string runDirectory { get; set; }
        [DataMember] public string runtimeDirectory { get; set; }
        [DataMember] public string saveDataDirectory { get; set; }
        [DataMember] public string evidenceDirectory { get; set; }
        [DataMember] public string logPath { get; set; }
        [DataMember] public string gameVersion { get; set; }
        [DataMember] public List<ModRecord> expectedMods { get; set; }
        [DataMember] public List<AssemblyRecord> expectedAssemblies { get; set; }
    }

    [DataContract]
    internal sealed class ModRecord
    {
        [DataMember] public string packageId;
        [DataMember] public string rootPath;
    }

    [DataContract]
    internal sealed class AssemblyRecord
    {
        [DataMember] public string name;
        [DataMember] public string path;
        [DataMember] public string sha256;
        [DataMember] public string assemblyVersion;
        [DataMember] public string moduleVersionId;
    }

    [DataContract]
    internal sealed class AssertionRecord
    {
        [DataMember] public string id;
        [DataMember] public bool passed;
        [DataMember] public string observed;
    }

    [DataContract]
    internal sealed class EventRecord
    {
        [DataMember] public int sequence;
        [DataMember] public string runId;
        [DataMember] public string caseId;
        [DataMember] public string utc;
        [DataMember] public string phase;
        [DataMember] public string detail;
        [DataMember] public int tick;
    }

    [DataContract]
    internal sealed class RunResult
    {
        [DataMember] public int schemaVersion;
        [DataMember] public string runId;
        [DataMember] public string caseId;
        [DataMember] public int processId;
        [DataMember] public string status;
        [DataMember] public string detail;
        [DataMember] public string startedUtc;
        [DataMember] public string finishedUtc;
        [DataMember] public int unityErrorsObserved;
        [DataMember] public string installedVersionFile;
        [DataMember] public string executingGameVersion;
        [DataMember] public List<AssertionRecord> assertions;
        [DataMember] public List<AssemblyRecord> assemblies;
        [DataMember] public List<ModRecord> mods;
        [DataMember] public ScenarioResult scenario;
        [DataMember] public Bg03Result partialInventoryBill;
        [DataMember] public L04B1Result candidateRecurrence;
        [DataMember] public B1InFlightResult inFlightRecurrence;
        [DataMember] public StorageOwnershipResult storageOwnership;
        [DataMember] public StorageSlotsResult storageSlots;
        [DataMember] public StorageProjectionResult storageProjection;
        [DataMember] public Cap03BudgetResult storageProjectionBudget;
        [DataMember] public Cap03FilterResult storageProjectionFilters;
        [DataMember] public Cap03WrapperResult storageProjectionWrappers;
        [DataMember] public StorageDeliveryResult storageDelivery;
        [DataMember] public NegativeControlResult negativeControl;
    }

    [DataContract]
    internal sealed class UnityErrorRecord
    {
        [DataMember] public int captureSequence;
        [DataMember] public int callbackThread;
        [DataMember] public string kind;
        [DataMember] public string message;
        [DataMember] public string stackTrace;
    }

    [DataContract]
    internal sealed class NegativeControlResult
    {
        [DataMember] public string name;
        [DataMember] public bool expectationMatched;
        [DataMember] public int workerThread;
        [DataMember] public int mainThread;
        [DataMember] public int expectedCapturedErrors;
        [DataMember] public string marker;
        [DataMember] public int callbackThread;
        [DataMember] public int markerCaptureSequence;
        [DataMember] public int finalCaptureSequence;
        [DataMember] public int matchingCapturedErrors;
        [DataMember] public bool workerFinished;
        [DataMember] public int callbackEntryOrder;
        [DataMember] public int closeRequestedOrder;
        [DataMember] public int capturedOrder;
        [DataMember] public int captureClosedOrder;
        [DataMember] public bool boundaryOrderingMatched;
        [DataMember] public string exception;
    }
}
