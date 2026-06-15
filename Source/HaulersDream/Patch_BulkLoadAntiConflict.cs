using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Anti-conflict patch: suppress the false "loading stalled" alert while HD haulers are mid-trip. Vanilla's
    /// <c>CompTransporter.AnyPawnCanLoadAnythingNow</c> only knows about <c>HaulToTransporter</c>/<c>EnterTransporter</c>
    /// jobs — it does NOT recognize HD's bulk-load driver, so without this it returns false while an HD courier is
    /// sweeping/walking, and the launchable fires <c>MessageCantLoadMore</c> every ~60 ticks. The prefix returns
    /// true when a spawned pawn is running <c>HaulersDream_LoadTransportersInBulk</c> targeting this group, OR a
    /// claim is still live on the ledger for this group.
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), "get_AnyPawnCanLoadAnythingNow")]
    public static class Patch_CompTransporter_AnyPawnCanLoadAnythingNow
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        static bool Prefix(CompTransporter __instance, ref bool __result)
        {
            if (BulkLoadAntiConflict.AnyHdLoaderForGroup(__instance)
                || (HaulersDreamGameComponent.Instance?.LoadAnyClaimsInProgress(__instance.groupID) ?? false))
            {
                __result = true;
                return false; // suppress the false stall
            }
            return true; // no HD activity -> vanilla decides
        }
    }

    /// <summary>
    /// Anti-conflict patch: don't let a pawn BOARD while HD hauling is in flight for its group — premature boarding
    /// can trigger launch before the claimed manifest is loaded. A prefix on
    /// <c>JobGiver_EnterTransporter.TryGiveJob</c> returns null (no board job) while there is potential bulk work or
    /// a live claim — but ONLY under the boarding lord (<c>DutyDefOf.LoadAndEnterTransporters</c> + a real
    /// <c>transportersGroup</c>), never for a drafted MANUAL enter.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_EnterTransporter), "TryGiveJob")]
    public static class Patch_JobGiver_EnterTransporter_BoardGate
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        static bool Prefix(Pawn pawn, ref Job __result)
        {
            var duty = pawn?.mindState?.duty;
            if (duty == null || duty.def != DutyDefOf.LoadAndEnterTransporters)
                return true; // not the loading lord (drafted manual enter, etc.) -> vanilla
            int group = duty.transportersGroup;
            if (group < 0)
                return true;
            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger != null && ledger.LoadAnyClaimsInProgress(group))
            {
                __result = null; // hauling still in flight -> don't board yet
                return false;
            }
            // Also gate on a spawned pawn still running the HD load driver for this group (claim may settle a tick
            // before the deposit completes; the active driver is the authoritative "still loading" signal).
            if (BulkLoadAntiConflict.AnyHdLoaderForGroupId(pawn.Map, group))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    /// <summary>Shared helpers for the anti-conflict patches.</summary>
    internal static class BulkLoadAntiConflict
    {
        /// <summary>True if a spawned pawn on this transporter's map runs HD's bulk-load driver targeting this group.</summary>
        internal static bool AnyHdLoaderForGroup(CompTransporter transporter)
        {
            if (transporter?.parent?.Map == null)
                return false;
            return AnyHdLoaderForGroupId(transporter.parent.Map, transporter.groupID);
        }

        internal static bool AnyHdLoaderForGroupId(Map map, int groupID)
        {
            if (map?.mapPawns == null || groupID < 0)
                return false;
            var spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                var p = spawned[i];
                if (p?.CurJob == null || p.CurJobDef != HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk)
                    continue;
                var comp = p.CurJob.GetTarget(TargetIndex.A).Thing?.TryGetComp<CompTransporter>();
                if (comp != null && comp.groupID == groupID)
                    return true;
            }
            return false;
        }
    }
}
