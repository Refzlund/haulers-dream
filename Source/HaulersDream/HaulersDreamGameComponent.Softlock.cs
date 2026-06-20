using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    public partial class HaulersDreamGameComponent
    {
        // --- A2 anti-softlock auto-drop ---
        // On a LONG interval (~30s, mirroring BLFT's hardcoded 1800-tick cadence) refill a queue with every
        // player pawn across all maps, then process ONE pawn per tick (time-sliced so a big colony never
        // spikes). A pawn holding HD-tagged cargo that can no longer haul (work disabled / priority 0 / a mech
        // that's charging-dormant-or-shut-down) would otherwise strand that cargo forever — drop only its
        // tagged items so other haulers reclaim them. Transient (in-flight scan state, not scribed): on load
        // the queue starts empty and refills on the next interval tick.
        private const int SoftlockCheckInterval = 1800;
        private readonly Queue<Pawn> softlockQueue = new Queue<Pawn>();
        // Reused scratch list so the tagged-item snapshot allocates nothing after first use (the drop must
        // iterate a COPY — TryDrop mutates the tracked set via Deregister).
        private readonly List<Thing> tmpSoftlockDrop = new List<Thing>();

        // Classify a pawn's mech state for SoftlockDropPolicy. None for a non-mech or an awake-and-capable mech;
        // a stuck state (charging / self-shutdown / dormant) otherwise. Mirrors BLFT's mech softlock check.
        private static MechState MechStateOf(Pawn pawn)
        {
            if (!pawn.RaceProps.IsMechanoid)
                return MechState.None;
            var def = pawn.CurJobDef;
            if (def == JobDefOf.SelfShutdown || pawn.IsSelfShutdown())
                return MechState.SelfShutdown;
            if (def == JobDefOf.MechCharge)
                return MechState.Charging;
            if (pawn.GetComp<CompCanBeDormant>()?.Awake == false)
                return MechState.Dormant;
            return MechState.None;
        }

        // The A2 driver: refill the queue every SoftlockCheckInterval ticks, then process one pawn per tick.
        private void RunSoftlockDropDriver(int tick)
        {
            if (HaulersDreamMod.Settings?.enableSoftlockDrop != true)
            {
                if (softlockQueue.Count > 0)
                    softlockQueue.Clear(); // off -> hold no stale refs
                return;
            }

            if (tick % SoftlockCheckInterval == 0)
            {
                softlockQueue.Clear();
                var maps = Find.Maps;
                for (int m = 0; m < maps.Count; m++)
                {
                    var pawns = maps[m].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                    for (int i = 0; i < pawns.Count; i++)
                        softlockQueue.Enqueue(pawns[i]);
                }
            }

            if (softlockQueue.Count > 0)
                TryDropSoftlockedCargo(softlockQueue.Dequeue());
        }

        // Detect + drop a single pawn's stranded HD-tagged cargo. Decision lives in the pure
        // SoftlockDropPolicy; this maps the live Verse state and performs the drop.
        private void TryDropSoftlockedCargo(Pawn pawn)
        {
            // The pawn may have despawned / died / drafted between enqueue and now (the queue is built up to
            // SoftlockCheckInterval ticks ago). A drafted pawn is under direct control — don't strip its cargo.
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Drafted || pawn.Map == null)
                return;

            var comp = pawn.TryGetComp<CompHauledToInventory>();
            if (comp == null)
                return;

            // Read-only peek for the decision (the UI/scan-path-safe view); the live count after pruning gives
            // the policy its taggedCount. PeekHashSet may hold destroyed/out-of-inventory tags — count only the
            // ones still really in this pawn's inventory so an empty-but-stale tracker doesn't trigger a no-op
            // "drop" loop.
            var owner = pawn.inventory?.innerContainer;
            if (owner == null)
                return;
            int liveTagged = 0;
            foreach (var t in comp.PeekHashSet())
                if (t != null && !t.Destroyed && t.stackCount > 0 && owner.Contains(t))
                    liveTagged++;

            // NOTE: a Hauling work PRIORITY of 0 is intentionally NOT treated as "stranded" — a pawn the player
            // set to never haul (a dedicated grower/crafter) still scoops its yields and still unloads them via
            // HD's own end-of-run / interval / idle / before-downtime paths (which don't use the vanilla Hauling
            // work giver). Dropping its cargo here caused the "pawn drops scooped items while it keeps working"
            // bug. Only genuine incapability (WorkTagIsDisabled) or a stuck mech actually strands cargo.
            bool drop = SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: pawn.WorkTagIsDisabled(WorkTags.Hauling),
                isMech: pawn.RaceProps.IsMechanoid,
                mechState: MechStateOf(pawn),
                taggedCount: liveTagged,
                runningHdJob: IsRunningHdJob(pawn));
            if (!drop)
                return;

            // The policy decision above is the gate; perform the drop via the shared loop, reusing this driver's
            // per-tick scratch list so the hot path stays allocation-free.
            DropTrackedSnapshot(pawn, comp, owner, tmpSoftlockDrop);
        }

        /// <summary>
        /// Drop a pawn's HD-tagged cargo at its feet: snapshot the tracked set (TryDrop -> Deregister mutates it),
        /// drop each item still in inventory with <see cref="ThingPlaceMode.Near"/>, Deregister only on a
        /// successful drop, and abort on the FIRST failure (saturated / boxed-in area) leaving the rest tracked
        /// for a later retry. No try/catch — a genuine drop fault must surface as a red error. UNCONDITIONAL: the
        /// CALLER owns the decision (SoftlockDropPolicy for the periodic driver; the about-to-charge condition for
        /// the mech-shed hook). Single implementation, so the two callers can never drift.
        /// </summary>
        private static void DropTrackedSnapshot(Pawn pawn, CompHauledToInventory comp, ThingOwner<Thing> owner, List<Thing> scratch)
        {
            scratch.Clear();
            // PeekHashSet (no self-heal) may hold null/destroyed tags; skip nulls here so the sort comparator
            // below never dereferences a null (the drop loop still re-checks Destroyed/Contains per item).
            foreach (var t in comp.PeekHashSet())
                if (t != null)
                    scratch.Add(t);
            // MP determinism: process tagged stacks in thingIDNumber order so a capacity-bound loop deposits/drops the same subset on every client.
            scratch.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            for (int i = 0; i < scratch.Count; i++)
            {
                var item = scratch[i];
                if (item == null || item.Destroyed || !owner.Contains(item))
                    continue;
                // TryDrop reassigns the out param to the (possibly merged) ground stack; hold the ORIGINAL tracked
                // reference to deregister.
                if (owner.TryDrop(item, pawn.Position, pawn.Map, ThingPlaceMode.Near, out Thing _))
                    comp.Deregister(item);
                else
                    break; // saturated area / boxed in — retry on the next cycle
            }
            scratch.Clear();
        }

        /// <summary>
        /// Drop ALL of a pawn's HD-tagged cargo at its feet, UNCONDITIONALLY (the caller owns the decision). Used
        /// by the mech-shed-before-charge hook (<see cref="Patch_MechShedCargoBeforeCharge"/>) as its fallback
        /// when there is no reachable storage to deliver to. Allocates a one-off snapshot list (a rare,
        /// non-per-tick path), unlike the periodic softlock driver which reuses its scratch.
        /// </summary>
        internal static void DropTaggedCargo(Pawn pawn)
        {
            if (pawn?.Map == null)
                return;
            var comp = pawn.TryGetComp<CompHauledToInventory>();
            var owner = pawn.inventory?.innerContainer;
            if (comp == null || owner == null)
                return;
            DropTrackedSnapshot(pawn, comp, owner, new List<Thing>(comp.PeekHashSet().Count));
        }
    }
}
