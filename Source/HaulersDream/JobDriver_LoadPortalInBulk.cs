using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Bulk-load a map portal (pit gate, cave / vault exit, "enter map" portal) — the portal counterpart to
    /// <see cref="JobDriver_LoadTransportersInBulk"/>. Same three-phase shape (sweep nearby ground stacks into tagged
    /// inventory → walk to the portal ONCE → deposit every tagged stack the portal still needs), but the deposit is
    /// THING-LESS:
    ///
    /// The portal's <c>PortalContainerProxy.TryAdd</c> teleports/consumes the deposited Thing (it fires
    /// <c>MapPortal.Notify_ThingAdded → SubtractFromToLoadList</c>, then <c>GenDrop.TryDropSpawn</c>s it onto the
    /// OTHER map). After the transfer the Thing reference is on a different map (or destroyed when fully moved), so
    /// reading the moved count off it would silently under-count. We therefore capture <c>(def, count)</c> BEFORE the
    /// transfer and settle the ledger thing-lessly via <see cref="HaulersDreamGameComponent.LoadNotifyDeposited(Pawn,
    /// IManagedLoadable, ThingDef, int)"/>. The deposit MUST go through <c>portal.GetDirectlyHeldThings()</c> (the
    /// proxy) — a manual <c>GenDrop</c> would skip BOTH the pocket-map generation AND the manifest decrement.
    /// <see cref="Global.IsExecutingManagedPortalUnload"/> is the portal-side per-thread flag (set in a try/finally
    /// around each transfer — reset even on throw, rethrow, no suppression) that makes the
    /// <c>MapPortal.SubtractFromToLoadList</c> intercept precise. There is NO group mass cap (portals are uncapped —
    /// <c>HasMassCap=false</c>), but each pull is still clamped via <see cref="TransportLoadPlan.DeliverableUnits"/>
    /// during the sweep.
    ///
    /// Concurrency: the CLAIM is recorded in <see cref="JobDriver_LoadInBulkBase.Notify_Starting"/> (so a
    /// built-but-never-started probe never claims); on every non-Success end the claim is RELEASED and the carried
    /// task item is SALVAGED back into inventory (re-tagged, rides HD's normal unload) — never dropped on a temp map,
    /// never stuck. The shared scaffold lives in <see cref="JobDriver_LoadInBulkBase"/>; this subclass supplies the
    /// thing-less portal deposit core.
    /// </summary>
    public class JobDriver_LoadPortalInBulk : JobDriver_LoadInBulkBase
    {
        private const TargetIndex PortalInd = TargetIndex.A; // the portal (deposit dest)

        private MapPortal Portal => job.GetTarget(PortalInd).Thing as MapPortal;

        protected override string ToilPrefix => "HD_Lpib";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdLpibLoadIndex", 0);
            Scribe_Values.Look(ref depositLoops, "hdLpibDepositLoops", 0);
            Scribe_Values.Look(ref passes, "hdLpibPasses", 0);
        }

        public override string GetReport() => "HaulersDream.LoadPortal.Report".Translate();

        protected override IManagedLoadable BuildLoadable()
        {
            var portal = Portal;
            return portal != null ? MapPortalBulkTarget.TryCreate(portal) : null;
        }

        protected override bool FindTargetStillValid()
        {
            var portal = Portal;
            return portal != null && portal.Spawned;
        }

        protected override void DepositOne(Thing thing, ThingOwner inner, CompHauledToInventory hcomp, IManagedLoadable adp, ref bool movedAny)
        {
            var portal = Portal;
            if (portal == null || !portal.Spawned)
                return;
            var destInner = portal.GetDirectlyHeldThings(); // the PortalContainerProxy
            if (destInner == null)
                return;

            // Clamp to what the portal's manifest still wants for this def (NOT the whole carried surplus —
            // depositing more than the manifest needs would over-load the other map / under-count nothing,
            // but the SubtractFromToLoadList intercept only decrements what the entry held, so leftover
            // surplus stays tagged for HD's normal unload).
            int portalRemaining = PortalRemainingFor(portal, thing);
            int count = System.Math.Min(InventorySurplus.SurplusOf(pawn, thing), portalRemaining);
            if (count <= 0)
                return; // portal no longer needs this exact variant (filled by another pawn) — leave it tagged

            // THING-LESS settle — capture (def, count) BEFORE the transfer and measure the moved amount from
            // the SOURCE side. The proxy's TryAdd teleports the split Thing to the other map via
            // GenDrop.TryDropSpawn(ThingPlaceMode.Near), which CAN MERGE the split into an existing stack
            // there and DESTROY it — so the transfer's return value (= the dropped split's stackCount AFTER
            // the drop) reads 0/partial on a merge, a silent under-count. The robust signal is how much LEFT
            // the inventory: beforeCount − (still-in-inventory remainder). That is always observable and
            // exactly equals the deposited count (the manifest decrement inside Notify_ThingAdded saw the
            // full split.stackCount BEFORE the drop, so it already decremented correctly).
            var depDef = thing.def;
            int beforeCount = thing.stackCount;

            // Set the per-thread portal flag so the MapPortal.SubtractFromToLoadList intercept does the
            // PRECISE decrement. try/finally resets it even on throw; the throw RETHROWS (no suppression).
            Global.IsExecutingManagedPortalUnload = true;
            try
            {
                inner.TryTransferToContainer(thing, destInner, count, out Thing _, canMergeWithExistingStacks: false);
            }
            finally
            {
                Global.IsExecutingManagedPortalUnload = false;
            }
            // Units that physically left the inventory: full move -> thing removed from inner (remainder 0);
            // partial -> thing stays with reduced stackCount. Never reads the teleported split.
            bool fullyMoved = thing.Destroyed || !inner.Contains(thing);
            int actuallyMoved = beforeCount - (fullyMoved ? 0 : thing.stackCount);
            if (actuallyMoved > 0)
            {
                movedAny = true;
                // Thing-less settle with (def, count) — both captured/derived without reading the teleported Thing.
                HaulersDreamGameComponent.Instance?.LoadNotifyDeposited(pawn, adp, depDef, actuallyMoved);
                if (fullyMoved)
                    hcomp.Deregister(thing); // fully moved -> drop the tag; a partial leaves the remainder tagged
            }
        }

        protected override bool HasDepositable() => HasDepositableForPortal();

        /// <summary>Units MATCHING <paramref name="item"/>'s exact transferable identity (def + stuff + quality, via
        /// the SAME vanilla matcher the auto-fired <c>MapPortal.SubtractFromToLoadList</c> uses to find the entry it
        /// decrements — <see cref="TransferableUtility.TransferableMatchingDesperate"/> in <c>PodsOrCaravanPacking</c>
        /// mode) the portal's manifest still wants. The deposit MUST clamp to this — the precise intercept only
        /// decrements what the matched entry held, so depositing more than that entry holds would over-supply the other
        /// map AND leave the excess un-accounted. Mirrors the vehicle/transporter clamp. Returns 0 when no entry
        /// matches the deposited variant.</summary>
        private static int PortalRemainingFor(MapPortal portal, Thing item)
        {
            var ltl = portal?.leftToLoad;
            if (ltl == null || item?.def == null)
                return 0;
            var match = TransferableUtility.TransferableMatchingDesperate(item, ltl, TransferAsOneMode.PodsOrCaravanPacking);
            int remaining = match?.CountToTransfer ?? 0;
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>True if the pawn holds any tagged surplus stack of a def the portal still wants.</summary>
        private bool HasDepositableForPortal()
        {
            var portal = Portal;
            var hcomp = pawn.GetComp<CompHauledToInventory>();
            var inner = pawn.inventory?.innerContainer;
            if (portal == null || hcomp == null || inner == null)
                return false;
            // HEALED view (not Peek): the deposit driver reads GetHashSet, so this gate must too — else a scooped
            // stack that MERGED into a same-def inventory stack after tagging is invisible here, the gate says
            // "nothing to deposit", and the merge-survivor cargo never loads into the portal. Same #62/#87 class.
            foreach (var t in hcomp.GetHashSet())
            {
                if (t == null || t.Destroyed || !inner.Contains(t))
                    continue;
                if (InventorySurplus.SurplusOf(pawn, t) <= 0)
                    continue;
                if (PortalRemainingFor(portal, t) > 0)
                    return true;
            }
            return false;
        }
    }
}
