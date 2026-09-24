using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    // Construct once after the disposable map is ready; Tick exactly once from
    // GameComponentTick, never Update. No fixture current-job assignment exists.
    internal sealed class L04B1Scenario : IDisposable
    {
        internal readonly L04B1Bridge Api;
        private readonly Map map;
        private readonly L04B1Result result;
        private readonly List<Scene> scenes = new List<Scene>();
        private readonly List<Job> borrowedPoolJobs = new List<Job>();
        private readonly WorkGiver_Scanner scanner;
        private L04B1Observers observers;
        private B1HealthyScenario healthy;
        private bool disposed, fault, executionOkay = true, behaviorOkay = true;
        private int sequence, lastTick = -1, phase, queryOrdinal, intervalStart = -1, intervalFirstSequence, phaseWaitingSince;
        private Job continuingIdle, lastCandidate;
        private B1Physical intervalBoundary;
        private Scene activeScene;
        private string fixtureCaller;
        private int? activeQueryOrdinal;
        internal bool Recycling { get; private set; }
        internal bool Borrowing { get; private set; }
        internal L04B1Scenario(Map map, string expectation)
        {
            this.map = map;
            result = new L04B1Result { expectedBehavior = expectation, startedTick = Find.TickManager.TicksGame,
                counterContract = L04B1Bridge.CounterContract };
            result.incompleteControls.Add("D2 in-flight query burst is not implemented in this first staged fixture; it remains unexercised, not passed.");
            try
            {
                Require("expectation", expectation == "baseline-gap" || expectation == "satisfied", expectation);
                Require("main-map", map != null && map.IsPlayerHome && UnityData.IsInMainThread, "Initialized disposable main-thread map.");
                Api = new L04B1Bridge(); result.bindings = Api.Bindings;
                scanner = DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").Worker as WorkGiver_Scanner;
                Require("actual-workgiver", scanner != null && scanner.GetType() == typeof(WorkGiver_HaulGeneral)
                    && scanner.def.defName == "HaulGeneral", "Actual def-owned automatic scanner.");
                Setup(); observers = new L04B1Observers(this);
                RunSynchronous(scenes[0], "Q1"); RunSynchronous(scenes[1], "Q2"); RunSynchronous(scenes[3], "Q4");
                phase = 0; activeScene = scenes[2]; phaseWaitingSince = Find.TickManager.TicksGame;
                Emit(new B1Event { kind = "await-natural-idle", scene = "Q3", detail = "No injected holding job; timeout2500 real ticks per phase." });
            }
            catch { Dispose(); throw; }
        }
        internal bool IsActor(Pawn p) => p != null && (healthy != null ? p.Spawned && p.Map == map && p.Faction == Faction.OfPlayer : scenes.Any(s => s.actors.Contains(p)));
        internal Pawn ActorFor(Pawn_JobTracker jobs) => map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => ReferenceEquals(p.jobs, jobs) && IsActor(p));
        internal bool IsSource(Thing t) => t != null && (healthy != null ? t.def == ThingDefOf.Steel : scenes.Any(s => s.originalIds.Contains(t.thingIDNumber)));
        private Scene For(Pawn p) => scenes.FirstOrDefault(s => s.actors.Contains(p));
        internal void Emit(B1Event e)
        {
            e.sequence = ++sequence; e.tick = Find.TickManager.TicksGame;
            if (e.scene == null) e.scene = healthy != null ? "D1" : activeScene?.id ?? "setup";
            if (e.caller == null) e.caller = fixtureCaller ?? "native-or-external";
            if (!e.queryOrdinal.HasValue) e.queryOrdinal = activeQueryOrdinal;
            result.events.Add(e); HarnessSession.Event("l04-b1-" + e.kind, Json.Stringify(e));
        }
        internal void ObserverFault(Exception error) { fault = true; Emit(new B1Event { kind = "observer-fault", detail = error.ToString() }); }
        internal void Warning(string text, int noteSourceId, L04B1Observers.Token sourceCall, int parentCallId)
        {
            int sourceId = noteSourceId >= 0 ? noteSourceId : sourceCall?.source?.thingIDNumber ?? -1;
            var scene = For(sourceCall?.pawn) ?? scenes.FirstOrDefault(s => s.originalIds.Contains(sourceId));
            Emit(new B1Event { kind = "warning", scene = scene?.id, actorId = sourceCall?.pawn?.thingIDNumber ?? -1,
                sourceId = sourceId, parentCallId = parentCallId, detail = text,
                warningAttribution = noteSourceId >= 0 ? "legacy-note" : sourceCall?.source != null ? "enclosing-source-call" : "unattributed" });
        }
        internal void ObservedCall(L04B1Observers.Token t, string boundary, Job job, bool? returned)
        {
            var s = For(t.pawn);
            var e = new B1Event { kind = t.kind + "-" + boundary, scene = s?.id ?? "D1", callId = t.id, parentCallId = t.parent,
                actorId = t.pawn.thingIDNumber, sourceId = t.source?.thingIDNumber ?? -1, forced = t.forced, forceSweep = t.forceSweep,
                returned = returned, inputJob = Api.Job(t.input), job = Api.Job(job), actualCurrent = job != null && ReferenceEquals(t.pawn.CurJob, job)
                    && ReferenceEquals(t.pawn.jobs.curDriver?.job, job) };
            if (t.source != null && IsSource(t.source)) { e.counter = Api.Counter(t.source); e.cache = Api.Cache(t.pawn, t.source); }
            Emit(e);
            if (fixtureCaller != null && t.kind == "candidate" && boundary == "return") lastCandidate = job;
        }
        internal void StartEvent(L04B1Observers.Token t, bool after) => Emit(new B1Event
        {
            kind = after ? "start-return" : "start-attempt", scene = For(t.pawn)?.id ?? "D1", actorId = t.pawn.thingIDNumber,
            callId = t.id, inputJob = Api.Job(t.input), job = Api.Job(t.pawn.CurJob, t.pawn.jobs.curDriver),
            actualCurrent = after && ReferenceEquals(t.pawn.CurJob, t.input) && ReferenceEquals(t.pawn.jobs.curDriver?.job, t.input)
        });
        private void Setup()
        {
            var settings = L04B1Bridge.TypeNamed("HaulersDream.HaulersDreamMod").GetProperty("Settings", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            foreach (var name in new[] { "masterEnabled", "haulToStack", "bulkHaul", "markForUnload" }) Set(settings, name, true);
            Set(settings, "pickupDelayOnHauling", false); Set(settings, "carryLimitFraction", 1f); Set(settings, "carryMassCapKg", 0f);
            Require("no-steel-rule", !(bool)AccessTools.Method(settings.GetType(), "TryGetItemRule").Invoke(settings, new object[] { ThingDefOf.Steel, null }), "No fixture steel keep rule.");
            Require("storage-seam", (bool)AccessTools.Method(L04B1Bridge.TypeNamed("HaulersDream.StorageCommitments"), "GatesVanillaStorage").Invoke(null, new object[] { map }), "Actual storage startup seam enabled.");
            Require("steel-limit", ThingDefOf.Steel.stackLimit == 75, "Actual steel stack limit75.");
            foreach (var p in map.listerThings.AllThings.Where(t => t is Pawn || t is Skyfaller || t is ActiveTransporter).ToList()) if (p.Spawned) p.DeSpawn();
            foreach (var steel in map.listerThings.ThingsOfDef(ThingDefOf.Steel).ToList()) if (steel.Spawned) steel.Destroy();
            for (int i = 0; i < 5; i++)
            {
                string id = new[] { "Q1", "Q2", "Q3", "Q4", "M0" }[i];
                var center = map.Center + new IntVec3(i % 2 == 0 ? -22 : 22, 0, (i / 2 - 1) * 16);
                var s = new Scene { id = id, rect = new CellRect(center.x - 20, center.z - 6, 41, 13), center = center, highCell = center + new IntVec3(14, 0, 0) };
                Require(id + "-bounds", s.rect.Cells.All(c => c.InBounds(map)), "Five isolated supported rooms require at least85x45 centered clear map footprint.");
                Require(id + "-no-zones", s.rect.Cells.All(c => map.zoneManager.ZoneAt(c) == null), "No unrelated zones overwritten.");
                GenDebug.ClearArea(s.rect, map);
                foreach (var c in s.rect.Cells) { map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null); map.fogGrid.Unfog(c); map.areaManager.Home[c] = true; }
                foreach (var c in s.rect.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), c, map);
                foreach (var c in s.rect.ContractedBy(1).Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                s.high = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager); map.zoneManager.RegisterZone(s.high); s.high.AddCell(s.highCell);
                s.high.settings.filter.SetDisallowAll(); s.high.settings.filter.SetAllow(ThingDefOf.Steel, true); s.high.settings.Priority = StoragePriority.Critical;
                s.anchor = Steel(center + new IntVec3(-14, 0, -1)); s.extra = Steel(center + new IntVec3(-14, 0, 1));
                Require(id + "-actual-destination-capacity", s.highCell.GetItemStackSpaceLeftFor(map, ThingDefOf.Steel) == 75
                    && s.high.GetSlotGroup().CellsList.Count == 1 && s.high.settings.AllowedToAccept(s.anchor), "Actual native empty Critical cell:75 steel units available.");
                s.originalIds.Add(s.anchor.thingIDNumber); s.originalIds.Add(s.extra.thingIDNumber);
                s.originalCell = s.anchor.Position; s.alternateCell = center + new IntVec3(-13, 0, -1);
                for (int p = 0; p < (id == "Q2" ? 6 : 1); p++)
                {
                    Pawn actor = null; Rand.PushState(104100 + 100 * i + p);
                    try
                    {
                        for (int n = 0; n < 32; n++)
                        {
                            var made = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                            if (!made.Downed && !made.Dead && !made.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) && made.RaceProps.Humanlike
                                && made.carryTracker != null && made.inventory != null && made.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                                && made.carryTracker.MaxStackSpaceEver(ThingDefOf.Steel) >= 10) { actor = made; break; }
                        }
                    }
                    finally { Rand.PopState(); }
                    Require(id + "-actor-" + p, actor != null, "Naturally generated capable human with ten-unit hand capacity.");
                    actor.inventory.innerContainer.ClearAndDestroyContents(); actor.workSettings.EnableAndInitializeIfNotAlreadyInitialized(); actor.workSettings.DisableAll();
                    actor.timetable.times = Enumerable.Repeat(TimeAssignmentDefOf.Work, 24).ToList();
                    if (actor.needs.food != null) actor.needs.food.CurLevelPercentage = 1;
                    if (actor.needs.rest != null) actor.needs.rest.CurLevelPercentage = 1;
                    if (actor.needs.joy != null) actor.needs.joy.CurLevelPercentage = 1;
                    GenSpawn.Spawn(actor, center + new IntVec3(-17, 0, p - 3), map);
                    var comp = actor.AllComps.SingleOrDefault(c => c.GetType().FullName == "HaulersDream.CompHauledToInventory");
                    Require(id + "-comp-" + p, comp != null && actor.CurJob == null && !actor.Drafted && actor.mindState.duty == null, "Actual HD comp, no duty or assigned current job."); Set(comp, "autoHaulYields", true);
                    s.actors.Add(actor);
                }
                scenes.Add(s); s.initial = Physical(s);
                Require(id + "-initial-cargo", s.initial.source == 10 && s.initial.total == 10 && s.initial.high == 0 && s.initial.inventory == 0 && s.initial.hands == 0
                    && s.initial.claims.Count == 0 && s.initial.reservations.Count == 0, "Two original5-unit floor stacks; empty available Critical destination; no claims or reservations.");
                var counter = Api.Counter(s.anchor); Require(id + "-fresh-counter", !counter.anchorPresent && !counter.backoffPresent && !counter.warned && !counter.failPresent, "Fresh identity; no counter reset performed.");
                result.layouts.Add(new B1Layout { scene = id, rectangle = s.rect.ToString(), highCell = s.highCell.ToString(),
                    sourceCells = new[] { s.originalCell, s.extra.Position, s.alternateCell }.Select(c => c.ToString()).ToList(),
                    sources = s.originalIds.ToList(), actors = s.actors.Select(a => a.thingIDNumber).ToList(), initial = s.initial });
                Emit(new B1Event { kind = "scene-ready", scene = id, physical = s.initial, counter = counter });
            }
        }
        private static void Set(object target, string name, object value)
        {
            var field = AccessTools.Field(target.GetType(), name);
            if (field == null || field.FieldType != value.GetType()) throw new MissingFieldException(name); field.SetValue(target, value);
        }
        private Thing Steel(IntVec3 cell)
        {
            var t = ThingMaker.MakeThing(ThingDefOf.Steel); t.stackCount = 5; var made = GenSpawn.Spawn(t, cell, map);
            Require("spawn-" + t.ThingID, ReferenceEquals(t, made) && t.Spawned && ReferenceEquals(t.ParentHolder, map) && t.Position == cell && t.stackCount == 5, "Actual five-unit floor identity."); return t;
        }
        private B1Thing Thing(Thing t, Scene scene)
        {
            var owner = scene.actors.FirstOrDefault(p => ReferenceEquals(t.holdingOwner, p.inventory.innerContainer) || ReferenceEquals(t, p.carryTracker.CarriedThing));
            return new B1Thing { id = t.thingIDNumber, count = t.stackCount, limit = t.def.stackLimit, spawned = t.Spawned, destroyed = t.Destroyed,
                cell = t.Spawned ? t.Position.ToString() : null, onMap = t.MapHeld == map, mapHolder = ReferenceEquals(t.ParentHolder, map), holder = t.ParentHolder?.GetType().FullName,
                ownerPawn = owner?.thingIDNumber, inventory = owner != null && ReferenceEquals(t.holdingOwner, owner.inventory.innerContainer), hands = owner != null && ReferenceEquals(t, owner.carryTracker.CarriedThing) };
        }
        private B1Physical Physical(Scene s)
        {
            var floor = map.listerThings.ThingsOfDef(ThingDefOf.Steel).Where(t => t.Spawned && s.rect.Contains(t.Position)).ToList();
            var held = s.actors.SelectMany(p => p.inventory.innerContainer).Where(t => t.def == ThingDefOf.Steel).ToList();
            var hands = s.actors.Select(p => p.carryTracker.CarriedThing).Where(t => t?.def == ThingDefOf.Steel).ToList();
            var r = new B1Physical { tick = Find.TickManager.TicksGame, mapId = map.uniqueID,
                source = floor.Where(t => s.originalIds.Contains(t.thingIDNumber)).Sum(t => t.stackCount), high = floor.Where(t => t.Position == s.highCell).Sum(t => t.stackCount),
                inventory = held.Sum(t => t.stackCount), hands = hands.Sum(t => t.stackCount), total = floor.Sum(t => t.stackCount) + held.Sum(t => t.stackCount) + hands.Sum(t => t.stackCount),
                things = floor.Concat(held).Concat(hands).Distinct().OrderBy(t => t.thingIDNumber).Select(t => Thing(t, s)).ToList(), claims = Api.Claims(s.actors, s.high.GetSlotGroup()) };
            r.elsewhere = r.total - r.source - r.high - r.inventory - r.hands;
            foreach (var p in s.actors)
                r.actors.Add(new B1Actor { id = p.thingIDNumber, cell = p.Position.ToString(), faction = p.Faction?.GetUniqueLoadID(), spawned = p.Spawned,
                    healthy = !p.Dead && !p.Downed, drafted = p.Drafted, current = Api.Job(p.CurJob, p.jobs.curDriver), driver = p.jobs.curDriver?.GetType().FullName,
                    handSpace = p.carryTracker.MaxStackSpaceEver(ThingDefOf.Steel), carryMass = MassUtility.Capacity(p), gearInventoryMass = MassUtility.GearAndInventoryMass(p),
                    haulingCapable = !p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling), haulingActive = p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling),
                    inventory = p.inventory.innerContainer.Sum(t => t.stackCount), hands = p.carryTracker.CarriedThing?.stackCount ?? 0,
                    queued = p.jobs.jobQueue.Select(q => Api.Job(q.job)).ToList() });
            foreach (var x in map.reservationManager.ReservationsReadOnly.Where(x => s.actors.Contains(x.Claimant) || (x.Target.HasThing && s.originalIds.Contains(x.Target.Thing.thingIDNumber))))
                r.reservations.Add(new B1Reservation { pawnId = x.Claimant?.thingIDNumber ?? -1, jobId = x.Job?.loadID ?? -1, jobToken = Api.Token(x.Job),
                    thingId = x.Target.Thing?.thingIDNumber ?? -1, cell = x.Target.Cell.ToString(), count = x.StackCount, layer = x.Layer?.defName });
            foreach (var x in L04B1Bridge.PhysicalReservations(map).Where(x => s.actors.Contains(x.claimant) || (x.target.HasThing && s.originalIds.Contains(x.target.Thing.thingIDNumber))))
                r.reservations.Add(new B1Reservation { pawnId = x.claimant?.thingIDNumber ?? -1, jobId = x.job?.loadID ?? -1, jobToken = Api.Token(x.job),
                    thingId = x.target.Thing?.thingIDNumber ?? -1, cell = x.target.Cell.ToString(), count = -1, layer = "physical-interaction" });
            return r;
        }
        private void RunSynchronous(Scene scene, string kind)
        {
            activeScene = scene; intervalFirstSequence = sequence; int tick = Find.TickManager.TicksGame;
            for (int i = 0; i < 6; i++)
            {
                var pawn = scene.actors[kind == "Q2" ? i : 0]; Query(scene, pawn, i + 1);
                if (kind == "Q4" && i < 5)
                {
                    RecycleCandidate(lastCandidate, i);
                }
            }
            if (kind != "Q1") Query(scene, scene.actors[0], 7);
            Require(kind + "-single-tick", Find.TickManager.TicksGame == tick, "Synchronous actual scanner calls.");
            Grade(scene, kind, intervalFirstSequence);
            if (kind == "Q4") ReleaseBorrowedJobs();
            activeScene = null;
        }
        private void RecycleCandidate(Job candidate, int index)
        {
            Api.AssertReturnable(map, candidate);
            var before = Api.Job(candidate); int freeBefore = SimplePool<Job>.FreeItemsCount;
            Recycling = true;
            try { JobMaker.ReturnToPool(candidate); }
            finally { Recycling = false; }
            int freeAfter = SimplePool<Job>.FreeItemsCount;
            Emit(new B1Event { kind = "fixture-pool-return", scene = "Q4", caller = "fixture-pool-recycle",
                inputJob = before, job = Api.Job(candidate), poolOperation = new B1PoolOperation
                { cycle = index + 1, action = "return-candidate", beforeCount = freeBefore, afterCount = freeAfter,
                    targetToken = before.token, priorLoadId = before.loadId } });
            Require("Q4-real-pool-clear-" + index, candidate.loadID == -1 && candidate.def == null
                && freeBefore >= 0 && freeBefore < 1000 && freeAfter == freeBefore + 1,
                "Native candidate Clear and FIFO enqueue; original token=" + before.token + "; pool=" + freeBefore + "->" + freeAfter);
            // Native SimplePool is FIFO. Keep every borrowed object out of the
            // pool until the final Q4 query; never assume next allocation is LIFO.
            bool reused = false, valid = true;
            for (int attempt = 0; attempt < freeAfter; attempt++)
            {
                int countBefore = SimplePool<Job>.FreeItemsCount;
                Job made; Borrowing = true; fixtureCaller = "fixture-pool-recycle";
                try { made = JobMaker.MakeJob(); }
                finally { Borrowing = false; fixtureCaller = null; }
                int countAfter = SimplePool<Job>.FreeItemsCount;
                bool duplicate = borrowedPoolJobs.Any(x => ReferenceEquals(x, made));
                if (made != null && !duplicate) borrowedPoolJobs.Add(made);
                reused = ReferenceEquals(made, candidate);
                var actual = Api.Job(made);
                valid &= actual != null && actual.loadId > 0 && actual.def == null && !duplicate
                    && countBefore > 0 && countAfter == countBefore - 1
                    && (!reused || actual.token == before.token && actual.loadId != before.loadId);
                Emit(new B1Event { kind = "fixture-pool-borrow", scene = "Q4", caller = "fixture-pool-recycle", job = actual,
                    poolOperation = new B1PoolOperation { cycle = index + 1, action = "borrow-unassigned", beforeCount = countBefore,
                        afterCount = countAfter, targetToken = before.token, priorLoadId = before.loadId, targetReused = reused } });
                if (!valid || reused) break;
            }
            Require("Q4-real-pool-reuse-" + index, valid && reused,
                "At most the observed FIFO count (<=1000) actual MakeJob allocations must reach the original object with a new native loadID; all borrowed jobs remain unassigned and held.");
        }
        private void ReleaseBorrowedJobs()
        {
            while (borrowedPoolJobs.Count > 0)
            {
                // Consume fixture ownership before any throwing check/return/event.
                // A later disposal must never enqueue an already-returned object twice.
                var borrowed = borrowedPoolJobs[0];
                borrowedPoolJobs.RemoveAt(0);
                if (borrowed.def != null || map.mapPawns.AllPawnsSpawned.Any(p => ReferenceEquals(p.CurJob, borrowed)
                    || p.jobs?.jobQueue?.Contains(borrowed) == true)
                    || map.reservationManager.ReservationsReadOnly.Any(r => ReferenceEquals(r.Job, borrowed))
                    || L04B1Bridge.PhysicalReservations(map).Any(r => ReferenceEquals(r.job, borrowed)))
                    throw new InvalidOperationException("Fixture-borrowed unassigned job became current, queued, configured or reserved; refuse to recycle it.");
                var before = Api.Job(borrowed); int freeBefore = SimplePool<Job>.FreeItemsCount;
                JobMaker.ReturnToPool(borrowed);
                Emit(new B1Event { kind = "fixture-pool-release", scene = "Q4", caller = "fixture-pool-cleanup",
                    inputJob = before, job = Api.Job(borrowed), poolOperation = new B1PoolOperation
                    { action = "release-unassigned", beforeCount = freeBefore, afterCount = SimplePool<Job>.FreeItemsCount } });
            }
        }
        private void Query(Scene s, Pawn pawn, int ordinal)
        {
            lastCandidate = null; var before = Physical(s); var counter = Api.Counter(s.anchor);
            bool continuing = intervalStart >= 0 && (s.id == "Q3" || s.id == "M0");
            if (continuing) Boundary(s, intervalBoundary, before, "query-entry", ordinal, false);
            Emit(new B1Event { kind = "query-before", scene = s.id, actorId = pawn.thingIDNumber, sourceId = s.anchor.thingIDNumber, callId = ordinal,
                queryOrdinal = ordinal, physical = before, counter = counter, cache = Api.Cache(pawn, s.anchor), forced = false, forceSweep = false, caller = "fixture-query" });
            bool answer; fixtureCaller = "fixture-query"; activeQueryOrdinal = ordinal;
            try { answer = scanner.HasJobOnThing(pawn, s.anchor, false); }
            finally { fixtureCaller = null; activeQueryOrdinal = null; }
            var after = Physical(s);
            Emit(new B1Event { kind = "query-after", scene = s.id, actorId = pawn.thingIDNumber, sourceId = s.anchor.thingIDNumber, callId = ordinal,
                queryOrdinal = ordinal, physical = after, counter = Api.Counter(s.anchor), cache = Api.Cache(pawn, s.anchor), job = Api.Job(lastCandidate), returned = answer, forced = false, forceSweep = false, caller = "fixture-query" });
            Assert(s.id + "-query-physical-" + ordinal, "execution", SameCargo(before, after) && after.total == 10 && after.source == 10 && after.high == 0
                && after.inventory == 0 && after.hands == 0 && after.claims.Count == 0 && after.reservations.Count == 0, "Query did not transfer, reserve, or claim fixture cargo.");
            Assert(s.id + "-query-actors-" + ordinal, "execution", Json.Stringify(before.actors) == Json.Stringify(after.actors)
                && after.actors.All(a => a.spawned && a.healthy && !a.drafted && a.inventory == 0 && a.hands == 0
                    && a.haulingCapable && !a.haulingActive && a.handSpace >= 10
                    && !Transport(a.current) && a.queued.All(j => !Transport(j))), "No query changes actor position/current job/queue; no current or queued fixture transport.");
            if (continuing) intervalBoundary = after;
        }
        private static bool Transport(B1Job job) => job != null && (job.def == "HaulersDream_BulkHaul"
            || job.def == B1HealthyScenario.UnloadDef || job.def == "HaulToCell" || job.def == "HaulToContainer");
        private static bool SameCargo(B1Physical a, B1Physical b) => Json.Stringify(a.things) == Json.Stringify(b.things)
            && Json.Stringify(a.reservations) == Json.Stringify(b.reservations) && Json.Stringify(a.claims) == Json.Stringify(b.claims);
        private static bool SameCounts(B1Physical a, B1Physical b) => a.mapId == b.mapId && a.source == b.source && a.high == b.high
            && a.elsewhere == b.elsewhere && a.inventory == b.inventory && a.hands == b.hands && a.total == b.total;
        private static bool SameIdleJob(B1Job a, B1Job b) => a != null && b != null && a.token == b.token && a.loadId == b.loadId
            && a.startTick == b.startTick && a.targetA == b.targetA && a.def == b.def && a.driver == b.driver && a.forced == b.forced
            && a.workgiver == b.workgiver && a.workgiverClass == b.workgiverClass
            && Json.Stringify(a.queueIds) == Json.Stringify(b.queueIds) && Json.Stringify(a.counts) == Json.Stringify(b.counts);
        private static bool SameIdleActor(B1Actor a, B1Actor b) => a.id == b.id && a.cell == b.cell && a.faction == b.faction
            && a.driver == b.driver && a.spawned == b.spawned && a.healthy == b.healthy && a.drafted == b.drafted
            && a.haulingCapable == b.haulingCapable && a.haulingActive == b.haulingActive && a.inventory == b.inventory && a.hands == b.hands
            && a.handSpace == b.handSpace && a.carryMass == b.carryMass && a.gearInventoryMass == b.gearInventoryMass
            && SameIdleJob(a.current, b.current) && Json.Stringify(a.queued) == Json.Stringify(b.queued);
        private static bool SameAcrossTicks(B1Physical a, B1Physical b) => a != null && b != null && SameCounts(a, b) && SameCargo(a, b)
            && a.actors.Count == b.actors.Count && a.actors.Zip(b.actors, SameIdleActor).All(x => x);
        private void Boundary(Scene s, B1Physical before, B1Physical after, string stage, int ordinal, bool mayAdvanceTick)
        {
            bool okay = SameAcrossTicks(before, after) && after.tick == before.tick + (mayAdvanceTick ? 1 : 0)
                && (mayAdvanceTick || Json.Stringify(before.actors) == Json.Stringify(after.actors));
            Emit(new B1Event { kind = "boundary-continuity", scene = s.id, callId = ordinal, method = stage,
                beforePhysical = before, physical = after, returned = okay,
                detail = "Exact cargo/custody/cells/cohort/queue/claim continuity. Only real tick advancement and the continuing native Wait job's observed expiry metadata may vary across ticks; no expiry or AI state is written." });
            Require(s.id + "-" + stage + "-continuity-" + ordinal, okay, "Every boundary joins the previous captured boundary; same source and actor positions throughout the measured interval.");
        }
        private static bool SameThingExceptCell(B1Thing a, B1Thing b) => a.id == b.id && a.count == b.count && a.limit == b.limit
            && a.holder == b.holder && a.ownerPawn == b.ownerPawn && a.spawned == b.spawned && a.destroyed == b.destroyed
            && a.onMap == b.onMap && a.mapHolder == b.mapHolder && a.inventory == b.inventory && a.hands == b.hands;
        private bool OnlyAnchorRelocated(Scene s, B1Physical before, B1Physical after, IntVec3 from, IntVec3 to)
        {
            if (before.tick != after.tick || !SameCounts(before, after) || Json.Stringify(before.actors) != Json.Stringify(after.actors)
                || Json.Stringify(before.reservations) != Json.Stringify(after.reservations) || Json.Stringify(before.claims) != Json.Stringify(after.claims)
                || before.things.Count != after.things.Count || before.things.Select(t => t.id).Distinct().Count() != before.things.Count
                || after.things.Select(t => t.id).Distinct().Count() != after.things.Count) return false;
            var oldAnchor = before.things.SingleOrDefault(t => t.id == s.originalIds[0]);
            var newAnchor = after.things.SingleOrDefault(t => t.id == s.originalIds[0]);
            if (oldAnchor == null || newAnchor == null || from == to || (from != s.originalCell && from != s.alternateCell)
                || (to != s.originalCell && to != s.alternateCell) || oldAnchor.cell != from.ToString() || newAnchor.cell != to.ToString()
                || oldAnchor.count != 5 || !oldAnchor.spawned || oldAnchor.destroyed || !oldAnchor.onMap || !oldAnchor.mapHolder
                || oldAnchor.inventory || oldAnchor.hands || oldAnchor.ownerPawn.HasValue || !SameThingExceptCell(oldAnchor, newAnchor)) return false;
            return before.things.Where(t => t.id != oldAnchor.id).All(t =>
                Json.Stringify(t) == Json.Stringify(after.things.SingleOrDefault(x => x.id == t.id)));
        }
        private bool QueueExact(B1Job job, Scene s) => job != null && job.def == "HaulersDream_BulkHaul" && !job.forced
            && job.targetA == s.anchor.thingIDNumber && job.queueIds.Count == 2 && job.queueIds.Distinct().Count() == 2
            && job.queueIds.All(s.originalIds.Contains) && job.counts.Count == 2 && job.counts.All(n => n == 5);
        private void Grade(Scene s, string kind, int afterSequence)
        {
            var events = result.events.Where(e => e.sequence > afterSequence && e.scene == s.id).ToList();
            var answers = events.Where(e => e.kind == "query-after").ToList(); var six = answers.Take(6).ToList();
            var builds = events.Where(e => e.kind == "build-return").ToList();
            int wantedBuilds = kind == "Q1" ? 1 : 6;
            int wantedCalls = kind == "Q1" ? 6 : 7;
            Assert(kind + "-actual-entry-chain", "execution", events.Count(e => e.kind == "has-enter") == wantedCalls
                && events.Count(e => e.kind == "has-return") == wantedCalls && events.Count(e => e.kind == "candidate-enter") == wantedCalls
                && events.Count(e => e.kind == "candidate-return") == wantedCalls && events.Count(e => e.kind == "build-enter") == builds.Count
                && events.Where(e => e.kind == "build-enter").All(e => e.forced == false && e.forceSweep == false && e.parentCallId > 0),
                "Exact patched Has to JobOnThing to actual builder chain; no direct builder call from fixture.");
            Assert(kind + "-six-real-candidates", "execution", six.Count == 6 && six.All(e => e.returned == true && QueueExact(e.job, s)), "First six actual Has answers and captured candidates must be exact5+5.");
            Assert(kind + "-zero-starts", "execution", !events.Any(e => e.kind == "start-attempt"), "Strict cohort interval: no StartJob invocation, including idle transitions.");
            Assert(kind + "-no-failure-state", "execution", answers.All(e => !e.counter.failPresent), "No failed-job tally is an expected query effect.");
            Assert(kind + "-build-count", "execution", builds.Count == wantedBuilds, "Expected real builder entries/returns=" + wantedBuilds + "; actual=" + builds.Count);
            if (kind == "Q1") Assert("Q1-live-cache-reuse", "execution", six.Select(e => e.job?.token).Distinct().Count() == 1 && six.Select(e => e.job?.loadId).Distinct().Count() == 1
                && six.All(e => e.cache.entryPresent && e.cache.pinnedLoadId == e.job.loadId && e.cache.actualJob?.token == e.job.token && e.cache.generation == e.tick), "One miss, five validated live cache returns.");
            if (kind == "Q2") Assert("Q2-distinct-pawns", "execution", six.Select(e => e.actorId).Distinct().Count() == 6 && six.Select(e => e.tick).Distinct().Count() == 1, "Six real actors in one tick.");
            if (kind == "Q3" || kind == "M0") Assert(kind + "-adjacent-ticks", "execution", six.Count == 6 && six.Select((e, i) => e.tick == six[0].tick + i).All(x => x), "Six adjacent GameComponentTick calls.");
            if (kind == "Q3" || kind == "M0")
            {
                var boundaries = events.Where(e => e.kind == "boundary-continuity").ToList();
                Assert(kind + "-full-boundary-chain", "execution", boundaries.Count == 13
                    && boundaries.All(e => e.returned == true && e.beforePhysical != null && e.physical != null)
                    && boundaries.Where(e => e.method == "tick-entry").Select(e => e.callId).SequenceEqual(Enumerable.Range(1, 6))
                    && boundaries.Where(e => e.method == "query-entry").Select(e => e.callId).SequenceEqual(Enumerable.Range(1, 7)),
                    "Six tick entries and seven immediate query entries must each join their predecessor without physical/cohort changes.");
                var moves = events.Where(e => e.kind == "fixture-relocation").ToList();
                Assert(kind + "-declared-relocations-only", "execution", kind == "Q3" ? moves.Count == 0 : moves.Count == 6
                    && moves.Select(e => e.callId).SequenceEqual(Enumerable.Range(1, 6))
                    && moves.All(e => e.returned == true && e.beforePhysical != null && e.physical != null),
                    "Q3 has no relocation; M0 has exactly six individually checked anchor-only relocations between continuity boundaries.");
            }
            if (kind == "Q4")
            {
                var pools = events.Where(e => e.kind == "pool-return").ToList();
                var reuses = events.Where(e => e.kind == "fixture-pool-borrow" && e.poolOperation?.targetReused == true).ToList();
                var queryEntries = events.Where(e => e.kind == "query-before").ToList();
                Assert("Q4-five-real-recycles", "execution", pools.Count == 5 && pools.All(e => e.inputJob != null && e.job.token == e.inputJob.token && e.job.loadId == -1 && e.job.def == null)
                    && reuses.Count == 5 && queryEntries.Count == 7 && pools.Select((p, i) => reuses[i].sequence > p.sequence
                        && reuses[i].sequence < queryEntries[i + 1].sequence && reuses[i].poolOperation.cycle == i + 1
                        && reuses[i].job.token == p.job.token && reuses[i].job.loadId > 0
                        && reuses[i].job.loadId != p.inputJob.loadId).All(x => x),
                    "Each FIFO pool return is followed by observed MakeJob reuse of the same object before the next query; borrowed jobs are held outside the pool throughout measurement.");
                Assert("Q4-recycled-cache-misses", "execution", builds.Count == 6 && six.Select(e => e.job.loadId).Distinct().Count() == 6, "Stale pinned cache entry cannot return a recycled job.");
            }
            // Do not silently discard sourceId=-1 or warnings outside the legacy
            // Note method. Unknown interval diagnostics invalidate attribution;
            // every actual query-scoped warning also prevents a clean outcome.
            var warnings = events.Where(e => e.kind == "warning").ToList();
            Assert(kind + "-warning-attribution", "execution", warnings.All(w => WarningMatchesQuery(w, events, s)),
                "Every measured warning must name its actual query ordinal, actor, source and enclosing live source call; unknown diagnostics remain unresolved, not absent.");
            bool clean = answers.All(e => !e.counter.anchorPresent && !e.counter.backoffPresent && !e.counter.warned && !e.counter.backedOff)
                && warnings.Count == 0 && !events.Any(e => e.kind == "note-enter");
            bool baseline;
            if (kind == "Q1") baseline = six.All(e => e.counter.anchorPresent && e.counter.count == 1 && !e.counter.backoffPresent && !e.counter.warned) && warnings.Count == 0;
            else baseline = six.Count == 6 && six.Take(5).Select((e, i) => e.counter.anchorPresent && e.counter.count == i + 1 && e.counter.stackCount == 5 && !e.counter.backoffPresent).All(x => x)
                && !six[5].counter.anchorPresent && six[5].counter.until == six[5].tick + 2500 && six[5].counter.warned && six[5].counter.backedOff
                && warnings.Count == 1 && answers.Count == 7 && answers[6].returned == false && answers[6].job == null;
            Assert(kind + "-no-query-recurrence", "behavior", clean, "Actual observed query-only recurrence state, warnings and backoff must remain absent on corrected code.");
            Assert(kind + "-expected-transition", "expectation", result.expectedBehavior == "baseline-gap" ? baseline : clean && answers.All(e => e.returned == true), "Mode-specific observed transitions; missing evidence is not zero state.");
        }
        private static bool WarningMatchesQuery(B1Event warning, List<B1Event> events, Scene scene)
        {
            if (warning.caller != "fixture-query" || !warning.queryOrdinal.HasValue || warning.sourceId != scene.anchor.thingIDNumber
                || warning.parentCallId <= 0 || (warning.warningAttribution != "legacy-note" && warning.warningAttribution != "enclosing-source-call")) return false;
            var before = events.Where(e => e.kind == "query-before" && e.callId == warning.queryOrdinal.Value).ToList();
            var after = events.Where(e => e.kind == "query-after" && e.callId == warning.queryOrdinal.Value).ToList();
            if (before.Count != 1 || after.Count != 1 || before[0].actorId != warning.actorId || after[0].actorId != warning.actorId
                || warning.sequence <= before[0].sequence || warning.sequence >= after[0].sequence) return false;
            // Follow actual observer call IDs, not stack-frame text. A source
            // warning outside Note must still be inside this real Has invocation.
            int parent = warning.parentCallId; var visited = new HashSet<int>();
            while (parent > 0 && visited.Add(parent))
            {
                var entries = events.Where(e => e.callId == parent && new[] { "has-enter", "candidate-enter", "try-build-enter", "build-enter" }.Contains(e.kind)).ToList();
                if (entries.Count != 1) return false;
                var entry = entries[0]; var returns = events.Where(e => e.callId == parent && e.kind == entry.kind.Replace("-enter", "-return")).ToList();
                if (returns.Count != 1 || entry.actorId != warning.actorId || entry.sourceId != warning.sourceId
                    || entry.queryOrdinal != warning.queryOrdinal || entry.forced != false || entry.forceSweep != false
                    || entry.sequence >= warning.sequence || returns[0].sequence <= warning.sequence) return false;
                if (entry.kind == "has-enter") return entry.sequence > before[0].sequence && returns[0].sequence < after[0].sequence;
                parent = entry.parentCallId;
            }
            return false;
        }
        internal L04B1Result Tick()
        {
            if (disposed) return result.status == "running" ? null : result;
            try
            {
                int now = Find.TickManager.TicksGame; if (now == lastTick) return null;
                if (lastTick >= 0 && now != lastTick + 1) throw new InvalidOperationException("B1 must observe every real GameComponentTick."); lastTick = now;
                if (healthy != null)
                {
                    healthy.ObserveSettled("game-tick"); var done = healthy.TryFinish(); if (done == null) return null;
                    result.healthyDelivery = done; result.healthyDeliveryComplete = done.expectationMatched;
                    Assert("D1-healthy-delivery", "execution", done.expectationMatched, "Fresh automatic ten-unit healthy work control.");
                    Assert("D1-native-selection-dispatch", "execution", HealthyDispatchChain(done.firstBulkJobId),
                        "Nested native work/Has/candidate call, returned workgiver Job and actual started bulk Job must share actor/object/loadID before physical delivery.");
                    return Finish();
                }
                if (fault) throw new InvalidOperationException("Observer fault; query evidence is inconclusive.");
                var s = activeScene; var pawn = s.actors[0];
                if (intervalStart < 0)
                {
                    if (now - phaseWaitingSince > 2500) throw new InvalidOperationException(s.id + " could not establish a natural long-enough idle job.");
                    var job = pawn.CurJob;
                    if (!(pawn.jobs.curDriver is JobDriver_Wait) || job == null || job.expiryInterval <= 0 || job.startTick + job.expiryInterval < now + 12) return null;
                    continuingIdle = job; intervalStart = now; intervalFirstSequence = sequence; queryOrdinal = 0;
                    intervalBoundary = Physical(s);
                    Require(s.id + "-ready-cargo-unchanged", SameCounts(s.initial, intervalBoundary) && SameCargo(s.initial, intervalBoundary),
                        "Natural idle preparation may change the actor job, but not the original cargo, cells, claims or source reservations.");
                    Emit(new B1Event { kind = "natural-idle-ready", scene = s.id, actorId = pawn.thingIDNumber, job = Api.Job(job, pawn.jobs.curDriver), physical = intervalBoundary });
                }
                Require(s.id + "-same-native-idle-" + queryOrdinal, ReferenceEquals(pawn.CurJob, continuingIdle) && pawn.jobs.curDriver is JobDriver_Wait,
                    "The naturally selected idle job survives the measured six-tick interval.");
                var tickEntry = Physical(s);
                Boundary(s, intervalBoundary, tickEntry, "tick-entry", queryOrdinal + 1, queryOrdinal > 0);
                intervalBoundary = tickEntry;
                if (s.id == "M0")
                {
                    var before = intervalBoundary; var from = s.anchor.Position; var dest = from == s.originalCell ? s.alternateCell : s.originalCell;
                    Require("M0-clear-relocation-" + queryOrdinal, !map.thingGrid.ThingsListAt(dest).Any(t => t.def.category == ThingCategory.Item)
                        && !map.reservationManager.ReservationsReadOnly.Any(r => r.Target.Thing == s.anchor), "Environmental control moves only an unreserved original stack.");
                    s.anchor.DeSpawn(); var returned = GenSpawn.Spawn(s.anchor, dest, map);
                    var afterMove = Physical(s);
                    bool onlyMove = ReferenceEquals(returned, s.anchor) && s.anchor.stackCount == 5 && s.anchor.thingIDNumber == s.originalIds[0]
                        && OnlyAnchorRelocated(s, before, afterMove, from, dest);
                    Emit(new B1Event { kind = "fixture-relocation", scene = "M0", callId = queryOrdinal + 1, sourceId = s.anchor.thingIDNumber,
                        beforePhysical = before, physical = afterMove, returned = onlyMove,
                        detail = "Only original anchor cell " + from + " -> " + dest + "; ordinary despawn/spawn, no pawn transport or counter reset." });
                    Require("M0-real-relocation-" + queryOrdinal, onlyMove, "Only the original five-unit anchor's cell changes; all other Things, actor fields, queues, custody, claims and reservations match exactly.");
                    intervalBoundary = afterMove;
                }
                Query(s, pawn, ++queryOrdinal);
                if (queryOrdinal < 6) return null;
                Query(s, pawn, 7); Grade(s, s.id, intervalFirstSequence);
                if (phase++ == 0) { activeScene = scenes[4]; intervalStart = -1; intervalBoundary = null; continuingIdle = null; phaseWaitingSince = now; return null; }
                result.queryControlsComplete = executionOkay && !fault;
                Emit(new B1Event { kind = "fixture-phase-reset", detail = "Query intervals ended; removing only their disposable rooms, zones and actors before independent D1. This is fixture setup, not transport." });
                foreach (var old in scenes)
                {
                    Emit(new B1Event { kind = "query-scene-final", scene = old.id, physical = Physical(old) });
                    RetireZone(old);
                    foreach (var actor in old.actors) if (actor.Spawned) actor.DeSpawn();
                    foreach (var c in old.rect.Cells) map.roofGrid.SetRoof(c, null);
                    GenDebug.ClearArea(old.rect, map);
                }
                activeScene = null; healthy = new B1HealthyScenario(map, "satisfied");
                return null;
            }
            catch (Exception error) { result.error = error.ToString(); fault = true; Emit(new B1Event { kind = "fixture-exception", detail = result.error }); return Finish(); }
        }
        private void RetireZone(Scene scene)
        {
            var group = scene.high.GetSlotGroup();
            var row = new B1ZoneRetirement
            {
                zoneId = scene.high.GetUniqueLoadID(), cell = scene.highCell.ToString(),
                beforeCells = scene.high.Cells.Select(c => c.ToString()).ToList(),
                beforeGridZone = map.zoneManager.ZoneAt(scene.highCell)?.GetUniqueLoadID(),
                beforeRegistered = map.zoneManager.AllZones.Contains(scene.high),
                beforeSlotMatches = ReferenceEquals(map.haulDestinationManager.SlotGroupAt(scene.highCell), group)
            };
            Require(scene.id + "-owned-zone-before-retirement", row.beforeRegistered && row.beforeSlotMatches
                && scene.high.Cells.Count == 1 && scene.high.Cells[0] == scene.highCell
                && ReferenceEquals(map.zoneManager.ZoneAt(scene.highCell), scene.high),
                "Only the original registered one-cell fixture zone may be retired: " + Json.Stringify(row));
            // DeregisterZone alone leaves zoneGrid/cells behind. Native Delete
            // removes the owned cells and deregisters through the normal lifecycle.
            scene.high.Delete(playSound: false);
            row.afterCells = scene.high.Cells.Select(c => c.ToString()).ToList();
            row.afterGridZone = map.zoneManager.ZoneAt(scene.highCell)?.GetUniqueLoadID();
            row.afterRegistered = map.zoneManager.AllZones.Contains(scene.high);
            row.afterSlotAbsent = map.haulDestinationManager.SlotGroupAt(scene.highCell) == null;
            row.afterGroupRemoved = !map.haulDestinationManager.AllGroupsListInPriorityOrder.Contains(group);
            Emit(new B1Event { kind = "fixture-zone-retirement", scene = scene.id, zoneRetirement = row,
                detail = "Native Delete(false) after final query evidence; fixture retirement, not haul progress." });
            Require(scene.id + "-owned-zone-retired", row.afterCells.Count == 0 && row.afterGridZone == null
                && !row.afterRegistered && row.afterSlotAbsent && row.afterGroupRemoved,
                "Owned zone cells, cell lookup and haul-destination registration are all removed: " + Json.Stringify(row));
        }
        private bool HealthyDispatchChain(int bulkId)
        {
            var d = result.events.Where(e => e.scene == "D1").ToList();
            foreach (var start in d.Where(e => e.kind == "start-return" && e.actualCurrent == true
                && e.job?.loadId == bulkId && e.job.def == "HaulersDream_BulkHaul"
                && e.job.driver == "HaulersDream.JobDriver_BulkHaul" && !e.job.forced
                && e.inputJob?.token == e.job.token && e.inputJob.loadId == bulkId))
            {
                var work = d.LastOrDefault(e => e.kind == "native-work-return" && e.sequence < start.sequence
                    && e.actorId == start.actorId && e.returned == true && e.job?.token == start.job.token
                    && e.job.loadId == bulkId && e.job.workgiver == "HaulGeneral"
                    && e.job.workgiverClass == typeof(WorkGiver_HaulGeneral).FullName && !e.job.forced);
                if (work == null) continue;
                var enter = d.SingleOrDefault(e => e.kind == "native-work-enter" && e.callId == work.callId);
                if (enter == null || enter.sequence >= work.sequence || enter.actorId != start.actorId) continue;
                var has = d.Where(e => e.kind == "has-enter" && e.parentCallId == work.callId
                    && e.sequence > enter.sequence && e.sequence < work.sequence && e.actorId == start.actorId && e.forced == false).ToList();
                bool queried = has.Any(h => d.Any(e => e.kind == "has-return" && e.callId == h.callId && e.returned == true
                    && e.sequence > h.sequence && e.sequence < work.sequence));
                bool selected = d.Any(e => e.kind == "candidate-return" && e.parentCallId == work.callId
                    && e.sequence > enter.sequence && e.sequence < work.sequence && e.actorId == start.actorId
                    && e.job?.token == start.job.token && e.job.loadId == bulkId && e.forced == false);
                if (queried && selected) return true;
            }
            return false;
        }
        private L04B1Result Finish()
        {
            result.finishedTick = Find.TickManager.TicksGame; result.fixtureValid = executionOkay && !fault;
            bool expected = result.assertions.Where(a => a.kind == "expectation").All(a => a.passed);
            result.expectationMatched = result.fixtureValid && result.queryControlsComplete && result.healthyDeliveryComplete && expected;
            result.implementedBehaviorSatisfied = result.expectationMatched && behaviorOkay;
            result.baselineGapObserved = result.expectationMatched && result.expectedBehavior == "baseline-gap";
            result.requestedBehaviorSatisfied = result.implementedBehaviorSatisfied && result.incompleteControls.Count == 0;
            result.status = fault ? "inconclusive" : !result.expectationMatched ? "failed" : result.incompleteControls.Count > 0 ? "partial" : "passed";
            Dispose(); HarnessSession.Event("l04-b1-result", Json.Stringify(result)); return result;
        }
        private void Assert(string id, string kind, bool okay, string detail)
        {
            result.assertions.Add(new B1Assertion { id = id, kind = kind, passed = okay, detail = detail });
            HarnessSession.Event("l04-b1-assertion", Json.Stringify(result.assertions[result.assertions.Count - 1]));
            if (!okay && kind == "execution") executionOkay = false; if (!okay && kind == "behavior") behaviorOkay = false;
        }
        private void Require(string id, bool okay, string detail) { Assert(id, "execution", okay, detail); if (!okay) throw new InvalidOperationException(id + ": " + detail); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { healthy?.Dispose(); }
            finally { try { ReleaseBorrowedJobs(); } finally { observers?.Dispose(); } }
        }
        private sealed class Scene
        {
            internal string id; internal CellRect rect; internal IntVec3 center, highCell, originalCell, alternateCell;
            internal Zone_Stockpile high; internal Thing anchor, extra; internal B1Physical initial;
            internal List<Pawn> actors = new List<Pawn>(); internal List<int> originalIds = new List<int>();
        }
    }
}
