using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    internal sealed partial class Cap03WrapperScenario
    {
        private List<Cap03WrapperCounter> Counters(Scene s) => s.inners.Select(inner => new Cap03WrapperCounter
        { innerId = inner.thingIDNumber, identity = Id(inner), stackCalls = inner.StackCalls, hitPointReads = inner.HitPointReads }).ToList();
        private ProjectionFixtureThing ThingState(Thing thing) => new ProjectionFixtureThing
        {
            thingId = thing.thingIDNumber, def = thing.def.defName, count = thing.stackCount, stackLimit = thing.def.stackLimit,
            cell = thing.Position.ToString(), spawned = thing.Spawned, destroyed = thing.Destroyed, mapId = thing.Map?.uniqueID,
            heldByFixtureMap = ReferenceEquals(thing.ParentHolder, map), holderType = thing.ParentHolder?.GetType().FullName,
            faction = thing.Faction?.GetUniqueLoadID(), stuff = thing.Stuff?.defName,
            // Direct base-backed accessor is essential: generic Thing.HitPoints would perturb the counter.
            hitPoints = thing is Cap03WrapperInner inner ? inner.RawHitPoints : thing.HitPoints
        };
        private Cap03WrapperState WrapperState(MinifiedThing wrapper, Cap03WrapperInner inner)
        {
            var owner = wrapper.GetDirectlyHeldThings();
            return new Cap03WrapperState { outer = ThingState(wrapper), inner = ThingState(inner), outerIdentity = Id(wrapper), innerIdentity = Id(inner),
                ownerIdentity = Id(owner), innerHoldingOwnerIdentity = Id(inner.holdingOwner), innerParentIdentity = Id(inner.ParentHolder), innerParentType = inner.ParentHolder?.GetType().FullName,
                outerType = wrapper.GetType().FullName, innerType = inner.GetType().FullName, innerDefPackage = inner.def.modContentPack.PackageId,
                sameInner = ReferenceEquals(wrapper.InnerThing, inner), directContainsInner = owner.Contains(inner), directCount = owner.Count, directIds = owner.Select(x => x.thingIDNumber).ToList(),
                innerOwnerMatches = ReferenceEquals(inner.holdingOwner, owner), innerParentIsOuter = ReferenceEquals(inner.ParentHolder, wrapper),
                innerMapHeldId = inner.MapHeld?.uniqueID, innerSpawnedOrParentSpawned = inner.SpawnedOrAnyParentSpawned,
                setupMaximumHitPoints = maximumHitPoints[inner], outerComps = wrapper.AllComps.Select(x => x.GetType().FullName).ToList(),
                innerComps = inner.AllComps.Select(x => x.GetType().FullName).ToList() };
        }
        private Cap03FilterSettings Settings(StorageSettings settings) => new Cap03FilterSettings
        {
            settingsIdentity = Id(settings), filterIdentity = Id(settings.filter), ownerIdentity = Id(settings.owner), ownerType = settings.owner?.GetType().FullName,
            priority = settings.Priority.ToString(), contents = new ProjectionFixtureFilter { type = settings.filter.GetType().FullName, onlySpecial = settings.filter.OnlySpecialFilters,
                allowedDefs = settings.filter.AllowedThingDefs.Select(x => x.defName).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                disallowedSpecials = ((List<SpecialThingFilterDef>)specials.GetValue(settings.filter)).Select(x => x?.defName).ToList(),
                hitPointsMin = settings.filter.AllowedHitPointsPercents.min, hitPointsMax = settings.filter.AllowedHitPointsPercents.max,
                mentalBreakMin = settings.filter.AllowedMentalBreakChance.min, mentalBreakMax = settings.filter.AllowedMentalBreakChance.max, qualities = settings.filter.AllowedQualityLevels.ToString() }
        };
        private Cap03WrapperSnapshot NeutralSnapshot(Scene s, string stage)
        {
            var before = Counters(s); Capture("census-begin", s.row.id + "/" + stage, before);
            var snapshot = new Cap03WrapperSnapshot { tick = Find.TickManager.TicksGame, mapId = map.uniqueID, actor = ThingState(actor), steel = ThingState(s.steel), actorIdle = ActorIdle(),
                wrappers = s.wrappers.Select((w, i) => WrapperState(w, s.inners[i])).ToList(), destinations = new List<Cap03WrapperDestination>() };
            foreach (var parent in s.parents)
            {
                var cells = parent.AllSlotCells().ToList(); var building = parent as Building_Storage; bool asf = building?.GetType() == api.AsfParentType;
                var registry = asf ? api.Registry(building, cells[0], s.wrappers[0], s.steel) : null;
                var declared = asf ? api.DeclaredFixed(building) : parent.GetParentStoreSettings();
                var fixedInterface = parent is IStorageGroupMember member ? member.ParentStoreSettings : parent.GetParentStoreSettings();
                snapshot.destinations.Add(new Cap03WrapperDestination { key = Key(parent), identity = Id(parent), runtimeType = parent.GetType().FullName,
                    slotIdentity = Id(parent.GetSlotGroup()), groupIdentity = Id(building?.storageGroup), registryIdentity = asf ? Id(api.RegistryIdentity(building)) : null,
                    defPackage = building?.def.modContentPack.PackageId, thing = building == null ? null : ThingState(building),
                    registered = cells.All(c => ReferenceEquals(map.haulDestinationManager.SlotGroupAt(c), parent.GetSlotGroup())),
                    zoneRegistered = parent is Zone zone && map.zoneManager.AllZones.Contains(zone), cells = cells.Select(c => c.ToString()).ToList(),
                    compTypes = building?.AllComps.Select(x => x.GetType().FullName).ToList() ?? new List<string>(),
                    effective = Settings(parent.GetStoreSettings()), declaredFixed = Settings(declared), interfaceFixed = Settings(fixedInterface), registry = registry,
                    grids = cells.Select(c => new Cap03WrapperGrid { cell = c.ToString(), nativeMaximum = c.GetMaxItemsAllowedInCell(map),
                        things = map.thingGrid.ThingsListAt(c).Select(ThingState).ToList() }).ToList() });
            }
            var after = Counters(s); Capture("census-end", s.row.id + "/" + stage, after);
            Require(s.row.id + "-neutral-" + stage, Same(before, after), "Census must not invoke either measured inner callback.");
            Capture(stage, s.row.id, snapshot); return snapshot;
        }
    }
}
