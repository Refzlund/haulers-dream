namespace HaulersDream.Core
{
    /// <summary>
    /// Pure MOW (eat-from) / ORG (build-from) holder guard for Vehicle Framework (addendum decision #5). The
    /// eat-from-vehicle / build-from-vehicle cases are DESIRABLE and work unchanged (a parked vehicle is a spawned
    /// player Pawn with a normal inventory tracker, so it passes the existing eligible-carrier check). The ONLY
    /// defensive addition is to skip a holder that is itself riding/inside a vehicle — its inventory is unreachable,
    /// so eating/building from it would only waste pathing. The runtime feeds <paramref name="holderInVehicle"/>
    /// from <c>VehicleFrameworkCompat.InVehicle(holder)</c> (false when VF absent → this collapses to
    /// <paramref name="baseEligible"/>, a no-op).
    ///
    /// No game types — unit-tested headlessly. The runtime inlines this trivially at the holder-loop guard, but it
    /// lives in Core+tests per the testable-logic mantra.
    /// </summary>
    public static class VehicleCarrierEligibilityPolicy
    {
        /// <summary>A spawned holder a colonist may eat-from / build-from: the base eligibility AND not itself
        /// embarked in a vehicle.</summary>
        public static bool IsEligibleVehicleAwareHolder(bool baseEligible, bool holderInVehicle)
            => baseEligible && !holderInVehicle;
    }
}
