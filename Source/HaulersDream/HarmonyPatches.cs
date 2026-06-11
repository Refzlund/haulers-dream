using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Yield hook #1: plants, mining, deep-drill and animal products all spawn via
    /// GenPlace.TryPlaceThing (verified by decompiling Assembly-CSharp). A prefix on the canonical
    /// out-overload routes the yield into the working pawn's inventory.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_GenPlace_TryPlaceThing
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(GenPlace), nameof(GenPlace.TryPlaceThing), new[]
        {
            typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(),
            typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)
        });

        // __state carries the producer from prefix to postfix PER INVOCATION (Harmony binds it by name
        // across the pair) — structurally immune to nested TryPlaceThing calls, which would clear a
        // static handoff (a modded comp spawning a side product mid-placement → missed scoop).
        static bool Prefix(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode, ref Thing lastResultingThing, ref bool __result, out Pawn __state)
            => YieldRouter.OnTryPlaceThing(thing, center, map, mode, ref lastResultingThing, ref __result, out __state);

        // DropThenHaul mode: after vanilla places the yield, record it for the producer to scoop up.
        static void Postfix(Thing lastResultingThing, Pawn __state) => YieldRouter.OnTryPlaceThingPost(lastResultingThing, __state);
    }

    /// <summary>
    /// Yield hook #2: deconstruction leavings ALSO travel through the patched GenPlace overload
    /// (DoLeavingsFor places them via ThingOwner.TryDrop → GenDrop → GenPlace; only detritus uses
    /// GenSpawn) — but the GenPlace prefix never routes them, because JobDriver_Deconstruct is
    /// deliberately absent from YieldRouter.TryGetWorkType (adding it there would double-process
    /// every leaving: prefix consume + this postfix's scoop). So this postfix IS the deconstruct
    /// path: we snapshot the items in the leavings rect before DoLeavingsFor runs and, afterwards,
    /// scoop only the items that newly appeared (never pre-existing ground items) into the
    /// deconstructing pawn's inventory. Positional (__N) injection so it's robust to param names;
    /// __state carries the snapshot from prefix to postfix.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_GenLeaving_DoLeavingsFor
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(GenLeaving), nameof(GenLeaving.DoLeavingsFor), new[]
        {
            typeof(Thing), typeof(Map), typeof(DestroyMode), typeof(CellRect), typeof(Predicate<IntVec3>), typeof(List<Thing>)
        });

        static void Prefix(Map __1, DestroyMode __2, CellRect __3, out HashSet<Thing> __state)
        {
            __state = null;
            var s = HaulersDreamMod.Settings;
            if (__2 == DestroyMode.Deconstruct && __1 != null && s != null && s.haulDeconstruct)
                __state = YieldRouter.SnapshotItems(__3, __1);
        }

        static void Postfix(Thing __0, Map __1, DestroyMode __2, CellRect __3, HashSet<Thing> __state)
        {
            if (__2 == DestroyMode.Deconstruct && __state != null)
                YieldRouter.OnDeconstructLeavings(__3, __1, __state, __0); // __0 = the deconstructed thing
        }
    }

    // NOTE: the old idle backstop here patched Verse.AI.JobGiver_Idle.TryGiveJob — but in RimWorld 1.6 that
    // class appears ONLY in gathering/ritual DUTY think trees, never in the ordinary colonist tree (idle
    // colonists wander via JobGiver_WanderColony and the distinct JobGiver_Idle* classes). The patch was
    // silently dead for its intended audience. The idle backstop now lives in HaulersDreamGameComponent,
    // which checks genuinely idle colonists on a short interval.

    /// <summary>
    /// F2: if the think-tree ever decides to run vanilla's unload (e.g. caravan loot sets
    /// UnloadEverything — we deliberately no longer set it ourselves, to avoid preempting work after every
    /// pickup), substitute our own unload job, which uses TryFindBestBetterStorageFor (proper storage,
    /// respecting filters and priorities) instead of vanilla's desperate near-drop, for pawns carrying our
    /// tracked items. Our own auto-unload is driven by PawnUnloadChecker (full / idle / interval / gizmo).
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_UnloadYourInventory), nameof(JobGiver_UnloadYourInventory.TryGiveJob))]
    public static class Patch_JobGiver_UnloadYourInventory
    {
        static void Postfix(ref Job __result, Pawn pawn)
        {
            if (__result == null || pawn == null)
                return; // vanilla didn't want to unload -> nothing to substitute
            var comp = pawn.GetComp<CompHauledToInventory>();
            var carried = comp?.GetHashSet();
            if (carried == null || carried.Count == 0)
                return; // no tracked items -> leave vanilla's unload as-is
            if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_UnloadInventory)
                return;
            // Substitute only when our unload can actually PROGRESS right now: at least one tagged stack
            // still in inventory and reservable by this pawn. Otherwise (e.g. other pawns hold
            // reservations on every tagged stack) our job ends Incompletable instantly while vanilla's
            // UnloadEverything flag stays set — the think tree re-issues it every few ticks forever, and
            // vanilla's own unload (which always progresses by dropping near) never drains the flag.
            var inner = pawn.inventory?.innerContainer;
            if (inner == null)
                return;
            bool anyUnloadable = false;
            foreach (var t in carried)
            {
                if (t != null && inner.Contains(t) && pawn.CanReserve(t))
                {
                    anyUnloadable = true;
                    break;
                }
            }
            if (!anyUnloadable)
                return;
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadInventory);
            if (job.TryMakePreToilReservations(pawn, false))
                __result = job;
        }
    }

    /// <summary>
    /// F6 (pass-by unload): when the work think-tree is about to send a pawn that's carrying scooped goods
    /// off on a real journey, and its storage is roughly on the way, divert it to unload first — rather
    /// than hauling the load across the map and making a dedicated trip later. After unloading it re-picks
    /// work normally (now empty, so it won't re-divert). See <see cref="OpportunisticUnload"/>.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_OpportunisticUnload
    {
        static void Postfix(ref ThinkResult __result, Pawn pawn)
        {
            if (!__result.IsValid || __result.Job == null)
                return;
            if (!OpportunisticUnload.ShouldDivert(pawn, __result.Job))
                return;
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadInventory);
            if (job.TryMakePreToilReservations(pawn, false))
            {
                OpportunisticUnload.NotifyDiverted(pawn);
                __result = new ThinkResult(job, __result.SourceNode, __result.Tag, __result.FromQueue);
            }
        }
    }

    /// <summary>The per-pawn "Unload inventory" gizmo.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var gizmo in __result)
                yield return gizmo;

            var s = HaulersDreamMod.Settings;
            if (s == null || s.hideGizmo || __instance.Faction != Faction.OfPlayerSilentFail)
                yield break;

            var comp = __instance.GetComp<CompHauledToInventory>();
            if (comp == null || comp.GetHashSet().Count == 0)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "HaulersDream.Gizmo.UnloadNow".Translate(),
                defaultDesc = "HaulersDream.Gizmo.UnloadNowDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Drop", false) ?? BaseContent.BadTex,
                action = () => PawnUnloadChecker.CheckIfShouldUnload(__instance, true)
            };
        }
    }
}
