using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>The concrete kind of a bulk-load target, so the GameComponent ledger and the
    /// <c>TransportLoad</c> planner can route by an explicit value instead of branching on
    /// <see cref="IManagedLoadable.HandlesAbstractDemands"/> (which only distinguishes the deposit SHAPE, not the
    /// three families). The vehicle kind keys a SEPARATE ledger dictionary (raw <c>thingIDNumber</c>, provably
    /// disjoint from transporter <c>groupID</c> and portal <c>-(id+1)</c> keys because it is a different dict).</summary>
    public enum LoadableKind
    {
        /// <summary>A <c>CompTransporter</c>/shuttle group — vanilla transporter bulk-load.</summary>
        Transporter,

        /// <summary>A <c>MapPortal</c> (pit gate / cave or vault exit / "enter map" portal) — deposit teleports/consumes.</summary>
        Portal,

        /// <summary>A Vehicle Framework <c>VehiclePawn</c> — single-target, mass-capped, deposit via VF's
        /// <c>AddOrTransfer</c> (the Thing survives in the vehicle's inventory).</summary>
        Vehicle
    }

    /// <summary>
    /// The engine-typed adapter contract the bulk-load targets implement and the ledger keys through — a uniform
    /// read-view over a "thing that has a manifest of stuff to be loaded into it" (a transporter/shuttle group, a
    /// map portal, or a Vehicle Framework vehicle). Keeping the ledger and planner behind this interface means the
    /// concurrency math, the sweep, and the deposit loop never branch on the concrete family — only the adapter
    /// (and the explicit <see cref="Kind"/> routing) differs.
    ///
    /// <see cref="LoadTransportersAdapter"/> handles CompTransporter groups (incl. shuttles),
    /// <c>MapPortalBulkTarget</c> handles portals, and <c>VehicleLoadTarget</c> handles VF vehicles.
    /// <see cref="HasMassCap"/> drives <c>TransportLoadPlan.TripMassBudget</c> (transporters/vehicles have a mass
    /// cap; portals do not). <see cref="HandlesAbstractDemands"/> distinguishes a thing-less settle (portals
    /// teleport/consume the deposited Thing) from a normal one — false for transporters and vehicles.
    /// </summary>
    public interface IManagedLoadable
    {
        /// <summary>Which family this loadable is — the GameComponent uses it to pick the ledger bucket
        /// (<see cref="LoadableKind.Vehicle"/> → the separate <c>loadVehicleTasks</c> dict) and <c>TransportLoad</c>
        /// uses it to pick the JobDef + the feature toggle (the explicit 3-way, addendum SF1/SF2).</summary>
        LoadableKind Kind { get; }

        /// <summary>The save-unique, monotonic-never-reused id the ledger keys this task by (a group's <c>groupID</c>).</summary>
        int GetUniqueLoadID();

        /// <summary>The map this loadable lives on (cached at adapter creation — the parent may despawn later).</summary>
        Map GetMap();

        /// <summary>The clicked/parent thing (the deposit-target anchor — the primary transporter's parent).</summary>
        Thing GetParentThing();

        /// <summary>The remaining manifest (every <c>leftToLoad</c> entry across the group with <c>CountToTransfer &gt; 0</c>),
        /// copied OUT of any shared buffer so the caller may hold it across toils.</summary>
        List<TransferableOneWay> GetTransferables();

        /// <summary>The inner container to deposit <paramref name="depositTarget"/> into — the matching group member's
        /// <c>innerContainer</c> (the primary fast-path, else a group scan), or null if none can hold it.</summary>
        ThingOwner GetInnerContainerFor(Thing depositTarget);

        /// <summary>The group's total mass capacity (Σ across the group); <c>float.MaxValue</c> for an uncapped target.</summary>
        float GetMassCapacity();

        /// <summary>The group's current mass usage (Σ across the group); 0 for an uncapped target.</summary>
        float GetMassUsage();

        /// <summary>True when <see cref="GetMassCapacity"/>/<see cref="GetMassUsage"/> bound the trip (transporters);
        /// false for an uncapped target (portals) so the trip-mass budget ignores the group term.</summary>
        bool HasMassCap { get; }

        /// <summary>True when the deposit consumes/teleports the Thing so the ledger settle must be thing-less
        /// (def/count captured before transfer) — portals. False for transporters (the Thing survives in the container).</summary>
        bool HandlesAbstractDemands { get; }
    }
}
