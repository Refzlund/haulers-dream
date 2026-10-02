using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Stage A only. All game reads below are fields, exact BCL lists, or runtime
    // type identity. In particular no MapHeld, ParentHolder, SpawnedParentOrMe,
    // InnerThing, HitPoints, MaxHitPoints, GetComp or gameplay predicate is called.
    internal sealed class ProjectionSubjectReader
    {
        private readonly StorageProjectionEnvironment environment;
        private readonly Game game;
        private readonly Guid session;
        private readonly long generation;
        private readonly ProjectionSubjectLimits limits;
        private readonly SubjectFields fields;

        // GuardChecks counts direct scalar/reference inspection and bounded list
        // visits. Fixed envelopes include branches, field reads and comparisons;
        // indexed owner/grid entries cost four each, component entries cost 32
        // plus the explicit definition-membership/duplicate scans. Preparation
        // counts metadata bindings; OutputRecords counts retained facts/seal entries.
        // There are no hidden native/filter/stat/stack calls in these envelopes.
        private const int LifetimeChecks = 12, ThingChecks = 48, MapChecks = 40;
        private const int OwnerChecks = 12, HolderChecks = 16, WrapperChecks = 20;
        private const int ComponentHeaderChecks = 12, ComponentChecks = 32;

        private ProjectionSubjectReader(StorageProjectionEnvironment environment, ProjectionSubjectLimits limits, SubjectFields fields)
        {
            this.environment = environment; game = environment.Game; session = environment.SessionId;
            generation = environment.CatalogGeneration; this.limits = limits; this.fields = fields;
        }

        internal static ProjectionSubjectResult TryCreate(StorageProjectionEnvironment environment,
            ProjectionSubjectLimits limits, ProjectionWorkBudget work, out ProjectionSubjectReader reader)
        {
            reader = null;
            if (environment == null || limits == null || work == null || environment.Game == null || environment.SessionId == Guid.Empty)
                return ProjectionSubjectResult.InvalidRequest;
            if (environment.MainThreadId <= 0 || Thread.CurrentThread.ManagedThreadId != environment.MainThreadId)
                return ProjectionSubjectResult.WrongThread;
            try
            {
                if (!work.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.Preparation, SubjectFields.BindingCount)
                    .Plus(ProjectionWork.Cost(ProjectionWorkKind.OutputRecords, SubjectFields.BindingCount + 1))))
                    return ProjectionSubjectResult.WorkLimit;
                var candidate = new ProjectionSubjectReader(environment, limits, new SubjectFields());
                candidate.CheckLifetime(work);
                reader = candidate;
                return ProjectionSubjectResult.Bound;
            }
            catch (ProjectionSubjectStop stop) { return stop.Result; }
            catch (ArgumentException) { return ProjectionSubjectResult.BindingFault; }
            catch (MemberAccessException) { return ProjectionSubjectResult.BindingFault; }
            catch (OverflowException) { return ProjectionSubjectResult.StateLimit; }
        }

        internal ProjectionSubjectResult TryBindSubject(Thing physical, ProjectionCustodyExpectation expected,
            ProjectionWorkBudget work, out ProjectionSubjectBinding binding)
        {
            binding = null;
            if (work == null) return ProjectionSubjectResult.InvalidRequest;
            try
            {
                var state = Capture(physical, expected, work, out var outer, out var filter,
                    out var outerQuality, out var outerQualityValue, out var filterQuality, out var filterQualityValue);
                SubjectStateBuilder.Charge(work, ProjectionWorkKind.OutputRecords, 1);
                binding = new ProjectionSubjectBinding(this, expected, state, outer, filter,
                    outerQuality, outerQualityValue, filterQuality, filterQualityValue);
                return ProjectionSubjectResult.Bound;
            }
            catch (ProjectionSubjectStop stop) { return stop.Result; }
            catch (ArgumentException) { return ProjectionSubjectResult.BindingFault; }
            catch (MemberAccessException) { return ProjectionSubjectResult.BindingFault; }
            catch (OverflowException) { return ProjectionSubjectResult.StateLimit; }
        }

        internal ProjectionSubjectResult Recheck(Thing physical, ProjectionCustodyExpectation expected,
            ProjectionSubjectState original, ProjectionWorkBudget work)
        {
            if (work == null) return ProjectionSubjectResult.InvalidRequest;
            try
            {
                CheckLifetime(work);
                original.CheckLists(work);
                var current = Capture(physical, expected, work, out _, out _, out _, out _, out _, out _);
                original.CheckLists(work);
                return original.Matches(current, work) ? ProjectionSubjectResult.Bound : ProjectionSubjectResult.Changed;
            }
            catch (ProjectionSubjectStop stop) { return stop.Result; }
            catch (ArgumentException) { return ProjectionSubjectResult.BindingFault; }
            catch (MemberAccessException) { return ProjectionSubjectResult.BindingFault; }
            catch (OverflowException) { return ProjectionSubjectResult.StateLimit; }
        }

        private void CheckLifetime(ProjectionWorkBudget work)
        {
            SubjectStateBuilder.Charge(work, ProjectionWorkKind.GuardChecks, LifetimeChecks);
            Require(environment.MainThreadId > 0 && Thread.CurrentThread.ManagedThreadId == environment.MainThreadId,
                ProjectionSubjectResult.WrongThread);
            Require(ReferenceEquals(environment.Game, game) && environment.SessionId == session
                && environment.CatalogGeneration == generation && ReferenceEquals(fields.CurrentGame.GetValue(null), game),
                ProjectionSubjectResult.LifetimeChanged);
            Require(!environment.TransferInProgress, ProjectionSubjectResult.TransferInProgress);
            // This direct-field reader needs no Unity API. The supplied owner must
            // establish the main thread/session and advance its generation; these
            // integers do not discover later Harmony/definition changes themselves.
        }

        private ProjectionSubjectState Capture(Thing physical, ProjectionCustodyExpectation expected, ProjectionWorkBudget work,
            out ProjectionSubjectFacts outer, out ProjectionSubjectFacts filter,
            out CompQuality outerQuality, out QualityCategory? outerQualityValue,
            out CompQuality filterQuality, out QualityCategory? filterQualityValue)
        {
            CheckLifetime(work);
            Require(physical != null && expected != null && expected.Map != null && expected.Faction != null,
                ProjectionSubjectResult.InvalidRequest);
            Require(expected.Custody >= ProjectionSubjectCustody.Floor && expected.Custody <= ProjectionSubjectCustody.Carry,
                ProjectionSubjectResult.InvalidRequest);
            Require(expected.Custody == ProjectionSubjectCustody.Floor ? expected.SourceHolder == null : expected.SourceHolder != null,
                ProjectionSubjectResult.InvalidRequest);
            SubjectStateBuilder.Charge(work, ProjectionWorkKind.OutputRecords, 1);
            var state = new SubjectStateBuilder(work, limits.StateRecords);
            state.Ref(expected.Map); state.Ref(expected.SourceHolder); state.Ref(expected.Carrier); state.Ref(expected.Faction);
            state.Value((int)expected.Custody);
            outer = Facts(physical, state);
            Require(outer.Def.category == ThingCategory.Item, ProjectionSubjectResult.InvalidSubject);
            Require(outer.Size.x == 1 && outer.Size.z == 1, ProjectionSubjectResult.UnsupportedFootprint);
            Require(outer.Faction == null || ReferenceEquals(outer.Faction, expected.Faction), ProjectionSubjectResult.InvalidCustody);

            // Validate complete outer custody before reading the wrapper container.
            if (expected.Custody == ProjectionSubjectCustody.Floor) Floor(outer, expected.Map, state);
            else
            {
                var source = PawnOnMap(expected.SourceHolder, expected, state);
                Distinct(outer, source);
                Held(outer, expected, state);
            }
            if (expected.Carrier != null)
            {
                var carrier = PawnOnMap(expected.Carrier, expected, state);
                Distinct(outer, carrier);
                if (expected.SourceHolder != null && !ReferenceEquals(expected.SourceHolder, expected.Carrier))
                    Require(expected.SourceHolder.thingIDNumber != carrier.Id, ProjectionSubjectResult.InvalidCustody);
            }

            filter = outer;
            if (physical is MinifiedThing)
            {
                state.Guards(WrapperChecks);
                Require(physical.GetType() == typeof(MinifiedThing), ProjectionSubjectResult.UnsupportedWrapper);
                Require(outer.Count == 1 && outer.StackLimit == 1, ProjectionSubjectResult.UnsupportedWrapperQuantity);
                var container = (ThingOwner)fields.MinifiedContainer.GetValue(physical);
                var children = Owner(container, physical, state, out int maxStacks);
                Require(maxStacks == 1 && children.Count == 1, ProjectionSubjectResult.InvalidInner);
                state.Guards(4);
                var inner = children[0];
                Require(inner != null && !ReferenceEquals(inner, physical) && !(inner is MinifiedThing), ProjectionSubjectResult.InvalidInner);
                // This is a structural Building profile, including subclasses whose
                // predicates Stage B must classify. Plants/other inners are unfinished.
                Require(inner is Building, ProjectionSubjectResult.UnsupportedInnerSubject);
                filter = Facts(inner, state);
                Require(filter.Def.category == ThingCategory.Building && filter.MapState == -1 && filter.Count == 1
                    && ReferenceEquals(filter.Owner, container) && ReferenceEquals(filter.Def.minifiedDef, outer.Def),
                    ProjectionSubjectResult.InvalidInner);
                Distinct(outer, filter);
                Require((expected.SourceHolder == null || filter.Id != expected.SourceHolder.thingIDNumber)
                    && (expected.Carrier == null || filter.Id != expected.Carrier.thingIDNumber), ProjectionSubjectResult.InvalidInner);
            }
            Components(outer, state, out outerQuality, out outerQualityValue);
            if (ReferenceEquals(outer, filter)) { filterQuality = outerQuality; filterQualityValue = outerQualityValue; }
            else Components(filter, state, out filterQuality, out filterQualityValue);
            CheckLifetime(work);
            return state.Finish();
        }

        private ProjectionSubjectFacts Facts(Thing thing, SubjectStateBuilder state)
        {
            state.Guards(ThingChecks);
            Require(thing != null && thing.def != null && thing.thingIDNumber > 0 && thing.stackCount > 0,
                ProjectionSubjectResult.InvalidSubject);
            var def = thing.def;
            Require(ReferenceEquals(def.thingClass, thing.GetType()) && def.stackLimit > 0 && def.size.x > 0 && def.size.z > 0,
                ProjectionSubjectResult.InvalidSubject);
            var mapState = (sbyte)fields.MapState.GetValue(thing);
            Require(mapState >= -1, ProjectionSubjectResult.InvalidSubject);
            var position = (IntVec3)fields.Position.GetValue(thing);
            var rotation = (byte)fields.RotationValue.GetValue(fields.Rotation.GetValue(thing));
            Require(rotation < 4, ProjectionSubjectResult.InvalidSubject);
            var stuff = (ThingDef)fields.Stuff.GetValue(thing);
            var faction = (Faction)fields.Faction.GetValue(thing);
            var hp = (int)fields.HitPoints.GetValue(thing);
            state.Ref(thing); state.Ref(thing.GetType()); state.Ref(def); state.Ref(def.thingClass);
            state.Ref(stuff); state.Ref(faction); state.Ref(thing.holdingOwner); state.Ref(def.minifiedDef);
            state.Value(thing.thingIDNumber); state.Value(thing.stackCount); state.Value(def.stackLimit);
            state.Value(def.size.x); state.Value(def.size.z); state.Value(position.x); state.Value(position.y); state.Value(position.z);
            state.Value(rotation); state.Value(mapState); state.Value(hp); state.Value((int)def.category); state.Value(def.useHitPoints ? 1 : 0);
            state.Record();
            return new ProjectionSubjectFacts(thing, def, stuff, faction, thing.holdingOwner, thing.thingIDNumber,
                thing.stackCount, def.stackLimit, def.size, position, rotation, mapState, hp);
        }

        private void Floor(ProjectionSubjectFacts subject, Map map, SubjectStateBuilder state)
        {
            state.Guards(MapChecks);
            var maps = (List<Map>)fields.GameMaps.GetValue(game);
            Require(maps != null && subject.MapState >= 0 && subject.MapState < maps.Count,
                ProjectionSubjectResult.InvalidCustody);
            state.List(maps, 128);
            Require(ReferenceEquals(maps[subject.MapState], map) && map.uniqueID >= 0 && map.info != null && map.thingGrid != null,
                ProjectionSubjectResult.InvalidCustody);
            var size = (IntVec3)fields.MapSize.GetValue(map.info);
            Require(size.x > 0 && size.y == 1 && size.z > 0 && (long)size.x * size.z <= int.MaxValue,
                ProjectionSubjectResult.InvalidCustody);
            var grid = (List<Thing>[])fields.GridCells.GetValue(map.thingGrid);
            Require(ReferenceEquals(fields.GridMap.GetValue(map.thingGrid), map) && grid != null
                && grid.Length == (long)size.x * size.z
                && (int)fields.CellSizeX.GetValue(map.cellIndices) == size.x && (int)fields.CellSizeZ.GetValue(map.cellIndices) == size.z,
                ProjectionSubjectResult.InvalidCustody);
            Require(subject.Size.x == 1 && subject.Size.z == 1, ProjectionSubjectResult.UnsupportedFootprint);
            var pos = subject.Position;
            Require(pos.y == 0 && pos.x >= 0 && pos.z >= 0 && pos.x < size.x && pos.z < size.z,
                ProjectionSubjectResult.InvalidCustody);
            state.Ref(map); state.Value(map.uniqueID); state.Ref(map.info); state.Value(size.x); state.Value(size.y); state.Value(size.z);
            state.Ref(map.thingGrid); state.Ref(grid);
            var members = Owner(map.spawnedThings, map, state, out _);
            Require(ReferenceEquals(subject.Owner, map.spawnedThings), ProjectionSubjectResult.InvalidCustody);
            Member(members, subject, state, limits.OwnerEntries);
            // Exact inspected CellIndicesUtility: z * sizeX + x. No game helper.
            var cell = grid[pos.z * size.x + pos.x];
            Member(cell, subject, state, limits.GridEntries);
        }

        private ProjectionSubjectFacts PawnOnMap(Pawn pawn, ProjectionCustodyExpectation expected, SubjectStateBuilder state)
        {
            var facts = Facts(pawn, state);
            Require(facts.Def.category == ThingCategory.Pawn && ReferenceEquals(facts.Faction, expected.Faction),
                ProjectionSubjectResult.InvalidCustody);
            Floor(facts, expected.Map, state);
            return facts;
        }

        private void Held(ProjectionSubjectFacts subject, ProjectionCustodyExpectation expected, SubjectStateBuilder state)
        {
            state.Guards(HolderChecks);
            Require(subject.MapState == -1, ProjectionSubjectResult.InvalidCustody);
            object tracker;
            ThingOwner container;
            if (expected.Custody == ProjectionSubjectCustody.Inventory)
            {
                var inventory = expected.SourceHolder.inventory;
                Require(inventory != null && inventory.GetType() == typeof(Pawn_InventoryTracker), ProjectionSubjectResult.UnsupportedOwner);
                Require(ReferenceEquals(inventory.pawn, expected.SourceHolder), ProjectionSubjectResult.InvalidCustody);
                tracker = inventory; container = inventory.innerContainer;
            }
            else
            {
                var carry = expected.SourceHolder.carryTracker;
                Require(carry != null && carry.GetType() == typeof(Pawn_CarryTracker), ProjectionSubjectResult.UnsupportedOwner);
                Require(ReferenceEquals(carry.pawn, expected.SourceHolder), ProjectionSubjectResult.InvalidCustody);
                tracker = carry; container = carry.innerContainer;
            }
            state.Ref(tracker);
            var members = Owner(container, tracker, state, out int maxStacks);
            Require(ReferenceEquals(subject.Owner, container), ProjectionSubjectResult.InvalidCustody);
            Require(expected.Custody != ProjectionSubjectCustody.Carry || (maxStacks == 1 && members.Count == 1),
                ProjectionSubjectResult.InvalidCustody);
            Member(members, subject, state, limits.OwnerEntries);
        }

        private List<Thing> Owner(ThingOwner owner, object holder, SubjectStateBuilder state, out int maxStacks)
        {
            state.Guards(OwnerChecks);
            Require(owner != null && owner.GetType() == typeof(ThingOwner<Thing>), ProjectionSubjectResult.UnsupportedOwner);
            // Never read abstract Count/indexer or call GetDirectlyHeldThings.
            Require(ReferenceEquals(fields.OwnerHolder.GetValue(owner), holder), ProjectionSubjectResult.InvalidCustody);
            var list = (List<Thing>)fields.OwnerList.GetValue(owner);
            maxStacks = (int)fields.OwnerLimit.GetValue(owner);
            Require(list != null && maxStacks > 0 && list.Count <= maxStacks, ProjectionSubjectResult.InvalidCustody);
            state.Ref(owner); state.Ref(holder); state.Value(maxStacks); state.List(list, limits.OwnerEntries);
            return list;
        }

        private static void Member(List<Thing> list, ProjectionSubjectFacts subject, SubjectStateBuilder state, int maximum)
        {
            Require(list != null, ProjectionSubjectResult.InvalidCustody);
            state.List(list, maximum);
            state.Guards(2L + 4L * list.Count);
            int matches = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                Require(item != null, ProjectionSubjectResult.InvalidCustody);
                if (ReferenceEquals(item, subject.Thing)) matches++;
                else Require(item.thingIDNumber != subject.Id, ProjectionSubjectResult.InvalidCustody);
            }
            Require(matches == 1, ProjectionSubjectResult.InvalidCustody);
        }

        private void Components(ProjectionSubjectFacts subject, SubjectStateBuilder state,
            out CompQuality quality, out QualityCategory? qualityValue)
        {
            state.Guards(ComponentHeaderChecks);
            var props = subject.Def.comps;
            var withComps = subject.Thing as ThingWithComps;
            var comps = withComps == null ? null : (List<ThingComp>)fields.Components.GetValue(withComps);
            quality = withComps == null ? null : withComps.compQuality; qualityValue = null;
            state.List(props, limits.DefinitionComponents); state.List(comps, limits.Components); state.Ref(quality);
            int propertyCount = props == null ? 0 : props.Count, compCount = comps == null ? 0 : comps.Count;
            Require(withComps != null || propertyCount == 0, ProjectionSubjectResult.InvalidComponents);
            // Reserve both actual nested scans, including duplicate comp identity.
            state.Guards(checked(4L * propertyCount + (long)ComponentChecks * compCount
                + (long)compCount * propertyCount + (long)compCount * compCount));
            for (int i = 0; i < propertyCount; i++)
            {
                var p = props[i];
                Require(p != null && p.compClass != null, ProjectionSubjectResult.InvalidComponents);
                state.Ref(p); state.Ref(p.compClass);
            }
            CompQuality firstQuality = null;
            for (int i = 0; i < compCount; i++)
            {
                var comp = comps[i];
                Require(comp != null && comp.props != null && ReferenceEquals(comp.parent, withComps)
                    && ReferenceEquals(comp.props.compClass, comp.GetType()), ProjectionSubjectResult.InvalidComponents);
                bool found = false;
                for (int p = 0; p < propertyCount; p++) if (ReferenceEquals(props[p], comp.props)) found = true;
                Require(found, ProjectionSubjectResult.InvalidComponents);
                for (int previous = 0; previous < i; previous++)
                    Require(!ReferenceEquals(comps[previous], comp), ProjectionSubjectResult.InvalidComponents);
                state.Ref(comp); state.Ref(comp.GetType()); state.Ref(comp.parent); state.Ref(comp.props); state.Ref(comp.props.compClass);
                if (comp is CompQuality q)
                {
                    var raw = (QualityCategory)fields.Quality.GetValue(q);
                    Require((int)raw >= 0 && (int)raw <= 6, ProjectionSubjectResult.InvalidComponents);
                    state.Value((int)raw);
                    if (firstQuality == null) { firstQuality = q; qualityValue = raw; }
                }
            }
            Require(ReferenceEquals(firstQuality, quality), ProjectionSubjectResult.InvalidComponents);
            // Comp fields beyond parent/props/quality (e.g. rot/stat/ingredient
            // inputs and compsByType cache contents) remain Stage B obligations.
        }

        private static void Distinct(ProjectionSubjectFacts first, ProjectionSubjectFacts second) =>
            Require(!ReferenceEquals(first.Thing, second.Thing) && first.Id != second.Id, ProjectionSubjectResult.InvalidCustody);
        private static void Require(bool condition, ProjectionSubjectResult result)
        { if (!condition) throw new ProjectionSubjectStop(result); }

        private sealed class SubjectFields
        {
            internal const int BindingCount = 21;
            internal readonly FieldInfo CurrentGame = Field(typeof(Current), "gameInt", typeof(Game), true);
            internal readonly FieldInfo GameMaps = Field(typeof(Game), "maps", typeof(List<Map>));
            internal readonly FieldInfo MapState = Field(typeof(Thing), "mapIndexOrState", typeof(sbyte));
            internal readonly FieldInfo Position = Field(typeof(Thing), "positionInt", typeof(IntVec3));
            internal readonly FieldInfo Rotation = Field(typeof(Thing), "rotationInt", typeof(Rot4));
            internal readonly FieldInfo RotationValue = Field(typeof(Rot4), "rotInt", typeof(byte));
            internal readonly FieldInfo Stuff = Field(typeof(Thing), "stuffInt", typeof(ThingDef));
            internal readonly FieldInfo Faction = Field(typeof(Thing), "factionInt", typeof(Faction));
            internal readonly FieldInfo HitPoints = Field(typeof(Thing), "hitPointsInt", typeof(int));
            internal readonly FieldInfo MinifiedContainer = Field(typeof(MinifiedThing), "innerContainer", typeof(ThingOwner));
            internal readonly FieldInfo OwnerHolder = Field(typeof(ThingOwner), "owner", typeof(IThingHolder));
            internal readonly FieldInfo OwnerLimit = Field(typeof(ThingOwner), "maxStacks", typeof(int));
            internal readonly FieldInfo OwnerList = Field(typeof(ThingOwner<Thing>), "innerList", typeof(List<Thing>));
            internal readonly FieldInfo MapSize = Field(typeof(MapInfo), "sizeInt", typeof(IntVec3));
            internal readonly FieldInfo GridMap = Field(typeof(ThingGrid), "map", typeof(Map));
            internal readonly FieldInfo GridCells = Field(typeof(ThingGrid), "thingGrid", typeof(List<Thing>[]));
            internal readonly FieldInfo CellSizeX = Field(typeof(CellIndices), "sizeX", typeof(int));
            internal readonly FieldInfo CellSizeZ = Field(typeof(CellIndices), "sizeZ", typeof(int));
            internal readonly FieldInfo Components = Field(typeof(ThingWithComps), "comps", typeof(List<ThingComp>));
            internal readonly FieldInfo Quality = Field(typeof(CompQuality), "qualityInt", typeof(QualityCategory));
            // Count includes the SubjectFields record itself (20 bound fields).
            private static FieldInfo Field(Type declaring, string name, Type fieldType, bool isStatic = false)
            {
                var field = declaring.GetField(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic
                    | (isStatic ? BindingFlags.Static : BindingFlags.Instance));
                Require(field != null && field.DeclaringType == declaring && field.FieldType == fieldType && field.IsStatic == isStatic,
                    ProjectionSubjectResult.BindingFault);
                return field;
            }
        }
    }
}
