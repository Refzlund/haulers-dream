using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    // Standalone D2: Tick is called once by each actual GameComponentTick. The
    // shared healthy scenario is this case's execution witness, not another D1.
    internal sealed class B1InFlightScenario : IDisposable
    {
        private const int MaximumProbes = 8, MaximumTicks = 6000, MaximumEvents = 20000;
        private readonly Map map;
        private readonly B1InFlightResult result;
        private readonly WorkGiver_Scanner scanner;
        internal readonly L04B1Bridge Api;
        private readonly Dictionary<JobDriver, int> drivers = new Dictionary<JobDriver, int>();
        private readonly Dictionary<PawnPath, int> paths = new Dictionary<PawnPath, int>();
        private B1HealthyScenario healthy;
        private B1InFlightObservers observers;
        private Pawn actor;
        private Thing anchor;
        private Job firstBulk, lastCandidate;
        private JobDriver firstDriver;
        private string firstBulkCell;
        private B1InFlightObservation priorApproach, burstInitial;
        private bool disposed, fault, burstClosed, waitingPostThreshold;
        private int sequence, lastTick = -1, lastProbeTick = -1;
        private int? activeOrdinal;
        private string caller;

        internal B1InFlightScenario(Map map, string expectedBehavior)
        {
            this.map = map;
            result = new B1InFlightResult { expectedBehavior = expectedBehavior, startedTick = Find.TickManager.TicksGame,
                counterContract = L04B1Bridge.CounterContract, probeStateUnchanged = true, counterTransitionsValid = true };
            try
            {
                Require("expectation", expectedBehavior == "baseline-gap" || expectedBehavior == "satisfied", expectedBehavior);
                Require("main-map", map != null && map.IsPlayerHome && UnityData.IsInMainThread, "Actual initialized disposable map.");
                Api = new L04B1Bridge(); result.bindings = Api.Bindings.ToList();
                BindPassiveReaders();
                scanner = DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").Worker as WorkGiver_Scanner;
                Require("scanner", scanner != null && scanner.GetType() == typeof(WorkGiver_HaulGeneral), "Actual def-owned HaulGeneral automatic scanner.");
                healthy = new B1HealthyScenario(map, "satisfied"); actor = healthy.ObservationActor;
                Require("fresh-actor", actor != null && actor.Spawned && actor.Map == map && actor.CurJob == null,
                    "Shared setup supplied a fresh actor; no fixture job assignment.");
                observers = new B1InFlightObservers(this);
                Append(new B1InFlightEvent { kind = "fixture-ready", detail = result.deliveryRole
                    + " Native first-source cell advance and exact current-job self-reservation required; at most8 one-per-tick automatic probes." });
            }
            catch { Dispose(); throw; }
        }
        private void Require(string id, bool okay, string detail)
        {
            HarnessSession.Check("fixture-l04-b1-d2-" + id, okay, detail);
            if (!okay) throw new InvalidOperationException(id + ": " + detail);
        }
        private void BindPassiveReaders()
        {
            void Field(Type type, string name, Type expected)
            {
                var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f == null || f.FieldType != expected || f.IsStatic) throw new MissingFieldException(type.FullName, name);
                result.bindings.Add("passive-field=" + type.FullName + "." + name + ";type=" + expected.FullName + ";token=" + f.MetadataToken + ";mvid=" + f.Module.ModuleVersionId);
            }
            void Getter(Type type, string name, Type expected)
            {
                var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                var getter = p?.GetGetMethod(true);
                if (p?.PropertyType != expected || getter == null || getter.IsStatic || getter.GetParameters().Length != 0)
                    throw new MissingMemberException(type.FullName, name);
                result.bindings.Add("passive-getter=" + type.FullName + "." + getter.Name + ";type=" + expected.FullName
                    + ";token=" + getter.MetadataToken + ";mvid=" + getter.Module.ModuleVersionId);
            }
            Getter(typeof(Pawn_PathFollower), "Moving", typeof(bool)); Getter(typeof(Pawn_PathFollower), "Destination", typeof(LocalTargetInfo));
            Field(typeof(Pawn_PathFollower), "nextCell", typeof(IntVec3)); Field(typeof(Pawn_PathFollower), "nextCellCostLeft", typeof(float));
            Field(typeof(Pawn_PathFollower), "nextCellCostTotal", typeof(float)); Field(typeof(Pawn_PathFollower), "curPath", typeof(PawnPath));
            Getter(typeof(ReservationManager), "ReservationsReadOnly", typeof(List<ReservationManager.Reservation>));
            Getter(typeof(ReservationManager.Reservation), "Claimant", typeof(Pawn)); Getter(typeof(ReservationManager.Reservation), "Job", typeof(Job));
            Getter(typeof(ReservationManager.Reservation), "Target", typeof(LocalTargetInfo)); Getter(typeof(ReservationManager.Reservation), "StackCount", typeof(int));
            Getter(typeof(ReservationManager.Reservation), "MaxPawns", typeof(int));
            Getter(typeof(ReservationManager.Reservation), "Layer", typeof(ReservationLayerDef));
        }
        internal bool IsActor(Pawn pawn) => ReferenceEquals(pawn, actor);
        internal bool IsSource(Thing thing) => healthy != null && healthy.IsOriginalSource(thing);
        internal Pawn ActorFor(Pawn_JobTracker jobs) => actor != null && ReferenceEquals(actor.jobs, jobs) ? actor : null;
        private int DriverToken(JobDriver driver)
        { if (driver == null) return 0; if (!drivers.TryGetValue(driver, out int token)) drivers.Add(driver, token = drivers.Count + 1); return token; }
        private int PathToken(PawnPath path)
        { if (path == null) return 0; if (!paths.TryGetValue(path, out int token)) paths.Add(path, token = paths.Count + 1); return token; }
        private void Append(B1InFlightEvent e)
        {
            // Leave room for a structured failed terminal result after the bound.
            if (result.events.Count >= MaximumEvents && e.kind != "assertion" && e.kind != "burst-close")
                throw new InvalidOperationException("D2 event bound exceeded.");
            e.sequence = ++sequence; e.tick = Find.TickManager.TicksGame;
            if (e.call != null) { e.call.sequence = e.sequence; e.call.tick = e.tick; }
            if (e.observation != null) { e.observation.sequence = e.sequence; e.observation.tick = e.tick; }
            result.events.Add(e); HarnessSession.Event("l04-b1-d2-event", Json.Stringify(e));
        }
        internal void EmitCall(B1Event call)
        {
            call.scene = "D2"; call.caller = caller ?? "native-or-external"; call.queryOrdinal = activeOrdinal;
            Append(new B1InFlightEvent { kind = "call", call = call });
        }
        internal void ObserverFault(Exception error)
        {
            if (fault) return; fault = true; result.error = error.ToString();
            if (result.events.Count < MaximumEvents) Append(new B1InFlightEvent { kind = "observer-fault", detail = result.error });
        }
        internal void ObservedCall(B1InFlightObservers.Token token, string boundary, Job job, bool? returned)
        {
            var e = new B1Event { kind = token.kind + "-" + boundary, actorId = token.pawn.thingIDNumber,
                sourceId = token.source?.thingIDNumber ?? -1, callId = token.id, parentCallId = token.parent,
                forced = token.forced, forceSweep = token.sweep, returned = returned,
                inputJob = Api.Job(token.input), job = Api.Job(job), actualCurrent = job != null
                    && ReferenceEquals(actor.CurJob, job) && ReferenceEquals(actor.jobs.curDriver?.job, job) };
            if (IsSource(token.source)) { e.counter = Api.Counter(token.source); e.cache = Api.Cache(actor, token.source); }
            EmitCall(e);
            if (activeOrdinal.HasValue && token.kind == "candidate" && boundary == "return" && ReferenceEquals(token.source, anchor)) lastCandidate = job;
        }
        internal void StartEvent(B1InFlightObservers.Token token, bool after)
        {
            bool actual = after && ReferenceEquals(actor.CurJob, token.input) && ReferenceEquals(actor.jobs.curDriver?.job, token.input);
            EmitCall(new B1Event { kind = after ? "start-return" : "start-attempt", actorId = actor.thingIDNumber,
                callId = token.id, inputJob = Api.Job(token.input), job = Api.Job(actor.CurJob, actor.jobs.curDriver), actualCurrent = actual });
            if (!actual || firstBulk != null || actor.CurJob.def?.defName != B1HealthyScenario.BulkDef) return;
            firstBulk = actor.CurJob; firstDriver = actor.jobs.curDriver; anchor = firstBulk.targetA.Thing;
            firstBulkCell = actor.Position.ToString(); result.firstBulkToken = Api.Token(firstBulk); result.firstBulkJobId = firstBulk.loadID;
            result.firstBulkDriverToken = DriverToken(firstDriver); result.anchorId = anchor?.thingIDNumber ?? -1;
            result.initialBulkJob = Api.Job(firstBulk, firstDriver);
            Append(new B1InFlightEvent { kind = "first-bulk-current", detail = "Actual native StartJob/current-driver witness; cell=" + firstBulkCell });
        }
        internal void Warning(string text, int noteSource, B1InFlightObservers.Token sourceCall, int parentCall)
        {
            EmitCall(new B1Event { kind = "warning", detail = text, sourceId = noteSource >= 0 ? noteSource : sourceCall?.source?.thingIDNumber ?? -1,
                actorId = sourceCall?.pawn?.thingIDNumber ?? -1, parentCallId = parentCall,
                warningAttribution = noteSource >= 0 ? "legacy-note" : sourceCall?.source != null ? "enclosing-source-call" : "unattributed" });
        }
        internal void ReservationGate(B1InFlightReservationGate gate)
        {
            gate.caller = caller ?? "native-or-external"; gate.queryOrdinal = activeOrdinal;
            Append(new B1InFlightEvent { kind = "reservation-gate", reservationGate = gate });
        }
        private B1InFlightPhysical Physical()
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("D2 attempted a worker-state read.");
            var current = actor.CurJob; var driver = actor.jobs.curDriver; var pather = actor.pather;
            var state = new B1InFlightPhysical { tick = Find.TickManager.TicksGame, mapId = map.uniqueID, actorId = actor.thingIDNumber,
                spawned = actor.Spawned && actor.Map == map, healthy = !actor.Dead && !actor.Downed, drafted = actor.Drafted,
                current = Api.Job(current, driver), driverToken = DriverToken(driver), driverOwnsCurrent = driver != null && ReferenceEquals(driver.job, current),
                currentTargetBThing = current?.targetB.Thing?.thingIDNumber ?? -1, currentTargetBCell = current?.targetB.Cell.ToString(),
                queued = actor.jobs.jobQueue.Select(q => Api.Job(q.job)).ToList(), anchor = healthy.Describe(anchor), cargo = healthy.Snapshot() };
            if (pather != null) state.path = new B1InFlightPath { moving = pather.Moving, pathToken = PathToken(pather.curPath),
                nextCell = pather.nextCell.ToString(), costLeft = pather.nextCellCostLeft, costTotal = pather.nextCellCostTotal,
                destinationThing = pather.Destination.Thing?.thingIDNumber ?? -1, destinationCell = pather.Destination.Cell.ToString() };
            // Copy actual rows; never sort or alter the manager's mutable backing list.
            var reservations = map.reservationManager.ReservationsReadOnly.Where(r => ReferenceEquals(r.Claimant, actor) || IsSource(r.Target.Thing)).ToList();
            var physical = L04B1Bridge.PhysicalReservations(map).Where(r => ReferenceEquals(r.claimant, actor) || IsSource(r.target.Thing)).ToList();
            if (reservations.Count + physical.Count > 64) throw new InvalidOperationException("D2 reservation observation bound exceeded.");
            foreach (var r in reservations) state.reservations.Add(new B1InFlightReservation { kind = "normal", pawnId = r.Claimant?.thingIDNumber ?? -1, jobId = r.Job?.loadID ?? -1,
                jobToken = Api.Token(r.Job), thingId = r.Target.Thing?.thingIDNumber ?? -1, cell = r.Target.Cell.ToString(), count = r.StackCount,
                maxPawns = r.MaxPawns, layer = r.Layer?.defName });
            foreach (var r in physical) state.reservations.Add(new B1InFlightReservation { kind = "physical-interaction", pawnId = r.claimant?.thingIDNumber ?? -1, jobId = r.job?.loadID ?? -1,
                jobToken = Api.Token(r.job), thingId = r.target.Thing?.thingIDNumber ?? -1, cell = r.target.Cell.ToString(), count = -1, maxPawns = null, layer = null });
            state.reservations = state.reservations.OrderBy(r => r.pawnId).ThenBy(r => r.jobToken).ThenBy(r => r.thingId).ThenBy(r => r.kind)
                .ThenBy(r => r.layer).ThenBy(r => r.cell).ThenBy(r => r.count).ThenBy(r => r.maxPawns).ToList();
            return state;
        }
        private B1InFlightObservation Observe(string purpose)
        {
            var o = new B1InFlightObservation { purpose = purpose, physical = Physical() };
            if (anchor != null && IsSource(anchor)) { o.counter = Api.Counter(anchor); o.cache = Api.Cache(actor, anchor); }
            Append(new B1InFlightEvent { kind = "observation", observation = o }); return o;
        }
        private bool Approach(B1InFlightPhysical p)
        {
            return firstBulk != null && IsSource(anchor) && ReferenceEquals(actor.CurJob, firstBulk) && firstBulk.loadID == result.firstBulkJobId
                && ReferenceEquals(actor.jobs.curDriver, firstDriver) && ReferenceEquals(firstDriver.job, firstBulk)
                && p.current?.def == B1HealthyScenario.BulkDef && p.current.driver == "HaulersDream.JobDriver_BulkHaul" && !p.current.forced
                && p.spawned && p.healthy && !p.drafted && p.driverOwnsCurrent && p.queued.Count == 0
                && anchor.Spawned && anchor.Map == map && ReferenceEquals(anchor.ParentHolder, map) && anchor.stackCount == 5
                && ReferenceEquals(firstBulk.targetA.Thing, anchor) && ReferenceEquals(firstBulk.targetB.Thing, anchor)
                && firstBulk.targetQueueB?.Count == 2 && firstBulk.countQueue?.Count == 2
                && ReferenceEquals(firstBulk.targetQueueB[0].Thing, anchor) && firstBulk.targetQueueB.All(t => IsSource(t.Thing))
                && !ReferenceEquals(firstBulk.targetQueueB[0].Thing, firstBulk.targetQueueB[1].Thing) && firstBulk.countQueue.All(n => n == 5)
                && p.cargo.source == 10 && p.cargo.high == 0 && p.cargo.inventory == 0 && p.cargo.hands == 0 && p.cargo.elsewhere == 0 && p.cargo.total == 10
                && p.path != null && p.path.moving && p.path.pathToken > 0 && p.path.destinationThing == result.anchorId && p.path.destinationCell == anchor.Position.ToString()
                && !float.IsNaN(p.path.costLeft) && !float.IsInfinity(p.path.costLeft) && !float.IsNaN(p.path.costTotal) && !float.IsInfinity(p.path.costTotal) && p.path.costTotal > 0
                && p.reservations.Any(r => r.kind == "normal" && r.layer == null && r.pawnId == actor.thingIDNumber && r.jobToken == result.firstBulkToken
                    && r.jobId == result.firstBulkJobId && r.thingId == result.anchorId && (r.count == -1 || r.count >= 5));
        }
        // Structural reads only. No serialization of anonymous types and no
        // normalization writes to captured DTOs. Only the documented observer
        // sequence / cross-tick counter observation tick are omitted.
        private static bool SameList<T>(IList<T> a, IList<T> b, Func<T, T, bool> same)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!same(a[i], b[i])) return false;
            return true;
        }
        private static bool SameThing(B1HealthyThing a, B1HealthyThing b) => ReferenceEquals(a, b) || (a != null && b != null
            && a.id == b.id && a.count == b.count && a.destroyed == b.destroyed && a.spawned == b.spawned
            && a.inventory == b.inventory && a.hands == b.hands && a.cell == b.cell && a.holder == b.holder);
        private static bool SameClaim(B1HealthyClaim a, B1HealthyClaim b) => ReferenceEquals(a, b) || (a != null && b != null
            && a.group == b.group && a.recordedUnits == b.recordedUnits && a.high == b.high);
        private static bool SameCargo(B1HealthyState a, B1HealthyState b) => ReferenceEquals(a, b) || (a != null && b != null
            && a.tick == b.tick && a.jobId == b.jobId && a.jobDef == b.jobDef && a.position == b.position
            && a.source == b.source && a.high == b.high && a.elsewhere == b.elsewhere && a.inventory == b.inventory && a.hands == b.hands && a.total == b.total
            && SameList(a.things, b.things, SameThing) && SameList(a.claims, b.claims, SameClaim));
        private static bool SamePath(B1InFlightPath a, B1InFlightPath b) => ReferenceEquals(a, b) || (a != null && b != null
            && a.moving == b.moving && a.pathToken == b.pathToken && a.destinationThing == b.destinationThing && a.destinationCell == b.destinationCell
            && a.nextCell == b.nextCell && a.costLeft == b.costLeft && a.costTotal == b.costTotal);
        private static bool SameReservation(B1InFlightReservation a, B1InFlightReservation b) => ReferenceEquals(a, b) || (a != null && b != null
            && a.kind == b.kind && a.pawnId == b.pawnId && a.jobToken == b.jobToken && a.jobId == b.jobId && a.thingId == b.thingId
            && a.cell == b.cell && a.count == b.count && a.maxPawns == b.maxPawns && a.layer == b.layer);
        private static bool SamePhysical(B1InFlightPhysical a, B1InFlightPhysical b) => ReferenceEquals(a, b) || (a != null && b != null
            && a.tick == b.tick && a.mapId == b.mapId && a.actorId == b.actorId && a.driverToken == b.driverToken
            && a.spawned == b.spawned && a.healthy == b.healthy && a.drafted == b.drafted && a.driverOwnsCurrent == b.driverOwnsCurrent
            && a.currentTargetBThing == b.currentTargetBThing && a.currentTargetBCell == b.currentTargetBCell
            && SameJob(a.current, b.current) && SameList(a.queued, b.queued, SameJob) && SameThing(a.anchor, b.anchor)
            && SamePath(a.path, b.path) && SameCargo(a.cargo, b.cargo) && SameList(a.reservations, b.reservations, SameReservation));
        private static bool SameCounter(B1Counter a, B1Counter b) => ReferenceEquals(a, b) || (a != null && b != null
            && a.contract == b.contract && a.subjectId == b.subjectId && a.anchorPresent == b.anchorPresent && a.anchorTick == b.anchorTick
            && a.count == b.count && a.stackCount == b.stackCount && a.backoffPresent == b.backoffPresent && a.until == b.until
            && a.warned == b.warned && a.failPresent == b.failPresent && a.failTick == b.failTick && a.failCount == b.failCount && a.backedOff == b.backedOff);
        private static int EffectivePrior(B1Counter c, int tick, int stack, out string reason)
        {
            if (c == null || !c.anchorPresent || !c.count.HasValue || c.count.Value <= 0) { reason = "absent-or-nonpositive"; return 0; }
            if (!c.anchorTick.HasValue || !c.stackCount.HasValue) throw new InvalidOperationException("Incomplete legacy anchor tuple.");
            if (c.anchorTick.Value > tick) throw new InvalidOperationException("Legacy row is from a future tick.");
            if ((long)tick - c.anchorTick.Value > 180) { reason = "gap-exceeds180"; return 0; }
            if (stack < c.stackCount.Value) { reason = "source-count-shrank"; return 0; }
            reason = "live-unshrunk-row"; return c.count.Value;
        }
        private void OpenBurst(B1InFlightObservation o)
        {
            if (o.counter == null || o.counter.backedOff || o.counter.backoffPresent || o.counter.warned || o.counter.failPresent)
            { CloseBurst("Preexisting backoff/warning/failure prevents a fresh source control.", true); return; }
            result.initialCounter = o.counter; result.initialCount = o.counter.count ?? 0;
            result.initialEffectiveCount = EffectivePrior(o.counter, o.tick, 5, out string reason); result.initialRowResetReason = reason;
            result.thresholdBuildsNeeded = 6 - result.initialEffectiveCount;
            if (result.thresholdBuildsNeeded <= 0 || result.thresholdBuildsNeeded > 6) { CloseBurst("Unreviewed initial threshold state.", true); return; }
            burstInitial = o; result.burstEntrySequence = o.sequence; result.approachWindowExercised = true;
            Append(new B1InFlightEvent { kind = "burst-open", detail = "rawCount=" + result.initialCount + "; effectiveCount=" + result.initialEffectiveCount
                + "; reset=" + reason + "; neededBuilds=" + result.thresholdBuildsNeeded + "; maximumProbes=" + MaximumProbes });
        }
        private void CloseBurst(string reason, bool unexercised)
        {
            if (burstClosed) return; burstClosed = true; result.burstStopReason = reason;
            if (!unexercised && !result.withinBurstProgress)
            { unexercised = true; reason += " No actual movement was observed between two queries within this burst."; result.burstStopReason = reason; }
            if (unexercised) result.unexercisedReason = reason;
            Append(new B1InFlightEvent { kind = "burst-close", detail = reason });
        }
        private static B1InFlightProgress ProgressBetween(B1InFlightObservation earlier, B1InFlightObservation later)
        {
            if (earlier == null || later == null || earlier.tick >= later.tick || earlier.sequence >= later.sequence) return null;
            var a = earlier.physical; var b = later.physical;
            if (a?.current == null || b?.current == null || a.path == null || b.path == null || a.cargo == null || b.cargo == null
                || a.current.token != b.current.token || a.current.loadId != b.current.loadId || a.driverToken != b.driverToken) return null;
            bool cell = a.cargo.position != b.cargo.position;
            bool cost = a.path.pathToken == b.path.pathToken && a.path.nextCell == b.path.nextCell && a.path.costTotal == b.path.costTotal
                && b.path.costLeft < a.path.costLeft;
            if (!cell && !cost) return null;
            return new B1InFlightProgress { fromObservationSequence = earlier.sequence, toObservationSequence = later.sequence,
                fromTick = earlier.tick, toTick = later.tick, cellChanged = cell, pathCostDecreased = cost };
        }
        private void Probe()
        {
            int now = Find.TickManager.TicksGame;
            if (result.queries.Count >= MaximumProbes || lastProbeTick == now || (lastProbeTick >= 0 && now != lastProbeTick + 1))
                throw new InvalidOperationException("D2 probe count/consecutive actual-tick contract violated.");
            var before = Observe("query-before"); if (!Approach(before.physical)) { CloseBurst("First-source approach ended before the next bounded query.", true); return; }
            var previous = result.queries.LastOrDefault()?.after ?? burstInitial;
            if (!SameCounter(previous.counter, before.counter)
                || result.events.Any(e => e.sequence > previous.sequence && e.sequence < before.sequence && e.call?.kind == "note-enter" && e.call.sourceId == result.anchorId))
            { CloseBurst("Counter changed between measured queries; the recorded native chronology cannot isolate this burst's threshold contribution.", true); return; }
            var priorQuery = result.queries.LastOrDefault();
            // The first comparison cannot borrow pre-burst motion: both ends
            // belong to actual consecutive probes, after/before their calls.
            var progress = priorQuery?.stateUnchanged == true ? ProgressBetween(priorQuery.after, before) : null;
            if (!result.withinBurstProgress && progress != null)
            {
                result.withinBurstProgress = true; result.burstProgressFromSequence = progress.fromObservationSequence;
                result.burstProgressToSequence = progress.toObservationSequence;
                Append(new B1InFlightEvent { kind = "burst-progress", progress = progress });
            }
            int ordinal = result.queries.Count + 1; lastCandidate = null; bool returned;
            caller = "fixture-in-flight-query"; activeOrdinal = ordinal;
            try { returned = scanner.HasJobOnThing(actor, anchor, false); }
            finally { caller = null; activeOrdinal = null; }
            var after = Observe("query-after");
            var calls = result.events.Where(e => e.call != null && e.sequence > before.sequence && e.sequence < after.sequence).Select(e => e.call).ToList();
            var gates = result.events.Where(e => e.reservationGate != null && e.sequence > before.sequence && e.sequence < after.sequence).Select(e => e.reservationGate).ToList();
            var q = new B1InFlightQuery { ordinal = ordinal, tick = now, kind = waitingPostThreshold ? "post-threshold-check" : "candidate-probe",
                beforeSequence = before.sequence, afterSequence = after.sequence, before = before, after = after, returned = returned,
                candidate = Api.Job(lastCandidate), candidateIsCurrent = lastCandidate != null && ReferenceEquals(lastCandidate, firstBulk),
                actualBuilds = calls.Count(e => e.kind == "build-return"), actualNotes = calls.Count(e => e.kind == "note-enter"),
                stateUnchanged = SamePhysical(before.physical, after.physical) && Approach(after.physical),
                selfReservationGateObserved = gates.Any(g => g.actorId == actor.thingIDNumber && g.sourceId == result.anchorId
                    && g.queryOrdinal == ordinal && g.caller == "fixture-in-flight-query" && g.returned && !g.ignoreOtherReservations
                    && g.maxPawns == 1 && g.layer == null && (g.stackCount == -1 || (g.stackCount > 0 && g.stackCount <= 5))) };
            q.completeCall = CompleteQuery(q, calls);
            q.cacheCoherent = QueryCache(q, calls);
            q.successfulBuilds = q.completeCall && q.cacheCoherent && q.returned && IsBulkPlan(q.candidate) && q.selfReservationGateObserved
                ? calls.Count(e => e.kind == "build-return" && SameJob(e.job, q.candidate)) : 0;
            result.queries.Add(q); lastProbeTick = now;
            result.probeStateUnchanged &= q.stateUnchanged && calls.All(e => e.kind != "start-attempt") && q.completeCall && q.cacheCoherent;
            result.actualBuilds += q.actualBuilds; result.successfulBuilds += q.successfulBuilds; result.actualNotes += q.actualNotes;
            if (returned && IsBulkPlan(q.candidate) && q.selfReservationGateObserved)
                result.successfulCandidateQueries++;
            result.counterTransitionsValid &= ReplayCounter(q, calls);
            Append(new B1InFlightEvent { kind = "query", query = q });
            if (waitingPostThreshold) { result.postThresholdRejected = !returned && q.candidate == null && after.counter.backedOff; CloseBurst("Measured post-threshold automatic gate.", false); return; }
            if (!returned || q.candidate == null) { CloseBurst("Native automatic query rejected before sufficient counter-path coverage; no gate was bypassed.", true); return; }
            if (after.counter.backedOff) waitingPostThreshold = true;
            else if (result.successfulBuilds >= result.thresholdBuildsNeeded && result.successfulCandidateQueries >= result.thresholdBuildsNeeded && result.withinBurstProgress)
            { CloseBurst("Required equivalent actual-build coverage completed.", false); return; }
            if (result.queries.Count == MaximumProbes) CloseBurst("Eight-probe bound reached before complete threshold/post-threshold coverage.", true);
        }
        private static bool SameJob(B1Job a, B1Job b) => ReferenceEquals(a, b) || (a != null && b != null
            && a.token == b.token && a.loadId == b.loadId && a.def == b.def && a.startTick == b.startTick && a.expiry == b.expiry
            && a.forced == b.forced && a.targetA == b.targetA && a.driver == b.driver && a.workgiver == b.workgiver && a.workgiverClass == b.workgiverClass
            && SameList(a.queueIds, b.queueIds, (x, y) => x == y) && SameList(a.counts, b.counts, (x, y) => x == y));
        private bool IsBulkPlan(B1Job job) => job != null && result.initialBulkJob != null && job.def == B1HealthyScenario.BulkDef
            && job.token > 0 && job.loadId >= 0 && !job.forced && job.targetA == result.anchorId
            && job.queueIds.Count == 2 && job.queueIds.Distinct().Count() == 2 && job.queueIds[0] == result.anchorId
            && job.queueIds.OrderBy(n => n).SequenceEqual(result.initialBulkJob.queueIds.OrderBy(n => n))
            && job.counts.Count == 2 && job.counts.All(n => n == 5);
        private bool CompleteQuery(B1InFlightQuery q, List<B1Event> calls)
        {
            var stack = new Stack<B1Event>(); int hasCount = 0, candidateCount = 0, tryCount = 0, buildCount = 0, noteCount = 0;
            foreach (var e in calls)
            {
                if (e.caller != "fixture-in-flight-query" || e.queryOrdinal != q.ordinal || e.tick != q.tick || e.scene != "D2") return false;
                if (e.kind == "warning") continue; // Exact text and enclosing Note interval are graded separately.
                if (e.kind == "make-job")
                { if (stack.Count == 0 || e.parentCallId != stack.Peek().callId || e.job == null) return false; continue; }
                if (e.kind.EndsWith("-enter", StringComparison.Ordinal))
                {
                    string family = e.kind.Substring(0, e.kind.Length - 6), parent = stack.Count == 0 ? null : stack.Peek().kind;
                    if (e.sourceId != result.anchorId || e.callId <= 0 || e.parentCallId != (stack.Count == 0 ? 0 : stack.Peek().callId)) return false;
                    if (family == "note") { if (parent != "build-enter" || ++noteCount > 1 || e.actorId != 0 || e.forced != null || e.forceSweep != null) return false; }
                    else
                    {
                        if (e.actorId != actor.thingIDNumber || e.forced != false || e.forceSweep != false) return false;
                        if (family == "has") { if (parent != null || ++hasCount > 1) return false; }
                        else if (family == "candidate") { if (parent != "has-enter" || ++candidateCount > 1) return false; }
                        else if (family == "try-build") { if (parent != "candidate-enter" || ++tryCount > 1) return false; }
                        else if (family == "build") { if (parent != "try-build-enter" || ++buildCount > 1) return false; }
                        else return false;
                    }
                    stack.Push(e); continue;
                }
                if (!e.kind.EndsWith("-return", StringComparison.Ordinal) || stack.Count == 0) return false;
                var start = stack.Pop(); string expected = start.kind.Substring(0, start.kind.Length - 6) + "-return";
                if (e.kind != expected || e.callId != start.callId || e.sourceId != start.sourceId || e.actorId != start.actorId
                    || e.forced != start.forced || e.forceSweep != start.forceSweep || !SameJob(e.inputJob, start.inputJob)
                    || e.parentCallId != (e.kind == "note-return" ? 0 : start.parentCallId)) return false;
                if (e.kind == "has-return") { if (e.returned != q.returned || e.job != null) return false; }
                else if (e.kind == "candidate-return") { if (e.returned != q.returned || !SameJob(e.job, q.candidate)) return false; }
                else if (e.kind != "note-return" && e.returned != (e.job != null)) return false;
            }
            if (stack.Count != 0 || hasCount != 1 || candidateCount != 1) return false;
            var built = calls.SingleOrDefault(e => e.kind == "build-return"); var tried = calls.SingleOrDefault(e => e.kind == "try-build-return");
            if (built != null && (tried == null || !SameJob(built.job, tried.job))) return false;
            if (tried?.job != null && !SameJob(tried.job, q.candidate)) return false;
            return q.actualBuilds == buildCount && q.actualNotes == noteCount;
        }
        private bool QueryCache(B1InFlightQuery q, List<B1Event> calls)
        {
            long key = ((long)actor.thingIDNumber << 32) | (uint)result.anchorId;
            foreach (var c in new[] { q.before.cache, q.after.cache })
                if (c == null || c.tick != q.tick || c.key != key || c.pawnId != actor.thingIDNumber || c.sourceId != result.anchorId) return false;
            var tries = calls.Where(e => e.kind == "try-build-return").ToList();
            if (tries.Count == 0) return Json.Stringify(q.before.cache) == Json.Stringify(q.after.cache);
            if (tries.Count != 1) return false;
            if (tries[0].job == null)
                return Json.Stringify(q.before.cache) == Json.Stringify(q.after.cache)
                    || (q.after.cache.generation == q.tick && q.after.cache.entryPresent && q.after.cache.actualJob == null
                        && q.after.cache.pinnedLoadId == -1 && q.after.cache.jobState == 0);
            var after = q.after.cache;
            if (!IsBulkPlan(q.candidate) || !after.dictionaryPresent || !after.entryPresent || after.generation != q.tick || after.jobState != 0
                || after.pinnedLoadId != q.candidate.loadId || !SameJob(after.actualJob, q.candidate)) return false;
            if (q.actualBuilds == 1) return true;
            var before = q.before.cache;
            return q.actualBuilds == 0 && before.dictionaryPresent && before.entryPresent && before.generation == q.tick && before.jobState == 0
                && before.pinnedLoadId == q.candidate.loadId && SameJob(before.actualJob, q.candidate);
        }
        private bool ReplayCounter(B1InFlightQuery q, List<B1Event> calls)
        {
            var previous = q.before.counter;
            foreach (var start in calls.Where(e => e.kind == "note-enter"))
            {
                var ends = calls.Where(e => e.kind == "note-return" && e.callId == start.callId && e.sequence > start.sequence).ToList();
                if (ends.Count != 1 || start.sourceId != result.anchorId || start.counter?.tick != q.tick || !SameCounter(previous, start.counter)) return false;
                var end = ends[0].counter; int next = EffectivePrior(start.counter, q.tick, 5, out _) + 1;
                if (next >= 6)
                {
                    if (end.anchorPresent || !end.backoffPresent || end.until != q.tick + 2500 || !end.backedOff || !end.warned) return false;
                }
                else if (!end.anchorPresent || end.count != next || end.anchorTick != q.tick || end.stackCount != 5
                    || end.backoffPresent || end.backedOff || end.warned) return false;
                if (end.failPresent || end.subjectId != result.anchorId || end.tick != q.tick || end.contract != L04B1Bridge.CounterContract) return false;
                previous = end;
            }
            return SameCounter(previous, q.after.counter);
        }
        internal B1InFlightResult Tick()
        {
            if (disposed) return result.status == "running" ? null : result;
            try
            {
                int now = Find.TickManager.TicksGame;
                if (now == lastTick) throw new InvalidOperationException("D2 Tick called twice in one actual game tick.");
                if (lastTick < 0 && (now < result.startedTick || now > result.startedTick + 1))
                    throw new InvalidOperationException("D2 integration missed ticks between construction and the first GameComponentTick.");
                if (lastTick >= 0 && now != lastTick + 1) throw new InvalidOperationException("D2 missed an actual GameComponentTick."); lastTick = now;
                observers?.DrainThreadFault();
                if (fault) return Finish(null);
                healthy.ObserveSettled("game-tick");
                var observed = Observe("game-tick");
                if (!burstClosed && firstBulk != null)
                {
                    bool approach = Approach(observed.physical);
                    if (approach)
                    {
                        if (result.approachEntrySequence == 0) result.approachEntrySequence = observed.sequence;
                        if (observed.physical.cargo.position != firstBulkCell && result.firstCellAdvanceSequence == 0) result.firstCellAdvanceSequence = observed.sequence;
                        if (ProgressBetween(priorApproach, observed) != null) result.realApproachProgress = true;
                        priorApproach = observed;
                        if (burstInitial == null && result.firstCellAdvanceSequence > 0) OpenBurst(observed);
                        if (!burstClosed && burstInitial != null) Probe();
                    }
                    else if (burstInitial != null) CloseBurst("Actual pinned first-source approach ended before complete query coverage.", true);
                }
                var done = healthy.TryFinish();
                if (done != null) return Finish(done);
                if (now - result.startedTick >= MaximumTicks) { result.timedOut = true; return Finish(null); }
                return null;
            }
            catch (Exception error) { ObserverFault(error); return Finish(null); }
        }
        private bool NativeDispatch()
        {
            var calls = result.events.Where(e => e.call != null).Select(e => e.call).ToList();
            var starts = calls.Where(e => e.kind == "start-return" && e.actualCurrent == true && e.job?.token == result.firstBulkToken
                && e.job.loadId == result.firstBulkJobId && e.job.def == B1HealthyScenario.BulkDef && e.job.driver == "HaulersDream.JobDriver_BulkHaul"
                && !e.job.forced && e.job.startTick == e.tick && e.job.workgiver == "HaulGeneral" && e.job.workgiverClass == typeof(WorkGiver_HaulGeneral).FullName).ToList();
            foreach (var start in starts)
                foreach (var work in calls.Where(e => e.kind == "native-work-return" && e.returned == true && e.sequence < start.sequence
                    && e.tick == start.tick && e.actorId == actor.thingIDNumber && e.job?.token == result.firstBulkToken && e.job.loadId == result.firstBulkJobId))
                {
                    var enter = calls.SingleOrDefault(e => e.kind == "native-work-enter" && e.callId == work.callId);
                    var attempt = calls.SingleOrDefault(e => e.kind == "start-attempt" && e.callId == start.callId);
                    if (enter == null || attempt == null || enter.sequence >= work.sequence || work.sequence >= attempt.sequence || attempt.sequence >= start.sequence
                        || enter.actorId != actor.thingIDNumber || attempt.actorId != actor.thingIDNumber || enter.tick != work.tick || attempt.tick != work.tick
                        || !StableJob(work.job, attempt.inputJob, true) || !StableJob(attempt.inputJob, start.job, true)
                        || !StableJob(result.initialBulkJob, start.job, true)) continue;
                    foreach (var candidate in calls.Where(e => e.kind == "candidate-return" && e.parentCallId == work.callId && e.sequence > enter.sequence
                        && e.sequence < work.sequence && e.actorId == actor.thingIDNumber && e.sourceId == result.anchorId && e.forced == false
                        && e.forceSweep == false && e.returned == true && e.tick == work.tick && StableJob(e.job, work.job, false)))
                    {
                        var selection = calls.SingleOrDefault(e => e.kind == "candidate-enter" && e.callId == candidate.callId);
                        if (selection == null || selection.parentCallId != work.callId || selection.sequence <= enter.sequence || selection.sequence >= candidate.sequence
                            || selection.actorId != actor.thingIDNumber || selection.sourceId != result.anchorId || selection.forced != false || selection.tick != work.tick) continue;
                        foreach (var has in calls.Where(e => e.kind == "has-return" && e.parentCallId == work.callId && e.sourceId == result.anchorId
                            && e.actorId == actor.thingIDNumber && e.returned == true && e.forced == false && e.tick == work.tick
                            && e.sequence > enter.sequence && e.sequence < selection.sequence))
                            if (calls.Any(e => e.kind == "has-enter" && e.callId == has.callId && e.parentCallId == work.callId
                                && e.actorId == actor.thingIDNumber && e.sourceId == result.anchorId && e.forced == false && e.tick == work.tick
                                && e.sequence > enter.sequence && e.sequence < has.sequence)
                                && new[] { enter, work, attempt, start, selection, candidate, has }.All(e => e.caller == "native-or-external" && e.queryOrdinal == null)) return true;
                    }
                }
            return false;
        }
        private static bool StableJob(B1Job a, B1Job b, bool workgiver) => a != null && b != null && a.token == b.token && a.loadId == b.loadId
            && a.def == b.def && a.expiry == b.expiry && a.forced == b.forced && a.targetA == b.targetA && a.queueIds.SequenceEqual(b.queueIds)
            && a.counts.SequenceEqual(b.counts) && (!workgiver || (a.workgiver == b.workgiver && a.workgiverClass == b.workgiverClass));
        private void Check(string id, string kind, bool okay, string detail)
        {
            var assertion = new B1Assertion { id = id, kind = kind, passed = okay, detail = detail };
            result.assertions.Add(assertion); Append(new B1InFlightEvent { kind = "assertion", assertion = assertion });
        }
        private bool KnownQueryWarning(B1Event warning, List<B1Event> calls)
        {
            const string suffix = " was bulk-hauled 6 times in quick succession without moving (net-zero). Another mod is very likely returning it to where HD keeps "
                + "re-hauling it (a logistics or loadout mod, e.g. RimIOT). HD is backing it off its automatic haul scan so pawns stop looping; a forced player order still hauls it. "
                + "Please report the mod combination (issue #214) if this is unexpected.";
            if (warning.sourceId != result.anchorId || warning.actorId != actor.thingIDNumber || warning.warningAttribution != "legacy-note"
                || warning.detail == null || warning.detail.Length <= suffix.Length || !warning.detail.EndsWith(suffix, StringComparison.Ordinal)) return false;
            return calls.Any(n => n.kind == "note-enter" && n.sourceId == result.anchorId && n.parentCallId == warning.parentCallId
                && n.sequence < warning.sequence && n.queryOrdinal == warning.queryOrdinal && n.tick == warning.tick
                && calls.Any(end => end.kind == "note-return" && end.callId == n.callId && end.sequence > warning.sequence
                    && end.counter?.warned == true && end.counter.backedOff && end.counter.until == warning.tick + 2500));
        }
        private B1InFlightResult Finish(B1HealthyResult delivery)
        {
            if (result.status != "running") return result;
            if (!burstClosed) CloseBurst("Delivery ended without a complete eligible first-source query window.", true);
            result.deliveryWitness = delivery; result.deliverySatisfied = delivery?.expectationMatched == true && delivery.requestedBehaviorSatisfied;
            result.finishedTick = Find.TickManager.TicksGame; result.timedOut |= delivery?.timedOut == true;
            result.nativeDispatchProven = NativeDispatch();
            var queryCalls = result.events.Where(e => e.call?.caller == "fixture-in-flight-query").Select(e => e.call).ToList();
            var warnings = queryCalls.Where(e => e.kind == "warning").ToList();
            result.warningsAttributed = warnings.All(e => KnownQueryWarning(e, queryCalls));
            bool enough = result.thresholdBuildsNeeded > 0 && result.successfulCandidateQueries >= result.thresholdBuildsNeeded && result.successfulBuilds >= result.thresholdBuildsNeeded;
            result.counterPathExercised = result.approachWindowExercised && enough && result.unexercisedReason == null;
            bool noEffects = result.actualNotes == 0 && warnings.Count == 0 && result.queries.All(q => SameCounter(q.before.counter, q.after.counter) && !q.after.counter.backedOff);
            bool baseline = result.postThresholdRejected && warnings.Count == 1 && result.warningsAttributed && result.actualNotes == result.thresholdBuildsNeeded;
            // Disposing only test hooks precedes grading, so cleanup faults cannot
            // leave an apparently successful exported result.
            Dispose();
            result.fixtureValid = !fault && !result.timedOut && result.probeStateUnchanged && result.counterTransitionsValid
                && (delivery == null || delivery.fixtureValid);
            bool complete = result.fixtureValid && result.nativeDispatchProven && result.firstCellAdvanceSequence > 0
                && result.realApproachProgress && result.withinBurstProgress && result.counterPathExercised && result.deliverySatisfied;
            result.requestedBehaviorSatisfied = complete && noEffects;
            result.baselineGapObserved = complete && baseline;
            result.expectationMatched = result.expectedBehavior == "baseline-gap" ? result.baselineGapObserved : result.requestedBehaviorSatisfied;
            result.status = fault || (!result.fixtureValid && !result.timedOut) ? "failed" : result.timedOut || !result.counterPathExercised ? "inconclusive"
                : result.expectationMatched ? (result.baselineGapObserved ? "behavior-gap-observed" : "passed") : "failed";
            if (!result.counterPathExercised && result.unexercisedReason == null) result.unexercisedReason = "Insufficient observed successful native candidate/build coverage.";
            Check("D2-native-dispatch", "execution", result.nativeDispatchProven, "Actual native Work/Has/candidate/StartJob and current exact bulk driver.");
            Check("D2-pinned-first-source-window", "execution", result.approachWindowExercised && result.firstCellAdvanceSequence > 0 && result.realApproachProgress,
                "Real first-source movement, floor10/held0, exact pawn/current-job reservation, targetA/queue0/targetB/path destination identity.");
            Check("D2-within-burst-progress", "execution", result.withinBurstProgress,
                "Actual cell transition or same-path cost decrease from a prior probe's after snapshot to a later probe's before snapshot, both within the burst.");
            Check("D2-query-isolation", "execution", result.probeStateUnchanged, "Synchronous native queries preserve current job, queue, pather, cargo, existing claims and reservations; no dispatch.");
            Check("D2-counter-path-coverage", "execution", result.counterPathExercised && result.counterTransitionsValid, "Actual initial tuple, gap/shrink reset and real Note replay; at most8 one-per-tick probes.");
            Check("D2-query-warning-attribution", "execution", result.warningsAttributed, "Every query warning has the exact reviewed recurrence suffix and a matching actual enclosing Note transition.");
            Check("D2-healthy-followthrough", "execution", result.deliverySatisfied, "Embedded actual ten-unit delivery with600 elapsed stable ticks; distinct from unperturbed D1.");
            Check("D2-no-false-query-recurrence", "behavior", result.requestedBehaviorSatisfied, "No progressing in-flight query is counted as a completed loop or suppressed.");
            HarnessSession.Event("l04-b1-d2-result", Json.Stringify(result)); return result;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { observers?.Dispose(); } catch (Exception error) { ObserverFault(error); } finally { observers = null; }
            try { healthy?.Dispose(); } catch (Exception error) { ObserverFault(error); }
        }
    }
}
