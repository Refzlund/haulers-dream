using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>Exact optional APIs needed by a confirmed count drop; no third-party types in metadata.</summary>
    internal static class InventoryDropCompat
    {
        private static readonly Type CeInventory = AccessTools.TypeByName("CombatExtended.CompInventory");
        private static readonly MethodInfo CeRefresh = StaticMethod("CombatExtended.CE_Utility",
            "TryUpdateInventory", typeof(void), typeof(Pawn));
        private static readonly MethodInfo CeQuestLocked = StaticMethod("CombatExtended.Utility_Loadouts",
            "IsItemQuestLocked", typeof(bool), typeof(Pawn), typeof(Thing));
        private static readonly MethodInfo CeMechWeapon = StaticMethod("CombatExtended.Utility_Loadouts",
            "IsItemMechanoidWeapon", typeof(bool), typeof(Pawn), typeof(Thing));
        private static readonly Type SidearmsMemory = AccessTools.TypeByName("SimpleSidearms.rimworld.CompSidearmMemory");
        private static readonly MethodInfo SidearmsCarried = StaticMethod("PeteTimesSix.SimpleSidearms.Extensions",
            "GetCarriedWeapons", typeof(List<ThingWithComps>), typeof(Pawn), typeof(bool), typeof(bool));
        private static readonly MethodInfo SidearmsDropped = SidearmsMemory == null ? null
            : AccessTools.Method(SidearmsMemory, "InformOfDroppedSidearm", new[] { typeof(Thing), typeof(bool) });

        private static MethodInfo StaticMethod(string typeName, string name, Type returnType, params Type[] args)
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null ? null : AccessTools.Method(type, name, args);
            return method != null && method.IsStatic && method.ReturnType == returnType ? method : null;
        }

        internal static bool CanUseCombatExtendedDrop(Pawn pawn, Thing thing) =>
            CeInventory != null && CeQuestLocked != null && CeMechWeapon != null
            && !(bool)CeQuestLocked.Invoke(null, new object[] { pawn, thing })
            && !(bool)CeMechWeapon.Invoke(null, new object[] { pawn, thing });

        private static object MemoryOf(Pawn pawn)
        {
            if (SidearmsMemory == null || pawn?.AllComps == null)
                return null;
            foreach (var comp in pawn.AllComps)
                if (SidearmsMemory.IsInstanceOfType(comp))
                    return comp;
            return null;
        }

        internal static bool AvailableFor(Pawn pawn, Thing thing)
        {
            if (CeInventory != null && CeRefresh == null)
                return false;
            if (thing?.def == null)
                return false;
            if (SidearmsMemory == null || !thing.def.IsWeapon || MemoryOf(pawn) == null)
                return true;
            return SimpleSidearmsCompat.MemoryApiOk && SidearmsCarried != null && SidearmsDropped != null
                && !SidearmsDropped.IsStatic && SidearmsDropped.ReturnType == typeof(void);
        }

        internal readonly struct Snapshot
        {
            internal readonly object Memory;
            internal readonly ThingDef Def, Stuff;
            internal readonly int Remembered, Carried;
            internal Snapshot(object memory, ThingDef def, ThingDef stuff, int remembered, int carried)
            { Memory = memory; Def = def; Stuff = stuff; Remembered = remembered; Carried = carried; }
        }

        internal static bool TrySnapshot(Pawn pawn, Thing thing, out Snapshot snapshot)
        {
            snapshot = default;
            if (!AvailableFor(pawn, thing))
                return false;
            var memory = thing.def.IsWeapon ? MemoryOf(pawn) : null;
            if (memory != null)
                snapshot = new Snapshot(memory, thing.def, thing.Stuff,
                    SimpleSidearmsCompat.RememberedCount(pawn, thing.def, thing.Stuff),
                    CarriedReferences(pawn, thing.def, thing.Stuff));
            return true;
        }

        private static int CarriedReferences(Pawn pawn, ThingDef def, ThingDef stuff)
        {
            var carried = (IEnumerable)SidearmsCarried.Invoke(null, new object[] { pawn, true, true });
            var matching = new List<Thing>();
            foreach (Thing thing in carried)
            {
                if (thing == null || thing.def != def || thing.Stuff != stuff)
                    continue;
                bool duplicate = false;
                foreach (var existing in matching)
                    if (ReferenceEquals(existing, thing)) { duplicate = true; break; }
                if (!duplicate)
                    matching.Add(thing);
            }
            return matching.Count;
        }

        internal static void SettleSidearms(Pawn pawn, Thing source, bool retained, Snapshot snapshot)
        {
            if (retained || snapshot.Memory == null)
                return;
            int after = CarriedReferences(pawn, snapshot.Def, snapshot.Stuff);
            int previouslyMissing = Math.Max(0, snapshot.Remembered - snapshot.Carried);
            int nowMissing = Math.Max(0, snapshot.Remembered - after);
            if (nowMissing > previouslyMissing)
                SidearmsDropped.Invoke(snapshot.Memory, new object[] { source, true });
        }

        internal static void RefreshCombatExtended(Pawn pawn)
        {
            if (CeInventory != null)
                CeRefresh.Invoke(null, new object[] { pawn });
        }
    }
}
