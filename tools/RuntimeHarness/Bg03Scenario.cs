using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    internal sealed partial class Bg03Scenario : IDisposable
    {
        private const string caseId = "BG03-P1", recipeDefName = "CookMealSimpleBulk";
        private const int ingredientUnitsPerDef = 14, expectedMealUnits = 4, maximumTicks = 12000;
        private bool ObserveIngredientConsumption => true;
        private readonly Map map;
        private readonly string expectedBehavior;
        private readonly int startedTick;
        private readonly Bg03Result result;
        private readonly Bg03Accounting accounting;
        private readonly Dictionary<ThingDef, int> expected = new Dictionary<ThingDef, int>();
        private readonly List<Filth> seededFilth = new List<Filth>();
        private readonly List<CandidateFrame> candidates = new List<CandidateFrame>();
        private readonly List<StartFrame> starts = new List<StartFrame>();
        private readonly Dictionary<int, Bg03Start> gatherStarts = new Dictionary<int, Bg03Start>();
        private readonly Dictionary<int, Bg03Start> nativeStarts = new Dictionary<int, Bg03Start>();
        private readonly Dictionary<int, Bg03Cleaning> removals = new Dictionary<int, Bg03Cleaning>();
        private readonly HashSet<int> cleaningPairs = new HashSet<int>();
        private readonly HashSet<int> seenJobs = new HashSet<int>();
        private readonly HashSet<int> consumedThings = new HashSet<int>();
        private static readonly PropertyInfo CurrentToil = typeof(JobDriver).GetProperty("CurToil", BindingFlags.NonPublic | BindingFlags.Instance);
        private Bg03Observers observers;
        private Pawn cook;
        private Building_WorkTable stove;
        private Bill_Production bill;
        private ThingDef milkDef, riceDef, potatoDef, mealDef;
        private Thing initialMilk;
        private object tagComp;
        private MethodInfo tagPeek;
        private Assembly commonSenseAssembly;
        private List<IntVec3> roofCells;
        private float cleanedAtStart;
        private float lastObservedCleaned;
        private string initialRiceId, initialPotatoId;
        private int sequence, stateEvents, stableTickCount, lastStableTick = -1;
        private int? stableSince;
        private bool active, observing, healthy = true, conserved = true, forced, batch, secondExcursion, storageDetour;
        private bool roofIntact = true, productValid = true, cleaningValid = true, consumptionValid = true, provenanceHealthy = true;
        private bool postProductStable = true;

        internal Bg03Scenario(Map map, string expectedBehavior)
        {
            this.map = map; this.expectedBehavior = expectedBehavior; startedTick = Find.TickManager.TicksGame;
            result = new Bg03Result { expectedBehavior = expectedBehavior, startedTick = startedTick };
            accounting = new Bg03Accounting(reason => ObserverFault(new InvalidOperationException("BG03 unit ledger: " + reason)));
            Setup(); // Explicit initial tag and unit seeding happen before observers are enabled.
            lastObservedCleaned = cleanedAtStart;
            result.origins = accounting.Origins;
            HarnessSession.Event("bg03-initial-unit-origins", Json.Stringify(result.origins));
            Require("bg03p1-initial-held-accounting", accounting.InitialHeld(milkDef) == 12 && accounting.Acquired(milkDef) == 0
                && accounting.InitialFloor(riceDef) == 14 && accounting.InitialFloor(potatoDef) == 14, "Initial held milk is not a new acquisition.");
            active = true;
            try
            {
                result.initial = Snapshot("initial-before-automatic-work");
                Require("bg03p1-initial-custody", AllInventory(result.initial, false) && result.initial.quantities.All(q => q.uniqueAcquired == 0),
                    Json.Stringify(result.initial));
                observers = new Bg03Observers(this, cook);
                CaptureUnloadGate("initial-before-automatic-work");
                result.observerOutControl = Bg03OutControl.Run(map, stove.InteractionCell + new IntVec3(-2, 0, 1));
                Require("bg03p1-native-out-observer-control", result.observerOutControl.transferValid
                    && result.observerOutControl.dropValid && result.observerOutControl.cleaned,
                    "Installed read-only hooks preserve native out Thing identity/count on transfer and drop; separate non-recipe cargo fully removed.");
                ObserveSettled("fixture-start");
            }
            catch { Dispose(); throw; }
        }
        internal bool Active => active;
        internal bool Relevant(Thing thing) => thing != null && expected.ContainsKey(thing.def);
        internal bool RelevantJob(Job job) => job != null && (ReferenceEquals(job.bill, bill) || job.targetA.Thing == stove);
        private bool IsNative => cook.CurJob?.bill == bill && cook.CurJob.def == JobDefOf.DoBill && cook.jobs.curDriver?.GetType() == typeof(JobDriver_DoBill);
        private static bool IsGather(Job job) => job?.def?.defName == "HaulersDream_GatherBillIngredients" || job?.def?.defName == "HaulersDream_BillPrepGather";
        private int CountInventory(ThingDef def) => cook.inventory.innerContainer.Where(x => x.def == def).Sum(x => x.stackCount);
        private int CountCarry(ThingDef def) => cook.carryTracker.CarriedThing?.def == def ? cook.carryTracker.CarriedThing.stackCount : 0;
        private int CountFloor(ThingDef def) => map.listerThings.ThingsOfDef(def).Where(x => x.Spawned).Sum(x => x.stackCount);
        private int CountAll(ThingDef def) => CountInventory(def) + CountCarry(def) + CountFloor(def);
        private bool IsTagged(Thing thing) => thing != null && ((HashSet<Thing>)tagPeek.Invoke(tagComp, null)).Contains(thing); // Never mutate returned set.

        internal Bg03ThingState ThingState(Thing thing)
        {
            if (thing == null) return null;
            string custody = thing.Destroyed ? "destroyed" : ReferenceEquals(thing.ParentHolder, cook.inventory) ? "inventory"
                : ReferenceEquals(thing.ParentHolder, cook.carryTracker) ? "hands"
                : thing.Spawned && thing.Map == map && ReferenceEquals(thing.ParentHolder, map) ? "floor" : "transient-or-other";
            return new Bg03ThingState { id = thing.thingIDNumber, def = thing.def.defName, count = thing.Destroyed ? 0 : thing.stackCount,
                custody = custody, holderType = thing.ParentHolder?.GetType().FullName, spawned = thing.Spawned, destroyed = thing.Destroyed,
                x = thing.Position.x, z = thing.Position.z, mapId = thing.MapHeld?.uniqueID };
        }
        internal Bg03State Snapshot(string reason)
        {
            var things = expected.Keys.SelectMany(d => map.listerThings.ThingsOfDef(d).Where(x => x.Spawned))
                .Concat(cook.inventory.innerContainer.Where(Relevant)).ToList();
            if (Relevant(cook.carryTracker.CarriedThing)) things.Add(cook.carryTracker.CarriedThing);
            if (things.Select(x => x.thingIDNumber).Distinct().Count() != things.Count) ObserverFault(new InvalidOperationException("Duplicate physical identity in custody census."));
            var state = new Bg03State { sequence = ++sequence, tick = Find.TickManager.TicksGame, reason = reason,
                jobId = cook.CurJob?.loadID, jobDef = cook.CurJob?.def?.defName, driver = cook.jobs.curDriver?.GetType().FullName,
                x = cook.Position.x, z = cook.Position.z, benchDistance = cook.Position.DistanceTo(stove.InteractionCell),
                cleanedTotal = cook.records.GetValue(RecordDefOf.MessesCleaned),
                meals = CountAll(mealDef), billRemaining = bill.repeatCount,
                things = things.OrderBy(x => x.thingIDNumber).Select(ThingState).ToList(),
                quantities = expected.Keys.OrderBy(x => x.defName, StringComparer.Ordinal).Select(d => new Bg03Quantity
                { def = d.defName, initialHeld = accounting.InitialHeld(d), initialFloor = accounting.InitialFloor(d),
                    inventory = CountInventory(d), hands = CountCarry(d), floor = CountFloor(d), uniqueAcquired = accounting.Acquired(d),
                    reenteredHeld = accounting.Reentered(d), consumed = accounting.Consumed(d) }).ToList() };
            return state;
        }
        private Bg03JobState JobState(Job job)
        {
            if (job == null) return null;
            var state = new Bg03JobState { id = job.loadID, def = job.def?.defName, bill = job.bill?.GetUniqueLoadID(), recipe = job.bill?.recipe?.defName,
                workGiver = job.workGiverDef?.defName, workGiverClass = job.workGiverDef?.giverClass?.FullName, playerForced = job.playerForced,
                targetA = job.targetA.Thing?.thingIDNumber, queueLengthsMatch = job.targetQueueB != null && job.countQueue != null && job.targetQueueB.Count == job.countQueue.Count };
            if (job.targetQueueB != null)
                for (int i = 0; i < job.targetQueueB.Count; i++)
                {
                    var thing = job.targetQueueB[i].Thing;
                    int? count = job.countQueue != null && i < job.countQueue.Count ? (int?)job.countQueue[i] : null;
                    state.selection.Add(new Bg03SelectionRow { ordinal = i, thing = ThingState(thing), selected = count,
                        tagged = Relevant(thing) && IsTagged(thing), units = Relevant(thing) && count.HasValue && count.Value >= 0
                            ? accounting.SelectionUnits(thing.thingIDNumber, count.Value) : Array.Empty<int>() });
                }
            return state;
        }
        private bool CompleteSelection(Bg03JobState job) => job != null && job.bill == bill.GetUniqueLoadID() && job.recipe == recipeDefName
            && job.targetA == stove.thingIDNumber && !job.playerForced && accounting.CompleteSelection(job, expected);
        private bool InitialMilkSelected(Bg03JobState job) => CompleteSelection(job) && job.selection.Count(x => x.thing?.id == initialMilk.thingIDNumber
            && x.thing.custody == "inventory" && x.selected == 12 && x.tagged) == 1;
        private static bool SameSelection(Bg03JobState a, Bg03JobState b) => a != null && b != null && a.selection.Count == b.selection.Count
            && a.selection.Zip(b.selection, (x, y) => x.thing?.id == y.thing?.id && x.selected == y.selected && x.units.SequenceEqual(y.units)).All(x => x);
        private bool AllInventory(Bg03State state, bool complete) => state.quantities.All(q => q.hands == 0
            && (complete ? q.inventory == q.initialHeld + q.initialFloor && q.floor == 0 : q.inventory == q.initialHeld && q.floor == q.initialFloor));

        internal sealed class CandidateFrame
        { internal Bg03Candidate record; internal Job routedJob; }
        internal CandidateFrame BeginCandidate(WorkGiver_DoBill giver, Pawn pawn, Thing target, bool forcedArgument)
        {
            if (!active || pawn != cook || target != stove) return null;
            var frame = new CandidateFrame { record = new Bg03Candidate { id = candidates.Count + 1, tick = Find.TickManager.TicksGame,
                forcedArgument = forcedArgument, workGiverClass = giver.GetType().FullName } };
            candidates.Add(frame); result.candidates.Add(frame.record); return frame;
        }
        internal void NativeCandidate(CandidateFrame frame, Job job)
        {
            if (frame == null) return;
            frame.record.nativeSequence = ++sequence; frame.record.native = JobState(job);
            HarnessSession.Event("bg03-native-candidate-before-route", Json.Stringify(frame.record));
            CaptureUnloadGate("native-candidate-before-route", candidateId: frame.record.id);
        }
        internal void RoutedCandidate(CandidateFrame frame, Job job)
        {
            if (frame == null) return;
            frame.routedJob = job; frame.record.routedSequence = ++sequence; frame.record.routed = JobState(job);
            if (frame.record.nativeSequence == 0 || frame.record.nativeSequence >= frame.record.routedSequence) provenanceHealthy = false;
            HarnessSession.Event("bg03-candidate-after-route", Json.Stringify(frame.record));
        }
        internal sealed class StartFrame
        { internal Bg03Start record; internal Job newJob; }
        internal StartFrame CaptureStart(Job next, JobCondition endCondition)
        {
            if (!active || (!RelevantJob(next) && !RelevantJob(cook.CurJob))) return null;
            var candidate = candidates.LastOrDefault(x => ReferenceEquals(x.routedJob, next) && x.record.routed?.id == next?.loadID);
            var previous = cook.CurJob; var oldDriver = cook.jobs.curDriver;
            var frame = new StartFrame { newJob = next, record = new Bg03Start { sequence = ++sequence, tick = Find.TickManager.TicksGame,
                candidateId = candidate?.record.id, previous = JobState(previous), requested = JobState(next),
                previousDriver = oldDriver?.GetType().FullName, previousToilInit = ToilMethod(oldDriver, false),
                previousExecutedGatherId = previous != null && gatherStarts.ContainsKey(previous.loadID) ? (int?)previous.loadID : null,
                lastJobEndCondition = endCondition.ToString() } };
            starts.Add(frame); result.starts.Add(frame.record);
            HarnessSession.Event("bg03-startjob-request-before-toils", Json.Stringify(frame.record));
            Geometry(Snapshot("before-startjob"));
            return frame;
        }
        internal void ActualStarting(JobDriver driver)
        {
            if (!active || driver.pawn != cook || !RelevantJob(driver.job)) return;
            var frame = starts.LastOrDefault(x => ReferenceEquals(x.newJob, driver.job) && x.record.requested?.id == driver.job.loadID);
            if (frame == null) { provenanceHealthy = false; ObserverFault(new InvalidOperationException("Actual Notify_Starting has no captured StartJob request.")); return; }
            if (frame.record.actualSequence != 0) provenanceHealthy = false;
            frame.record.actualSequence = ++sequence; frame.record.actual = JobState(driver.job);
            frame.record.actualDriver = driver.GetType().FullName; frame.record.actualDriverAssembly = driver.GetType().Assembly.FullName;
            frame.record.actualDriverMvid = driver.GetType().Module.ModuleVersionId.ToString();
            frame.record.actualCustody = Snapshot("actual-notify-starting-before-toils");
            if (IsGather(driver.job))
            {
                if (gatherStarts.ContainsKey(driver.job.loadID)) provenanceHealthy = false;
                else gatherStarts.Add(driver.job.loadID, frame.record);
            }
            if (driver.job.def == JobDefOf.DoBill && driver.GetType() == typeof(JobDriver_DoBill))
            {
                if (nativeStarts.ContainsKey(driver.job.loadID)) provenanceHealthy = false;
                else nativeStarts.Add(driver.job.loadID, frame.record);
            }
            HarnessSession.Event("bg03-actual-start-before-instant-toils", Json.Stringify(frame.record));
            ObserveActualJob(); Geometry(frame.record.actualCustody);
        }
        internal void ObserveActualJob()
        {
            if (!active || cook.CurJob == null) return;
            var job = cook.CurJob;
            if (RelevantJob(job)) { forced |= job.playerForced; batch |= job.def.defName.IndexOf("Batch", StringComparison.OrdinalIgnoreCase) >= 0; }
            if (seenJobs.Add(job.loadID)) HarnessSession.Event("bg03-executing-job", Json.Stringify(new Bg03Start
                { sequence = ++sequence, tick = Find.TickManager.TicksGame, actual = JobState(job), actualDriver = cook.jobs.curDriver?.GetType().FullName }));
        }

        internal sealed class HeldFrame { internal Bg03State before; internal HashSet<int> held; internal string boundary; }
        internal HeldFrame BeginHeld(string boundary)
        {
            var before = Snapshot("before-" + boundary); return new HeldFrame { before = before, held = accounting.Held(before), boundary = boundary };
        }
        internal void EndHeld(HeldFrame frame)
        {
            if (!active || frame == null) return;
            var after = Snapshot("after-" + frame.boundary);
            accounting.Transfer(frame.held, accounting.Held(after), out var fresh, out var repeats, out var left);
            // Refresh the typed accounting values after recording the actual entry.
            after = Snapshot("after-accounted-" + frame.boundary);
            if (left.Length > 0 && after.benchDistance > 3f && after.quantities.Any(q => q.floor > frame.before.quantities.Single(b => b.def == q.def).floor)) storageDetour = true;
            if (fresh.Length > 0 || repeats.Length > 0 || left.Length > 0)
            {
                var record = new Bg03Acquisition { sequence = ++sequence, tick = after.tick, boundary = frame.boundary,
                    before = frame.before, after = after, newlyAcquiredUnits = fresh, reenteredUnits = repeats, leftHeldUnits = left };
                result.transfers.Add(record); HarnessSession.Event("bg03-actual-custody-transfer", Json.Stringify(record));
            }
            Geometry(after);
        }
        internal void SplitObserved(Bg03ThingState before, Thing source, Thing child, int requested, string method, Exception error)
        {
            int[] moved = error == null ? accounting.Split(before, source, child) : Array.Empty<int>();
            var record = new Bg03Ancestry { sequence = ++sequence, tick = Find.TickManager.TicksGame, operation = "SplitOff", method = method,
                sourceBefore = before, sourceAfter = ThingState(source), targetAfter = ThingState(child), requested = requested,
                valid = error == null && accounting.Healthy, movedUnits = moved };
            result.ancestry.Add(record); HarnessSession.Event("bg03-actual-split", Json.Stringify(record));
            if (error != null) ObserverFault(error);
        }
        internal void MergeObserved(Bg03ThingState targetBefore, Bg03ThingState sourceBefore, Thing target, Thing source, bool? nativeResult, string method, Exception error)
        {
            int[] moved = accounting.Merge(targetBefore, sourceBefore, target, source);
            var record = new Bg03Ancestry { sequence = ++sequence, tick = Find.TickManager.TicksGame, operation = "TryAbsorbStack", method = method,
                targetBefore = targetBefore, sourceBefore = sourceBefore, targetAfter = ThingState(target), sourceAfter = ThingState(source),
                nativeResult = nativeResult, valid = error == null && accounting.Healthy, movedUnits = moved };
            result.ancestry.Add(record); HarnessSession.Event("bg03-actual-merge", Json.Stringify(record));
            if (error != null) ObserverFault(error);
        }
        private void Geometry(Bg03State state)
        {
            if (!active || result.products.Count > 0) return;
            bool outside = state.benchDistance > 3f;
            if (result.departure == null && outside)
            { result.departure = state; result.departureTick = state.tick; HarnessSession.Event("bg03-actual-departure", Json.Stringify(state)); }
            if (result.departure != null && result.firstReturn == null && outside && AllInventory(state, true) && result.completeInventory == null)
            { result.completeInventory = state; HarnessSession.Event("bg03-complete-inventory-before-return", Json.Stringify(state)); }
            if (result.departure != null && result.firstReturn == null && !outside && state.quantities.Any(q => q.inventory + q.hands + q.floor > 0))
            { result.firstReturn = state; result.firstReturnTick = state.tick; HarnessSession.Event("bg03-actual-first-return", Json.Stringify(state)); }
            if (result.firstReturn != null && outside && state.quantities.Any(q => q.inventory + q.hands + q.floor > 0)) secondExcursion = true;
        }
        internal void Observe(string reason) { if (reason == "tick") ObserveSettled("game-tick"); else ObserveSettled(reason); }
        internal void ObserveSettled(string reason)
        {
            if (!active || observing) return;
            observing = true;
            try
            {
                ObserveActualJob(); var state = Snapshot(reason); Geometry(state);
                if (float.IsNaN(state.cleanedTotal) || float.IsInfinity(state.cleanedTotal)
                    || state.cleanedTotal < lastObservedCleaned || state.cleanedTotal - cleanedAtStart < cleaningPairs.Count)
                    cleaningValid = false;
                else lastObservedCleaned = state.cleanedTotal;
                if (!accounting.ValidateSettled(state)) conserved = false;
                if (roofCells.Any(c => map.roofGrid.RoofAt(c) != RoofDefOf.RoofConstructed)) roofIntact = false;
                if (state.things.Any(x => x.custody == "transient-or-other")) conserved = false;
                bool exact = state.quantities.All(q => q.inventory == 0 && q.hands == 0 && q.floor == 0 && q.consumed == q.initialHeld + q.initialFloor)
                    && state.meals == 4 && state.billRemaining == 0;
                if (result.products.Count > 0 && !exact) postProductStable = false;
                if (exact && SuccessfulNativeCleanup())
                {
                    if (!stableSince.HasValue) { stableSince = state.tick; stableTickCount = 0; lastStableTick = -1; }
                    if (state.tick != lastStableTick)
                    {
                        if (lastStableTick >= 0 && state.tick != lastStableTick + 1) { stableSince = state.tick; stableTickCount = 0; }
                        stableTickCount++; lastStableTick = state.tick;
                    }
                }
                else { stableSince = null; stableTickCount = 0; lastStableTick = -1; }
                stateEvents++; HarnessSession.Event("bg03-settled-state", Json.Stringify(state));
            }
            finally { observing = false; }
        }
        internal void ObserverFault(Exception error)
        { healthy = false; HarnessSession.Event("bg03-observer-fault", error.ToString()); }

        private string ToilMethod(JobDriver driver, bool tick)
        {
            var toil = driver == null ? null : CurrentToil?.GetValue(driver, null) as Toil;
            var method = tick ? toil?.tickAction?.Method : toil?.initAction?.Method;
            return method == null ? null : method.DeclaringType.FullName + "." + method.Name + ";assembly=" + method.Module.Assembly.FullName + ";mvid=" + method.Module.ModuleVersionId;
        }
        private bool CsToil()
        {
            var toil = CurrentToil?.GetValue(cook.jobs.curDriver, null) as Toil; var method = toil?.tickAction?.Method;
            return method != null && method.Module.Assembly == commonSenseAssembly && method.DeclaringType.FullName.StartsWith("CommonSense.Utility", StringComparison.Ordinal);
        }
        internal sealed class CleanFrame { internal Filth filth; internal Bg03Cleaning record; internal float recordBefore; }
        internal CleanFrame CaptureFilth(Filth filth, string operation)
        {
            if (!active || !seededFilth.Contains(filth)) return null;
            return new CleanFrame { filth = filth, recordBefore = cook.records.GetValue(RecordDefOf.MessesCleaned), record = new Bg03Cleaning
                { sequence = ++sequence, tick = Find.TickManager.TicksGame, filthId = filth.thingIDNumber, jobId = cook.CurJob?.loadID ?? -1,
                    operation = operation, native = IsNative && CsToil(), toilTickMethod = ToilMethod(cook.jobs.curDriver, true) } };
        }
        internal CleanFrame CaptureCleaningIncrement() => CaptureFilth(cook.CurJob?.targetA.Thing as Filth, "record-increment");
        internal void FilthThinned(CleanFrame frame)
        {
            if (frame == null) return;
            var row = frame.record; row.destroyed = frame.filth.Destroyed;
            row.attributed = row.destroyed && row.native && IsNative && cook.CurJob.loadID == row.jobId && result.products.Count == 0 && !removals.ContainsKey(row.filthId);
            if (!row.attributed) cleaningValid = false;
            removals[row.filthId] = row; result.cleaning.Add(row); HarnessSession.Event("bg03-seeded-filth-removal", Json.Stringify(row));
        }
        internal void CleaningIncremented(CleanFrame frame)
        {
            if (frame == null) return;
            var row = frame.record; row.recordDelta = cook.records.GetValue(RecordDefOf.MessesCleaned) - frame.recordBefore;
            row.destroyed = frame.filth.Destroyed;
            row.attributed = row.native && IsNative && cook.CurJob.loadID == row.jobId && result.products.Count == 0 && row.recordDelta == 1f
                && removals.TryGetValue(row.filthId, out var removal) && removal.attributed && removal.tick == row.tick
                && removal.jobId == row.jobId && removal.sequence < row.sequence && cleaningPairs.Add(row.filthId);
            if (!row.attributed) cleaningValid = false;
            result.cleaning.Add(row); HarnessSession.Event("bg03-seeded-cleaning-increment", Json.Stringify(row));
        }
        internal void ProductCreated(Thing product, RecipeDef recipe)
        {
            if (!active || recipe != bill.recipe) return;
            var row = new Bg03Product { sequence = ++sequence, tick = Find.TickManager.TicksGame, jobId = cook.CurJob?.loadID ?? -1,
                thingId = product?.thingIDNumber, count = product?.stackCount ?? 0,
                native = IsNative && product != null && product.def == mealDef && !product.Destroyed,
                cleaningComplete = cleaningValid && cleaningPairs.Count == 3 && seededFilth.All(x => x.Destroyed)
                    && removals.Values.All(x => x.jobId == cook.CurJob?.loadID) };
            productValid &= row.native && row.count == 4 && row.cleaningComplete;
            result.products.Add(row); HarnessSession.Event("bg03-native-product-created-before-consumption", Json.Stringify(row));
        }
        internal sealed class ConsumeFrame { internal Thing thing; internal RecipeWorker worker; internal bool native, mapMatched; internal Bg03Consumption record; }
        internal ConsumeFrame CaptureConsumption(RecipeWorker worker, Thing thing, RecipeDef recipe, Map actualMap)
        {
            if (!active || recipe != bill.recipe) return null;
            return new ConsumeFrame { thing = thing, worker = worker, native = IsNative, mapMatched = actualMap == map,
                record = new Bg03Consumption { sequence = ++sequence, tick = Find.TickManager.TicksGame, jobId = cook.CurJob?.loadID ?? -1, before = ThingState(thing) } };
        }
        internal void Consumed(ConsumeFrame frame)
        {
            if (frame == null) return;
            var row = frame.record; row.after = ThingState(frame.thing);
            bool context = frame.native && IsNative && cook.CurJob.loadID == row.jobId && frame.mapMatched
                && ReferenceEquals(frame.worker, bill.recipe.Worker) && frame.worker.GetType() == typeof(RecipeWorker)
                && Relevant(frame.thing) && row.before != null && !row.before.destroyed && row.before.count > 0 && frame.thing.Destroyed
                && result.products.Count == 1 && result.products[0].sequence < row.sequence && result.products[0].jobId == row.jobId
                && consumedThings.Add(row.before.id);
            row.units = context ? accounting.Consume(row.before, frame.thing) : Array.Empty<int>();
            row.attributed = context && row.units.Length == row.before.count && accounting.Healthy;
            if (!row.attributed) consumptionValid = false;
            result.consumption.Add(row); HarnessSession.Event("bg03-native-ingredient-consumption", Json.Stringify(row));
        }
        internal sealed class EndFrame { internal Job job; internal JobDriver driver; internal Bg03End record; }
        internal EndFrame CaptureCleanup(JobCondition condition)
        {
            if (!active || !RelevantJob(cook.CurJob)) return null;
            return new EndFrame { job = cook.CurJob, driver = cook.jobs.curDriver, record = new Bg03End
                { sequence = ++sequence, tick = Find.TickManager.TicksGame, jobId = cook.CurJob.loadID, jobDef = cook.CurJob.def.defName, condition = condition.ToString() } };
        }
        internal void CleanupCompleted(EndFrame frame)
        {
            if (frame == null) return;
            frame.record.released = frame.driver.ended && !ReferenceEquals(cook.CurJob, frame.job) && !ReferenceEquals(cook.jobs.curDriver, frame.driver);
            result.ends.Add(frame.record); HarnessSession.Event("bg03-actual-job-cleanup", Json.Stringify(frame.record));
        }
        private bool SuccessfulNativeCleanup()
        {
            var ends = result.ends.Where(x => x.jobDef == JobDefOf.DoBill.defName).ToList();
            return ends.Count == 1 && ends[0].condition == JobCondition.Succeeded.ToString() && ends[0].released && nativeStarts.ContainsKey(ends[0].jobId);
        }
        private bool ChangedProvenance()
        {
            if (!provenanceHealthy || gatherStarts.Count != 1 || nativeStarts.Count != 1) return false;
            var gather = gatherStarts.Values.Single(); var native = nativeStarts.Values.Single();
            var candidate = gather.candidateId.HasValue ? result.candidates.SingleOrDefault(x => x.id == gather.candidateId) : null;
            var gatherEnds = result.ends.Where(x => x.jobId == gather.actual.id).ToList();
            return candidate != null && !candidate.forcedArgument && candidate.native?.def == JobDefOf.DoBill.defName
                && candidate.routed?.def == "HaulersDream_GatherBillIngredients" && candidate.nativeSequence < candidate.routedSequence
                && candidate.routedSequence < gather.sequence && gather.sequence < gather.actualSequence
                && InitialMilkSelected(candidate.native) && SameSelection(candidate.native, candidate.routed)
                && InitialMilkSelected(gather.actual) && SameSelection(candidate.routed, gather.requested) && SameSelection(gather.requested, gather.actual)
                && gather.actualDriver == "HaulersDream.JobDriver_GatherBillIngredients" && native.previousExecutedGatherId == gather.actual.id
                && native.previousDriver == gather.actualDriver && native.previousToilInit != null
                && native.previousToilInit.StartsWith("HaulersDream.JobDriver_GatherBillIngredients.Handoff;", StringComparison.Ordinal)
                && native.sequence > gather.actualSequence && native.actualSequence > native.sequence && native.candidateId == null
                && gatherEnds.Count == 1 && gatherEnds[0].condition == JobCondition.Succeeded.ToString() && gatherEnds[0].released
                && gatherEnds[0].sequence > native.sequence && gatherEnds[0].sequence < native.actualSequence
                && native.lastJobEndCondition == JobCondition.Succeeded.ToString() && CompleteSelection(native.previous)
                && CompleteSelection(native.requested) && CompleteSelection(native.actual)
                && SameSelection(native.requested, native.actual) && AllInventory(native.actualCustody, true)
                && native.actualDriver == typeof(JobDriver_DoBill).FullName;
        }
        private bool BaselineProvenance()
        {
            if (!provenanceHealthy || gatherStarts.Count != 0 || nativeStarts.Count != 1) return false;
            var native = nativeStarts.Values.Single();
            var candidate = native.candidateId.HasValue ? result.candidates.SingleOrDefault(x => x.id == native.candidateId) : null;
            return candidate != null && !candidate.forcedArgument && InitialMilkSelected(candidate.native)
                && candidate.native.def == JobDefOf.DoBill.defName && candidate.routed?.def == JobDefOf.DoBill.defName
                && candidate.nativeSequence < candidate.routedSequence && candidate.routedSequence < native.sequence
                && native.sequence < native.actualSequence && SameSelection(candidate.native, candidate.routed)
                && SameSelection(candidate.routed, native.requested) && SameSelection(native.requested, native.actual) && InitialMilkSelected(native.actual);
        }
        internal Bg03Result TryFinish()
        {
            if (!active) return null;
            ObserveSettled("finish-poll"); int tick = Find.TickManager.TicksGame;
            bool timeout = tick - startedTick >= maximumTicks;
            bool stable = stableSince.HasValue && tick - stableSince.Value >= 300 && stableTickCount >= 301;
            if (!timeout && !stable) return null;
            var final = Snapshot("final");
            bool exactConsumption = consumptionValid && accounting.Healthy && expected.All(x => accounting.Consumed(x.Key) == x.Value)
                && result.consumption.All(x => x.attributed) && result.consumption.SelectMany(x => x.units).Distinct().Count() == 40
                && result.consumption.Sum(x => x.units.Length) == 40;
            bool ordinary = nativeStarts.Count == 1 && !forced && !batch && nativeStarts.Values.All(x => x.actual.workGiverClass != null
                && typeof(WorkGiver_DoBill).IsAssignableFrom(RequireType(x.actual.workGiverClass)));
            bool products = productValid && result.products.Count == 1 && result.products[0].count == 4 && postProductStable;
            bool cleaning = cleaningValid && cleaningPairs.Count == 3 && removals.Count == 3 && result.cleaning.Count == 6 && result.cleaning.All(x => x.attributed)
                // Only the three seeded pairs must belong to this native bill.
                // Ordinary Cleaning remains enabled during stable follow-up.
                && cook.records.GetValue(RecordDefOf.MessesCleaned) - cleanedAtStart >= 3f;
            bool nativeComplete = !timeout && stable && SuccessfulNativeCleanup() && exactConsumption && products
                && final.quantities.All(q => q.inventory + q.hands + q.floor == 0) && final.meals == 4 && final.billRemaining == 0;
            bool unique = accounting.Acquired(milkDef) == 0 && accounting.Acquired(riceDef) == 14 && accounting.Acquired(potatoDef) == 14;
            bool noRepeat = expected.Keys.All(d => accounting.Reentered(d) == 0) && !secondExcursion && gatherStarts.Count == 1;
            bool sweep = result.completeInventory != null && result.firstReturn != null && result.departure != null
                && result.departure.benchDistance > 3 && result.completeInventory.benchDistance > 3 && result.firstReturn.benchDistance <= 3
                && result.departure.sequence < result.completeInventory.sequence && result.completeInventory.sequence < result.firstReturn.sequence
                && AllInventory(result.completeInventory, true) && AllInventory(result.firstReturn, true);
            bool changedProvenance = ChangedProvenance(), baselineProvenance = BaselineProvenance();
            bool initialGateObserved = InitialWorkGateObserved(), freshSelection = FreshCargoSelectionWitness();
            bool controls = nativeComplete && ordinary && cleaning && healthy && conserved && roofIntact && unique && !storageDetour && freshSelection;
            bool satisfied = controls && sweep && noRepeat && changedProvenance;
            bool baselineGap = controls && baselineProvenance && result.departure != null && result.firstReturn != null
                && !sweep && !AllInventory(result.firstReturn, true);
            Check("native-completed-with-exact-consumption", nativeComplete, "Exact twelve milk/fourteen rice/fourteen potatoes consumed once, four meals, one released native cleanup, 300 elapsed stable ticks.");
            Check("ordinary-native-work", ordinary, "One actual native Notify_Starting and ordinary WorkGiver_DoBill provenance; no forced/batch job.");
            Check("three-native-cs-cleaning-pairs", cleaning, "Three one-layer removals each precede their same-filth/job/tick record increment before product.");
            Check("initial-held-and-unique-floor-acquisition", unique, "Unique newly held units: Milk0, RawRice14, RawPotatoes14. Initial Milk12 is separate.");
            Check("settled-custody-and-ancestry-conserved", conserved && accounting.Healthy, "Every settled boundary reconciles all forty origin units across actual live custody plus attributed native consumption.");
            Check("observers-and-roof-healthy", healthy && roofIntact, "Observer faults and observed exceptions remain failures; supported roof preserved.");
            Check("no-ingredient-storage-detour", !storageDetour, "No observed held-to-floor ingredient drop outside the bench region.");
            Check("fresh-inventory-milk-selected", freshSelection, "One explicit setup pickup clock; first native selection retains original tagged Milk12 within grace, with no raw unload flag or queued HD unload.");
            Check("initial-work-gate-observed", initialGateObserved, "Actual downtime callback inside complete early empty-work before/after HD brackets; independent of the gate's value or selected unload.");
            Check("complete-inventory-before-first-return", sweep, "Departure, full outside inventory, and first return are separate actual coordinate/custody witnesses.", "requested");
            Check("no-reentry-second-excursion-or-extra-gather", noRepeat, "No unit reentered held custody and exactly one gather executed.", "requested");
            Check("changed-complete-selection-provenance", changedProvenance, "Native candidate → routed candidate → requested/actual gather → native handoff before instant toils, with full forty-unit selection.", "requested");
            Check("baseline-initial-milk-selected", baselineProvenance, "Baseline acceptance requires the initial actual inventory milk selection and genuine native execution, not timeout.", "baseline");
            result.fixtureValid = true; result.timedOut = timeout; result.completedNativeRecipe = nativeComplete;
            result.initialWorkGateObserved = initialGateObserved;
            result.fullInventoryBeforeReturn = sweep; result.changedProvenanceComplete = changedProvenance; result.baselineProvenanceComplete = baselineProvenance;
            result.requestedBehaviorSatisfied = satisfied; result.expectationMatched = expectedBehavior == "satisfied" ? satisfied : baselineGap;
            result.status = timeout || !initialGateObserved || (expectedBehavior == "baseline-gap" && (!controls || !baselineProvenance)) ? "inconclusive"
                : !result.expectationMatched ? "failed" : satisfied ? "passed" : "behavior-gap-observed";
            result.finishedTick = tick; result.stableSinceTick = stableSince; result.stableTickCount = stableTickCount;
            result.stateEvents = stateEvents; result.gatherJobs = gatherStarts.Count; result.nativeJobs = nativeStarts.Count; result.final = final;
            Dispose(); HarnessSession.Event("bg03-p1-result", Json.Stringify(result)); return result;
        }
        private void Check(string id, bool passed, string detail, string category = "control")
        {
            var row = new Bg03Assertion { id = id, category = category, passed = passed, detail = detail };
            result.assertions.Add(row); HarnessSession.Event("bg03-assertion", Json.Stringify(row));
        }
        public void Dispose() { active = false; observers?.Dispose(); observers = null; }
    }
}
