using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // Native out-parameter validation with installed observer wrappers. These
    // holders/cargo never belong to the cook and cannot enter the recipe or tags.
    internal static class Bg03OutControl
    {
        private sealed class Holder : IThingHolder
        {
            internal readonly ThingOwner<Thing> contents;
            internal Holder() { contents = new ThingOwner<Thing>(this); }
            public IThingHolder ParentHolder => null;
            public ThingOwner GetDirectlyHeldThings() => contents;
            public void GetChildHolders(List<IThingHolder> outChildren) { }
        }
        internal static Bg03OutResult Run(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map) || map.thingGrid.ThingsListAt(cell).Any(x => x.def.category == ThingCategory.Item))
                throw new InvalidOperationException("Observer out-control needs its own empty fixture cell.");
            var source = new Holder(); var target = new Holder();
            var initialAtCell = new HashSet<Thing>(map.thingGrid.ThingsListAt(cell));
            var cargo = ThingMaker.MakeThing(ThingDefOf.Steel); cargo.stackCount = 7;
            var result = new Bg03OutResult { tick = Find.TickManager.TicksGame, initialThingId = cargo.thingIDNumber };
            try
            {
                if (!source.contents.TryAdd(cargo, false)) throw new InvalidOperationException("Native out-control setup TryAdd failed.");
                int moved = source.contents.TryTransferToContainer(cargo, target.contents, 3, out Thing transferred, false);
                result.transferredThingId = transferred?.thingIDNumber; result.transferredCount = transferred?.stackCount ?? -1;
                result.sourceRemaining = cargo.stackCount; result.nativeMoved = moved;
                result.transferValid = moved == 3 && transferred != null && transferred.stackCount == 3 && cargo.stackCount == 4
                    && ReferenceEquals(transferred.holdingOwner, target.contents) && ReferenceEquals(transferred.ParentHolder, target);
                HarnessSession.Event("bg03-observer-native-out-transfer", Json.Stringify(result));
                if (result.transferValid)
                {
                    result.nativeDropped = target.contents.TryDrop(transferred, cell, map, ThingPlaceMode.Direct, 3, out Thing dropped);
                    result.droppedThingId = dropped?.thingIDNumber; result.droppedCount = dropped?.stackCount ?? -1;
                    result.dropValid = result.nativeDropped && ReferenceEquals(dropped, transferred) && dropped.stackCount == 3
                        && dropped.Spawned && dropped.Map == map && dropped.Position == cell && ReferenceEquals(dropped.ParentHolder, map)
                        && target.contents.Count == 0;
                    HarnessSession.Event("bg03-observer-native-out-drop", Json.Stringify(result));
                }
            }
            finally
            {
                source.contents.ClearAndDestroyContents(); target.contents.ClearAndDestroyContents();
                if (!cargo.Destroyed) cargo.Destroy(DestroyMode.Vanish);
                // Exact new non-recipe control cargo only; no scene/colony cleanup.
                foreach (var thing in map.thingGrid.ThingsListAt(cell).Where(x => !initialAtCell.Contains(x) && x.def == ThingDefOf.Steel).ToList())
                    thing.Destroy(DestroyMode.Vanish);
                result.cleaned = source.contents.Count == 0 && target.contents.Count == 0
                    && initialAtCell.SetEquals(map.thingGrid.ThingsListAt(cell)) && Find.TickManager.TicksGame == result.tick;
                HarnessSession.Event("bg03-observer-native-out-control-final", Json.Stringify(result));
            }
            return result;
        }
    }
    [DataContract] internal sealed class Bg03OutResult
    {
        [DataMember] public int tick { get; set; }
        [DataMember] public int initialThingId { get; set; }
        [DataMember] public int? transferredThingId { get; set; }
        [DataMember] public int transferredCount { get; set; }
        [DataMember] public int sourceRemaining { get; set; }
        [DataMember] public int nativeMoved { get; set; }
        [DataMember] public bool transferValid { get; set; }
        [DataMember] public bool nativeDropped { get; set; }
        [DataMember] public int? droppedThingId { get; set; }
        [DataMember] public int droppedCount { get; set; }
        [DataMember] public bool dropValid { get; set; }
        [DataMember] public bool cleaned { get; set; }
    }
}
