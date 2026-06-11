using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// "All pawns can haul / clean / cut plants" — three opt-in overrides (default OFF = vanilla) that make
    /// work-incapability ("incapable of dumb labor" and friends) unable to block those three work types,
    /// for VANILLA and this mod alike.
    ///
    /// Vanilla aggregates every incapability source — backstories, traits, genes, pawn kind, conceited
    /// royal titles, ideology roles, hediff stages, quest lodger restrictions, Anomaly mutants
    /// (decompile-verified list) — into exactly two APIs: <c>Pawn.CombinedDisabledWorkTags</c> (tag-level)
    /// and <c>Pawn.GetDisabledWorkTypes</c> (work-type-level, which feeds <c>WorkTypeIsDisabled</c>).
    /// Hooking those two covers every source, present and future, modded included — and because the work
    /// tab, the float menu's "Prioritize…", the work scan, and this mod's own gates all read those same
    /// APIs, one hook makes everyone agree.
    ///
    /// Granularity matters: the Hauling and Cleaning TAGS map 1:1 onto their work types, so both levels
    /// are stripped for them; "cut plants" must NOT strip the PlantWork tag (it also covers Growing — the
    /// skilled sowing work the override doesn't promise), so PlantCutting is force-enabled at the
    /// work-type level only. The type-level hook is the load-bearing one anyway: a "dumb labor" backstory
    /// disables Hauling via the ManualDumb tag, which no Hauling-bit strip would touch.
    ///
    /// HUMANLIKE pawns only: mechanoids' disabled work types encode what the mech CAN do at all —
    /// force-enabling Hauling there would let combat mechs haul, which no one asked for.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.CombinedDisabledWorkTags), MethodType.Getter)]
    public static class Patch_Pawn_CombinedDisabledWorkTags_Override
    {
        static void Postfix(Pawn __instance, ref WorkTags __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || __result == WorkTags.None)
                return;
            if (!s.allPawnsCanHaul && !s.allPawnsCanClean)
                return;
            if (__instance?.RaceProps == null || !__instance.RaceProps.Humanlike)
                return;
            if (s.allPawnsCanHaul)
                __result &= ~WorkTags.Hauling;
            if (s.allPawnsCanClean)
                __result &= ~WorkTags.Cleaning;
            // deliberately NOT ~WorkTags.PlantWork — that tag also covers Growing (see class doc)
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes))]
    public static class Patch_Pawn_GetDisabledWorkTypes_Override
    {
        static void Postfix(Pawn __instance, ref List<WorkTypeDef> __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || __result == null || __result.Count == 0)
                return;
            if (!s.allPawnsCanHaul && !s.allPawnsCanClean && !s.allPawnsCanCutPlants)
                return;
            if (__instance?.RaceProps == null || !__instance.RaceProps.Humanlike)
                return;
            // Copy-on-filter: vanilla returns its CACHED list by reference — mutating it would make a later
            // settings toggle-off unable to restore the original disables. Reads always reflect the live
            // settings this way, so no cache invalidation is ever needed.
            List<WorkTypeDef> filtered = null;
            for (int i = 0; i < __result.Count; i++)
            {
                if (!WorkOverride.IsForceEnabled(__result[i], s))
                    continue;
                if (filtered == null)
                    filtered = new List<WorkTypeDef>(__result);
                filtered.Remove(__result[i]);
            }
            if (filtered != null)
                __result = filtered;
        }
    }

    public static class WorkOverride
    {
        /// <summary>Whether the settings force-enable this work type. Haul/clean match by TAG (so modded
        /// hauling work types like Allow Tool's "Haul urgently" are covered too); cut-plants matches the
        /// PlantCutting def exactly (the PlantWork tag would drag Growing along).</summary>
        internal static bool IsForceEnabled(WorkTypeDef def, HaulersDreamSettings s)
        {
            if (def == null)
                return false;
            if (s.allPawnsCanHaul && (def.workTags & WorkTags.Hauling) != WorkTags.None)
                return true;
            if (s.allPawnsCanClean && (def.workTags & WorkTags.Cleaning) != WorkTags.None)
                return true;
            if (s.allPawnsCanCutPlants && def == WorkTypeDefOf.PlantCutting)
                return true;
            return false;
        }

        // ── bench bill work types (the PlanCraft float-menu gate) ─────────────────────────────────────

        private static readonly Dictionary<ThingDef, List<WorkTypeDef>> billWorkTypesByBench
            = new Dictionary<ThingDef, List<WorkTypeDef>>();

        /// <summary>
        /// True when the pawn is capable of at least one work type that does bills on this bench (Cooking
        /// for a stove, Crafting for a crafting spot, …) — the same capability vanilla requires before it
        /// lets the pawn work the bench. Used to gate "Plan prioritized crafting…" exactly like vanilla
        /// gates its own "Prioritize…" option; the overrides above flow through automatically since this
        /// reads WorkTypeIsDisabled.
        /// </summary>
        internal static bool CanDoBillsAt(Pawn pawn, Building_WorkTable bench)
        {
            if (pawn == null || bench?.def == null)
                return false;
            var types = BillWorkTypesFor(bench.def);
            if (types.Count == 0)
                return true; // no DoBill giver claims this bench (modded oddity) — don't block on a guess
            for (int i = 0; i < types.Count; i++)
                if (!pawn.WorkTypeIsDisabled(types[i]))
                    return true;
            return false;
        }

        private static List<WorkTypeDef> BillWorkTypesFor(ThingDef benchDef)
        {
            if (billWorkTypesByBench.TryGetValue(benchDef, out var cached))
                return cached;
            var list = new List<WorkTypeDef>();
            var defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                var wg = defs[i];
                if (wg.workType == null || wg.fixedBillGiverDefs == null)
                    continue;
                if (!typeof(WorkGiver_DoBill).IsAssignableFrom(wg.giverClass))
                    continue;
                if (wg.fixedBillGiverDefs.Contains(benchDef) && !list.Contains(wg.workType))
                    list.Add(wg.workType);
            }
            billWorkTypesByBench[benchDef] = list;
            return list;
        }
    }
}
