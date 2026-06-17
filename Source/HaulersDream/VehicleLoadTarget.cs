using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// <see cref="IManagedLoadable"/> over a single Vehicle Framework vehicle (a <c>VehiclePawn</c>, reached
    /// reflection-only through <see cref="VehicleFrameworkCompat"/> so NO <c>Vehicles.*</c> type appears here — the
    /// vehicle is held as a plain <see cref="Thing"/>). The ledger + planner + deposit loop see the same uniform
    /// manifest/mass view they do for a transporter group; only this adapter differs:
    ///   • a vehicle is a SINGLE target (no group) — its ledger id is the RAW <c>thingIDNumber</c>
    ///     (<see cref="GetUniqueLoadID"/>), which is routed to the GameComponent's SEPARATE <c>loadVehicleTasks</c>
    ///     dict (provably disjoint from transporter <c>groupID</c> / portal <c>-(id+1)</c> keys because it is a
    ///     DIFFERENT dictionary, so no key arithmetic is needed — addendum SF1);
    ///   • it HAS a mass cap (<see cref="HasMassCap"/> = true) — the <c>CargoCapacity</c> VehicleStatDef — so the
    ///     trip-mass budget honors the vehicle's cargo headroom exactly like a transporter group;
    ///   • the Thing SURVIVES in the vehicle's inventory (<see cref="HandlesAbstractDemands"/> = false), like a
    ///     transporter and unlike a portal.
    ///
    /// <b>DEPOSIT DIVERGENCE:</b> the vehicle's deposit container IS <c>((Pawn)vehicle).inventory.innerContainer</c>,
    /// but the manifest decrement is baked into VF's <c>VehiclePawn.AddOrTransfer(thing, count)</c> (there is NO
    /// <c>SubtractFromToLoadList</c> hook to intercept). So <see cref="GetInnerContainerFor"/> is exposed only for
    /// the engine's shape-uniformity / "does a container exist" checks — the actual deposit is performed BY THE
    /// DRIVER via <c>VehicleFrameworkCompat.AddOrTransfer</c> (clamped to <c>RemainingDemandForThing</c>), NOT a raw
    /// <c>TryTransferToContainer</c> into this container (which would NOT fire the CargoAdded event nor decrement the
    /// manifest). This adapter therefore exposes NO raw-transfer deposit helper.
    ///
    /// Created via <see cref="TryCreate"/> (null / !IsVehicle / !Spawned / Map==null guarded; the Map is cached
    /// because the vehicle may despawn / aerial-launch mid-trip).
    /// </summary>
    public class VehicleLoadTarget : IManagedLoadable
    {
        private readonly Thing vehicle; // a Vehicles.VehiclePawn (held as Thing — no hard VF reference)
        private readonly Map map;

        private VehicleLoadTarget(Thing vehicle, Map map)
        {
            this.vehicle = vehicle;
            this.map = map;
        }

        /// <summary>Build an adapter for a vehicle, or null when it is unusable (null / not a VF vehicle / not
        /// spawned / no map). The <c>!IsVehicle</c> guard also returns null whenever VF is absent (the shim reports
        /// every Thing as a non-vehicle), so HD never produces a vehicle bulk-load job without VF.</summary>
        public static VehicleLoadTarget TryCreate(Thing vehicle)
        {
            if (vehicle == null || !VehicleFrameworkCompat.IsVehicle(vehicle) || !vehicle.Spawned)
                return null;
            var map = vehicle.Map;
            if (map == null)
                return null;
            return new VehicleLoadTarget(vehicle, map);
        }

        /// <summary>The vehicle this adapter wraps (the deposit-target anchor; the float-menu / driver read it as a
        /// plain <see cref="Thing"/>).</summary>
        public Thing Vehicle => vehicle;

        // RAW thingIDNumber (≥0): the GameComponent routes a Vehicle-kind loadable to the SEPARATE loadVehicleTasks
        // dict (see BucketFor), so the raw id never shares the int space with a transporter groupID — no
        // namespacing arithmetic needed (unlike the portal's -(id+1)).
        public int GetUniqueLoadID() => vehicle.thingIDNumber;

        public Map GetMap() => map;

        public Thing GetParentThing() => vehicle;

        public List<TransferableOneWay> GetTransferables()
        {
            // A vehicle is a SINGLE list (the cargoToLoad manifest, read reflection-only via the shim) — copy OUT
            // the entries with a positive remaining. The LIST is ours; the entries are live TransferableOneWay refs
            // the caller reads.
            var result = new List<TransferableOneWay>();
            var ltl = VehicleFrameworkCompat.CargoToLoad(vehicle);
            if (ltl == null)
                return result;
            for (int i = 0; i < ltl.Count; i++)
                if (ltl[i] is TransferableOneWay tr && tr.HasAnyThing && tr.CountToTransfer > 0)
                    result.Add(tr);
            return result;
        }

        public bool AnythingToLoad()
        {
            // Allocation-free emptiness pre-gate: short-circuit on the first positive entry in the cargoToLoad
            // manifest (read reflection-only as a non-generic IList; TransferableOneWay is a reference type so the
            // indexer does not box). No List<TransferableOneWay> materialised, unlike GetTransferables.
            var ltl = VehicleFrameworkCompat.CargoToLoad(vehicle);
            if (ltl == null)
                return false;
            for (int i = 0; i < ltl.Count; i++)
                if (ltl[i] is TransferableOneWay tr && tr.HasAnyThing && tr.CountToTransfer > 0)
                    return true;
            return false;
        }

        public ThingOwner GetInnerContainerFor(Thing depositTarget)
        {
            // The vehicle's cargo hold IS the inherited Pawn inventory container (VehiclePawn : Pawn, no override) —
            // exposed for the engine's "a container exists" uniformity only. NOTE: the DRIVER deposits via
            // VehicleFrameworkCompat.AddOrTransfer (which fires CargoAdded + decrements cargoToLoad), NOT a raw
            // transfer into this container. A null vehicle.inventory (defensive) yields null.
            return vehicle is Pawn p ? p.inventory?.innerContainer : null;
        }

        // Mass-capped: the CargoCapacity VehicleStatDef bounds TripMassBudget exactly like a transporter group's cap.
        public float GetMassCapacity() => VehicleFrameworkCompat.CargoCapacity(vehicle);

        // Used cargo mass — vanilla MassUtility on the vehicle Pawn (matches VF's Dialog_LoadCargo.MassUsage); a
        // non-Pawn vehicle is impossible past TryCreate's IsVehicle guard, but stay defensive.
        public float GetMassUsage() => vehicle is Pawn p ? MassUtility.GearAndInventoryMass(p) : 0f;

        public bool HasMassCap => true;

        public bool HandlesAbstractDemands => false;

        public LoadableKind Kind => LoadableKind.Vehicle;
    }
}
