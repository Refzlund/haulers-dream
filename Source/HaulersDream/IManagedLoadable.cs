using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The engine-typed adapter contract the bulk-load targets implement and the ledger keys through — a uniform
    /// read-view over a "thing that has a manifest of stuff to be loaded into it" (a transporter/shuttle group;
    /// later a map portal). Keeping the ledger and planner behind this interface means the concurrency math, the
    /// sweep, and the deposit loop never branch on transporter-vs-portal — only the adapter differs.
    ///
    /// Stage 2 implements <see cref="LoadTransportersAdapter"/> (CompTransporter groups, incl. shuttles); the portal
    /// adapter is Stage 3. <see cref="HasMassCap"/> drives <c>TransportLoadPlan.TripMassBudget</c> (transporters
    /// have a group mass cap; portals do not). <see cref="HandlesAbstractDemands"/> distinguishes a thing-less
    /// settle (portals teleport/consume the deposited Thing) from a normal one — false for transporters.
    /// </summary>
    public interface IManagedLoadable
    {
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
