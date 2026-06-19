using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Route vanilla single-item portal loading through HD bulk. The automatic work scan
    /// (<see cref="WorkGiver_HaulToPortal"/>) and the portal's own "any pawn can load now" check both funnel through
    /// <c>EnterPortalUtility.HasJobOnPortal</c> / <c>.JobOnPortal</c>. When the feature is on and a
    /// <see cref="MapPortalBulkTarget"/> can be created, these prefixes answer with HD's bulk path
    /// (<c>HasPotentialBulkWorkPortal</c> / <c>TryGiveBulkJob</c>) and skip vanilla.
    ///
    /// FAIL-OPEN: feature off, a null adapter, or HD finding no work → return true (fall through to vanilla single-
    /// item loading unchanged). The two answers can't diverge — both re-check the same gate.
    /// </summary>
    [HarmonyPatch(typeof(EnterPortalUtility), nameof(EnterPortalUtility.HasJobOnPortal))]
    public static class Patch_EnterPortalUtility_HasJob
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadPortal ?? true;

        static bool Prefix(Pawn pawn, MapPortal portal, ref bool __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableBulkLoadPortal)
                return true;
            var adapter = MapPortalBulkTarget.TryCreate(portal);
            if (adapter == null)
                return true; // can't adapt -> vanilla
            // Cheap pre-reject first; then BUILD the job — the count-based HasPotentialBulkWorkPortal can say "yes" while
            // the SWEEP finds nothing buildable (remaining manifest is pawns/corpses, all candidates forbidden / out of
            // radius / mass-full / claimed-by-others). Vanilla's JobGiver TRUSTS HasJob and calls JobOn; if we claimed
            // work here but JobOn returned null, vanilla's JobOnPortal would issue a haul with no valid target that ends
            // the same tick and re-fires forever (the "10 jobs in one tick" loop). So TryGiveBulkJob — the SAME
            // authoritative build the JobOn prefix runs — is the source of truth here too.
            if (!TransportLoad.HasPotentialBulkWorkPortal(pawn, adapter))
                return true; // HD has no work (not eligible / nothing claimable) -> let vanilla try
            var job = TransportLoad.TryGiveBulkJob(pawn, adapter); // side-effect-free (claim happens later in the driver) -> safe to build speculatively
            if (job == null)
                return true; // nothing HD can actually build -> let vanilla decide (its HasJob self-checks FindThingToLoad), no asymmetric loop
            __result = true;
            return false; // HD will build the same job in the JobOn prefix
        }
    }

    [HarmonyPatch(typeof(EnterPortalUtility), nameof(EnterPortalUtility.JobOnPortal))]
    public static class Patch_EnterPortalUtility_JobOn
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadPortal ?? true;

        static bool Prefix(Pawn p, MapPortal portal, ref Job __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableBulkLoadPortal)
                return true;
            var adapter = MapPortalBulkTarget.TryCreate(portal);
            if (adapter == null)
                return true;
            var job = TransportLoad.TryGiveBulkJob(p, adapter);
            if (job == null)
                return true; // HD couldn't build a job -> fall through to vanilla single-item load
            __result = job;
            return false;
        }
    }

    /// <summary>
    /// Make the portal's "is it in transit?" dialog (<c>Dialog_EnterPortal</c>) aware of HD couriers' IN-FLIGHT items.
    /// Vanilla's <c>EnterPortalUtility.ThingsBeingHauledTo</c> only yields items carried in the <c>carryTracker</c> by
    /// <c>JobDriver_HaulToPortal</c> — but HD couriers carry their swept load in INVENTORY (tagged) under
    /// <c>HaulersDream_LoadPortalInBulk</c>, so without this postfix the dialog reports nothing in transit while HD is
    /// hauling. The postfix appends, for each spawned pawn running HD's portal-load driver targeting this portal, the
    /// tagged surplus stacks of defs the portal still wants (deduped via a HashSet so a thing already yielded by
    /// vanilla isn't double-counted).
    /// </summary>
    [HarmonyPatch(typeof(EnterPortalUtility), nameof(EnterPortalUtility.ThingsBeingHauledTo))]
    public static class Patch_EnterPortalUtility_ThingsBeingHauledTo
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadPortal ?? true;

        static IEnumerable<Thing> Postfix(IEnumerable<Thing> __result, MapPortal portal)
        {
            var seen = new HashSet<Thing>();
            foreach (var t in __result)
            {
                if (t != null)
                    seen.Add(t);
                yield return t;
            }
            if (portal?.Map?.mapPawns == null)
                yield break;
            var spawned = portal.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                var p = spawned[i];
                if (p?.CurJob == null || p.CurJobDef != HaulersDreamDefOf.HaulersDream_LoadPortalInBulk)
                    continue;
                if (p.CurJob.GetTarget(TargetIndex.A).Thing != portal)
                    continue;
                var hcomp = p.GetComp<CompHauledToInventory>();
                var inner = p.inventory?.innerContainer;
                if (hcomp == null || inner == null)
                    continue;
                // PeekHashSet (read-only): this is a UI/feedback touchpoint, NOT game logic — GetHashSet would mutate.
                foreach (var carried in hcomp.PeekHashSet())
                {
                    if (carried == null || carried.Destroyed || !inner.Contains(carried) || carried.def == null)
                        continue;
                    if (!seen.Add(carried))
                        continue;
                    if (PortalStillWants(portal, carried.def))
                        yield return carried;
                }
            }
        }

        private static bool PortalStillWants(MapPortal portal, ThingDef def)
        {
            var ltl = portal.leftToLoad;
            if (ltl == null)
                return false;
            for (int i = 0; i < ltl.Count; i++)
            {
                var tr = ltl[i];
                if (tr != null && tr.ThingDef == def && tr.CountToTransfer > 0)
                    return true;
            }
            return false;
        }
    }
}
