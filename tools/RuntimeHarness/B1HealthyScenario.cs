using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    // Disposable, automatic L04-B1 healthy execution witness. No job injection, claim injection,
    // forced workgiver call, product mutation, or asserted success from a commit log.
    internal sealed class B1HealthyScenario : IDisposable
    {
        internal const string BulkDef = "HaulersDream_BulkHaul", UnloadDef = "HaulersDream_UnloadInventory";
        private const int FollowupTicks = 600, StableTicks = 600, MaximumTicks = 6000;
        private readonly Map map;
        private readonly string expectation;
        private readonly int startedTick;
        private readonly List<B1HealthyJob> jobs = new List<B1HealthyJob>();
        private readonly List<B1HealthyTransfer> transfers = new List<B1HealthyTransfer>();
        private readonly List<B1HealthyQuery> queries = new List<B1HealthyQuery>();
        private readonly List<B1HealthyGate> gates = new List<B1HealthyGate>();
        private readonly HashSet<int> candidateBulkIds = new HashSet<int>();
        private readonly List<IntVec3> roof = new List<IntVec3>();
        private readonly List<int> originalSourceIds = new List<int>();
        private readonly Dictionary<int, int> pickedFromOriginal = new Dictionary<int, int>();
        private B1HealthyObservers observers;
        private FieldInfo claimRows;
        private object hauledComp;
        private MethodInfo peekTags;
        private Zone_Stockpile source, high;
        private ISlotGroup sourceGroup, highGroup;
        private IntVec3 highCell;
        private Pawn actor;
        private bool active, observing, observerHealthy = true, conserved = true, layoutIntact = true, forcedObserved;
        private bool fullTaggedInventoryWithOwnClaim, initialBulkQueueExact;
        private int sequence, boundaries, maxTotal = 10, minTotal = 10, lastEventTick = -1;
        private int firstBulkId = -1, firstUnloadId = -1, firstUnloadEndTick = -1;
        private int firstBulkPicked, firstUnloadHigh, firstUnloadSource, firstUnloadElsewhere;
        private int stableSince = -1, stableDistinctTicks, lastStableTick = -1;
        private int baselineCycles, sourceReacquired, laterHandDelivered;
        private string previousState;

        internal B1HealthyScenario(Map map, string expectedBehavior)
        {
            this.map = map;
            expectation = expectedBehavior;
            startedTick = Find.TickManager.TicksGame;
            try
            {
                Setup();
                active = true;
                observers = new B1HealthyObservers(this, actor);
                ObserveSettled("fixture-start");
            }
            catch { Dispose(); throw; }
        }

        private static void Require(string id, bool passed, string detail)
        {
            HarnessSession.Check("fixture-l04-b1-healthy-" + id, passed, detail);
            if (!passed) throw new InvalidDataException(id + ": " + detail);
        }

        internal static Type TypeNamed(string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).Where(t => t != null).ToList();
            if (matches.Count != 1) throw new TypeLoadException(name + "; loaded definitions=" + matches.Count);
            return matches[0];
        }

        private void Setup()
        {
            Require("expectation", expectation == "baseline-gap" || expectation == "satisfied", expectation);
            Require("home-map", map != null && map.IsPlayerHome, "New disposable player-home map.");
            Require("steel-limit", ThingDefOf.Steel.stackLimit == 75, "Actual Steel.stackLimit=" + ThingDefOf.Steel.stackLimit);
            var hd = TypeNamed("HaulersDream.HaulersDreamMod");
            var settings = hd.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            Require("settings", settings != null, hd.Assembly.FullName + "; MVID=" + hd.Module.ModuleVersionId);
            foreach (var name in new[] { "masterEnabled", "haulToStack", "bulkHaul", "markForUnload" }) SetBool(settings, name, true);
            SetBool(settings, "pickupDelayOnHauling", false);
            SetFloat(settings, "carryLimitFraction", 1f);
            SetFloat(settings, "carryMassCapKg", 0f);
            var itemRule = AccessTools.Method(settings.GetType(), "TryGetItemRule");
            Require("no-steel-keep-rule", itemRule != null && !(bool)itemRule.Invoke(settings, new object[] { ThingDefOf.Steel, null }),
                "No individual steel rule may mask automatic unloading in this isolated run.");
            var adapter = TypeNamed("HaulersDream.StorageCommitments");
            Require("seam-active", (bool)AccessTools.Method(adapter, "GatesVanillaStorage").Invoke(null, new object[] { map }),
                "Actual startup bind tripwire, master/map gate and haul-to-stack active.");
            claimRows = AccessTools.Field(TypeNamed("HaulersDream.HaulersDreamGameComponent"), "storageClaims");
            Require("claim-field", claimRows != null, "Read-only access to immutable production claim rows.");
            ExactPatch(typeof(WorkGiver_HaulGeneral).GetMethod("JobOnThing"), "HaulersDream.Patch_WorkGiver_HaulGeneral_BulkHaul", false, "bulk-route");
            ExactPatch(typeof(StoreUtility).GetMethod("IsGoodStoreCell"), "HaulersDream.Patch_IsGoodStoreCell_HonourCommitments", false, "storage-gate");
            ExactPatch(typeof(HaulAIUtility).GetMethod("HaulToCellStorageJob"), "HaulersDream.Patch_HaulToCellStorageJob_ClampToCommitments", false, "storage-counter");
            ExactPatch(typeof(JobDriver_HaulToCell).GetMethod("TryMakePreToilReservations"), "HaulersDream.Patch_JobDriver_HaulToCell_NoCellReservation", true, "storage-reservation");

            var initial = map.listerThings.AllThings.Where(t => t is Pawn || t is Skyfaller || t is ActiveTransporter).ToList();
            foreach (var t in initial) if (t.Spawned) t.DeSpawn();
            // Existing generated steel is removed only on this disposable map, so material conservation
            // covers every spawned Steel Thing plus this actor's real inventory/hands.
            foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.Steel).ToList()) if (t.Spawned) t.Destroy();
            var rect = new CellRect(map.Center.x - 20, map.Center.z - 6, 41, 13);
            Require("bounds", rect.Cells.All(c => c.InBounds(map)), rect.ToString());
            Require("no-overwritten-zones", rect.Cells.All(c => map.zoneManager.ZoneAt(c) == null), "Fixture rectangle has no existing zones.");
            GenDebug.ClearArea(rect, map);
            foreach (var c in rect.Cells)
            {
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(c, null);
                map.areaManager.Home[c] = true;
                map.fogGrid.Unfog(c);
            }
            foreach (var c in rect.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), c, map);
            roof.AddRange(rect.ContractedBy(1).Cells);
            Require("roof-support", roof.All(c => Math.Min(Math.Min(c.x - rect.minX, rect.maxX - c.x),
                Math.Min(c.z - rect.minZ, rect.maxZ - c.z)) <= 6), "41x13 supported enclosure; no access to unrelated outside work.");
            foreach (var c in roof) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            var sourceCells = new[] { map.Center + new IntVec3(-14, 0, -1), map.Center + new IntVec3(-14, 0, 1) };
            highCell = map.Center + new IntVec3(14, 0, 0);
            source = NewZone(StoragePriority.Normal, sourceCells);
            high = NewZone(StoragePriority.Critical, new[] { highCell });
            sourceGroup = source.GetSlotGroup(); highGroup = high.GetSlotGroup();
            foreach (var c in sourceCells) originalSourceIds.Add(SpawnSteel(5, c).thingIDNumber);
            Thing highThing = null; // Empty destination; ten units of total cargo.
            Require("exact-stockpiles", sourceGroup.CellsList.Count == 2 && highGroup.CellsList.Count == 1
                && highCell.GetItemStackSpaceLeftFor(map, ThingDefOf.Steel) == 75
                && sourceCells.All(c => c.GetMaxItemsAllowedInCell(map) == 1),
                "Normal source5+5; Critical high0/75; sourceIDs=" + string.Join(",", originalSourceIds)
                + "; highID=" + (highThing?.thingIDNumber ?? -1) + "; highCell=" + highCell);
            Require("no-other-higher-storage", map.haulDestinationManager.AllGroupsListInPriorityOrder.All(g =>
                ReferenceEquals(g, highGroup) || (int)g.Settings.Priority <= (int)StoragePriority.Normal || !g.Settings.AllowedToAccept(ThingDefOf.Steel)),
                "No other group above Normal accepts steel; only the Critical fixture cell can improve these sources.");
            Rand.PushState(10402);
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    var p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    if (p.RaceProps.Humanlike && !p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) && !p.Downed
                        && p.inventory != null && p.carryTracker != null && p.carryTracker.MaxStackSpaceEver(ThingDefOf.Steel) >= 10)
                    { actor = p; break; }
                }
            }
            finally { Rand.PopState(); }
            Require("capable-human", actor != null, "Seed10402; ordinary human capable of hauling and at least ten steel in hands.");
            actor.inventory.innerContainer.ClearAndDestroyContents();
            actor.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            actor.workSettings.DisableAll();
            actor.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            actor.timetable.times = Enumerable.Repeat(TimeAssignmentDefOf.Work, 24).ToList();
            if (actor.needs.food != null) actor.needs.food.CurLevelPercentage = 1;
            if (actor.needs.rest != null) actor.needs.rest.CurLevelPercentage = 1;
            if (actor.needs.joy != null) actor.needs.joy.CurLevelPercentage = 1;
            GenSpawn.Spawn(actor, map.Center + new IntVec3(-16, 0, 0), map);
            hauledComp = actor.AllComps.SingleOrDefault(c => c.GetType().FullName == "HaulersDream.CompHauledToInventory");
            Require("haul-comp", hauledComp != null, "Actual injected component; no inventory tags supplied by fixture.");
            SetBool(hauledComp, "autoHaulYields", true);
            peekTags = AccessTools.Method(hauledComp.GetType(), "PeekHashSet");
            Require("tag-observation", peekTags != null, "Read-only PeekHashSet; no self-heal invoked.");
            Require("native-work-only", actor.CurJob == null && actor.jobs.curDriver == null
                && DefDatabase<WorkTypeDef>.AllDefsListForReading.All(w => w == WorkTypeDefOf.Hauling || !actor.workSettings.WorkIsActive(w)),
                "Only Hauling1; no injected/started/ordered current or queued job.");
            Require("initial-no-claims", ReadClaims().Count == 0, "No actual steel claim for the fixture pawn at setup.");
            Require("initial-counts", Snapshot().source == 10 && Snapshot().high == 0 && Snapshot().total == 10,
                "Exactly10 physical steel, two5-unit sources and0 high; empty inventory/hands.");
            HarnessSession.Event("l04-b1-healthy-fixture", "case=L04-B1-HEALTHY; actor=" + actor.ThingID
                + "; source=" + string.Join(",", sourceCells.Select(c => c.ToString())) + "; high=" + highCell
                + "; ordinary workgiver only; no runtime job/claim injection; first unload then at least600 stable ticks; healthy on both selections.");
        }

        private static void SetBool(object instance, string name, bool value)
        {
            var f = AccessTools.Field(instance.GetType(), name);
            Require("setting-" + name, f != null && f.FieldType == typeof(bool), name);
            f.SetValue(instance, value);
        }
        private static void SetFloat(object instance, string name, float value)
        {
            var f = AccessTools.Field(instance.GetType(), name);
            Require("setting-" + name, f != null && f.FieldType == typeof(float), name);
            f.SetValue(instance, value);
        }
        private static void ExactPatch(MethodBase target, string name, bool prefix, string id)
        {
            var type = TypeNamed(name);
            var info = target == null ? null : Harmony.GetPatchInfo(target);
            var list = prefix ? info?.Prefixes : info?.Postfixes;
            bool exact = list?.Count(p => p.owner == "giwaffed.HaulersDream" && p.PatchMethod.DeclaringType == type
                && p.PatchMethod.Name == (prefix ? "Prefix" : "Postfix") && p.PatchMethod.Module.Assembly == TypeNamed("HaulersDream.HaulersDreamMod").Assembly) == 1;
            Require("patch-" + id, exact, name + "; target=" + target + "; assembly=" + type.Assembly.FullName
                + "; path=" + type.Assembly.Location + "; MVID=" + type.Module.ModuleVersionId);
        }
        private Zone_Stockpile NewZone(StoragePriority priority, IEnumerable<IntVec3> cells)
        {
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            zone.settings.filter.SetDisallowAll(); zone.settings.filter.SetAllow(ThingDefOf.Steel, true); zone.settings.Priority = priority;
            map.zoneManager.RegisterZone(zone);
            foreach (var c in cells) zone.AddCell(c);
            return zone;
        }
        private Thing SpawnSteel(int count, IntVec3 cell)
        {
            var t = ThingMaker.MakeThing(ThingDefOf.Steel); t.stackCount = count;
            return GenSpawn.Spawn(t, cell, map);
        }
        internal bool IsActor(Pawn pawn) => ReferenceEquals(pawn, actor);
        internal Pawn ObservationActor => actor;
        internal bool IsOriginalSource(Thing thing) => thing != null && originalSourceIds.Contains(thing.thingIDNumber);
        internal bool IsSteel(Thing thing) => thing?.def == ThingDefOf.Steel;
        internal bool IsHigh(ISlotGroup group) => ReferenceEquals(group, highGroup);
        internal bool IsHighCell(IntVec3 cell) => cell == highCell;
        internal bool IsActive => active;
        private bool Actual(JobDriver driver) => driver != null && ReferenceEquals(actor.jobs.curDriver, driver)
            && ReferenceEquals(actor.CurJob, driver.job) && driver.pawn == actor;
        private string GroupName(object group) => ReferenceEquals(group, highGroup) ? "critical@" + highCell
            : ReferenceEquals(group, sourceGroup) ? "source-normal" : "other:" + group;
        private int InventoryCount => actor.inventory.innerContainer.Where(IsSteel).Sum(t => t.stackCount);
        private int CarryCount => IsSteel(actor.carryTracker.CarriedThing) ? actor.carryTracker.CarriedThing.stackCount : 0;
        private int TaggedCount => ((IEnumerable)peekTags.Invoke(hauledComp, null)).Cast<Thing>()
            .Where(t => IsSteel(t) && !t.Destroyed && ReferenceEquals(t.holdingOwner, actor.inventory.innerContainer)).Sum(t => t.stackCount);

        private List<B1HealthyClaim> ReadClaims()
        {
            var answer = new List<B1HealthyClaim>();
            foreach (object row in (IEnumerable)claimRows.GetValue(null))
            {
                var type = row.GetType();
                if (!ReferenceEquals(type.GetField("Pawn").GetValue(row), actor) || !ReferenceEquals(type.GetField("Def").GetValue(row), ThingDefOf.Steel)) continue;
                object group = type.GetField("Group").GetValue(row);
                answer.Add(new B1HealthyClaim { group = GroupName(group), high = ReferenceEquals(group, highGroup),
                    recordedUnits = (int)type.GetField("Units").GetValue(row) });
            }
            return answer;
        }
        internal B1HealthyThing Describe(Thing t)
        {
            if (t == null) return null;
            return new B1HealthyThing { id = t.thingIDNumber, count = t.stackCount, destroyed = t.Destroyed, spawned = t.Spawned,
                cell = t.Spawned ? t.Position.ToString() : null, holder = t.ParentHolder?.GetType().FullName,
                inventory = ReferenceEquals(t.holdingOwner, actor.inventory.innerContainer), hands = ReferenceEquals(t, actor.carryTracker.CarriedThing) };
        }
        internal B1HealthyState Snapshot()
        {
            var floor = map.listerThings.ThingsOfDef(ThingDefOf.Steel).Where(t => t.Spawned).ToList();
            int low = floor.Where(t => ReferenceEquals(t.Position.GetSlotGroup(map), sourceGroup)).Sum(t => t.stackCount);
            int top = floor.Where(t => t.Position == highCell).Sum(t => t.stackCount);
            var state = new B1HealthyState { tick = Find.TickManager.TicksGame, sequence = ++sequence,
                jobId = actor.CurJob?.loadID ?? -1, jobDef = actor.CurJob?.def?.defName, position = actor.Position.ToString(),
                source = low, high = top, elsewhere = floor.Sum(t => t.stackCount) - low - top,
                inventory = InventoryCount, hands = CarryCount,
                things = floor.Concat(actor.inventory.innerContainer.Where(IsSteel)).Concat(actor.carryTracker.GetDirectlyHeldThings().Where(IsSteel))
                    .Distinct().OrderBy(t => t.thingIDNumber).Select(Describe).ToList(), claims = ReadClaims() };
            state.total = state.source + state.high + state.elsewhere + state.inventory + state.hands;
            return state;
        }

        internal void Candidate(Job job, Thing original, bool forced)
        {
            if (!active || job == null || !IsSteel(original)) return;
            if (job.def.defName == BulkDef) candidateBulkIds.Add(job.loadID);
            // Probes remain separate from the actual current-job/driver observations.
            HarnessSession.Event("l04-b1-healthy-candidate", "id=" + job.loadID + "; def=" + job.def.defName
                + "; source=" + original.thingIDNumber + "; forcedArgument=" + forced + "; queue=" + QueueDescription(job));
        }
        private static string QueueDescription(Job job) => job.targetQueueB == null ? "none" : string.Join(",", job.targetQueueB.Select((t, i) =>
            (t.Thing?.thingIDNumber ?? -1) + "x" + (job.countQueue != null && i < job.countQueue.Count ? job.countQueue[i] : -1)));
        internal void ObserveActualJob()
        {
            if (!active || !Actual(actor.jobs.curDriver)) return;
            var job = actor.CurJob;
            var found = jobs.FirstOrDefault(j => j.id == job.loadID);
            if (found != null) return;
            string def = job.def.defName;
            var record = new B1HealthyJob { id = job.loadID, def = def, driver = actor.jobs.curDriver.GetType().FullName,
                workgiver = job.workGiverDef?.defName, workgiverClass = job.workGiverDef?.giverClass?.FullName,
                forced = job.playerForced, observedTick = Find.TickManager.TicksGame, endTick = -1, queue = QueueDescription(job),
                candidateObserved = candidateBulkIds.Contains(job.loadID) };
            jobs.Add(record);
            if (def == BulkDef && firstBulkId < 0)
            {
                firstBulkId = job.loadID;
                initialBulkQueueExact = job.targetQueueB != null && job.countQueue != null && job.targetQueueB.Count == 2 && job.countQueue.Count == 2
                    && job.targetQueueB.All(t => t.HasThing && originalSourceIds.Contains(t.Thing.thingIDNumber))
                    && job.targetQueueB.Select(t => t.Thing.thingIDNumber).Distinct().Count() == 2 && job.countQueue.All(n => n == 5);
            }
            if (def == UnloadDef && firstUnloadId < 0) firstUnloadId = job.loadID;
            if (def == BulkDef || def == UnloadDef || def == JobDefOf.HaulToCell.defName) forcedObserved |= job.playerForced;
            HarnessSession.Event("l04-b1-healthy-current-job", Json.Stringify(record));
        }

        internal B1HealthyQuery BeginFree(Pawn pawn, ISlotGroup group, Thing subject)
        {
            if (!active || !IsActor(pawn) || !IsSteel(subject)) return null;
            ObserveActualJob();
            var state = Snapshot();
            return new B1HealthyQuery { tick = state.tick, sequence = state.sequence, jobId = state.jobId, jobDef = state.jobDef,
                group = GroupName(group), highGroup = IsHigh(group), subject = Describe(subject), before = state,
                taggedInventory = TaggedCount, delivering = -1, productionLiveUnits = -1, free = -1 };
        }
        internal void EndFree(B1HealthyQuery query, int free, bool truncated)
        {
            if (!active || query == null) return;
            query.free = free; query.truncated = truncated;
            queries.Add(query);
            if (query.highGroup && query.subject.inventory && query.before.inventory == 10 && query.taggedInventory == 10
                && query.before.source == 0 && query.before.high == 0 && query.before.claims.Any(c => c.high && c.recordedUnits == 10))
                fullTaggedInventoryWithOwnClaim = true;
            HarnessSession.Event("l04-b1-healthy-free-query", Json.Stringify(query));
        }
        internal void CellGate(IntVec3 cell, Thing subject, bool allowed)
        {
            if (!active || !IsHighCell(cell) || !IsSteel(subject)) return;
            ObserveActualJob();
            var state = Snapshot();
            var gate = new B1HealthyGate { tick = state.tick, sequence = state.sequence, jobId = state.jobId,
                jobDef = state.jobDef, allowed = allowed, subject = Describe(subject), before = state };
            gates.Add(gate);
            HarnessSession.Event("l04-b1-healthy-cell-gate", Json.Stringify(gate));
        }

        internal PickupToken BeginPickup(JobDriver driver, Thing split)
        {
            if (!active || !Actual(driver) || driver.job.def.defName != BulkDef || !IsSteel(split)) return null;
            ObserveActualJob();
            return new PickupToken { jobId = driver.job.loadID, split = Describe(split), inventoryBefore = InventoryCount };
        }
        internal void EndPickup(PickupToken token, bool added)
        {
            if (!active || token == null) return;
            var after = Snapshot();
            int gained = after.inventory - token.inventoryBefore;
            if (token.jobId == firstBulkId)
            {
                firstBulkPicked += Math.Max(gained, 0);
                if (originalSourceIds.Contains(token.split.id)) pickedFromOriginal[token.split.id] =
                    (pickedFromOriginal.TryGetValue(token.split.id, out int prior) ? prior : 0) + Math.Max(gained, 0);
            }
            transfers.Add(new B1HealthyTransfer { kind = "bulk-pickup", jobId = token.jobId, tick = after.tick, sequence = after.sequence,
                units = gained, returnedSuccess = added, original = token.split, after = after });
            HarnessSession.Event("l04-b1-healthy-bulk-pickup", Json.Stringify(transfers[transfers.Count - 1]));
            ObserveSettled("bulk-pickup-return");
        }
        internal sealed class PickupToken { internal int jobId, inventoryBefore; internal B1HealthyThing split; }

        internal DropToken BeginDrop()
        {
            if (!active || !IsSteel(actor.carryTracker.CarriedThing)) return null;
            ObserveActualJob();
            return new DropToken { jobId = actor.CurJob?.loadID ?? -1, jobDef = actor.CurJob?.def?.defName,
                original = Describe(actor.carryTracker.CarriedThing), before = Snapshot() };
        }
        internal void EndDrop(DropToken token, bool returned, Thing resultingThing)
        {
            if (!active || token == null) return;
            var after = Snapshot();
            int highDelta = after.high - token.before.high, lowDelta = after.source - token.before.source;
            int floorDelta = highDelta + lowDelta + after.elsewhere - token.before.elsewhere;
            var transfer = new B1HealthyTransfer { kind = "carry-drop", jobId = token.jobId, tick = after.tick, sequence = after.sequence,
                units = floorDelta, returnedSuccess = returned, original = token.original, resulting = Describe(resultingThing), before = token.before, after = after };
            transfers.Add(transfer);
            if (token.jobId == firstUnloadId)
            {
                firstUnloadHigh += highDelta; firstUnloadSource += lowDelta;
                firstUnloadElsewhere += after.elsewhere - token.before.elsewhere;
            }
            else if (firstUnloadSource == 10 && token.jobDef == JobDefOf.HaulToCell.defName) laterHandDelivered += Math.Max(0, highDelta);
            HarnessSession.Event("l04-b1-healthy-physical-deposit", Json.Stringify(transfer));
            ObserveSettled("carry-drop-return");
        }
        internal sealed class DropToken { internal int jobId; internal string jobDef; internal B1HealthyThing original; internal B1HealthyState before; }
        internal B1HealthyState BeginCarry()
        {
            if (!active) return null;
            ObserveActualJob(); return Snapshot();
        }
        internal void EndCarry(B1HealthyState before)
        {
            if (!active || before == null) return;
            var after = Snapshot();
            int acquired = Math.Max(0, after.inventory + after.hands - before.inventory - before.hands);
            if (firstUnloadSource == 10 && before.jobDef == JobDefOf.HaulToCell.defName) sourceReacquired += acquired;
            var transfer = new B1HealthyTransfer {
                kind = "start-carry", jobId = before.jobId, tick = after.tick, sequence = after.sequence, units = acquired, before = before, after = after };
            transfers.Add(transfer);
            HarnessSession.Event("l04-b1-healthy-carry-transfer", Json.Stringify(transfer));
            ObserveSettled("carry-transfer-return");
        }
        internal EndToken BeginCleanup(JobCondition condition)
        {
            if (!active || !Actual(actor.jobs.curDriver)) return null;
            ObserveActualJob();
            return new EndToken { job = actor.CurJob, driver = actor.jobs.curDriver, id = actor.CurJob.loadID, condition = condition };
        }
        internal void EndCleanup(EndToken token)
        {
            if (!active || token == null) return;
            var record = jobs.Single(j => j.id == token.id);
            record.endTick = Find.TickManager.TicksGame; record.endCondition = token.condition.ToString();
            record.released = token.driver.ended && !ReferenceEquals(actor.CurJob, token.job) && !ReferenceEquals(actor.jobs.curDriver, token.driver);
            if (token.id == firstUnloadId)
            {
                firstUnloadEndTick = record.endTick;
                if (firstBulkPicked == 10 && firstUnloadSource == 10 && firstUnloadHigh == 0 && firstUnloadElsewhere == 0
                    && token.condition == JobCondition.Succeeded && record.released) baselineCycles++;
            }
            HarnessSession.Event("l04-b1-healthy-job-cleanup", Json.Stringify(record));
        }
        internal sealed class EndToken { internal Job job; internal JobDriver driver; internal int id; internal JobCondition condition; }

        internal void ObserveSettled(string reason)
        {
            if (!active || observing) return;
            observing = true;
            try
            {
                ObserveActualJob();
                var state = Snapshot(); boundaries++;
                maxTotal = Math.Max(maxTotal, state.total); minTotal = Math.Min(minTotal, state.total);
                conserved &= state.total == 10 && state.elsewhere == 0;
                layoutIntact &= roof.All(c => map.roofGrid.RoofAt(c) == RoofDefOf.RoofConstructed)
                    && source.settings.Priority == StoragePriority.Normal && high.settings.Priority == StoragePriority.Critical
                    && sourceGroup.CellsList.Count == 2 && highGroup.CellsList.Count == 1 && actor.Spawned && !actor.Dead;
                bool highComplete = firstUnloadEndTick >= 0 && state.high == 10 && state.source == 0 && state.inventory == 0 && state.hands == 0 && state.elsewhere == 0;
                if (highComplete)
                {
                    if (stableSince < 0) stableSince = state.tick;
                    if (lastStableTick != state.tick) stableDistinctTicks++;
                    lastStableTick = state.tick;
                }
                else { stableSince = -1; stableDistinctTicks = 0; lastStableTick = -1; }
                string signature = state.jobId + "/" + state.jobDef + "/" + state.source + "/" + state.high + "/" + state.inventory + "/" + state.hands + "/" + state.total;
                if (signature != previousState || reason != "tick" || state.tick - lastEventTick >= 60)
                {
                    HarnessSession.Event("l04-b1-healthy-settled-state", Json.Stringify(state));
                    previousState = signature; lastEventTick = state.tick;
                }
            }
            finally { observing = false; }
        }
        internal void ObserverFault(Exception error)
        {
            observerHealthy = false;
            HarnessSession.Event("l04-b1-healthy-observer-fault", error.ToString());
        }
        private bool Succeeded(int id) => jobs.Any(j => j.id == id && j.endCondition == JobCondition.Succeeded.ToString() && j.released);
        internal B1HealthyResult TryFinish()
        {
            if (!active) return null;
            ObserveSettled("tick");
            int now = Find.TickManager.TicksGame;
            bool timedOut = now - startedTick >= MaximumTicks;
            bool followup = firstUnloadEndTick >= 0 && now - firstUnloadEndTick >= FollowupTicks;
            if (!timedOut && !followup) return null;
            var final = Snapshot();
            var bulk = jobs.FirstOrDefault(j => j.id == firstBulkId);
            bool ordinary = bulk != null && bulk.candidateObserved && initialBulkQueueExact && !forcedObserved
                && bulk.driver == "HaulersDream.JobDriver_BulkHaul" && bulk.workgiverClass == typeof(WorkGiver_HaulGeneral).FullName
                && jobs.Any(j => j.id == firstUnloadId && j.driver == "HaulersDream.JobDriver_UnloadHauledInventory");
            var pickups = transfers.Where(t => t.kind == "bulk-pickup" && t.jobId == firstBulkId).ToList();
            bool exactPickup = firstBulkPicked == 10 && originalSourceIds.All(id => pickedFromOriginal.TryGetValue(id, out int n) && n == 5)
                && pickups.Count == 2 && pickups.Select(t => t.original.id).Distinct().Count() == 2
                && pickups.Select((t, i) => t.returnedSuccess && t.units == 5 && t.original.count == 5
                    && originalSourceIds.Contains(t.original.id) && t.after.inventory == 5 * (i + 1)
                    && t.after.source == 5 * (1 - i) && t.after.high == 0 && t.after.hands == 0
                    && t.after.total == 10 && t.after.jobId == firstBulkId && t.after.jobDef == BulkDef).All(x => x);
            bool physicalDeposit = transfers.Any(t => t.kind == "carry-drop" && t.jobId == firstUnloadId && t.returnedSuccess
                && t.units == 10 && t.before.inventory + t.before.hands == 10 && t.after.inventory + t.after.hands == 0
                && t.after.total == 10 && t.resulting != null && t.resulting.spawned && !t.resulting.destroyed
                && t.resulting.count == 10 && t.resulting.cell == highCell.ToString()
                && t.before.jobId == firstUnloadId && t.before.jobDef == UnloadDef
                && t.after.jobId == firstUnloadId && t.after.jobDef == UnloadDef
                && t.after.things.Count(x => x.id == t.resulting.id && x.count == 10 && x.cell == highCell.ToString() && x.spawned) == 1);
            bool completed = Succeeded(firstBulkId) && Succeeded(firstUnloadId);
            bool firstHigh = firstUnloadHigh == 10 && firstUnloadSource == 0 && firstUnloadElsewhere == 0;
            bool settled = stableSince >= 0 && now - stableSince >= StableTicks && stableDistinctTicks >= StableTicks
                && final.high == 10 && final.source == 0 && final.inventory == 0 && final.hands == 0;
            bool noRehaul = jobs.Count(j => j.def == BulkDef) == 1 && jobs.Count(j => j.def == UnloadDef) == 1
                && jobs.All(j => j.def != JobDefOf.HaulToCell.defName);
            bool okay = ordinary && exactPickup && physicalDeposit && completed && firstHigh && settled && noRehaul
                && conserved && layoutIntact && observerHealthy && followup && !timedOut;
            Check("ordinary-chain", ordinary, "Actual automatic workgiver candidate/current driver; bulk=" + firstBulkId + "; unload=" + firstUnloadId);
            Check("exact-original-pickups", exactPickup, "Exactly five units from each original physical source ID.");
            Check("physical-unload", physicalDeposit && firstHigh, "Actual drop to the empty destination; no own-claim rejection requirement.");
            Check("successful-cleanups", completed, "Actual successful bulk and unload driver cleanup.");
            Check("stable-conservation", conserved && settled && noRehaul, "Ten total; high10/source0/empty holders for at least600 real ticks.");
            Check("execution-health", observerHealthy && layoutIntact && followup && !timedOut, "No observer faults, disrupted layout, or assumed timeout success.");
            var answer = new B1HealthyResult { fixtureValid = observerHealthy && layoutIntact && !timedOut,
                expectationMatched = okay, requestedBehaviorSatisfied = okay, baselineGapObserved = false,
                status = okay ? "passed" : timedOut ? "inconclusive" : "failed", expectedBehavior = expectation,
                caseId = "L04-B1-HEALTHY", startedTick = startedTick, finishedTick = now, firstUnloadEndTick = firstUnloadEndTick,
                firstBulkJobId = firstBulkId, firstUnloadJobId = firstUnloadId, highCell = highCell.ToString(),
                sourceCells = source.cells.Select(c => c.ToString()).ToList(), stableSinceTick = stableSince,
                originalSourceIds = originalSourceIds.ToList(), jobs = jobs.ToList(), transfers = transfers.ToList(),
                queries = queries.ToList(), gates = gates.ToList(), finalState = final };
            answer.ordinaryChain = ordinary; answer.exactPickup = exactPickup; answer.physicalUnload = physicalDeposit; answer.successfulCleanups = completed;
            answer.conserved = conserved; answer.observerHealthy = observerHealthy; answer.layoutIntact = layoutIntact; answer.followupWindowComplete = followup;
            answer.settledHigh = settled; answer.noRehaul = noRehaul; answer.stableDistinctTicks = stableDistinctTicks; answer.minTotal = minTotal; answer.maxTotal = maxTotal;
            answer.ownClaimInventoryWitness = fullTaggedInventoryWithOwnClaim; answer.sourceZoneNetZeroBulkCycles = baselineCycles;
            answer.laterSourceReacquired = sourceReacquired; answer.laterNativeHandDelivered = laterHandDelivered; answer.firstUnloadElsewhere = firstUnloadElsewhere;
            answer.settledBoundaries = boundaries; answer.timedOut = timedOut; answer.firstBulkPicked = firstBulkPicked; answer.firstUnloadHigh = firstUnloadHigh; answer.firstUnloadSource = firstUnloadSource;
            Dispose(); HarnessSession.Event("l04-b1-healthy-result", Json.Stringify(answer)); return answer;
        }
        private static void Check(string id, bool pass, string detail) => HarnessSession.Check("execution-l04-b1-healthy-" + id, pass, detail);
        public void Dispose() { active = false; observers?.Dispose(); observers = null; }
    }

    [DataContract] internal sealed class B1HealthyThing
    {
        [DataMember] public int id, count;
        [DataMember] public bool destroyed, spawned, inventory, hands;
        [DataMember] public string cell, holder;
    }
    [DataContract] internal sealed class B1HealthyClaim
    {
        [DataMember] public string group;
        [DataMember] public int recordedUnits;
        [DataMember] public bool high;
    }
    [DataContract] internal sealed class B1HealthyState
    {
        [DataMember] public int tick, sequence, jobId, source, high, elsewhere, inventory, hands, total;
        [DataMember] public string jobDef, position;
        [DataMember] public List<B1HealthyThing> things;
        [DataMember] public List<B1HealthyClaim> claims;
    }
    [DataContract] internal sealed class B1HealthyJob
    {
        [DataMember] public int id, observedTick, endTick;
        [DataMember] public string def, driver, workgiver, workgiverClass, queue, endCondition;
        [DataMember] public bool forced, candidateObserved, released;
    }
    [DataContract] internal sealed class B1HealthyTransfer
    {
        [DataMember] public string kind;
        [DataMember] public int jobId, tick, sequence, units;
        [DataMember] public bool returnedSuccess;
        [DataMember] public B1HealthyThing original, resulting;
        [DataMember] public B1HealthyState before, after;
    }
    [DataContract] internal sealed class B1HealthyQuery
    {
        [DataMember] public int tick, sequence, jobId, free, delivering, productionLiveUnits, taggedInventory;
        [DataMember] public string jobDef, group;
        [DataMember] public bool highGroup, truncated;
        [DataMember] public B1HealthyThing subject;
        [DataMember] public B1HealthyState before;
    }
    [DataContract] internal sealed class B1HealthyGate
    {
        [DataMember] public int tick, sequence, jobId;
        [DataMember] public string jobDef;
        [DataMember] public bool allowed;
        [DataMember] public B1HealthyThing subject;
        [DataMember] public B1HealthyState before;
    }
    [DataContract] internal sealed class B1HealthyResult
    {
        [DataMember] public string caseId, expectedBehavior, status, highCell;
        [DataMember] public bool fixtureValid, requestedBehaviorSatisfied, expectationMatched, baselineGapObserved, timedOut;
        [DataMember] public bool ordinaryChain, exactPickup, ownClaimInventoryWitness, physicalUnload, successfulCleanups;
        [DataMember] public bool conserved, observerHealthy, layoutIntact, followupWindowComplete;
        [DataMember] public bool settledHigh, noRehaul;
        [DataMember] public int startedTick, finishedTick, firstBulkJobId, firstUnloadJobId, firstUnloadEndTick;
        [DataMember] public int firstBulkPicked, firstUnloadHigh, firstUnloadSource, firstUnloadElsewhere, sourceZoneNetZeroBulkCycles;
        [DataMember] public int laterSourceReacquired, laterNativeHandDelivered, stableDistinctTicks, stableSinceTick, minTotal, maxTotal, settledBoundaries;
        [DataMember] public List<string> sourceCells;
        [DataMember] public List<int> originalSourceIds;
        [DataMember] public B1HealthyState finalState;
        [DataMember] public List<B1HealthyJob> jobs;
        [DataMember] public List<B1HealthyTransfer> transfers;
        [DataMember] public List<B1HealthyQuery> queries;
        [DataMember] public List<B1HealthyGate> gates;
    }
}
