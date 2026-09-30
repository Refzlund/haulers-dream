using System;
using System.Collections.Generic;
using System.Reflection;
using HaulersDream.Core;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Synchronous raw dependencies of the selected native HP/quality/minified
    // predicates. This is neither a compatibility decision nor a capacity cache.
    // Matches performs no virtual getter, native predicate or provider callback.
    internal sealed class StorageSubjectAttributeGuard
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static FieldInfo Field(Type owner, string name, Type type)
        {
            var field = owner.GetField(name, Fields);
            return field != null && field.DeclaringType == owner && field.FieldType == type
                && !field.IsStatic ? field : null;
        }
        // Resolve the exact inspected fields once. Each delegate is a direct ldflda
        // reader; invocation performs no reflection, boxing, virtual getter or callback.
        // A missing field/layout or failed binding leaves this guard unavailable.
        private static AccessTools.FieldRef<T, V> Read<T, V>(string name) where T : class
        {
            var field = Field(typeof(T), name, typeof(V));
            if (field == null) return null;
            try { return AccessTools.FieldRefAccess<T, V>(field); }
            catch { return null; }
        }
        private static readonly AccessTools.FieldRef<Thing, int> hp = Read<Thing, int>("hitPointsInt");
        private static readonly AccessTools.FieldRef<Thing, ThingDef> stuff = Read<Thing, ThingDef>("stuffInt");
        private static readonly AccessTools.FieldRef<ThingWithComps, List<ThingComp>> comps = Read<ThingWithComps, List<ThingComp>>("comps");
        private static readonly AccessTools.FieldRef<CompQuality, QualityCategory> quality = Read<CompQuality, QualityCategory>("qualityInt");
        private static readonly AccessTools.FieldRef<MinifiedThing, ThingOwner> innerContainer = Read<MinifiedThing, ThingOwner>("innerContainer");
        private static readonly AccessTools.FieldRef<ThingOwner, IThingHolder> owner = Read<ThingOwner, IThingHolder>("owner");
        private static readonly AccessTools.FieldRef<ThingOwner<Thing>, List<Thing>> ownerList = Read<ThingOwner<Thing>, List<Thing>>("innerList");
        private readonly RawThing outer, inner;
        private readonly MinifiedThing wrapper;
        private readonly ThingOwner container;
        private readonly List<Thing> contents;
        private readonly ProjectionListGuard<Thing> contentsGuard;
        private readonly bool valid;

        internal StorageSubjectAttributeGuard(Thing subject)
        {
            if (hp == null || stuff == null || comps == null || quality == null
                || innerContainer == null || owner == null || ownerList == null || subject == null) return;
            outer = new RawThing(subject);
            if (subject is MinifiedThing minified)
            {
                // Certify only the inspected direct native holder, never recursively
                // traverse a foreign IThingHolder or use the callback-bearing InnerThing.
                if (subject.GetType() != typeof(MinifiedThing)) return;
                wrapper = minified;
                container = innerContainer(wrapper);
                if (container == null || container.GetType() != typeof(ThingOwner<Thing>)
                    || !ReferenceEquals(owner(container), wrapper)) return;
                contents = ownerList((ThingOwner<Thing>)container);
                if (contents == null || contents.Count != 1 || contents[0] == null
                    || contents[0] is MinifiedThing || !ReferenceEquals(contents[0].holdingOwner, container)) return;
                contentsGuard = new ProjectionListGuard<Thing>(contents);
                inner = new RawThing(contents[0]);
            }
            valid = true;
        }

        internal bool Matches()
        {
            if (!valid || !outer.Matches()) return false;
            return wrapper == null || ReferenceEquals(innerContainer(wrapper), container)
                && ReferenceEquals(owner(container), wrapper)
                && contentsGuard.Matches(ownerList((ThingOwner<Thing>)container))
                && contents.Count == 1 && ReferenceEquals(contents[0], inner.Value) && inner.Matches();
        }

        private sealed class RawThing
        {
            internal readonly Thing Value;
            private readonly ThingDef definition, material;
            private readonly ThingOwner holder;
            private readonly int id, count, hitPoints;
            private readonly ThingWithComps componentThing;
            private readonly List<ThingComp> components;
            private readonly ProjectionListGuard<ThingComp> componentGuard;
            private readonly CompQuality cachedQuality;
            private readonly QualityCategory cachedValue;
            private readonly List<Tuple<CompQuality, QualityCategory, ThingWithComps>> qualities
                = new List<Tuple<CompQuality, QualityCategory, ThingWithComps>>();
            private readonly bool valid;
            internal RawThing(Thing thing)
            {
                StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                Value = thing; definition = thing.def; material = stuff(thing);
                holder = thing.holdingOwner; id = thing.thingIDNumber; count = thing.stackCount;
                hitPoints = hp(thing);
                if (thing is ThingWithComps withComps)
                {
                    componentThing = withComps; components = comps(withComps);
                    if (components != null)
                    {
                        if (components.Count > 256) return;
                        componentGuard = new ProjectionListGuard<ThingComp>(components);
                        foreach (var comp in components)
                        {
                            StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                            if (comp is CompQuality q)
                                qualities.Add(Tuple.Create(q, quality(q), q.parent));
                        }
                    }
                    cachedQuality = withComps.compQuality;
                    if (cachedQuality != null) cachedValue = quality(cachedQuality);
                }
                valid = definition != null;
            }
            internal bool Matches()
            {
                StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                if (!valid || !ReferenceEquals(Value.def, definition) || !ReferenceEquals(stuff(Value), material)
                    || !ReferenceEquals(Value.holdingOwner, holder) || Value.thingIDNumber != id || Value.stackCount != count
                    || hp(Value) != hitPoints) return false;
                if (componentThing == null) return true;
                var actual = comps(componentThing);
                if (components == null ? actual != null : !componentGuard.Matches(actual)) return false;
                if (!ReferenceEquals(componentThing.compQuality, cachedQuality)
                    || cachedQuality != null && quality(cachedQuality) != cachedValue) return false;
                foreach (var q in qualities)
                {
                    StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                    if (!ReferenceEquals(q.Item1.parent, q.Item3) || quality(q.Item1) != q.Item2) return false;
                }
                return true;
            }
        }
    }
}
