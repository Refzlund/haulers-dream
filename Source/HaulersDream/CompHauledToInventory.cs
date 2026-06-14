using System.Collections.Generic;
using RimWorld;
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

        /// <summary>Tick each tagged stack was FIRST tagged, for per-item staleness in the cannot-unload alert
        /// (a busy hauler refreshing lastYieldTick must not mask one stranded stack). Transient: not scribed —
        /// on load, tags get a fresh clock via the GetHashSet backfill, so a loaded save won't false-alert and
        /// a genuinely stuck item simply re-arms its clock (the alert is a slow backstop, not a fast trigger).</summary>
        [System.NonSerialized] private Dictionary<Thing, int> taggedTick = new Dictionary<Thing, int>();

        /// <summary>Tick of the last "pass-by storage" divert — a short cooldown that prevents a divert loop
        /// if an unload ever fails to clear the load. Transient (in-flight timing, not scribed).</summary>
        public int lastOpportunisticUnloadTick = -99999;

        /// <summary>Tick this pawn last claimed a stack from a hand-hauler — a short cooldown so a partial
        /// take can't immediately re-intercept the same hauler. Transient (in-flight timing, not scribed).</summary>
        public int lastInterceptedTick = -99999;

        /// <summary>Tick this pawn last ran the opportunistic "sweep nearby loose items into pending self-pickup"
        /// area-cleanup scan — a per-pawn cooldown so a fast work run doesn't re-scan on every single yield drop.
        /// Transient (in-flight timing, not scribed).</summary>
        public int lastSweepTick = -99999;

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
        // Scratch for pruning the tag-age map without a per-call allocation (matches the tmpScoopedDefs idiom).
        [System.ThreadStatic] private static List<Thing> tmpStaleTicks;
        // Scratch for defs whose last tag was destroyed by a merge this pass (carried into the self-heal).
        [System.ThreadStatic] private static HashSet<ThingDef> tmpCarryOverDefs;

        public HashSet<Thing> GetHashSet()
        {
            // Capture the defs of tags about to be pruned because their Thing was DESTROYED — typically a
            // MERGE (a stack absorbed into another same-def stack; the absorbed Thing is Destroy()ed). Thing.def
            // survives Destroy(), so we read it here and feed these defs into the self-heal below, so the
            // same-def stack that ABSORBED the destroyed tag is re-tagged. Without this, a merge that kills a
            // def's LAST tag (e.g. CommonSense's interrupt "put carried thing back in inventory" merging into
            // an untagged same-def stack) would strand that stack's surplus untagged — invisible to the unload
            // pass AND the alert: a silent black hole. (Cannot mis-tag a foreign mod's stash, e.g. a Simple
            // Sidearms weapon: HD never tagged that def, so its def never enters this carry-over set.)
            var carryOver = tmpCarryOverDefs ?? (tmpCarryOverDefs = new HashSet<ThingDef>());
            carryOver.Clear();
            foreach (var x in takenToInventory)
                if ((x == null || x.Destroyed) && x?.def != null)
                    carryOver.Add(x.def);

            takenToInventory.RemoveWhere(x => x == null || x.Destroyed);

            // Self-heal: a single scoop can land across MULTIPLE inventory stacks (a yield exceeding the
            // stack limit, e.g. >75 berries, or not merging into the first stack), but the registration only
            // ever tags one of them; stacks also merge/split over time. Treat EVERY current inventory stack
            // whose def we've already scooped (or whose last tag was just merged away) as surplus — otherwise
            // the untagged stacks are invisible to both sharing and the unload pass (stranded in inventory
            // forever). Bounded to already-scooped defs, so a pawn's non-scooped kit is never claimed. (A rare
            // def overlap — e.g. a pawn that harvested healroot AND carries personal herbal medicine — would
            // tag both; harmless: the surplus is merely unloaded to storage, where it stays usable.)
            var owner = (parent as Pawn)?.inventory?.innerContainer;
            if (owner != null && (takenToInventory.Count > 0 || carryOver.Count > 0))
            {
                var defs = tmpScoopedDefs ?? (tmpScoopedDefs = new HashSet<ThingDef>());
                defs.Clear();
                foreach (var t in takenToInventory)
                    defs.Add(t.def);
                foreach (var d in carryOver) // re-seed defs whose last tag a merge just destroyed
                    defs.Add(d);
                for (int i = 0; i < owner.Count; i++)
                {
                    var thing = owner[i];
                    // Re-tagged (merged/split) stacks also re-register with CE's HoldTracker — a merge can grow
                    // a stack past the originally-notified count, and CE drops the un-held excess otherwise.
                    // NEVER def-overlap-tag a genuine Simple Sidearms remembered sidearm: weapons don't stack, so a
                    // sidearm of a scooped weapon's def is a separate Thing that would otherwise be tagged here and
                    // then shipped to storage (SS re-fetches it — the "unloads its own sidearm" bug). The precise
                    // (def,stuff) check only ever excludes a genuine sidearm; a loose swept weapon still tags.
                    if (thing != null && defs.Contains(thing.def)
                        && !SimpleSidearmsCompat.IsRememberedSidearm(parent as Pawn, thing)
                        && takenToInventory.Add(thing))
                    {
                        StampTick(thing);
                        CECompat.NotifyHeld(parent as Pawn, thing, thing.stackCount);
                    }
                }
                defs.Clear();
            }
            carryOver.Clear();
            // Keep the per-item tag-age map in sync with the live set: drop ages for tags that left, and
            // backfill a fresh clock for any tag without one (self-healed above, or loaded from a save).
            if (taggedTick == null)
                taggedTick = new Dictionary<Thing, int>();
            if (taggedTick.Count > takenToInventory.Count)
            {
                var stale = tmpStaleTicks ?? (tmpStaleTicks = new List<Thing>());
                stale.Clear();
                foreach (var kv in taggedTick)
                    if (!takenToInventory.Contains(kv.Key))
                        stale.Add(kv.Key);
                for (int i = 0; i < stale.Count; i++)
                    taggedTick.Remove(stale[i]);
                stale.Clear();
            }
            foreach (var t in takenToInventory)
                if (!taggedTick.ContainsKey(t))
                    StampTick(t);
            return takenToInventory;
        }

        private void StampTick(Thing thing)
        {
            if (thing == null)
                return;
            if (taggedTick == null)
                taggedTick = new Dictionary<Thing, int>();
            taggedTick[thing] = Find.TickManager?.TicksGame ?? 0;
        }

        /// <summary>Tick a still-held tagged stack was first tagged (for the cannot-unload staleness check).
        /// Unknown stacks read as "now" (conservative: a tag with no recorded age never looks stale).</summary>
        public int FirstTaggedTick(Thing thing)
        {
            if (thing != null && taggedTick != null && taggedTick.TryGetValue(thing, out int tick))
                return tick;
            return Find.TickManager?.TicksGame ?? 0;
        }

        public void RegisterHauledItem(Thing thing, int mergedCount = 0)
        {
            // A NEW tag notifies CE's HoldTracker with the full stack, so loadout enforcement doesn't dump
            // the scooped/swept goods on the floor before the unload trip runs. A RE-register of an
            // already-tagged stack notifies only when a merge GREW it (mergedCount > 0) — CE's record
            // otherwise still holds the originally-notified count and a custom-loadout pawn would drop the
            // un-held growth mid-run. Over-counting is the safe direction (CE resets the record to
            // live + count on re-notify). No-op without CE.
            if (thing == null)
                return;
            if (takenToInventory.Add(thing))
            {
                StampTick(thing);
                CECompat.NotifyHeld(parent as Pawn, thing, thing.stackCount);
            }
            else if (mergedCount > 0)
                CECompat.NotifyHeld(parent as Pawn, thing, mergedCount);
        }

        /// <summary>The tracked set WITHOUT the self-heal / CE-notify side effects — for read-only consumers
        /// on the UI/render path (the cannot-unload alert's staleness scan) that must not mutate game state.
        /// May contain destroyed or out-of-inventory tags; callers guard each entry.</summary>
        public HashSet<Thing> PeekHashSet() => takenToInventory;

        public void Deregister(Thing thing) => takenToInventory.Remove(thing);

        /// <summary>The next still-valid pending drop this pawn can scoop, or null. Prunes invalid ones:
        /// despawned/destroyed, on another map (a pawn that changed maps must not walk foreign coords or
        /// scoop across maps), or forbidden (the player forbade it, or vanilla forbade a yield it never
        /// credited to a player pawn — e.g. a mineable finished off by an explosion).</summary>
        public Thing TakeNextValidPending()
        {
            var pawn = parent as Pawn;
            while (pendingSelfPickups.Count > 0)
            {
                var t = pendingSelfPickups[pendingSelfPickups.Count - 1];
                pendingSelfPickups.RemoveAt(pendingSelfPickups.Count - 1);
                if (t != null && t.Spawned && !t.Destroyed
                    && pawn != null && t.MapHeld == pawn.Map && !t.IsForbidden(pawn))
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
