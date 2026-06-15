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
            // Substitute only when our unload can actually PROGRESS right now: at least one tagged stack that is
            // still in inventory, reservable by this pawn, AND genuinely SURPLUS (above the pawn's keep-stock).
            // Otherwise (every tagged stack reserved by another pawn, OR all tagged stock is now keep-stock — e.g.
            // carried ammo a compat mod keeps, which has SurplusOf 0) our job would end with nothing to do while
            // vanilla's UnloadEverything flag stays set — the think tree re-issues it every few ticks forever, and
            // vanilla's own unload (which always progresses by dropping near) never drains the flag. The SurplusOf
            // check matches PawnUnloadChecker.AnyUnloadable and the driver's own FirstUnloadableThing selection,
            // so the three agree on "is there anything to unload."
            var inner = pawn.inventory?.innerContainer;
            if (inner == null)
                return;
            bool anyUnloadable = false;
            foreach (var t in carried)
            {
                if (t != null && inner.Contains(t) && pawn.CanReserve(t) && InventorySurplus.SurplusOf(pawn, t) > 0)
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

    /// <summary>
    /// "Put the load away before relaxing." RimWorld evaluates the rest / food / joy job-givers ABOVE work in
    /// the think tree, so a tired/hungry pawn enters downtime before JobGiver_Work (and the end-of-run trigger)
    /// is ever reached. These three postfixes swap the downtime job for an unload trip when the pawn is carrying
    /// scooped goods and the matching toggle is on; once it unloads (and is empty) the need-giver re-fires next
    /// determination and the pawn rests/eats/relaxes normally. A severity gate keeps a critically tired/starving
    /// pawn from detouring — it sleeps/eats now (the load is then caught on wake by the interval safety net).
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
    public static class Patch_JobGiver_GetRest_UnloadFirst
    {
        static void Postfix(ref Job __result, Pawn pawn)
        {
            // Don't detour an EXHAUSTED pawn (about to collapse) — let it sleep now.
            if (__result == null || pawn?.needs?.rest == null || pawn.needs.rest.CurCategory == RestCategory.Exhausted)
                return;
            var unload = OpportunisticUnload.TryGetPreDowntimeUnloadJob(pawn, HaulersDreamMod.Settings?.unloadBeforeSleep ?? false);
            if (unload != null)
                __result = unload;
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class Patch_JobGiver_GetFood_UnloadFirst
    {
        static void Postfix(ref Job __result, Pawn pawn)
        {
            // Don't detour a STARVING pawn (taking damage) — let it eat now.
            if (__result == null || pawn?.needs?.food == null || pawn.needs.food.CurCategory == HungerCategory.Starving)
                return;
            var unload = OpportunisticUnload.TryGetPreDowntimeUnloadJob(pawn, HaulersDreamMod.Settings?.unloadBeforeEating ?? false);
            if (unload != null)
                __result = unload;
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetJoy), "TryGiveJob")]
    public static class Patch_JobGiver_GetJoy_UnloadFirst
    {
        static void Postfix(ref Job __result, Pawn pawn)
        {
            if (__result == null)
                return;
            var unload = OpportunisticUnload.TryGetPreDowntimeUnloadJob(pawn, HaulersDreamMod.Settings?.unloadBeforeLeisure ?? false);
            if (unload != null)
                __result = unload;
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
            if (s == null || __instance.Faction != Faction.OfPlayerSilentFail)
                yield break;

            var comp = __instance.GetComp<CompHauledToInventory>();
            if (comp == null)
                yield break;

            // Per-pawn auto-haul opt-out. A FUNCTIONAL standing control (not a one-shot action), so — unlike the
            // "Unload inventory" button below — it is NOT governed by s.hideGizmo (whose label describes only the
            // Unload button); a player who hid that button must still be able to toggle a pawn's auto-hauling.
            // Shown for any scoop-eligible player pawn (the same eligibility that decides whether this pawn would
            // ever scoop), independent of whether it currently carries anything. Command_Toggle aggregates across a
            // multi-select automatically (matching isActive merges the buttons; toggleAction fires per pawn).
            if (YieldRouter.IsEligible(__instance))
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "HaulersDream.Gizmo.AutoHaul".Translate(),
                    defaultDesc = "HaulersDream.Gizmo.AutoHaulDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Buttons/Drop", false) ?? BaseContent.BadTex,
                    isActive = () => comp.autoHaulYields,
                    toggleAction = () => comp.autoHaulYields = !comp.autoHaulYields
                };
            }

            // The "Unload inventory" button below is a one-shot convenience — hideGizmo may hide it.
            if (s.hideGizmo)
                yield break;

            // Show the gizmo when HD has tagged stock OR the pawn carries foreign surplus a forced unload would
            // actually adopt. With the global "unload all surplus" toggle on that's ANY surplus; with it off it's
            // only stock whose def has an explicit surplus-producing rule (keep-at-most / always-unload) — matching
            // exactly what AdoptSurplusInventory tags in each case, so the button is never shown as a no-op. Both
            // checks are read-only (no tagging on the render path); clicking runs forced CheckIfShouldUnload, which
            // does the adopting + unload. Caravan-loading inventory is intentional, so it's excluded.
            bool hasTagged = comp.GetHashSet().Count > 0;
            bool hasForeignSurplus = !__instance.IsFormingCaravan() && (s.unloadAllSurplus
                ? InventorySurplus.HasAnySurplus(__instance)
                : (s.HasAnySurplusProducingRule && InventorySurplus.HasAnyRuledSurplus(__instance)));
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
    /// #4 ANIMAL HAULERS (opt-in, default OFF): give trained colony hauling animals the same bulk-into-inventory
    /// sweep colonists/mechs already get. Animals never reach HD's <see cref="WorkGiver_HaulGeneral"/> postfix —
    /// they have no workSettings and haul through the THINK TREE, where the Animal think tree's
    /// <c>ThinkNode_ConditionalTrainableCompleted(Haul)</c> subtree runs <see cref="JobGiver_Haul"/>, whose
    /// <c>TryGiveJob</c> finds the nearest haulable and returns <c>HaulAIUtility.HaulToStorageJob(pawn, thing,
    /// forced:false)</c> — a single-stack <see cref="JobDefOf.HaulToCell"/> job (decompile-verified RW1.6). This
    /// postfix upgrades that single-item haul into HD's bulk-to-inventory job via the SAME planner the work scan
    /// uses (<see cref="BulkHaul.TryBuildBulkJob"/>), so the swept stacks are tagged on the animal's
    /// <see cref="CompHauledToInventory"/> (every <c>thingClass="Pawn"</c> def with a comps node gets the comp —
    /// animals included) and serviced by the normal unload pass — keeping scoop/bulk/unload symmetric.
    ///
    /// CONSERVATIVE SCOPE: restricted to non-humanlike non-mechanoid pawns (animals) — humanlikes/mechs already
    /// get the bulk sweep on the work-scan path and we must not alter their behavior here. The deeper gates live
    /// in <see cref="BulkHaul.TryBuildBulkJob"/>: it requires <c>Faction.OfPlayer</c> (so wild/enemy/insect
    /// animals — never player faction — are rejected), <see cref="YieldRouter.IsEligible"/> (which returns
    /// <c>allowAnimals</c> for an animal, so with the setting OFF this is false and the whole path is a no-op →
    /// byte-identical to today), the tracking comp, and the bulkHaul/map gates. No try/catch: a failure is a real
    /// bug to surface, exactly like <see cref="Patch_WorkGiver_HaulGeneral_BulkHaul"/>.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Haul), "TryGiveJob")]
    public static class Patch_JobGiver_Haul_AnimalBulk
    {
        static void Postfix(ref Job __result, Pawn pawn)
        {
            // Cheap, allocation-free pre-gates before the planner runs: feature on, an animal, with a haul job
            // to upgrade. allowAnimals defaults OFF — when off this returns immediately and behavior is unchanged.
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.allowAnimals || !s.bulkHaul || __result == null || pawn?.RaceProps == null)
                return;
            // ANIMALS only here (the think-tree path). Humanlikes/mechs that also run JobGiver_Haul in other
            // subtrees keep their unchanged result — their bulk sweep comes from the work-scan postfix.
            if (pawn.RaceProps.Humanlike || pawn.RaceProps.IsMechanoid)
                return;
            // The primary is the thing the produced haul targets (StartCarryThing retargets later, but at
            // JobGiver_Haul time targetA is still the grounded stack). TryBuildBulkJob requires a HaulToCell
            // vanilla job (container destinations keep their flow) — it rejects anything else internally.
            var primary = __result.targetA.Thing;
            if (primary == null)
                return;
            var bulk = BulkHaul.TryBuildBulkJob(pawn, primary, __result, forced: false);
            if (bulk != null)
                __result = bulk;
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

    /// <summary>
    /// "Second haul order takes over immediately." Under BulkHaulTrigger.SecondTasked, ordering a SINGLE haul
    /// stays surgical, but ordering a SECOND nearby haul means "clean up this area" — the bulk sweep should
    /// start NOW, not after the pawn finishes hauling the first item solo and comes back. By the time the
    /// second order reaches the job tracker it is ALREADY a HaulersDream_BulkHaul (the JobOnThing postfix
    /// converted it because a nearby first order existed), so this prefix only needs to fold it into a running
    /// sweep, or interrupt the still-solo first haul and start the (first-item-inclusive) sweep immediately.
    /// All policy lives in <see cref="BulkHaul.TryTakeoverSecondOrder"/>. Sibling to the F35 pack-animal
    /// coalescer above — both early-return on a non-matching job def, so they never collide. Fires only on
    /// player orders (TryTakeOrderedJob), not per tick.
    /// </summary>
    [HarmonyPatch(typeof(Verse.AI.Pawn_JobTracker), nameof(Verse.AI.Pawn_JobTracker.TryTakeOrderedJob))]
    public static class Patch_TryTakeOrderedJob_BulkHaulTakeover
    {
        private static readonly AccessTools.FieldRef<Verse.AI.Pawn_JobTracker, Pawn> PawnOf =
            AccessTools.FieldRefAccess<Verse.AI.Pawn_JobTracker, Pawn>("pawn");

        // Deliberately ignores requestQueueing (shift): the user wants the sweep to take over IMMEDIATELY even
        // when the second order was shift-queued — the first item is absorbed into the one-trip sweep, and
        // unrelated queued work is preserved (TryTakeoverSecondOrder does not ClearQueuedJobs).
        static bool Prefix(Verse.AI.Pawn_JobTracker __instance, Verse.AI.Job job, JobTag? tag, ref bool __result)
        {
            // Only a player-ordered bulk haul can take over; vanilla single hauls (the FIRST, surgical order),
            // pack-animal loads, and every other job run vanilla unchanged.
            if (job == null || job.def != HaulersDreamDefOf.HaulersDream_BulkHaul)
                return true;
            var pawn = PawnOf(__instance);
            if (pawn == null)
                return true;
            return !BulkHaul.TryTakeoverSecondOrder(pawn, job, tag, ref __result);
        }
    }
}
