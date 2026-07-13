using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Suppress the vanilla single-item "Load X into transporter" right-click float-menu option when HD's bulk-load
    /// replacement is on, so the player sees HD's bulk order instead of fighting the vanilla one-stack option. A
    /// prefix on the private <c>FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption(Pawn pawn, WorkGiverDef
    /// workGiver, LocalTargetInfo target, FloatMenuContext context)</c> returns false (suppress the vanilla option)
    /// when the work giver's <c>giverClass</c> is <see cref="WorkGiver_LoadTransporters"/> /
    /// <see cref="WorkGiver_HaulToPortal"/> AND HD would actually give a bulk-load job for this pawn+target.
    ///
    /// The two branches are INDEPENDENTLY gated (transporters → <c>WorkGiver_LoadTransporters</c>; portals →
    /// <c>WorkGiver_HaulToPortal</c>) so enabling one while disabling the other leaves the still-vanilla option intact.
    /// Returning false makes the method produce no option; HD's own auto-discovered providers
    /// (<see cref="FloatMenuOptionProvider_BulkLoadTransporter"/> / <see cref="FloatMenuOptionProvider_BulkLoadPortal"/>)
    /// add the bulk order in its place.
    ///
    /// ALL-PAWN / CORPSE MANIFEST FALLBACK (A5): HD's bulk path carries only ITEM cargo into inventory; it cannot
    /// scoop pawns/animals/corpses on the manifest. For a manifest that is wholly (or, for the remaining claimable
    /// slice, partly) pawns/corpses, HD's planner returns no job — so suppressing the vanilla option would leave a
    /// DEAD END (no way to load those bodies at all). Therefore each branch suppresses ONLY when
    /// <see cref="TransportLoad.TryGiveBulkJob"/> would return a non-null job for THIS pawn+target; when it would not,
    /// the vanilla carry option survives so the player can still hand-load the pawns/corpses one at a time.
    ///
    /// The probe is the SAME call (with the same <c>playerOrder: true</c> eligibility path) HD's float-menu provider
    /// makes when the bulk option is clicked, and it is SIDE-EFFECT-FREE: <c>TryGiveBulkJob</c> is pure planning — it
    /// refreshes the ledger's manifest view (idempotent, the same <c>LoadRegisterOrUpdate</c> the work-scan/menu does)
    /// and reads available-to-claim, but records NO claim and NO reservation (the driver claims in its
    /// <c>Notify_Starting</c>, never the builder). So building-but-discarding the probed job reserves no quota.
    ///
    /// Vehicles are NOT handled here: Vehicle Framework loads cargo via a Hauling work-scan, not a vanilla
    /// float-menu option, so there is no vanilla load option to suppress for vehicles (the autonomous scan is
    /// upgraded to bulk by <c>Patch_WorkGiver_PackVehicle_Redirect</c> instead).
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption")]
    public static class Patch_SuppressVanillaLoadFloatMenu
    {
        // Apply when EITHER sub-feature is on; the per-branch gates inside decide which option to suppress.
        static bool Prepare()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return true;
            return s.enableBulkLoadTransporters || s.enableBulkLoadPortal;
        }

        // Returning false from a prefix that sets __result=null suppresses the vanilla option entirely.
        static bool Prefix(Pawn pawn, WorkGiverDef workGiver, LocalTargetInfo target, ref FloatMenuOption __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || workGiver?.giverClass == null || pawn == null)
                return true;

            var clicked = target.Thing;
            if (clicked == null)
                return true; // no concrete container to load into — let vanilla decide

            if (s.enableBulkLoadTransporters && workGiver.giverClass == typeof(WorkGiver_LoadTransporters))
            {
                // Under the boarding lord (LoadAndEnterTransporters) HD's OWN bulk-load provider declines to offer
                // its option (FloatMenuOptionProvider_BulkLoadTransporter, "let vanilla's gather-and-board run"), so
                // suppressing vanilla's option here too would leave the player with NO way to hand-load a pawn that
                // is mid-caravan-boarding ("even forcing doesn't work"). Mirror the provider's duty gate so the two
                // stay symmetric: keep vanilla's option whenever HD's provider would decline.
                if (pawn.mindState?.duty?.def == DutyDefOf.LoadAndEnterTransporters)
                    return true;
                // Only suppress when HD can actually give a bulk-load job for this pawn + transporter group. An
                // all-pawn/corpse manifest (or one with nothing item-like left to claim) yields no HD job → leave the
                // vanilla carry option so the player can still load by hand.
                var adapter = LoadTransportersAdapter.TryCreate(clicked.TryGetComp<CompTransporter>());
                // #138 perf: the CACHED probe — the full TryGiveBulkJob plan is memoized per (pawn, group, tick) so a
                // group probed several times per right-click is planned once (behaviour-identical; the probe is
                // side-effect-free). The A5 corpse/all-pawn fallback is preserved: a manifest with nothing item-like
                // to claim still yields no job → the vanilla carry option survives.
                if (adapter != null && TransportLoad.WouldGiveBulkJobForMenu(pawn, adapter))
                {
                    __result = null; // HD's bulk-load float-menu provider supplies the order instead
                    return false;
                }
                return true; // HD has no bulk job here — keep the vanilla single-item load option (A5 fallback)
            }
            if (s.enableBulkLoadPortal && workGiver.giverClass == typeof(WorkGiver_HaulToPortal))
            {
                // Symmetric to the transporter branch: HD's portal provider declines under the portal-boarding lord
                // (LoadAndEnterPortal), so keep vanilla's option there rather than hide it with no replacement.
                if (pawn.mindState?.duty?.def == DutyDefOf.LoadAndEnterPortal)
                    return true;
                var adapter = MapPortalBulkTarget.TryCreate(clicked as MapPortal);
                if (adapter != null && TransportLoad.WouldGiveBulkJobForMenu(pawn, adapter))
                {
                    __result = null; // HD's bulk-portal-load float-menu provider supplies the order instead
                    return false;
                }
                return true; // HD has no bulk job here — keep the vanilla single-item load option (A5 fallback)
            }
            return true;
        }
    }
}
