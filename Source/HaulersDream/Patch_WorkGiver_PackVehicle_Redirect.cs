using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Autonomous bulk-load redirect: upgrade VF's single-stack vehicle pack job to HD's one-trip BULK load, so
    /// colonists load a vehicle's designated cargo the SAME way they already bulk-load transporters and portals —
    /// sweep many stacks into inventory, deposit them in one trip, with idle haulers joining a single manifest via the
    /// shared <see cref="HaulersDreamGameComponent"/> load ledger. This is what makes the feature fire on the PRIMARY
    /// path (the player sets a vehicle's cargo via VF's own load dialog and colonists pick it up autonomously), not
    /// only on an explicit right-click.
    ///
    /// VF's <c>WorkGiver_PackVehicle</c> builds its one-item-in-hands job in the generic base
    /// <c>Vehicles.WorkGiver_CarryToVehicle&lt;TransferableOneWay&gt;.JobOnThing</c>
    /// (<c>JobMaker.MakeJob(LoadVehicle, thing, vehicle)</c>; its <c>HasJobOnThing</c> is the default
    /// <c>JobOnThing(...) != null</c>). This POSTFIX lets VF do ALL of its own eligibility work (forbidden / CanReach /
    /// manifest-non-empty / not-over-encumbered / FindThingToPack) and, only when VF WOULD hand this pawn a load job,
    /// swaps that single-stack job for HD's bulk job through the same <see cref="VehicleLoadTarget"/> +
    /// <see cref="TransportLoad.TryGiveBulkJob(Pawn, IManagedLoadable, bool)"/> path the float menu uses. Because every
    /// scanning colonist is upgraded, VF's single-stack loader never runs in parallel with HD's bulk courier (it is
    /// replaced, not raced) — so no separate suppression/anti-conflict patch is needed.
    ///
    /// FAIL-OPEN at every step: VF found no work (<c>__result</c> null), the feature is off, VF is inactive, the thing
    /// is not a vehicle, the adapter can't be built, or HD can't build a bulk job → leave VF's result untouched (the
    /// single-stack load stands, unchanged). HD therefore only ever ACCELERATES vehicle loading, never starves it.
    /// Reflection-bound via <c>AccessTools.Method</c> (null when the <c>Vehicles</c> assembly is absent → the patch is
    /// simply not applied; the <see cref="HarmonyPatch"/> attribute carries no compile-time VF type, and the vehicle
    /// arrives as a plain <see cref="Thing"/> param — NO <c>Vehicles.*</c> type anywhere). Mirrors the redirect idiom
    /// of <see cref="Patch_LoadTransportersJobUtility_JobOn"/>; gated like the rest of the loader
    /// (<c>enableVehicleFramework &amp;&amp; enableBulkLoadVehicles</c>, plus VF actually active).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WorkGiver_PackVehicle_Redirect
    {
        // The pack-vehicle workgiver type; cached to (1) bind its inherited JobOnThing and (2) guard the postfix
        // against generic code-sharing (see Postfix). null when VF is absent.
        private static readonly System.Type PackVehicleType = AccessTools.TypeByName("Vehicles.WorkGiver_PackVehicle");

        // VF's single-stack job factory, inherited by WorkGiver_PackVehicle from the closed generic base
        // WorkGiver_CarryToVehicle<TransferableOneWay>. AccessTools walks base types, so this resolves the base's
        // MethodInfo (which is what the pack workgiver actually runs). null when VF is absent -> Harmony skips us.
        private static readonly MethodInfo TargetJobOnThing = AccessTools.Method(
            PackVehicleType, "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) });

        static bool Prepare()
        {
            if (TargetJobOnThing == null)
                return false; // VF not loaded -> don't patch
            if (!VehicleFrameworkCompat.IsActive)
                return false;
            var s = HaulersDreamMod.Settings;
            return s != null && s.enableVehicleFramework && s.enableBulkLoadVehicles;
        }

        // Normalise to the DECLARING method so Harmony patches the closed generic base
        // WorkGiver_CarryToVehicle<TransferableOneWay>.JobOnThing directly rather than the inherited MethodInfo off the
        // WorkGiver_PackVehicle subclass (whose ReflectedType != DeclaringType, which Harmony refuses). This is a no-op
        // when the resolved method is already declared, so it never changes the shipped, working redirect target — it
        // only guarantees the redirect and the universal exception tagger converge on the SAME patchable method.
        static MethodBase TargetMethod() => HaulersDreamMod.NormalizeToDeclared(TargetJobOnThing);

        // `pawn` and `t` bind by name to the original JobOnThing(Pawn pawn, Thing t, bool forced) params (t IS the
        // vehicle, already a Thing -> no cast / no Vehicles.* type). VF already decided this pawn should load `t`.
        static void Postfix(object __instance, Pawn pawn, Thing t, ref Job __result)
        {
            if (__result == null)
                return; // VF found no loadable work for this pawn -> nothing to bulk
            // GUARD vs generic code-sharing: JobOnThing is inherited from the closed generic base
            // WorkGiver_CarryToVehicle<TransferableOneWay>, and for reference-type T the JIT SHARES that method's
            // native code with the sibling instantiations (WorkGiver_BringUpgradeMaterial / WorkGiver_RefuelVehicleTurret,
            // both <ThingDefCountClass>, whose target is ALSO a vehicle). So this postfix can be invoked for THEIR jobs
            // too — only proceed for the actual cargo-pack workgiver, else a refuel/upgrade job would be hijacked into
            // a cargo bulk-load.
            if (PackVehicleType == null || !PackVehicleType.IsInstanceOfType(__instance))
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableVehicleFramework || !s.enableBulkLoadVehicles
                || !VehicleFrameworkCompat.IsActive || !VehicleFrameworkCompat.IsVehicle(t))
                return;
            var adapter = VehicleLoadTarget.TryCreate(t);
            if (adapter == null)
                return; // can't adapt this vehicle -> leave VF's single-stack job
            // Cheap pre-gate (mirrors the transporter HasJob/JobOn split): this postfix runs on BOTH the default
            // HasJobOnThing probe (JobOnThing()!=null) and the real JobOnThing, so short-circuit the full sweep when
            // there is no claimable HD work for this pawn (ineligible, or every manifest unit already claimed by other
            // bulk haulers). Only when this passes do we pay TransportLoad.TryGiveBulkJob's pool build + nearest-scan.
            if (!TransportLoad.HasPotentialBulkWork(pawn, adapter))
            {
                // issue #164: same fully-covered guard as the transporter/portal paths. VF's own pack job has no
                // visibility into HD's in-flight claims, so when every remaining manifest unit is already covered by
                // other pawns' live claims (not yet delivered), VF would hand this pawn a REDUNDANT single-stack haul
                // on top. Null the result so VF's workgiver sees no work here, the same way the transporter/portal
                // prefixes suppress vanilla's own HasJob. A released claim (interrupted hauler) re-opens the gate next
                // scan cycle, so this is never a permanent block.
                if (HaulersDreamGameComponent.Instance?.LoadFullyClaimedByOthers(adapter) ?? false)
                {
                    __result = null;
                    return;
                }
                return; // no claimable bulk work (ineligible / nothing to sweep) -> leave VF's single-stack job
            }
            var bulk = TransportLoad.TryGiveBulkJob(pawn, adapter);
            if (bulk != null)
                __result = bulk; // upgrade to a one-trip bulk load; if HD builds none, VF's single-stack stands
        }
    }
}
