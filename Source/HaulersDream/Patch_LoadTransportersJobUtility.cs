using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Route vanilla single-item transporter loading through HD bulk. The automatic work scan and the float menu
    /// both funnel through <c>LoadTransportersJobUtility.HasJobOnTransporter</c> / <c>.JobOnTransporter</c> (the
    /// UTILITY, not the <c>WorkGiver_LoadTransporters</c> class — that's what the fragments confirmed). When the
    /// feature is on and a <see cref="LoadTransportersAdapter"/> can be created, these prefixes answer with HD's
    /// bulk path (<c>HasPotentialBulkWork</c> / <c>TryGiveBulkJob</c>) and skip vanilla.
    ///
    /// FAIL-OPEN: feature off, a null adapter, or HD finding no work → return true (fall through to vanilla single-
    /// item loading unchanged). The two answers can't diverge — both re-check the same gate.
    /// </summary>
    [HarmonyPatch(typeof(LoadTransportersJobUtility), nameof(LoadTransportersJobUtility.HasJobOnTransporter))]
    public static class Patch_LoadTransportersJobUtility_HasJob
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        static bool Prefix(Pawn pawn, CompTransporter transporter, ref bool __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableBulkLoadTransporters)
                return true;
            var adapter = LoadTransportersAdapter.TryCreate(transporter);
            if (adapter == null)
                return true; // can't adapt -> vanilla
            if (!TransportLoad.HasPotentialBulkWork(pawn, adapter))
                return true; // HD has no work (not eligible / nothing claimable) -> let vanilla try
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(LoadTransportersJobUtility), nameof(LoadTransportersJobUtility.JobOnTransporter))]
    public static class Patch_LoadTransportersJobUtility_JobOn
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        static bool Prefix(Pawn p, CompTransporter transporter, ref Job __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableBulkLoadTransporters)
                return true;
            var adapter = LoadTransportersAdapter.TryCreate(transporter);
            if (adapter == null)
                return true;
            var job = TransportLoad.TryGiveBulkJob(p, adapter);
            if (job == null)
                return true; // HD couldn't build a job -> fall through to vanilla single-item load
            __result = job;
            return false;
        }
    }
}
