using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Keep H&H's productive work, but give it exact placement identity and count-aware unloading.</summary>
    internal static class HarvestAndHaulCompat
    {
        internal static readonly Type PlacementPatch = AccessTools.TypeByName("HarvestAndHaul.Patch_GenPlace_TryPlaceThing");
        internal static readonly Type UnloadDriver = AccessTools.TypeByName("HarvestAndHaul.JobDriver_HAH_UnloadHarvestInventory");
        internal static readonly MethodInfo PlacementPostfix = PlacementPatch == null ? null : AccessTools.DeclaredMethod(
            PlacementPatch, "Postfix", new[] { typeof(bool), typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode) });
        internal static readonly MethodInfo CanonicalPlacement = AccessTools.DeclaredMethod(typeof(GenPlace), "TryPlaceThing", new[] {
            typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(),
            typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int) });
        internal static bool PlacementAvailable => PlacementPostfix?.IsStatic == true
            && PlacementPostfix.ReturnType == typeof(void) && CanonicalPlacement != null;

        // A wrapper and its canonical call are one placement. H&H patches both and otherwise guesses a
        // nearby same-def stack when its first pickup destroyed the original. This invocation-local frame
        // is ready only during the canonical postfixes; nested calls and exceptions restore their parent.
        internal sealed class Placement
        {
            internal Placement Parent;
            internal Thing Original, Result;
            internal Map Map;
            internal IntVec3 Center;
            internal ThingPlaceMode Mode;
            internal bool Ready;
        }
        [ThreadStatic] internal static Placement CurrentPlacement;
    }

    [HarmonyPatch]
    internal static class Patch_HarvestAndHaul_PlacementContext
    {
        static bool Prepare() => HarvestAndHaulCompat.PlacementAvailable;
        static MethodBase TargetMethod() => HarvestAndHaulCompat.CanonicalPlacement;

        [HarmonyPriority(Priority.First)]
        static void Prefix(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode,
            out HarvestAndHaulCompat.Placement __state)
        {
            __state = new HarvestAndHaulCompat.Placement { Parent = HarvestAndHaulCompat.CurrentPlacement,
                Original = thing, Center = center, Map = map, Mode = mode };
            HarvestAndHaulCompat.CurrentPlacement = __state;
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("laredson.harvestandhaul")]
        static void Postfix(Thing lastResultingThing, bool __result, HarvestAndHaulCompat.Placement __state)
        {
            if (__state == null) return;
            __state.Result = __result ? lastResultingThing : null;
            __state.Ready = true;
        }

        // Void finalizer preserves the native exception rather than swallowing/replacing it.
        static void Finalizer(HarvestAndHaulCompat.Placement __state)
        {
            if (__state != null) HarvestAndHaulCompat.CurrentPlacement = __state.Parent;
        }
    }

    [HarmonyPatch]
    internal static class Patch_HarvestAndHaul_ExactPlacedThing
    {
        static bool Prepare() => HarvestAndHaulCompat.PlacementAvailable;
        static MethodBase TargetMethod() => HarvestAndHaulCompat.PlacementPostfix;

        static bool Prefix(bool __0, ref Thing thing, IntVec3 center, Map map, ThingPlaceMode mode)
        {
            var frame = HarvestAndHaulCompat.CurrentPlacement;
            if (!__0 || frame == null || !frame.Ready || !ReferenceEquals(frame.Original, thing)
                || frame.Map != map || frame.Center != center || frame.Mode != mode)
                return false;
            var placed = frame.Result;
            if (placed == null || placed.Destroyed || !placed.Spawned || placed.Map != map)
                return false;
            // This is the actual native out-result, including a ground merge. HD's direct-to-inventory
            // result is not spawned, so it never turns unrelated floor stock into H&H's fresh output.
            // Keep H&H's own filters, producer attribution, settings, intake and tracking unchanged.
            thing = placed;
            return true;
        }
    }

    [HarmonyPatch]
    internal static class Patch_HarvestAndHaul_KeptUnload
    {
        private static readonly MethodInfo WholeDrop = AccessTools.DeclaredMethod(typeof(ThingOwner<Thing>), "TryDrop",
            new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(),
                typeof(Action<Thing, int>), typeof(Predicate<IntVec3>) });
        private static MethodInfo target;
        private static FieldInfo currentItem;

        static bool Prepare()
        {
            var driver = HarvestAndHaulCompat.UnloadDriver;
            if (driver == null || !typeof(JobDriver).IsAssignableFrom(driver)) return false;
            currentItem = AccessTools.DeclaredField(driver, "curItem");
            var component = AccessTools.TypeByName("HarvestAndHaul.HAH_GameComponent");
            var deregister = component == null ? null : AccessTools.DeclaredMethod(component, "DeregisterHarvest", new[] { typeof(Pawn), typeof(Thing) });
            if (WholeDrop == null || currentItem?.FieldType != typeof(Thing) || deregister == null) return Unavailable();
            // Bind the actual drop toil's callback by its calls, not a compiler-generated lambda ordinal.
            var matches = AccessTools.GetDeclaredMethods(driver).Where(m => !m.IsStatic && m.ReturnType == typeof(void)
                && m.GetParameters().Length == 0 && m.GetMethodBody() != null)
                .Where(m => { var code = PatchProcessor.GetOriginalInstructions(m);
                    return code.Any(i => i.Calls(deregister)) && code.Count(i => i.Calls(WholeDrop)) == 6; }).ToArray();
            if (matches.Length != 1) return Unavailable();
            target = matches[0];
            return true;
        }

        private static bool Unavailable()
        {
            HDLog.Warn("Harvest and Haul unload callback has an unrecognized shape; its original unload behavior remains in place.");
            return false;
        }

        static MethodBase TargetMethod() => target;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            if (code.Count(i => i.Calls(WholeDrop)) != 6)
            {
                Unavailable();
                return code;
            }
            var replacement = AccessTools.DeclaredMethod(typeof(Patch_HarvestAndHaul_KeptUnload), nameof(DropSurplus));
            foreach (var instruction in code)
                if (instruction.Calls(WholeDrop)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
            return code;
        }

        private static bool DropSurplus(ThingOwner<Thing> owner, Thing thing, IntVec3 cell, Map map, ThingPlaceMode mode,
            out Thing result, Action<Thing, int> placedAction, Predicate<IntVec3> validator)
        {
            var inventory = owner?.Owner as Pawn_InventoryTracker;
            var pawn = inventory?.pawn;
            var driver = pawn?.jobs?.curDriver;
            if (driver == null || driver.GetType() != HarvestAndHaulCompat.UnloadDriver
                || currentItem.GetValue(driver) != thing || thing == null || thing.Destroyed || !owner.Contains(thing))
                return owner.TryDrop(thing, cell, map, mode, out result, placedAction, validator);

            // Recalculate at each real placement attempt: the keep may change while walking, and a failed
            // Direct placement can already have moved part of the requested quantity before Near retries.
            // Explicit HD keep counts remain meaningful when automatic HD intake is switched off.
            int count = InventorySurplus.SurplusOf(pawn, thing);
            if (count >= thing.stackCount)
                return owner.TryDrop(thing, cell, map, mode, out result, placedAction, validator);
            result = null;
            if (count <= 0) return false;
            // Native count-drop owns splitting, rollback and placedAction accounting. H&H still selects
            // storage, walks, retries Direct/Near, deregisters its harvested stack and advances its toils.
            return ((ThingOwner)owner).TryDrop(thing, cell, map, mode, count, out result, placedAction, validator);
        }
    }
}
