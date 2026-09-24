using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Bare native cell adapter. Storage/provider cells use the later shared shelf admission path.
    // Observation records are shared with the storage projector; there is no second reservation ledger.
    internal static class ExplicitHaulCell
    {
        internal static bool Observe(Pawn pawn, Thing subject, IntVec3 cell, out int space,
            out List<StorageStackResource> stacks)
        {
            space = 0; stacks = new List<StorageStackResource>();
            var map = pawn?.Map;
            if (map == null || subject?.def == null || !cell.InBounds(map) || !cell.Standable(map)
                || cell.IsForbidden(pawn) || map.haulDestinationManager.SlotGroupAt(cell) != null
                || subject.def.size.x != 1 || subject.def.size.z != 1
                || cell.GetMaxItemsAllowedInCell(map) != 1) return false;
            int items = 0;
            foreach (var t in cell.GetThingList(map))
            {
                if (t is Building || t.def.IsBlueprint || t.def.IsFrame || GenSpawn.SpawningWipes(subject.def, t.def)) return false;
                if (t.def.category != ThingCategory.Item) continue;
                if (t.stackCount <= 0 || t.def.size.x != 1 || t.def.size.z != 1) return false;
                items++;
                var row = new StorageStackResource("bare:" + map.uniqueID + ":" + t.thingIDNumber,
                    t.thingIDNumber, t.def.defName, t.stackCount, t.def.stackLimit, null);
                stacks.Add(row);
                if (t.CanStackWith(subject)) space = checked(space + (int)row.Deficit);
            }
            if (items == 0) space = subject.def.stackLimit;
            return items <= 1;
        }
    }
}
