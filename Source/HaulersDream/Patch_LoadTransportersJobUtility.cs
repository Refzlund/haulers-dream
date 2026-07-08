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
            // Cheap pre-reject first; then BUILD the job — the count-based HasPotentialBulkWork can say "yes" while the
            // SWEEP finds nothing buildable (remaining manifest is pawns/corpses, all candidates forbidden / out of
            // radius / mass-full / claimed-by-others). Vanilla's JobGiver TRUSTS HasJob and calls JobOn; if we claimed
            // work here but JobOn returned null, vanilla's JobOnTransporter would issue a HaulToTransporter with no
            // valid target that ends the same tick and re-fires forever (the "10 jobs in one tick" loop). So
            // TryGiveBulkJob — the SAME authoritative build the JobOn prefix runs — is the source of truth here too.
            if (!TransportLoad.HasPotentialBulkWork(pawn, adapter))
            {
                // issue #164: HD has no NEW work for this pawn: either it's ineligible, the manifest is done, or
                // (the reported case) every remaining unit is already claimed by OTHER pawns who are still mid-trip
                // (not yet delivered). Vanilla's own HasJobOnTransporter only tracks physical deliveries, so it
                // would still see "steel still wanted" and hand this pawn a REDUNDANT haul on top of what's already
                // in flight, over-filling the group once everyone delivers. Only suppress vanilla in that specific
                // fully-covered case; a genuinely ineligible pawn or a done manifest falls through unchanged (vanilla
                // would find nothing either way there).
                if (HaulersDreamGameComponent.Instance?.LoadFullyClaimedByOthers(adapter) ?? false)
                {
                    __result = false;
                    return false;
                }
                return true; // HD has no work (not eligible / nothing claimable) -> let vanilla try
            }
            var job = TransportLoad.TryGiveBulkJob(pawn, adapter); // side-effect-free (claim happens later in the driver) -> safe to build speculatively
            if (job == null)
                return true; // nothing HD can actually build -> let vanilla decide (its HasJob self-checks FindThingToLoad), no asymmetric loop
            __result = true;
            return false; // HD will build the same job in the JobOn prefix
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
            {
                // Same fully-covered guard as HasJob's prefix (issue #164): a menu click or a re-scan can reach
                // JobOn directly without a fresh HasJob check in between, so this must stand on its own too.
                if (HaulersDreamGameComponent.Instance?.LoadFullyClaimedByOthers(adapter) ?? false)
                    return false; // already fully covered by other pawns' in-flight claims -> don't let vanilla duplicate
                return true; // HD couldn't build a job -> fall through to vanilla single-item load
            }
            __result = job;
            return false;
        }
    }
}
