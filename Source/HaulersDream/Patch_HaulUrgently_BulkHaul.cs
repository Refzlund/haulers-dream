using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Compatibility: make "Haul Urgently" sweep in bulk like every other HD haul, instead of falling back to
    /// vanilla's one-item-at-a-time carry.
    ///
    /// Allow Tool (<c>unlimitedhugs.allowtool</c>) and its performance-friendly reimplementation Keyz' Allow
    /// Utilities (<c>keyz182.allowtoolutils</c>) BOTH implement urgent hauling as
    /// <c>WorkGiver_HaulUrgently : WorkGiver_Scanner</c> whose <c>JobOnThing</c> returns a plain single-stack
    /// <c>HaulAIUtility.HaulToStorageJob</c> (via a <c>JobOnThingDelegate</c>). That job NEVER routes through
    /// <see cref="WorkGiver_HaulGeneral.JobOnThing"/> — the funnel <see cref="Patch_WorkGiver_HaulGeneral_BulkHaul"/>
    /// patches — so HD's bulk sweep never saw it and an urgent haul moved one stack per trip.
    ///
    /// This postfix runs the EXACT same conversion HD uses for ordinary hauls (<see cref="BulkHaul.TryBuildBulkJob"/>):
    /// when the urgent giver hands back a HaulToCell job and a sweep is worth it, swap it for a
    /// <see cref="JobDriver_BulkHaul"/> that picks up the whole nearby cluster and makes one storage trip. It
    /// inherits most of HD's bulk-haul gating (the <c>bulkHaul</c> setting, eligibility, the map gate, the carry
    /// ceiling, and the <c>HasPotentialBulkWork</c> automatic front gate) for free — when HD's bulk-haul is off,
    /// urgent hauls stay vanilla, exactly as before. It does NOT inherit the SecondTasked/Always TRIGGER: passing
    /// <c>forceSweep: true</c> deliberately bypasses it, because an urgent order should sweep on its own (like HD's
    /// "Haul everything nearby") rather than wait for a second queued haul. A lone bulky stack still defers to a hand
    /// carry under Combat Extended via the build's count&lt;2 tail, so only a real nearby cluster is backpacked.
    ///
    /// SOFT DEPENDENCY: neither mod is referenced at compile time — the targets are resolved by string via
    /// <see cref="AccessTools"/>, and <see cref="Prepare"/> skips the whole patch when neither type is loaded, so
    /// HD compiles and runs identically with or without either mod present. Discovered + applied by HD's resilient
    /// patcher (<c>HaulersDreamMod.ApplyPatchesResilient</c>) like every other HD patch container.
    ///
    /// NOTE (the PUAH rebind, unchanged): both mods carry a Pick Up And Haul compat handler that, when PUAH is
    /// present, rebinds their <c>JobOnThingDelegate</c> to PUAH's bulk-into-inventory giver. HD is a PUAH
    /// replacement (it ships no <c>PickUpAndHaul.*</c> type), so that rebind never targets HD; if PUAH IS also
    /// installed (unsupported), the urgent job becomes a PUAH inventory job (not a HaulToCell) and
    /// <see cref="BulkHaul.TryBuildBulkJob"/> declines it — no conflict either way.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_HaulUrgently_BulkHaul
    {
        // The two known "Haul Urgently" workgivers, by full type name (string — no compile-time dependency).
        private static readonly string[] UrgentWorkGiverTypes =
        {
            "KeyzAllowUtilities.WorkGiver_HaulUrgently", // Keyz' Allow Utilities (keyz182.allowtoolutils)
            "AllowTool.WorkGiver_HaulUrgently",          // Allow Tool (unlimitedhugs.allowtool)
        };

        // Skip the entire patch unless at least one of the urgent-haul mods is loaded.
        static bool Prepare()
        {
            foreach (var typeName in UrgentWorkGiverTypes)
                if (AccessTools.TypeByName(typeName) != null)
                    return true;
            return false;
        }

        // Patch JobOnThing on every urgent-haul giver that resolves. Both override the standard
        // WorkGiver_Scanner.JobOnThing(Pawn, Thing, bool) — resolved by explicit parameter types so the exact
        // 3-arg override is found regardless of any unrelated overloads.
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var typeName in UrgentWorkGiverTypes)
            {
                var type = AccessTools.TypeByName(typeName);
                var method = type != null
                    ? AccessTools.Method(type, "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) })
                    : null;
                if (method != null)
                    yield return method;
            }
        }

        // Positional arg injection (__0/__1/__2) — name-independent, so it binds correctly even if the two mods
        // name JobOnThing's parameters differently. __0 = pawn, __1 = the thing, __2 = forced.
        // No try/catch: a failure here is a real bug to surface (Harmony propagates to RimWorld's handler), not
        // silently downgraded — matching Patch_WorkGiver_HaulGeneral_BulkHaul.
        static void Postfix(ref Job __result, Pawn __0, Thing __1, bool __2)
        {
            // forceSweep: true — "Haul Urgently" is a deliberate aggressive order, so (like HD's own "Haul everything
            // nearby") it bypasses the Combat Extended #115 guard ("backpacking one bulky stack is worse than hands"),
            // which otherwise leaves an urgent haul as a single-stack HAND carry under CE — the reported CE + Keyz
            // "pawns no longer backpack urgent hauls" break. On the automatic scan path (forced/__2 = false) the
            // HasPotentialBulkWork front gate still runs, so a LONE bulky stack with nothing nearby stays a hand-carry
            // (CE #115's benefit kept) and only a real nearby CLUSTER is swept into the backpack.
            var bulk = BulkHaul.TryBuildBulkJob(__0, __1, __result, __2, forceSweep: true);
            if (bulk != null)
                __result = bulk;
        }
    }
}
