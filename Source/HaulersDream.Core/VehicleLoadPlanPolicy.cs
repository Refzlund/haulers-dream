using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure deposit clamp for bulk-loading a Vehicle Framework vehicle (addendum MF1). One swept stack is deposited
    /// into the vehicle's cargo via VF's <c>AddOrTransfer(thing, count)</c>, whose manifest decrement subtracts the
    /// PASSED count from the single matching <c>TransferableOneWay</c> — so <paramref name="remaining"/> here is that
    /// ONE transferable's remaining count (fed by the runtime via
    /// <c>VehicleFrameworkCompat.RemainingDemandForThing</c>), NEVER a def-sum. Over-passing would drive the entry
    /// negative→removed and over-load the def; under-passing leaves the rest tagged for the next pass.
    ///
    /// No game types — unit-tested headlessly, like <see cref="TransportLoadPlan"/>.
    /// </summary>
    public static class VehicleLoadPlanPolicy
    {
        /// <summary>
        /// Units to deposit into the vehicle for one tagged stack: the tightest of the surplus actually held in the
        /// pawn's inventory and the matching transferable's remaining demand. Never &lt; 0 (a negative/absent demand
        /// clamps to 0 — nothing to deposit).
        /// </summary>
        public static int DepositUnits(int surplus, int remaining)
            => Math.Max(0, Math.Min(surplus, remaining));
    }
}
