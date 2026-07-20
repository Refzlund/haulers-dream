using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Compatibility: make "Haul Urgently" pocket the nearby urgent-marked cluster into the backpack in ONE trip,
    /// instead of vanilla's one-item-at-a-time carry (the reported "pawns pick urgent items one by one").
    ///
    /// Allow Tool (<c>unlimitedhugs.allowtool</c>) and its performance-friendly reimplementation Keyz' Allow
    /// Utilities (<c>keyz182.KeyzAllowUtilities</c>) BOTH implement urgent hauling as
    /// <c>WorkGiver_HaulUrgently : WorkGiver_Scanner</c> whose <c>JobOnThing</c> returns a plain single-stack
    /// <c>HaulAIUtility.HaulToStorageJob</c> (via a <c>JobOnThingDelegate</c>). That job NEVER routes through
    /// <see cref="WorkGiver_HaulGeneral.JobOnThing"/> — the funnel <see cref="Patch_WorkGiver_HaulGeneral_BulkHaul"/>
    /// patches — so HD's bulk sweep never saw it and an urgent haul moved one stack per trip.
    ///
    /// This postfix converts the urgent haul into a <see cref="JobDriver_BulkHaul"/> in two INDEPENDENT stages:
    /// <list type="number">
    ///   <item><b>Feature 1</b> (<see cref="HaulersDreamSettings.bulkHaulUrgent"/>, default ON, independent of the
    ///   general <c>bulkHaul</c> toggle): <see cref="UrgentHaulBulk.TryBuild"/> pockets the OTHER urgent-marked
    ///   stacks within <see cref="HaulersDreamSettings.bulkHaulUrgentRadius"/> of the anchor, urgent-first. Unlike
    ///   HD's general sweep it does NOT require each urgent stack to have strictly-better storage (urgent means
    ///   "move it now"; the storage-aware unload re-homes them): the layer-2 fix for an urgent cluster with
    ///   nowhere better collapsing to a single stack. When it builds a job, that job wins and we stop.</item>
    ///   <item><b>Feature 2</b> (<see cref="HaulersDreamSettings.bulkHaulUrgentIncludeNonUrgent"/>, opt-in): on an
    ///   urgent trip ALSO fold in ordinary nearby haulables. UrgentHaulBulk already adds them alongside the urgent
    ///   ones (with normal better-storage eligibility) in its small radius; this stage additionally falls back to
    ///   HD's general bulk sweep (<see cref="BulkHaul.TryBuildBulkJob"/>, wider radius, <c>forceSweep: true</c>)
    ///   when there was no urgent cluster to build. With it OFF (default) an urgent trip carries only urgent items.</item>
    /// </list>
    ///
    /// LAYER 1 (elsewhere): <see cref="ForeignOrderGuard"/> now whitelists the Keyz urgent designation (via
    /// <see cref="UrgentHaulCompat"/>) alongside vanilla "Haul" and Allow Tool's, so a Keyz-urgent item is no
    /// longer misread as foreign-claimed, which had been blocking the general bulk conversion + en-route/sweep
    /// pickups for it.
    ///
    /// SOFT DEPENDENCY: neither mod is referenced at compile time — the targets are resolved by string via
    /// <see cref="AccessTools"/>, and <see cref="Prepare"/> skips the whole patch when neither type is loaded, so
    /// HD compiles and runs identically with or without either mod present. Discovered + applied by HD's resilient
    /// patcher (<c>HaulersDreamMod.ApplyPatchesResilient</c>) like every other HD patch container.
    ///
    /// NOTE (the PUAH rebind, unchanged): both mods carry a Pick Up And Haul compat handler that, when PUAH is
    /// present, rebinds their <c>JobOnThingDelegate</c> to PUAH's bulk-into-inventory giver. HD is a PUAH
    /// replacement (it ships no <c>PickUpAndHaul.*</c> type), so that rebind never targets HD; if PUAH IS also
    /// installed (unsupported), the urgent job becomes a PUAH inventory job (not a HaulToCell) and both stages
    /// decline it, no conflict either way.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_HaulUrgently_BulkHaul
    {
        // The two known "Haul Urgently" workgivers, by full type name (string — no compile-time dependency).
        private static readonly string[] UrgentWorkGiverTypes =
        {
            "KeyzAllowUtilities.WorkGiver_HaulUrgently", // Keyz' Allow Utilities (keyz182.KeyzAllowUtilities)
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
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return; // settings not loaded yet, leave vanilla's single urgent haul untouched
            // Feature 1 (independent of the general bulkHaul toggle): pocket the nearby urgent-marked cluster into
            // the backpack in one trip. When it builds a job it wins outright and no further conversion runs.
            if (s.bulkHaulUrgent)
            {
                var urgent = UrgentHaulBulk.TryBuild(__0, __1, __result, __2, s.bulkHaulUrgentIncludeNonUrgent);
                if (urgent != null)
                {
                    __result = urgent;
                    return;
                }
            }
            // Feature 2 (opt-in): on an urgent trip also fold ordinary nearby haulables into the backpack via HD's
            // general bulk sweep (wider radius, normal better-storage eligibility), the fallback when there was no
            // urgent cluster to build. forceSweep: true, an urgent order is a deliberate aggressive sweep (bypasses
            // the SecondTasked trigger, like "Haul everything nearby"); a lone bulky stack still defers to a hand
            // carry under Combat Extended via the general builder's count<2 tail, so only a real cluster is backpacked.
            if (s.bulkHaulUrgentIncludeNonUrgent)
            {
                var bulk = BulkHaul.TryBuildBulkJob(__0, __1, __result, __2, forceSweep: true);
                if (bulk != null)
                    __result = bulk;
            }
        }
    }
}
