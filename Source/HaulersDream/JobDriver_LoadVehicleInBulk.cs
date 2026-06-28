using System.Collections;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Bulk-load a Vehicle Framework vehicle (a <c>VehiclePawn</c>, reached reflection-only via
    /// <see cref="VehicleFrameworkCompat"/> — held as a plain <see cref="Thing"/>, NO <c>Vehicles.*</c> type here) —
    /// the single-target VF counterpart to <see cref="JobDriver_LoadPortalInBulk"/>. Same three-phase shape (sweep
    /// nearby ground stacks into tagged inventory → walk to the vehicle ONCE → deposit every tagged stack the vehicle
    /// still needs), but the DEPOSIT goes through VF's own event-correct path rather than a raw container transfer:
    ///
    /// The vehicle's deposit container is <c>((Pawn)vehicle).inventory.innerContainer</c>, but there is NO
    /// <c>SubtractFromToLoadList</c> hook to intercept — the manifest decrement (and the <c>CargoAdded</c> event) is
    /// baked into <c>VehiclePawn.AddOrTransfer(thing, count)</c>. So the deposit toil splits the surplus off the
    /// inventory and hands it to <see cref="VehicleFrameworkCompat.AddOrTransfer"/> — clamped to
    /// <see cref="VehicleFrameworkCompat.RemainingDemandForThing"/> (the SINGLE matching transferable's count — MF1:
    /// over-clamping to a def-sum would drive a matching <c>cargoToLoad</c> entry negative→removed and over-load the
    /// def). The shim decrements <c>cargoToLoad</c> by the PASSED count, so the clamp lines up exactly. On a
    /// <c>moved &lt;= 0</c> / <c>-1</c> sentinel (VF absent mid-trip / member unbound) the split is put back into the
    /// inventory and re-tagged for HD's normal unload — never dropped. The <c>(def, moved)</c> pair is captured BEFORE
    /// the transfer for the thing-less <see cref="HaulersDreamGameComponent.LoadNotifyDeposited(Pawn,
    /// IManagedLoadable, ThingDef, int)"/> settle (the split may have been merged/destroyed inside the vehicle so
    /// reading the moved count off it afterward is unsafe).
    ///
    /// DIVERGENCES vs the portal/transporter drivers (addendum MF1/MF2): (a) deposit via
    /// <c>VehicleFrameworkCompat.AddOrTransfer</c> instead of <c>TryTransferToContainer</c>; (b) NO
    /// <c>SubtractFromToLoadList</c> intercept path (AddOrTransfer IS the precise decrement) — the
    /// <see cref="Global.IsExecutingManagedUnload"/> flag is still toggled around the transfer for other-patch
    /// hygiene only, never load-bearing; (c) OMIT the group-redirect (a vehicle is single-target with no Group — the
    /// per-stack <c>RemainingDemandForThing</c> re-check in the deposit loop already leaves fully-satisfied stock
    /// tagged for the normal unload); (d) <c>FailOnDespawnedOrNull</c> on the vehicle so a despawn / aerial-launch
    /// mid-load ends the job → the finish action releases the VRM claim + ledger claim + re-tags swept survivors.
    ///
    /// Concurrency: the CLAIM (ledger + belt-and-suspenders VF <c>VehicleReservationManager</c>) is recorded in
    /// <see cref="JobDriver_LoadInBulkBase.Notify_Starting"/> (so a built-but-never-started probe never claims); on
    /// every non-Success end the claim is RELEASED and the carried task item is SALVAGED back into inventory (re-tagged,
    /// rides HD's normal unload) — never dropped on a temp map, never stuck. The shared scaffold lives in
    /// <see cref="JobDriver_LoadInBulkBase"/>; this subclass supplies the VF deposit core + the VRM claim/release.
    /// </summary>
    public class JobDriver_LoadVehicleInBulk : JobDriver_LoadInBulkBase
    {
        private const TargetIndex VehicleInd = TargetIndex.A; // the vehicle (deposit dest)

        private Thing Vehicle => job.GetTarget(VehicleInd).Thing;

        protected override string ToilPrefix => "HD_Lvib";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdLvibLoadIndex", 0);
            Scribe_Values.Look(ref depositLoops, "hdLvibDepositLoops", 0);
            Scribe_Values.Look(ref passes, "hdLvibPasses", 0);
        }

        public override string GetReport() => "HaulersDream.LoadVehicle.Report".Translate();

        protected override IManagedLoadable BuildLoadable()
        {
            var vehicle = Vehicle;
            return vehicle != null ? VehicleLoadTarget.TryCreate(vehicle) : null;
        }

        protected override void OnExtraClaim()
        {
            // Belt-and-suspenders VF claim. The WorkGiver JobOnThing redirect is the authoritative stand-down
            // (it replaces VF's single-stack job with this bulk job for every HD-eligible scanning pawn); this VRM
            // claim only covers the fail-open / non-HD-eligible case. Fail-open in the shim.
            VehicleFrameworkCompat.ReserveVehicle(Vehicle, pawn, job);
        }

        protected override void OnReleaseExtraClaims()
        {
            VehicleFrameworkCompat.ReleaseVehicleClaims(pawn);
        }

        protected override bool FindTargetStillValid()
        {
            var vehicle = Vehicle;
            return vehicle != null && vehicle.Spawned;
        }

        protected override void DepositOne(Thing thing, ThingOwner inner, CompHauledToInventory hcomp, IManagedLoadable adp, ref bool movedAny)
        {
            var vehicle = Vehicle;
            // MF1 clamp: the SINGLE matching transferable's remaining demand (NOT a def-sum) — exactly what
            // VehiclePawn.AddOrTransfer decrements. Over-clamping to a def-sum would drive a matching
            // cargoToLoad entry negative→removed and over-load the def.
            int remaining = VehicleFrameworkCompat.RemainingDemandForThing(vehicle, thing);
            int count = VehicleLoadPlanPolicy.DepositUnits(InventorySurplus.SurplusOf(pawn, thing), remaining);
            if (count <= 0)
                return; // vehicle no longer wants this def (filled by another pawn) — leave it tagged

            // Split the exact count off the inventory; AddOrTransfer moves the SPLIT (an event-correct deposit
            // that fires CargoAdded + decrements cargoToLoad by the passed count). Capture (def, count) BEFORE
            // the transfer — the split may merge/destroy inside the vehicle, so reading the moved count off it
            // after is unsafe; the thing-less LoadNotifyDeposited overload uses the captured pair. (ThingOwner.
            // Take returns the SAME Thing when count==stackCount, or a fresh SplitOff otherwise.)
            var split = inner.Take(thing, count);
            if (split == null)
                return;
            var depDef = split.def;

            int moved;
            // The IsExecutingManagedUnload flag is toggled around the transfer for OTHER-patch hygiene only —
            // there is no SubtractFromToLoadList intercept on the vehicle path (AddOrTransfer IS the precise
            // decrement), so this is not load-bearing. try/finally resets it even on throw; the throw RETHROWS.
            Global.IsExecutingManagedUnload = true;
            try
            {
                moved = VehicleFrameworkCompat.AddOrTransfer(vehicle, split, count);
            }
            finally
            {
                Global.IsExecutingManagedUnload = false;
            }

            if (moved > 0)
            {
                movedAny = true;
                // Thing-less settle with the captured (def, moved) — the split may be gone inside the vehicle.
                HaulersDreamGameComponent.Instance?.LoadNotifyDeposited(pawn, adp, depDef, moved);
            }
            // Put any un-MOVED remainder of the SPLIT back into the inventory (merging with the source stack)
            // and re-tag it so it rides HD's normal unload — never dropped. On a clean full move VF owns/
            // destroys the split (ParentHolder != null / Destroyed), so this is a no-op. The moved<=0/-1
            // sentinel path (VF absent mid-trip / member unbound / nothing accepted) lands here too.
            if (!split.Destroyed && !split.Spawned && split.ParentHolder == null)
            {
                if (inner.TryAdd(split, canMergeWithExistingStacks: true))
                    hcomp.RegisterHauledItem(split);
                else
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            }
            // Drop the now-stale tag only when NEITHER the original stack NOR the split remains in the
            // inventory (the original was fully consumed by the Take and the split was fully deposited);
            // a remainder merged back leaves it tagged. Mirrors JobDriver_LoadPackAnimal's deposit dereg.
            if (!inner.Contains(thing) && !inner.Contains(split))
                hcomp.Deregister(thing);
        }

        protected override bool HasDepositable() => HasDepositableForVehicle();

        /// <summary>Units of <paramref name="def"/> the vehicle's manifest still wants — the Σ CountToTransfer across
        /// its <c>cargoToLoad</c> entries for that def (a quick "anything left for this def?" probe; the actual deposit
        /// clamps per-stack via <see cref="VehicleFrameworkCompat.RemainingDemandForThing"/> on the exact
        /// transferable identity).</summary>
        private static int VehicleRemainingForDef(Thing vehicle, ThingDef def)
        {
            if (def == null)
                return 0;
            var ltl = VehicleFrameworkCompat.CargoToLoad(vehicle);
            if (ltl == null)
                return 0;
            int sum = 0;
            for (int i = 0; i < ltl.Count; i++)
                if (ltl[i] is TransferableOneWay tr && tr.ThingDef == def && tr.CountToTransfer > 0)
                    sum += tr.CountToTransfer;
            return sum;
        }

        /// <summary>True if the pawn holds any tagged surplus stack of a def the vehicle still wants.</summary>
        private bool HasDepositableForVehicle()
        {
            var vehicle = Vehicle;
            var hcomp = pawn.GetComp<CompHauledToInventory>();
            var inner = pawn.inventory?.innerContainer;
            if (vehicle == null || hcomp == null || inner == null)
                return false;
            // HEALED view (not Peek): the deposit driver reads GetHashSet, so this gate must too — else a scooped
            // stack that MERGED into a same-def inventory stack after tagging is invisible here, the gate says
            // "nothing to deposit", and the merge-survivor cargo never loads onto the vehicle. Same #62/#87 class.
            foreach (var t in hcomp.GetHashSet())
            {
                if (t == null || t.Destroyed || !inner.Contains(t))
                    continue;
                if (InventorySurplus.SurplusOf(pawn, t) <= 0)
                    continue;
                if (VehicleRemainingForDef(vehicle, t.def) > 0)
                    return true;
            }
            return false;
        }
    }
}
