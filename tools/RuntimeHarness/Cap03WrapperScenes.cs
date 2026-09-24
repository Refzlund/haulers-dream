using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    internal sealed partial class Cap03WrapperScenario
    {
        private void RunScene(string id, bool resident, bool asf, IntVec3 anchor)
        {
            var s = new Scene { row = new Cap03WrapperScene { id = id, resident = resident, asf = asf, startedTick = Find.TickManager.TicksGame,
                zonesBefore = map.zoneManager.AllZones.Count, groupsBefore = map.storageGroups.StorageGroupsForReading.Count } };
            result.scenes.Add(s.row);
            try
            {
                // MakeMinified normally despawns the real inner and places it in a native owner.
                MakeWrapper(s, anchor + new IntVec3(-3, 0, 4)); MakeWrapper(s, anchor + new IntVec3(-1, 0, 4));
                s.steel = Own(s, ThingMaker.MakeThing(ThingDefOf.Steel)); s.steel.stackCount = 1;
                SpawnExact(s.steel, anchor + new IntVec3(1, 0, 4));
                if (!resident && !asf)
                {
                    s.zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager); zones.Add(s.zone);
                    map.zoneManager.RegisterZone(s.zone); s.zone.AddCell(anchor); s.parent = s.zone;
                }
                else s.parent = MakeDestination(s, asf ? "sbz_SmallCrate" : "Shelf", anchor);
                s.parents.Add(s.parent); s.cell = s.parent.AllSlotCells().First(); s.positiveParent = s.parent; s.positiveCell = s.cell;
                if (resident)
                {
                    if (asf) { s.positiveParent = MakeDestination(s, "sbz_SmallCrate", anchor + new IntVec3(5, 0, 0)); s.parents.Add(s.positiveParent); s.positiveCell = s.positiveParent.AllSlotCells().Single(); }
                    else { var cells = s.parent.AllSlotCells().ToList(); Require(id + "-two-native-shelf-cells", cells.Count == 2, "Native Shelf supplies a matched empty second cell."); s.positiveCell = cells[1]; }
                }
                foreach (var parent in s.parents)
                {
                    s.footprints.Add(parent, parent.AllSlotCells().ToList());
                    var settings = parent.GetStoreSettings(); settings.filter.SetDisallowAll();
                    settings.filter.SetAllow(ThingDefOf.Steel, true); settings.filter.SetAllow(innerDef, true);
                    settings.filter.AllowedHitPointsPercents = new FloatRange(0.5f, 1f); settings.Priority = StoragePriority.Critical;
                    Require(id + "-effective-filter-" + Key(parent), settings.filter.GetType() == typeof(ThingFilter) && settings.filter.CaresAboutHitPoints
                        && settings.filter.Allows(innerDef) && settings.filter.Allows(ThingDefOf.Steel) && ((List<SpecialThingFilterDef>)specials.GetValue(settings.filter)).Count == 0,
                        "Actual native effective filter permits both explicit defs and uses a nondefault HP range; no unreviewed special worker.");
                }
                if (resident)
                {
                    s.wrappers[0].DeSpawn(); SpawnExact(s.wrappers[0], s.cell);
                }
                s.row.parentKey = Key(s.parent); s.row.positiveParentKey = Key(s.positiveParent); s.row.cell = s.cell.ToString(); s.row.positiveCell = s.positiveCell.ToString();
                s.row.beforeProtected = NeutralSnapshot(s, "physical-before"); ValidateScene(s);
                s.row.protectedBegin = Counters(s); Capture("protected-begin", id, s.row.protectedBegin);
                foreach (var parent in s.parents) { Prepare(s, parent, false); Prepare(s, parent, true); }
                Trial(s, s.parent, s.cell, resident ? s.steel : (Thing)s.wrappers[0], resident ? "resident-boundary" : "incoming-boundary");
                Trial(s, s.positiveParent, s.positiveCell, s.steel, "ordinary-positive");
                s.row.protectedEnd = Counters(s); Capture("protected-end", id, s.row.protectedEnd);
                s.row.afterProtected = NeutralSnapshot(s, "physical-after-protected");
                Check(id + "-protected-counters", Same(s.row.protectedBegin, s.row.protectedEnd), "No inner Stack or virtual HP getter invocation in preparation/projection/disposal.");
                Check(id + "-protected-physical", Same(s.row.beforeProtected, s.row.afterProtected), "Full outer/inner custody, filters, grid and provider registry remain unchanged.");
                NativeControls(s);
                s.row.nativeEnd = Counters(s); Capture("native-end", id, s.row.nativeEnd);
                s.row.afterNative = NeutralSnapshot(s, "physical-after-native");
                Check(id + "-native-physical", Same(s.row.beforeProtected, s.row.afterNative), "Explicit counter-positive controls leave physical/filter state unchanged.");
                s.row.completed = true;
            }
            catch (Exception error) { s.row.error = error.ToString(); Capture("scene-exception", id, s.row.error); throw; }
            finally
            {
                bool restored = true;
                // Retire wrappers before destinations, preserving native deregistration.
                // Their owners destroy inners; the later inner entries are skipped when destroyed.
                foreach (var thing in s.wrappers.Cast<Thing>().Reverse().Concat(s.owned.AsEnumerable().Reverse().Where(t => !(t is MinifiedThing))))
                {
                    try { if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); s.row.retiredThingIds.Add(thing.thingIDNumber); }
                    catch (Exception error) { restored = false; s.row.error = (s.row.error ?? "") + "\nRetirement: " + error; }
                }
                if (s.zone != null)
                    try { if (map.zoneManager.AllZones.Contains(s.zone)) s.zone.Delete(false); }
                    catch (Exception error) { restored = false; s.row.error = (s.row.error ?? "") + "\nZone retirement: " + error; }
                foreach (var parent in s.parents)
                {
                    try
                    {
                        if (!s.footprints.TryGetValue(parent, out var cells)) { restored = false; continue; }
                        var row = new Cap03WrapperRetirement { parentKey = Key(parent), cells = cells.Select(c => c.ToString()).ToList(),
                            slotsCleared = cells.All(c => !ReferenceEquals(map.haulDestinationManager.SlotGroupAt(c), parent.GetSlotGroup())),
                            zoneRegistered = parent is Zone zone && map.zoneManager.AllZones.Contains(zone) };
                        if (parent.GetType() == api.AsfParentType)
                            row.asfCount = api.Registry((Building_Storage)parent, cells[0], s.wrappers[0], s.steel).count;
                        s.row.retiredDestinations.Add(row);
                        restored &= row.slotsCleared && !row.zoneRegistered && (!row.asfCount.HasValue || row.asfCount == 0);
                    }
                    catch (Exception error) { restored = false; s.row.error = (s.row.error ?? "") + "\nRetirement census: " + error; }
                }
                s.row.zonesAfter = map.zoneManager.AllZones.Count; s.row.groupsAfter = map.storageGroups.StorageGroupsForReading.Count;
                s.row.restored = restored && s.owned.All(t => t.Destroyed && !t.Spawned) && s.wrappers.All(w => w.GetDirectlyHeldThings().Count == 0)
                    && s.row.zonesAfter == s.row.zonesBefore && s.row.groupsAfter == s.row.groupsBefore
                    && s.row.retiredDestinations.Count == s.parents.Count;
                s.row.retirementEnd = Counters(s); s.row.finishedTick = Find.TickManager.TicksGame;
                Check(id + "-retired", s.row.restored, "All scene Things and native wrapper owners retired, native zone/group/slot registrations restored.");
                Capture("scene-result", id, s.row);
            }
        }

        private T Own<T>(Scene s, T thing) where T : Thing { owned.Add(thing); s.owned.Add(thing); return thing; }
        private void SpawnExact(Thing thing, IntVec3 cell)
        {
            var actual = GenSpawn.Spawn(thing, cell, map, Rot4.North);
            Require("spawn-" + thing.thingIDNumber + "-" + cell, ReferenceEquals(actual, thing) && thing.Spawned && thing.Position == cell && ReferenceEquals(thing.ParentHolder, map), "Native spawn preserves exact object/map/cell ownership.");
        }
        private Building_Storage MakeDestination(Scene s, string name, IntVec3 cell)
        {
            var def = DefDatabase<ThingDef>.GetNamed(name);
            var building = Own(s, (Building_Storage)ThingMaker.MakeThing(def, ThingDefOf.WoodLog)); SpawnExact(building, cell); building.SetFaction(Faction.OfPlayer);
            Require(s.row.id + "-destination-" + building.thingIDNumber, building.GetType() == (s.row.asf ? api.AsfParentType : typeof(Building_Storage)) && building.storageGroup == null
                && def.modContentPack.PackageId.Equals(s.row.asf ? "sbz.NeatStorage" : "ludeon.rimworld", StringComparison.OrdinalIgnoreCase), "Actual unlinked installed Neat or native Shelf class/definition.");
            return building;
        }
        private void MakeWrapper(Scene s, IntVec3 cell)
        {
            var inner = Own(s, (Cap03WrapperInner)ThingMaker.MakeThing(innerDef)); s.inners.Add(inner); inner.stackCount = 1; inner.SetFaction(Faction.OfPlayer);
            // Native stat computation is deliberately outside any protected/census boundary.
            maximumHitPoints.Add(inner, inner.MaxHitPoints); SpawnExact(inner, cell);
            Require(s.row.id + "-healthy-inner-" + inner.thingIDNumber, maximumHitPoints[inner] == 100 && inner.RawHitPoints == maximumHitPoints[inner], "Normally initialized full HP and count1; no clamp or native field seeding.");
            var receipt = new Cap03WrapperMinify { before = ThingState(inner), countersBefore = Counters(s) };
            var wrapper = inner.MakeMinified();
            if (wrapper == null) throw new InvalidOperationException("Normal MakeMinified returned null.");
            Own(s, wrapper); s.wrappers.Add(wrapper); SpawnExact(wrapper, cell);
            receipt.after = WrapperState(wrapper, inner); receipt.countersAfter = Counters(s); s.row.minifications.Add(receipt); Capture("minification", s.row.id, receipt);
            Require(s.row.id + "-native-inner-custody-" + inner.thingIDNumber, receipt.after.sameInner && receipt.after.directContainsInner && receipt.after.innerOwnerMatches
                && receipt.after.innerParentIsOuter && receipt.after.directCount == 1 && receipt.after.outer.count == 1 && receipt.after.outer.stackLimit == 1
                && receipt.after.inner.count == 1 && !receipt.after.inner.spawned && receipt.after.innerMapHeldId == map.uniqueID,
                "Native one-stack owner contains this exact original inner after normal minification.");
        }
        private void ValidateScene(Scene s)
        {
            var state = s.row.beforeProtected;
            Require(s.row.id + "-two-full-wrappers", state.wrappers.Count == 2 && state.wrappers.All(w => w.outerType == typeof(MinifiedThing).FullName && w.innerType == typeof(Cap03WrapperInner).FullName
                && w.outer.count == w.outer.stackLimit && w.outer.count == 1 && w.inner.count == 1 && w.sameInner && w.directContainsInner && w.directCount == 1
                && w.innerOwnerMatches && w.innerParentIsOuter && !w.inner.spawned && w.innerMapHeldId == map.uniqueID && w.innerSpawnedOrParentSpawned), "Actual full native wrappers with independent bound inner identities/custody.");
            foreach (var d in state.destinations)
            {
                bool main = d.key == Key(s.parent); var address = main ? s.cell : s.positiveCell;
                var grid = d.grids.Single(x => x.cell == address.ToString()); int expectedItems = main && s.row.resident ? 1 : 0;
                Require(s.row.id + "-physical-destination-" + d.key, d.registered && d.groupIdentity == null && d.effective.ownerIdentity == d.identity
                    && grid.nativeMaximum == (s.row.asf ? 6 : s.zone != null ? 1 : 3)
                    && grid.things.Count == expectedItems + (d.thing == null ? 0 : 1) && d.effective.contents.allowedDefs.Contains(innerDef.defName)
                    && d.declaredFixed.contents.allowedDefs.Contains("Steel") && d.interfaceFixed.contents.allowedDefs.Contains("Steel"), "Exact real grid/capacity/filter/parent/slot identities before measurement.");
                Require(s.row.id + "-inner-fixed-semantics-" + d.key, d.declaredFixed.contents.allowedDefs.Contains(innerDef.defName) == (s.row.asf || !s.row.resident)
                    && d.interfaceFixed.contents.allowedDefs.Contains(innerDef.defName) == (s.row.asf || !s.row.resident), "Native Shelf intentionally excludes the inner Building; stockpile/Neat accept its storable category.");
                if (s.row.asf)
                    Require(s.row.id + "-nonfull-asf-" + d.key, d.registry.count == expectedItems && d.registry.cellCount == expectedItems && d.registry.cellWiseCount == expectedItems
                        && d.registry.slotLimit == 6 && d.registry.anyFree && !d.registry.packed && !d.registry.performanceFish
                        && d.registry.members.All(m => m.contains && m.indexOf == m.index && m.memberValid && m.targetValid && m.storingParentId == d.thing.thingId), "Normally registered valid non-full ASF collection; no full-member preflight masks the resident boundary.");
            }
            Require(s.row.id + "-correct-parcel-locations", ActorIdle() && s.steel.Spawned && s.steel.stackCount == 1 && !s.parents.Any(p => p.AllSlotCells().Contains(s.steel.Position))
                && (s.row.resident ? s.wrappers[0].Position == s.cell : s.wrappers[0].Position.ToString() == s.row.minifications[0].after.outer.cell
                    && !s.parents.Any(p => p.AllSlotCells().Contains(s.wrappers[0].Position)))
                && !s.parents.Any(p => p.AllSlotCells().Contains(s.wrappers[1].Position))
                && map.thingGrid.ThingsListAt(s.positiveCell).All(t => t.def.category != ThingCategory.Item), "External ordinary/peer parcels, selected resident placement and a genuinely empty matched positive cell.");
        }
    }
}
