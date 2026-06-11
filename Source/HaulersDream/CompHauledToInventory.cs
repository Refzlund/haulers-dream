using System.Collections.Generic;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Tracks the items a pawn scooped into its inventory (so the single unload pass knows what to
    /// put back), plus the tick of the most recent pickup (for the unload grace period). Injected
    /// onto every pawn def via Patches/HaulersDream_Pawns.xml.
    /// </summary>
    public class CompHauledToInventory : ThingComp
    {
        private HashSet<Thing> takenToInventory = new HashSet<Thing>();
        public int lastYieldTick = -99999;

        /// <summary>Tick of the last "pass-by storage" divert — a short cooldown that prevents a divert loop
        /// if an unload ever fails to clear the load. Transient (in-flight timing, not scribed).</summary>
        public int lastOpportunisticUnloadTick = -99999;

        /// <summary>Tick this pawn last claimed a stack from a hand-hauler — a short cooldown so a partial
        /// take can't immediately re-intercept the same hauler. Transient (in-flight timing, not scribed).</summary>
        public int lastInterceptedTick = -99999;

        /// <summary>
        /// Fresh ground drops this pawn produced and should scoop up (DropThenHaul mode). Transient —
        /// not scribed: it's in-flight state rebuilt as drops happen, and persisting live Thing
        /// references that may be gone on reload only risks dangling refs.
        /// </summary>
        public readonly List<Thing> pendingSelfPickups = new List<Thing>();

        // Reused scratch set for the GetHashSet self-heal, so the per-call scan allocates nothing.
        // [ThreadStatic] to match this assembly's convention for hook-reachable scratch state (the bill
        // share that drives GetHashSet runs off a [ThreadStatic] worker), so a threading mod can't race it.
        [System.ThreadStatic] private static HashSet<ThingDef> tmpScoopedDefs;

        public HashSet<Thing> GetHashSet()
        {
            takenToInventory.RemoveWhere(x => x == null || x.Destroyed);

            // Self-heal: a single scoop can land across MULTIPLE inventory stacks (a yield exceeding the
            // stack limit, e.g. >75 berries, or not merging into the first stack), but the registration only
            // ever tags one of them; stacks also merge/split over time. Treat EVERY current inventory stack
            // whose def we've already scooped as surplus — otherwise the untagged stacks are invisible to
            // both sharing and the unload pass (stranded in inventory forever). Bounded to already-scooped
            // defs, so a pawn's non-scooped kit is never claimed. (A rare def overlap — e.g. a pawn that
            // harvested healroot AND carries personal herbal medicine — would tag both; harmless: the surplus
            // is merely unloaded to storage, where it stays usable.)
            var owner = (parent as Pawn)?.inventory?.innerContainer;
            if (owner != null && takenToInventory.Count > 0)
            {
                var defs = tmpScoopedDefs ?? (tmpScoopedDefs = new HashSet<ThingDef>());
                defs.Clear();
                foreach (var t in takenToInventory)
                    defs.Add(t.def);
                for (int i = 0; i < owner.Count; i++)
                {
                    var thing = owner[i];
                    // Re-tagged (merged/split) stacks also re-register with CE's HoldTracker — a merge can grow
                    // a stack past the originally-notified count, and CE drops the un-held excess otherwise.
                    if (thing != null && defs.Contains(thing.def) && takenToInventory.Add(thing))
                        CECompat.NotifyHeld(parent as Pawn, thing, thing.stackCount);
                }
                defs.Clear();
            }
            return takenToInventory;
        }

        public void RegisterHauledItem(Thing thing)
        {
            // Only a NEW tag notifies (Add returns false for re-registers — the self-heal re-tags freely):
            // under Combat Extended, register the held stack with CE's HoldTracker so its loadout enforcement
            // doesn't dump the scooped/swept goods on the floor before the unload trip runs. CE's own cleanup
            // prunes the record once the goods leave the inventory. No-op without CE.
            if (takenToInventory.Add(thing) && thing != null)
                CECompat.NotifyHeld(parent as Pawn, thing, thing.stackCount);
        }

        public void Deregister(Thing thing) => takenToInventory.Remove(thing);

        /// <summary>The next still-valid pending drop this pawn can scoop, or null. Prunes invalid ones.</summary>
        public Thing TakeNextValidPending()
        {
            while (pendingSelfPickups.Count > 0)
            {
                var t = pendingSelfPickups[pendingSelfPickups.Count - 1];
                pendingSelfPickups.RemoveAt(pendingSelfPickups.Count - 1);
                if (t != null && t.Spawned && !t.Destroyed)
                    return t;
            }
            return null;
        }

        public void NotifyYieldPicked()
        {
            if (Find.TickManager != null)
                lastYieldTick = Find.TickManager.TicksGame;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref takenToInventory, "haulersDreamTakenToInventory", LookMode.Reference);
            Scribe_Values.Look(ref lastYieldTick, "haulersDreamLastYieldTick", -99999);
            if (takenToInventory == null)
                takenToInventory = new HashSet<Thing>();
        }
    }
}
