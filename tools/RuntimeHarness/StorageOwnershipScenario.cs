using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    // L04-O1 adapter witness. All objects belong to the disposable quick-test map.
    // This intentionally does not claim that a job executed: custody and job evidence are fixture setup.
    internal sealed class StorageOwnershipScenario
    {
        private readonly Map map;
        private readonly StorageOwnershipResult result;
        private readonly List<Pawn> actors = new List<Pawn>();
        private readonly List<Tuple<Pawn, ISlotGroup>> claims = new List<Tuple<Pawn, ISlotGroup>>();
        private Type adapter;
        private MethodInfo delivering, freeUnits, tryCommit, retireClaim, unitsMoving, claimedByOthers;
        private ThingDef cargoDef;
        private int nextScene;

        private StorageOwnershipScenario(Map map, string expectedBehavior)
        {
            this.map = map;
            result = new StorageOwnershipResult
            {
                caseId = "L04-O1", expectedBehavior = expectedBehavior,
                scope = "real-map production adapter and patched StoreUtility; no executed hauling or save/load claim",
                startedTick = Find.TickManager.TicksGame, finishedTick = -1,
                fixtureValid = false, requestedBehaviorSatisfied = false, expectationMatched = false,
                status = "inconclusive", error = null,
                assertions = new List<StorageOwnershipAssertion>(), observations = new List<StorageOwnershipObservation>()
            };
        }

        internal static StorageOwnershipResult Run(Map map, string expectedBehavior)
        {
            var scenario = new StorageOwnershipScenario(map, expectedBehavior);
            try
            {
                scenario.Setup();
                scenario.RunControls();
                scenario.Require("same-tick-adapter-scope", Find.TickManager.TicksGame == scenario.result.startedTick,
                    "No TickManager advance or driver execution is part of this fixture.");
                scenario.result.fixtureValid = true;
                scenario.result.requestedBehaviorSatisfied = scenario.result.observations.All(x => x.correctedMatched);
                scenario.result.expectationMatched = expectedBehavior == "satisfied"
                    ? scenario.result.requestedBehaviorSatisfied
                    : scenario.result.observations.All(x => x.baselineMatched)
                        && !scenario.result.requestedBehaviorSatisfied;
                scenario.result.status = !scenario.result.expectationMatched ? "failed"
                    : scenario.result.requestedBehaviorSatisfied ? "passed" : "behavior-gap-observed";
            }
            catch (Exception error)
            {
                var cause = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                scenario.result.error = cause.ToString();
                scenario.Record("fixture", "exception", false, cause.ToString());
                scenario.result.fixtureValid = false;
                scenario.result.requestedBehaviorSatisfied = false;
                scenario.result.expectationMatched = false;
                scenario.result.status = "inconclusive";
            }
            finally
            {
                // No fabricated running job is allowed to survive into a later map tick.
                foreach (var pawn in scenario.actors) if (pawn.jobs != null) pawn.jobs.curJob = null;
                foreach (var claim in scenario.claims)
                {
                    try { scenario.retireClaim.Invoke(null, new object[] { claim.Item1, claim.Item2, scenario.cargoDef, 0, "runtime-fixture-retire" }); }
                    catch (Exception error)
                    {
                        scenario.Record("fixture", "claim-retirement", false, error.ToString());
                        scenario.result.fixtureValid = false;
                        scenario.result.expectationMatched = false;
                        scenario.result.requestedBehaviorSatisfied = false;
                        scenario.result.status = "inconclusive";
                    }
                }
                scenario.result.finishedTick = Find.TickManager.TicksGame;
                HarnessSession.Event("storage-ownership-result", Json.Stringify(scenario.result));
            }
            return scenario.result;
        }

        private void Setup()
        {
            Require("expectation", result.expectedBehavior == "baseline-gap" || result.expectedBehavior == "satisfied", result.expectedBehavior);
            Require("map", map != null && map.IsPlayerHome, "Requires the newly generated player-home map.");
            adapter = FindType("HaulersDream.StorageCommitments");
            delivering = Method(adapter, "IsDelivering", typeof(Pawn), typeof(Thing));
            freeUnits = Method(adapter, "FreeUnitsFor", typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef), typeof(Thing), typeof(bool).MakeByRefType());
            tryCommit = Method(adapter, "TryCommit", typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef), typeof(int), typeof(string));
            retireClaim = Method(adapter, "Commit", typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef), typeof(int), typeof(string));
            unitsMoving = Method(adapter, "UnitsMovingOf", typeof(Pawn), typeof(ThingDef));
            claimedByOthers = Method(adapter, "ClaimedByOthersFor", typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef));
            result.adapterAssembly = adapter.Assembly.FullName;
            result.adapterModuleId = adapter.Module.ModuleVersionId.ToString();
            HarnessSession.Event("storage-ownership-bind", "assembly=" + result.adapterAssembly + "; MVID=" + result.adapterModuleId
                + "; IsDelivering token=" + delivering.MetadataToken + "; FreeUnitsFor token=" + freeUnits.MetadataToken);

            var hd = FindType("HaulersDream.HaulersDreamMod");
            var settings = hd.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            Require("settings", settings != null, "Actual HaulersDreamMod.Settings instance");
            foreach (var name in new[] { "masterEnabled", "haulToStack" })
            {
                var field = settings.GetType().GetField(name);
                Require("setting-" + name, field != null && field.FieldType == typeof(bool), name);
                field.SetValue(settings, true);
            }
            Require("active-storage-gates", (bool)Method(adapter, "GatesVanillaStorage", typeof(Map)).Invoke(null, new object[] { map }),
                "Master enable, home-map gate, haul-to-stack and startup seam verification all active.");
            cargoDef = ThingDefOf.Steel;
            Require("cargo-stack-limit", cargoDef.stackLimit > 14, "Steel stackLimit=" + cargoDef.stackLimit);

            var initial = map.listerThings.AllThings.Where(x => x is Pawn || x is Skyfaller || x is ActiveTransporter).ToList();
            foreach (var thing in initial) if (thing.Spawned) thing.DeSpawn();
            var rect = new CellRect(map.Center.x - 22, map.Center.z - 12, 45, 25);
            Require("scene-bounds", rect.Cells.All(x => x.InBounds(map)), rect.ToString());
            Require("scene-has-no-zones", rect.Cells.All(x => map.zoneManager.ZoneAt(x) == null), "No pre-existing zone is overwritten.");
            GenDebug.ClearArea(rect, map);
            foreach (var cell in rect.Cells)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
                map.areaManager.Home[cell] = true;
                map.fogGrid.Unfog(cell);
            }
            HarnessSession.Event("storage-ownership-fixture", "Despawned generated actors/landing objects=" + initial.Count
                + "; cleared 45x25 outdoor rectangle; fresh pawns/groups for every control; no game ticks advanced.");
        }

        private void RunControls()
        {
            var inventory = Scene("own-inventory", 10);
            var ownCargo = PutInInventory(inventory.asker, 10);
            Require("own-inventory-holder", ReferenceEquals(ownCargo.holdingOwner, inventory.asker.inventory.innerContainer)
                && ReferenceEquals(ownCargo.ParentHolder, inventory.asker.inventory), Describe(ownCargo, inventory.asker));
            BaselineCapacity(inventory, ownCargo);
            Claim(inventory.asker, inventory, 10);
            Probe(inventory, ownCargo, true, 10, false, 0);

            var hands = Scene("own-hands", 10);
            var handCargo = PutInHands(hands.asker, hands, 10);
            BaselineCapacity(hands, handCargo);
            Claim(hands.asker, hands, 10);
            Probe(hands, handCargo, true, 10, true, 10);

            var foreign = Scene("other-pawn-inventory", 10);
            var owner = NewPawn(foreign.cell + new IntVec3(2, 0, 1));
            var foreignCargo = PutInInventory(owner, 10);
            BaselineCapacity(foreign, foreignCargo);
            Claim(owner, foreign, 10);
            Require("foreign-claim-live", Others(foreign) == 10, "ClaimedByOthersFor=" + Others(foreign));
            Probe(foreign, foreignCargo, false, 0, false, 0);

            var floor = Scene("floor-with-own-in-flight-claim", 10);
            PutInInventory(floor.asker, 10);
            var floorCargo = SpawnCargo(10, floor.cell + new IntVec3(0, 0, 3));
            BaselineCapacity(floor, floorCargo);
            Claim(floor.asker, floor, 10);
            // This game registers spawned Things in the map's ThingOwner. A floor item
            // is identified by its real map custody, not an assumed null holdingOwner.
            Require("floor-subject-spawned", floorCargo.Spawned && floorCargo.Map == map
                && ReferenceEquals(floorCargo.ParentHolder, map), Describe(floorCargo, floor.asker));
            Probe(floor, floorCargo, false, 0, false, 0);

            var competing = Scene("own-inventory-with-other-live-claim", 14);
            var competingCargo = PutInInventory(competing.asker, 10);
            var other = NewPawn(competing.cell + new IntVec3(2, 0, 1));
            PutInInventory(other, 4);
            BaselineCapacity(competing, competingCargo);
            Claim(competing.asker, competing, 10);
            Claim(other, competing, 4);
            Require("competing-claim-live", Others(competing) == 4, "ClaimedByOthersFor=" + Others(competing));
            Probe(competing, competingCargo, true, 10, false, 0);

            var missing = Scene("missing-inventory", 10);
            var unheld = NewCargo(10);
            BaselineCapacity(missing, unheld);
            var tracker = missing.asker.inventory;
            try
            {
                missing.asker.inventory = null;
                Probe(missing, unheld, false, 10, false, 10);
            }
            finally { missing.asker.inventory = tracker; }

            var missingHands = Scene("hands-with-missing-inventory", 10);
            var missingHandCargo = PutInHands(missingHands.asker, missingHands, 10);
            BaselineCapacity(missingHands, missingHandCargo);
            tracker = missingHands.asker.inventory;
            try
            {
                missingHands.asker.inventory = null;
                Claim(missingHands.asker, missingHands, 10);
                Probe(missingHands, missingHandCargo, true, 10, true, 10);
            }
            finally { missingHands.asker.inventory = tracker; }

            // Null controls deliberately avoid StoreUtility, whose public contract needs a real Thing.
            ProbeNull("null-pawn", null, ownCargo, inventory.group);
            ProbeNull("null-subject", inventory.asker, null, inventory.group);
            ProbeNull("both-null", null, null, inventory.group);
            Require("only-fixture-pawns", map.mapPawns.AllPawnsSpawned.Count == actors.Count,
                "spawned=" + map.mapPawns.AllPawnsSpawned.Count + "; fixture actors=" + actors.Count);
        }

        private StorageScene Scene(string id, int capacity)
        {
            int index = nextScene++;
            var cell = map.Center + new IntVec3(-18 + (index % 4) * 10, 0, -8 + (index / 4) * 10);
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            zone.settings.filter.SetDisallowAll();
            zone.settings.filter.SetAllow(cargoDef, true);
            zone.settings.Priority = StoragePriority.Critical;
            map.zoneManager.RegisterZone(zone);
            zone.AddCell(cell);
            SpawnCargo(cargoDef.stackLimit - capacity, cell);
            var group = zone.GetSlotGroup();
            Require(id + "-single-cell-group", group.CellsList.Count == 1 && group.CellsList[0] == cell
                && ReferenceEquals(map.haulDestinationManager.SlotGroupAt(cell), group), "cell=" + cell + "; capacity=" + capacity);
            Require(id + "-physical-capacity", cell.GetItemStackSpaceLeftFor(map, cargoDef) == capacity,
                "GetItemStackSpaceLeftFor=" + cell.GetItemStackSpaceLeftFor(map, cargoDef));
            return new StorageScene { id = id, cell = cell, group = group, capacity = capacity, asker = NewPawn(cell + new IntVec3(-2, 0, 0)) };
        }

        private Pawn NewPawn(IntVec3 cell)
        {
            Pawn pawn;
            Rand.PushState(10401 + actors.Count);
            try { pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); }
            finally { Rand.PopState(); }
            Require("actor-" + actors.Count, pawn != null && pawn.RaceProps.Humanlike && !pawn.Downed
                && pawn.inventory != null && pawn.carryTracker != null, "Generated ordinary human colonist.");
            pawn.inventory.innerContainer.ClearAndDestroyContents();
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            pawn.workSettings.DisableAll();
            GenSpawn.Spawn(pawn, cell, map);
            actors.Add(pawn);
            return pawn;
        }

        private Thing NewCargo(int count)
        {
            var thing = ThingMaker.MakeThing(cargoDef);
            thing.stackCount = count;
            return thing;
        }

        private Thing SpawnCargo(int count, IntVec3 cell) => GenSpawn.Spawn(NewCargo(count), cell, map);

        private Thing PutInInventory(Pawn pawn, int count)
        {
            var cargo = NewCargo(count);
            Require("inventory-transfer-" + cargo.ThingID, pawn.inventory.innerContainer.TryAdd(cargo, false), "Actual Verse ThingOwner.TryAdd; count=" + count);
            Require("inventory-custody-" + cargo.ThingID, !cargo.Spawned && cargo.stackCount == count
                && ReferenceEquals(cargo.holdingOwner, pawn.inventory.innerContainer)
                && ReferenceEquals(cargo.ParentHolder, pawn.inventory), Describe(cargo, pawn));
            var comp = pawn.AllComps.SingleOrDefault(x => x.GetType().FullName == "HaulersDream.CompHauledToInventory");
            Require("tag-component-" + pawn.ThingID, comp != null, "Actual injected CompHauledToInventory");
            var register = comp.GetType().GetMethod("RegisterHauledItem", new[] { typeof(Thing), typeof(int) });
            Require("tag-method", register != null, "RegisterHauledItem(Thing,int)");
            register.Invoke(comp, new object[] { cargo, 0 });
            HarnessSession.Event("storage-ownership-setup-transfer", Describe(cargo, pawn) + "; tagged via production RegisterHauledItem; not an executed hauling job");
            return cargo;
        }

        private Thing PutInHands(Pawn pawn, StorageScene scene, int count)
        {
            var cargo = NewCargo(count);
            Require("hand-transfer-" + cargo.ThingID, pawn.carryTracker.GetDirectlyHeldThings().TryAdd(cargo, false)
                && ReferenceEquals(pawn.carryTracker.CarriedThing, cargo), "Actual carry tracker ThingOwner.TryAdd");
            var job = JobMaker.MakeJob(JobDefOf.HaulToCell, cargo, scene.cell);
            job.count = count;
            // StorageEvidence intentionally counts hands only for real storage-bound JobDefs. Install
            // that adapter input explicitly; do not call StartJob, execute toils or imply driver evidence.
            pawn.jobs.curJob = job;
            HarnessSession.Event("storage-ownership-setup-job", "pawn=" + pawn.ThingID + "; fixture-only CurJob=" + job.def.defName
                + "; targetA=" + cargo.ThingID + "; targetB=" + scene.cell + "; count=" + count + "; no driver/toils started");
            return cargo;
        }

        private void BaselineCapacity(StorageScene scene, Thing subject)
        {
            int before = Free(scene.asker, scene.group, subject, out bool truncated);
            bool allowed = StoreUtility.IsGoodStoreCell(scene.cell, map, subject, scene.asker, scene.asker.Faction);
            Require(scene.id + "-unclaimed-capacity", before == scene.capacity && !truncated && allowed,
                "FreeUnitsFor before this group's claims=" + before + "; truncated=" + truncated + "; patched StoreUtility=" + allowed);
        }

        private void Claim(Pawn pawn, StorageScene scene, int count)
        {
            bool accepted = (bool)tryCommit.Invoke(null, new object[] { pawn, scene.group, cargoDef, count, "runtime-fixture" });
            if (accepted) claims.Add(Tuple.Create(pawn, (ISlotGroup)scene.group));
            Require(scene.id + "-claim-" + pawn.ThingID, accepted, "Actual StorageCommitments.TryCommit; units=" + count);
            int evidence = (int)unitsMoving.Invoke(null, new object[] { pawn, cargoDef });
            Require(scene.id + "-cargo-evidence-" + pawn.ThingID, evidence == count,
                "Production UnitsMovingOf=" + evidence + "; committed=" + count + "; currentJob=" + pawn.CurJob?.def?.defName);
            HarnessSession.Event("storage-ownership-live-claim", "scene=" + scene.id + "; pawn=" + pawn.ThingID + "; units=" + count + "; liveEvidence=" + evidence);
        }

        private int Others(StorageScene scene) => (int)claimedByOthers.Invoke(null, new object[] { scene.asker, scene.group, cargoDef });

        private int Free(Pawn pawn, ISlotGroup group, Thing subject, out bool truncated)
        {
            object[] args = { pawn, group, cargoDef, subject, false };
            int free = (int)freeUnits.Invoke(null, args);
            truncated = (bool)args[4];
            return free;
        }

        private void Probe(StorageScene scene, Thing subject, bool correctedDelivery, int correctedFree, bool baselineDelivery, int baselineFree)
        {
            bool actualDelivery = (bool)delivering.Invoke(null, new object[] { scene.asker, subject });
            int actualFree = Free(scene.asker, scene.group, subject, out bool truncated);
            bool gate = StoreUtility.IsGoodStoreCell(scene.cell, map, subject, scene.asker, scene.asker.Faction);
            Require(scene.id + "-complete-measurement", !truncated, "Single-cell measurement; truncated=" + truncated);
            AddObservation(scene.id, subject, scene.asker, actualDelivery, actualFree, gate,
                correctedDelivery, correctedFree, baselineDelivery, baselineFree);
        }

        private void ProbeNull(string id, Pawn pawn, Thing subject, ISlotGroup group)
        {
            bool actualDelivery = (bool)delivering.Invoke(null, new object[] { pawn, subject });
            int actualFree = Free(pawn, group, subject, out bool truncated);
            Require(id + "-no-truncation", !truncated, "Invalid adapter inputs remain explicitly unmeasured.");
            AddObservation(id, subject, pawn, actualDelivery, actualFree, null, false, int.MaxValue, false, int.MaxValue);
        }

        private void AddObservation(string id, Thing subject, Pawn pawn, bool actualDelivery, int actualFree, bool? gate,
            bool correctedDelivery, int correctedFree, bool baselineDelivery, int baselineFree)
        {
            var row = new StorageOwnershipObservation
            {
                id = id, tick = Find.TickManager.TicksGame, custody = Describe(subject, pawn),
                actualDelivering = actualDelivery, actualFreeUnits = actualFree, actualStoreCellAllowed = gate,
                correctedDelivering = correctedDelivery, correctedFreeUnits = correctedFree,
                baselineDelivering = baselineDelivery, baselineFreeUnits = baselineFree,
                correctedMatched = actualDelivery == correctedDelivery && actualFree == correctedFree && (!gate.HasValue || gate.Value == (correctedFree > 0)),
                baselineMatched = actualDelivery == baselineDelivery && actualFree == baselineFree && (!gate.HasValue || gate.Value == (baselineFree > 0))
            };
            result.observations.Add(row);
            Record("behavior", id, row.correctedMatched, Json.Stringify(row));
            HarnessSession.Event("storage-ownership-observation", Json.Stringify(row));
        }

        private static string Describe(Thing subject, Pawn pawn) => "pawn=" + (pawn?.ThingID ?? "null")
            + "; subject=" + (subject?.ThingID ?? "null") + "; spawned=" + subject?.Spawned
            + "; holdingOwnerType=" + (subject?.holdingOwner?.GetType().FullName ?? "null")
            + "; ParentHolderType=" + (subject?.ParentHolder?.GetType().FullName ?? "null")
            + "; holdingOwnerIsOwnInventory=" + (subject != null && pawn?.inventory != null && ReferenceEquals(subject.holdingOwner, pawn.inventory.innerContainer))
            + "; ParentHolderIsOwnTracker=" + (subject != null && pawn?.inventory != null && ReferenceEquals(subject.ParentHolder, pawn.inventory))
            + "; isOwnHands=" + (subject != null && pawn?.carryTracker != null && ReferenceEquals(subject, pawn.carryTracker.CarriedThing));

        private Type FindType(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(name, false)).FirstOrDefault(x => x != null);
            Require("type-" + name, type != null, name);
            return type;
        }

        private MethodInfo Method(Type type, string name, params Type[] signature)
        {
            var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, signature, null);
            Require("method-" + name, method != null, type.FullName + "." + name);
            return method;
        }

        private void Require(string id, bool passed, string observed)
        {
            Record("fixture", id, passed, observed);
            if (!passed) throw new InvalidDataException("Storage fixture precondition failed: " + id + "; " + observed);
        }

        private void Record(string kind, string id, bool passed, string observed)
        {
            result.assertions.Add(new StorageOwnershipAssertion { kind = kind, id = id, passed = passed, observed = observed });
            HarnessSession.Check("storage-ownership-" + kind + "-" + id, passed, observed);
        }

        private sealed class StorageScene
        {
            internal string id { get; set; }
            internal IntVec3 cell { get; set; }
            internal SlotGroup group { get; set; }
            internal int capacity { get; set; }
            internal Pawn asker { get; set; }
        }
    }

    [DataContract]
    internal sealed class StorageOwnershipResult
    {
        [DataMember] public string caseId { get; set; }
        [DataMember] public string expectedBehavior { get; set; }
        [DataMember] public string scope { get; set; }
        [DataMember] public int startedTick { get; set; }
        [DataMember] public int finishedTick { get; set; }
        [DataMember] public string adapterAssembly { get; set; }
        [DataMember] public string adapterModuleId { get; set; }
        [DataMember] public bool fixtureValid { get; set; }
        [DataMember] public bool requestedBehaviorSatisfied { get; set; }
        [DataMember] public bool expectationMatched { get; set; }
        [DataMember] public string status { get; set; }
        [DataMember] public string error { get; set; }
        [DataMember] public List<StorageOwnershipAssertion> assertions { get; set; }
        [DataMember] public List<StorageOwnershipObservation> observations { get; set; }
    }

    [DataContract]
    internal sealed class StorageOwnershipAssertion
    {
        [DataMember] public string kind { get; set; }
        [DataMember] public string id { get; set; }
        [DataMember] public bool passed { get; set; }
        [DataMember] public string observed { get; set; }
    }

    [DataContract]
    internal sealed class StorageOwnershipObservation
    {
        [DataMember] public string id { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public string custody { get; set; }
        [DataMember] public bool actualDelivering { get; set; }
        [DataMember] public int actualFreeUnits { get; set; }
        [DataMember] public bool? actualStoreCellAllowed { get; set; }
        [DataMember] public bool correctedDelivering { get; set; }
        [DataMember] public int correctedFreeUnits { get; set; }
        [DataMember] public bool baselineDelivering { get; set; }
        [DataMember] public int baselineFreeUnits { get; set; }
        [DataMember] public bool correctedMatched { get; set; }
        [DataMember] public bool baselineMatched { get; set; }
    }
}
