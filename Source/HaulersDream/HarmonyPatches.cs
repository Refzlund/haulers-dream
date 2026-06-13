using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
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
    /// GenSpawn) — but the GenPlace prefix never ROUTES them, because JobDriver_Deconstruct is
    /// deliberately absent from YieldRouter.TryGetWorkType (adding it there would double-process
    /// every leaving: prefix consume + the capture's scoop). So we CAPTURE instead: the prefix opens a
    /// capture window crediting the deconstructing pawn, the GenPlace postfix records the exact item each
    /// placement produces (wherever Near-placement put it, including a merge into a pre-existing stack),
    /// and the postfix here scoops them once DoLeavingsFor finishes. This replaces the old "snapshot the
    /// footprint rect and diff" path, which missed leavings that spilled outside the footprint or merged
    /// into a pre-existing ground stack. Positional (__N) injection so it's robust to param names.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_GenLeaving_DoLeavingsFor
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(GenLeaving), nameof(GenLeaving.DoLeavingsFor), new[]
        {
            typeof(Thing), typeof(Map), typeof(DestroyMode), typeof(CellRect), typeof(Predicate<IntVec3>), typeof(List<Thing>)
        });

        static void Prefix(Thing __0, Map __1, DestroyMode __2, CellRect __3)
        {
            if (__2 == DestroyMode.Deconstruct && __1 != null)
                YieldRouter.BeginDeconstructCapture(__3, __1, __0); // __0 = the deconstructed thing (self-gated on settings)
        }

        // Finalizer (not Postfix) so the capture window is ALWAYS closed, even if DoLeavingsFor throws (e.g. a
        // modded leaving's spawn fails) — otherwise the ThreadStatic capture state could leak into the next
        // placement. We take no Exception parameter and return nothing, so any in-flight exception is preserved
        // and still surfaces (the no-suppression rule: we clean up, we don't swallow).
        static void Finalizer(DestroyMode __2)
        {
            if (__2 == DestroyMode.Deconstruct)
                YieldRouter.EndDeconstructCapture();
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
    /// F6 (pass-by unload) + the end-of-work-run trigger, both on the work node's own think result:
    /// <list type="bullet">
    /// <item>Work WAS found: if the pawn carries scooped goods and its storage is roughly on the way to
    /// the new job, divert it to unload first — rather than hauling the load across the map and making a
    /// dedicated trip later. After unloading it re-picks work normally (now empty, so it won't
    /// re-divert). See <see cref="OpportunisticUnload.ShouldDivert"/>.</item>
    /// <item>Work ran DRY: the run is over — unload NOW, before the priority sorter falls through to
    /// recreation/wandering with a full backpack. This is the trigger the settings have always promised
    /// ("at end of work run"); needs that outrank work in the sorter (urgent food, rest) still win, and
    /// a pawn whose next determination picks joy directly is caught by the GameComponent backstop
    /// instead. See <see cref="OpportunisticUnload.TryGetEndOfRunUnloadJob"/>.</item>
    /// </list>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_OpportunisticUnload
    {
        static void Postfix(ref ThinkResult __result, Pawn pawn, JobGiver_Work __instance)
        {
            if (__result.IsValid && __result.Job != null)
            {
                // If the pawn just picked a NON-yield, NON-haul job, its accumulate run is over — divert it to
                // shed its load at nearby storage first (relaxed run-end criteria). While it keeps picking
                // yield work, runOver is false and the strict journey bar applies, so a continuing mining/
                // deconstruct run is never interrupted (F38 preserved).
                bool runOver = !OpportunisticUnload.IsYieldOrHaulJobDef(__result.Job.def);
                if (!OpportunisticUnload.ShouldDivert(pawn, __result.Job, runOver))
                    return;
                var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadInventory);
                if (job.TryMakePreToilReservations(pawn, false))
                {
                    OpportunisticUnload.NotifyDiverted(pawn);
                    __result = new ThinkResult(job, __result.SourceNode, __result.Tag, __result.FromQueue);
                }
                return;
            }

            // No work left for this pawn — end of its work run. (Fully gated inside, incl. a cooldown;
            // returns null for pawns with nothing tracked, so the common idle case is two cheap checks.)
            var unload = OpportunisticUnload.TryGetEndOfRunUnloadJob(pawn);
            if (unload != null)
                __result = new ThinkResult(unload, __instance, JobTag.UnloadingOwnInventory, false);
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
            if (comp == null)
                yield break;
            // Show the gizmo when HD has tagged stock OR (unloadAllSurplus) the pawn carries any foreign surplus
            // it would adopt — so the button appears immediately, not only after the backstop's first adopt pass.
            // HasAnySurplus is read-only (no tagging on the render path); clicking runs forced CheckIfShouldUnload,
            // which does the adopting + unload. Caravan-loading inventory is intentional, so it's excluded.
            bool hasTagged = comp.GetHashSet().Count > 0;
            bool hasForeignSurplus = s.unloadAllSurplus && !__instance.IsFormingCaravan()
                                     && InventorySurplus.HasAnySurplus(__instance);
            if (!hasTagged && !hasForeignSurplus)
                yield break;

            var unload = new Command_Action
            {
                defaultLabel = "HaulersDream.Gizmo.UnloadNow".Translate(),
                defaultDesc = "HaulersDream.Gizmo.UnloadNowDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Drop", false) ?? BaseContent.BadTex,
                action = () =>
                {
                    // On a non-home / temporary map there is no storage — the gizmo loads the nearest pack animal
                    // with the carried loot instead of the (no-op) storage unload.
                    if (__instance.Map != null && !__instance.Map.IsPlayerHome)
                        PackAnimalLoad.GizmoLoadNearest(__instance);
                    else
                        PawnUnloadChecker.CheckIfShouldUnload(__instance, true);
                }
            };
            // The unload checker hard-gates drafted pawns (they must stand to orders, not march to
            // storage) — show that as a disabled reason instead of a button that silently does nothing.
            if (__instance.Drafted)
                unload.Disable("HaulersDream.Gizmo.UnloadNowDrafted".Translate());
            yield return unload;
        }
    }

    /// <summary>
    /// Coalesce vanilla "Load onto pack animal" orders into ONE trip. Each vanilla GiveToPackAnimal order is a
    /// one-stack-in-hands job, so shift-clicking several = several trips. This redirects them into HD's
    /// inventory-based <see cref="JobDriver_LoadPackAnimal"/>: the first becomes one HD load job, and each
    /// subsequent order APPENDS its item to that job's sweep queue — so the pawn sweeps them all into inventory
    /// and loads the animal in one trip (the job loops fill→deposit, so a large stack still fully loads). Only
    /// on a caravan/away map with the feature on; off the away map (or with no carrier) vanilla is untouched.
    /// TryTakeOrderedJob fires only on PLAYER ORDERS (not per tick), so the patch is cheap.
    /// </summary>
    [HarmonyPatch(typeof(Verse.AI.Pawn_JobTracker), nameof(Verse.AI.Pawn_JobTracker.TryTakeOrderedJob))]
    public static class Patch_TryTakeOrderedJob_CoalescePackAnimalLoad
    {
        private static readonly AccessTools.FieldRef<Verse.AI.Pawn_JobTracker, Pawn> PawnOf =
            AccessTools.FieldRefAccess<Verse.AI.Pawn_JobTracker, Pawn>("pawn");

        static bool Prefix(Verse.AI.Pawn_JobTracker __instance, Verse.AI.Job job, JobTag? tag,
            bool requestQueueing, ref bool __result)
        {
            if (job?.def != JobDefOf.GiveToPackAnimal)
                return true; // not a pack-animal load order — run vanilla
            var pawn = PawnOf(__instance);
            if (!PackAnimalLoad.ShouldRedirectGiveToPackAnimal(pawn, job))
                return true; // feature off / at home / no carrier -> vanilla single-stack load
            var item = job.targetA.Thing;
            int count = job.count > 0 ? job.count : item.stackCount;
            var existing = PackAnimalLoad.FindActiveLoadJob(pawn);
            if (existing != null)
            {
                // Coalesce into the in-progress / queued HD load job — one trip for all the loads.
                PackAnimalLoad.AppendToLoadJob(existing, item, count);
                __result = true;
                return false;
            }
            var hd = PackAnimalLoad.BuildRedirectJob(pawn, job);
            if (hd == null)
                return true; // couldn't build (carrier vanished) -> let vanilla handle it
            // Re-enter with the HD job (not a GiveToPackAnimal, so this prefix passes it through). The recursion
            // preserves the player's queue-vs-now choice (TryTakeOrderedJob reads the same shift state).
            __result = __instance.TryTakeOrderedJob(hd, tag, requestQueueing);
            return false;
        }
    }
}
