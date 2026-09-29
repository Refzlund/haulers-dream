using System;
using System.Collections.Generic;
using System.Reflection;
using HaulersDream.Core;
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
        private static readonly FieldInfo hp = Field(typeof(Thing), "hitPointsInt", typeof(int)),
            stuff = Field(typeof(Thing), "stuffInt", typeof(ThingDef)),
            comps = Field(typeof(ThingWithComps), "comps", typeof(List<ThingComp>)),
            quality = Field(typeof(CompQuality), "qualityInt", typeof(QualityCategory)),
            innerContainer = Field(typeof(MinifiedThing), "innerContainer", typeof(ThingOwner)),
            owner = Field(typeof(ThingOwner), "owner", typeof(IThingHolder)),
            ownerList = Field(typeof(ThingOwner<Thing>), "innerList", typeof(List<Thing>));
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
                container = (ThingOwner)innerContainer.GetValue(wrapper);
                if (container == null || container.GetType() != typeof(ThingOwner<Thing>)
                    || !ReferenceEquals(owner.GetValue(container), wrapper)) return;
                contents = (List<Thing>)ownerList.GetValue(container);
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
            return wrapper == null || ReferenceEquals(innerContainer.GetValue(wrapper), container)
                && ReferenceEquals(owner.GetValue(container), wrapper)
                && contentsGuard.Matches((List<Thing>)ownerList.GetValue(container))
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
                Value = thing; definition = thing.def; material = (ThingDef)stuff.GetValue(thing);
                holder = thing.holdingOwner; id = thing.thingIDNumber; count = thing.stackCount;
                hitPoints = (int)hp.GetValue(thing);
                if (thing is ThingWithComps withComps)
                {
                    componentThing = withComps; components = (List<ThingComp>)comps.GetValue(withComps);
                    if (components != null)
                    {
                        if (components.Count > 256) return;
                        componentGuard = new ProjectionListGuard<ThingComp>(components);
                        foreach (var comp in components)
                            if (comp is CompQuality q)
                                qualities.Add(Tuple.Create(q, (QualityCategory)quality.GetValue(q), q.parent));
                    }
                    cachedQuality = withComps.compQuality;
                    if (cachedQuality != null) cachedValue = (QualityCategory)quality.GetValue(cachedQuality);
                }
                valid = definition != null;
            }
            internal bool Matches()
            {
                if (!valid || !ReferenceEquals(Value.def, definition) || !ReferenceEquals(stuff.GetValue(Value), material)
                    || !ReferenceEquals(Value.holdingOwner, holder) || Value.thingIDNumber != id || Value.stackCount != count
                    || (int)hp.GetValue(Value) != hitPoints) return false;
                if (componentThing == null) return true;
                var actual = (List<ThingComp>)comps.GetValue(componentThing);
                if (components == null ? actual != null : !componentGuard.Matches(actual)) return false;
                if (!ReferenceEquals(componentThing.compQuality, cachedQuality)
                    || cachedQuality != null && (QualityCategory)quality.GetValue(cachedQuality) != cachedValue) return false;
                foreach (var q in qualities)
                    if (!ReferenceEquals(q.Item1.parent, q.Item3) || (QualityCategory)quality.GetValue(q.Item1) != q.Item2) return false;
                return true;
            }
        }
    }
}
