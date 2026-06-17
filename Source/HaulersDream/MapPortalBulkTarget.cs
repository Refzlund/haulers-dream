using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// <see cref="IManagedLoadable"/> over a single <see cref="MapPortal"/> (a pit gate, cave / vault exit, or any
    /// "enter map" portal — every <c>MapPortal</c> subclass inherits the same <c>leftToLoad</c> manifest +
    /// <c>containerProxy</c> + 2-arg <c>SubtractFromToLoadList</c>, so ONE adapter covers them all). The ledger +
    /// planner + deposit loop see one uniform manifest/mass view exactly as they do for a transporter group; only the
    /// adapter differs:
    ///   • a portal is a SINGLE list (no group concat) — its ledger id is <see cref="LedgerKey"/> (a negative-
    ///     namespaced <c>thingIDNumber</c>, disjoint from transporter groupIDs — see that method);
    ///   • it has NO mass cap (<see cref="HasMassCap"/> = false → the trip-mass budget ignores the group term);
    ///   • the deposit teleports/consumes the Thing (the <c>PortalContainerProxy</c> drops it onto the other map), so
    ///     the ledger settle is THING-LESS — the driver captures (def, count) BEFORE the transfer and
    ///     <see cref="HandlesAbstractDemands"/> documents that.
    ///
    /// Created via <see cref="TryCreate"/> (null/!Spawned/Map==null guarded; the Map is cached because the portal may
    /// despawn mid-trip — a pit gate collapse).
    /// </summary>
    public class MapPortalBulkTarget : IManagedLoadable
    {
        private readonly MapPortal portal;
        private readonly Map map;

        private MapPortalBulkTarget(MapPortal portal, Map map)
        {
            this.portal = portal;
            this.map = map;
        }

        /// <summary>Build an adapter for a portal, or null when the portal is unusable (null / not spawned / no map).</summary>
        public static MapPortalBulkTarget TryCreate(MapPortal portal)
        {
            if (portal == null || !portal.Spawned)
                return null;
            var map = portal.Map;
            if (map == null)
                return null;
            return new MapPortalBulkTarget(portal, map);
        }

        /// <summary>The portal this adapter wraps (the deposit-target anchor; the float-menu/board-gate read it).</summary>
        public MapPortal Portal => portal;

        /// <summary>
        /// The ledger key for a portal. CRITICAL: the GameComponent's <c>loadTasks</c> is ONE flat
        /// <c>Dictionary&lt;int, LoadLedgerEntry&gt;</c> shared by transporters (keyed by <c>CompTransporter.groupID</c>)
        /// AND portals (keyed off <c>MapPortal.thingIDNumber</c>). Those come from two INDEPENDENT counters
        /// (<c>UniqueIDsManager.GetNextTransporterGroupID</c> vs <c>GetNextThingID</c>), both ≥ 0 — so a raw
        /// <c>thingIDNumber</c> could COLLIDE with an unrelated transporter's <c>groupID</c> (e.g. both == 7) and
        /// corrupt both manifests. Namespacing portal keys into the strictly-NEGATIVE range
        /// (<c>-(thingIDNumber + 1)</c>) makes them disjoint from every non-negative transporter groupID. This is the
        /// SINGLE source of truth — every direct ledger lookup for a portal (the anti-conflict / board-gate patches)
        /// must route through here, never use the raw thingIDNumber.
        /// </summary>
        public static int LedgerKey(MapPortal portal) => -(portal.thingIDNumber + 1);

        public int GetUniqueLoadID() => LedgerKey(portal);

        public Map GetMap() => map;

        public Thing GetParentThing() => portal;

        public List<TransferableOneWay> GetTransferables()
        {
            // A portal is a SINGLE list (no group concat) — copy OUT the entries with a positive remaining. The
            // LIST is ours (never a shared buffer); the entries are live refs the caller may read.
            var result = new List<TransferableOneWay>();
            var ltl = portal.leftToLoad;
            if (ltl == null)
                return result;
            for (int i = 0; i < ltl.Count; i++)
            {
                var tr = ltl[i];
                if (tr != null && tr.HasAnyThing && tr.CountToTransfer > 0)
                    result.Add(tr);
            }
            return result;
        }

        public bool AnythingToLoad()
        {
            // Allocation-free emptiness pre-gate: short-circuit on the first positive entry in the portal's single
            // leftToLoad list (no List<TransferableOneWay> materialised, unlike GetTransferables).
            var ltl = portal.leftToLoad;
            if (ltl == null)
                return false;
            for (int i = 0; i < ltl.Count; i++)
            {
                var tr = ltl[i];
                if (tr != null && tr.HasAnyThing && tr.CountToTransfer > 0)
                    return true;
            }
            return false;
        }

        public ThingOwner GetInnerContainerFor(Thing depositTarget)
        {
            // The portal's PortalContainerProxy: a ThingOwner whose TryAdd teleports the deposited Thing to the
            // other map (and self-fires Notify_ThingAdded -> SubtractFromToLoadList). Always the same container
            // regardless of the deposit target — a portal is one list.
            return portal.GetDirectlyHeldThings();
        }

        // Portals are uncapped: float.MaxValue capacity / 0 usage so TripMassBudget (with HasMassCap=false) uses
        // only the pawn's free space and never the group term.
        public float GetMassCapacity() => float.MaxValue;

        public float GetMassUsage() => 0f;

        public bool HasMassCap => false;

        public bool HandlesAbstractDemands => true;

        public LoadableKind Kind => LoadableKind.Portal;
    }
}
