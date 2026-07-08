using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

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
        // Stacks the player told this pawn to KEEP in inventory (the "Keep X in inventory" order). Distinct from
        // takenToInventory (which HD unloads to storage): a kept stack is HELD — the unload never touches it
        // (InventorySurplus returns 0 for it) and vanilla's drop-unused never sheds it. Thing-ref (not a count) so it
        // auto-releases: the GetHashSet heal drops any entry that was consumed/destroyed or that left this inventory
        // (used in a recipe, dropped from the gear tab), which is how a kept item is un-kept — no bookkeeping tick.
        // Kept stacks are added canMerge:false, so they never fold into personal/hauled stock (kept stays isolated).
        private HashSet<Thing> kept = new HashSet<Thing>();
        public int lastYieldTick = -99999;

        /// <summary>Tick the <see cref="GetHashSet"/> self-heal last ran. The heal (an <c>owner.Count</c> inventory
        /// walk + Simple Sidearms reflection + CE re-notify + tag-age sync) is idempotent WITHIN one tick — the
        /// inventory can't change mid-tick from the read-only share/probe callers that drive it — so once healed
        /// this tick, repeat calls short-circuit straight to <c>return takenToInventory</c>. Any path that MUTATES
        /// the set (scoop registration / deregister) resets this to force the next call to re-heal, so a same-tick
        /// scoop is always observed (the scoop path itself calls <see cref="RegisterHauledItem"/> which invalidates,
        /// then the next GetHashSet re-heals — correctness is preserved). Transient (in-flight timing, not scribed).</summary>
        [System.NonSerialized] private int lastHealTick = -1;

        /// <summary>Per-pawn opt-out for the auto-haul-into-inventory feature, surfaced as a Command_Toggle
        /// gizmo. Default ON (so old saves and untouched pawns keep scooping); scribed with a true default so
        /// a pre-feature save loads ON. Gates only the SCOOP/sweep/self-pickup intake paths — a pawn toggled
        /// OFF still empties what it already carries (the unload paths never read this).</summary>
        public bool autoHaulYields = true;

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
        // Scratch for the Core TagHealPolicy hand-off (all reused per-thread, cleared at each use): the scooped
        // def union, the flat per-stack view fed to the policy, and the indices it selects to (re)tag.
        [System.ThreadStatic] private static HashSet<object> tmpScoopedUnion;
        [System.ThreadStatic] private static List<TagHealPolicy.Stack> tmpStacks;
        [System.ThreadStatic] private static List<int> tmpTagIndices;

        public HashSet<Thing> GetHashSet()
        {
            // HD-GETHASHSET: already self-healed this tick (and no mutation since — a scoop/deregister resets the
            // stamp) -> skip the whole heal (owner.Count walk + SS reflection + CE re-notify + tag-age sync) and
            // hand back the live set. The read-only share/probe callers (bill ingredient search, load deposit
            // probes, GetRest/GetFood/GetJoy postfixes) hit this many times per scan; the inventory can't change
            // mid-tick from them, so the second-and-later calls are pure waste without this gate. (ShouldReheal:
            // re-heal unless lastHealTick == now; a tickless now == -1 always re-heals — see TagHealPolicy.)
            int now = Find.TickManager?.TicksGame ?? -1;
            if (!TagHealPolicy.ShouldReheal(lastHealTick, now))
                return takenToInventory;

            // Capture the defs of tags about to be pruned because their Thing was DESTROYED — typically a
            // MERGE (a stack absorbed into another same-def stack; the absorbed Thing is Destroy()ed). Thing.def
            // survives Destroy(), so we read it here and feed these "carry-over" defs into the heal below, so the
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

            // Self-heal (the DECISION lives in the pure, unit-tested Core TagHealPolicy): a single scoop can land
            // across MULTIPLE inventory stacks (a yield exceeding the stack limit, e.g. >75 berries, or not
            // merging into the first stack) but the registration tags only one; stacks also merge/split over
            // time. Treat every current inventory stack whose def we've already scooped — or whose last tag a
            // merge just destroyed (carryOver) — as surplus and (re)tag it. Bounded to scooped defs (a pawn's
            // non-scooped kit is never claimed) and never a genuine Simple Sidearms remembered sidearm (it would
            // be re-fetched — the "unloads its own sidearm" bug). A rare def overlap (harvested healroot +
            // personal herbal medicine) tags both; harmless: the surplus is merely unloaded to storage.
            var owner = (parent as Pawn)?.inventory?.innerContainer;
            // Maintain the KEPT set on the same heal beat: drop refs consumed/destroyed or that left this inventory
            // (used in a recipe, or dropped from the gear tab). That removal IS the release path — a kept item stops
            // being kept the moment it is no longer held. Runs on the synced/decision path only (GetHashSet), so it
            // never mutates from a render/alert read; identical across MP clients (each prunes the same dead refs).
            if (kept.Count > 0)
                kept.RemoveWhere(x => x == null || x.Destroyed || owner == null || !owner.Contains(x));
            if (owner != null && (takenToInventory.Count > 0 || carryOver.Count > 0))
            {
                var pawn = parent as Pawn;
                var liveDefs = tmpScoopedDefs ?? (tmpScoopedDefs = new HashSet<ThingDef>());
                liveDefs.Clear();
                foreach (var t in takenToInventory)
                    liveDefs.Add(t.def);
                var union = tmpScoopedUnion ?? (tmpScoopedUnion = new HashSet<object>());
                TagHealPolicy.BuildScoopedUnion(liveDefs, carryOver, union); // union = live-tag defs ∪ carry-over defs

                // Flatten the inventory into the policy's per-stack view. The keep-exclusion (a Simple Sidearms
                // reflection walk, and the Grab Your Tool carried-tool check) is resolved LAZILY — only for a
                // union-member, not-already-tagged stack (the only stacks the exclusion can affect) — preserving
                // the original's reflection short-circuit (and both checks short-circuit cheaply on a non-weapon
                // stack, or when their mod is absent, regardless).
                var stacks = tmpStacks ?? (tmpStacks = new List<TagHealPolicy.Stack>());
                stacks.Clear();
                for (int i = 0; i < owner.Count; i++)
                {
                    var thing = owner[i];
                    var def = thing?.def;
                    bool alreadyTagged = thing != null && takenToInventory.Contains(thing);
                    bool candidate = def != null && !alreadyTagged && union.Contains(def);
                    // Never auto-tag a stack another system keeps for the pawn: a genuine SS remembered sidearm, or
                    // a Grab Your Tool carried tool. Tagging it would make HD ship it to storage while that mod
                    // re-fetches it (an unload<->pickup loop).
                    bool excludeFromTag = candidate && (SimpleSidearmsCompat.IsRememberedSidearm(pawn, thing)
                                                        || GrabYourToolCompat.IsCarriedTool(pawn, thing));
                    stacks.Add(new TagHealPolicy.Stack(def, alreadyTagged, excludeFromTag));
                }

                var toTag = tmpTagIndices ?? (tmpTagIndices = new List<int>());
                TagHealPolicy.SelectStacksToTag(union, stacks, toTag);
                for (int i = 0; i < toTag.Count; i++)
                {
                    var thing = owner[toTag[i]];
                    // Re-tagged (merged/split) stacks also re-register with CE's HoldTracker — a merge can grow a
                    // stack past the originally-notified count, and CE drops the un-held excess otherwise.
                    if (takenToInventory.Add(thing))
                    {
                        StampTick(thing);
                        CECompat.NotifyHeld(pawn, thing, thing.stackCount);
                    }
                }
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
            // Stamp the heal so the rest of this tick's read-only share/probe calls short-circuit (above).
            // Only when the tick clock is available (-1 = no TickManager, e.g. a unit-test/edit-mode call) so a
            // tickless call never poisons the stamp into matching a future "now == -1".
            if (now != -1)
                lastHealTick = now;
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
            // A new tag (or any registration) mutates the tracked set, so force the next GetHashSet to re-heal:
            // a same-tick scoop must be reflected in the share/probe view even though it was already healed
            // earlier this tick (HD-GETHASHSET). Cheap (an int write) vs the missed-scoop correctness it protects.
            lastHealTick = -1;
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

        /// <summary>The KEPT-in-inventory set WITHOUT side effects — for the read-only unload-surplus math and the
        /// drop-unused guards. May hold a stale ref (a stack dropped from the gear tab isn't pruned until the next
        /// GetHashSet heal), but a stale ref never matches a LIVE inventory thing, so a <c>Contains(liveThing)</c>
        /// test is safe; the heal (GetHashSet) is what removes stale entries.</summary>
        public HashSet<Thing> PeekKept() => kept;

        /// <summary>Mark <paramref name="thing"/> as kept in this pawn's inventory (the "Keep X in inventory" order),
        /// so HD's unload never hauls it away and vanilla's drop-unused never sheds it, until it leaves inventory.
        /// Called from <see cref="JobDriver_KeepInInventory"/> on every MP client (the ordered job replicates), so the
        /// set stays deterministic. No-op on null.</summary>
        public void RegisterKept(Thing thing)
        {
            if (thing == null)
                return;
            kept.Add(thing);
        }

        public void Deregister(Thing thing)
        {
            // Mutates the tracked set -> force a re-heal next GetHashSet (HD-GETHASHSET), so a same-tick share/
            // probe view doesn't keep handing out a just-removed tag from the short-circuited cache.
            lastHealTick = -1;
            takenToInventory.Remove(thing);
        }

        /// <summary>The still-valid pending drop NEAREST to this pawn's current position, or null. Prunes invalid
        /// ones along the way: despawned/destroyed, on another map (a pawn that changed maps must not walk
        /// foreign coords or scoop across maps), forbidden (the player forbade it, or vanilla forbade a yield it
        /// never credited to a player pawn, e.g. a mineable finished off by an explosion), or genuinely
        /// UNREACHABLE (issue #160: this queue is filled over a whole work run, from the producer's own drops
        /// via RecordSelfPickup, which never checks reachability because the item is always right where the
        /// pawn just worked, plus the area-cleanup sweep's nearby loose stacks; by the time an OLDER entry is
        /// walked to, a later-dropped stack, another pawn, or a newly-grown plant can have sealed off its only
        /// approach). JobDriver_SelfPickup's goto toil has no custom fail handler, so walking toward an
        /// unreachable target lets vanilla's own pathing failure end the job as Errored, and vanilla's
        /// Pawn_JobTracker.EndCurrentJob response to an Errored/ErroredPather condition is a hardcoded,
        /// uninterruptible 250-tick JobDefOf.Wait (decompile-verified): a freshly EnqueueFirst'd self-pickup
        /// just sits queued behind it, since EnqueueFirst never preempts a job already running, only
        /// ThinkNode_QueuedJob's own next determination does. Repeating for however many pending stacks are
        /// similarly stuck is exactly the reported "colonists standing 'Wait' for ~10 seconds, worse with more
        /// colonists". Checked with the SAME PawnCanAutomaticallyHaulFast every sibling picker (BulkHaul's
        /// snowball, the area-cleanup sweep) already gates on, so an entry that would have been skipped there
        /// is never even queued into the sweep in the first place; this is the one intake path (a producer's
        /// own fresh drop) that never had that check.
        ///
        /// NEAREST rather than last-queued (the previous behavior): the list is built nearest-to-farthest per
        /// sweep event but across a whole work run, so blindly popping its tail walked the FARTHEST entry ever
        /// queued first. Scanning for the entry nearest to the pawn's CURRENT position (not wherever it stood
        /// when the item was queued) is what "take what's closer" actually means, and it composes with
        /// <see cref="SelfPickupClaims"/>: a pawn's own queue can still hold an over-reaching sweep candidate a
        /// closer colleague hasn't reclaimed yet, so preferring the nearest one here keeps that pawn's walking
        /// path sensible even before any cross-pawn reassignment happens.</summary>
        public Thing TakeNextValidPending()
        {
            var pawn = parent as Pawn;
            if (pawn == null)
            {
                pendingSelfPickups.Clear();
                return null;
            }
            int skippedUnreachable = 0;
            int bestIndex = -1;
            float bestDistSq = float.MaxValue;
            for (int i = pendingSelfPickups.Count - 1; i >= 0; i--)
            {
                var t = pendingSelfPickups[i];
                // The self-pickup queue is persisted, so an entry can be stale (queued before the danger cap, or
                // now only reachable across vacuum/fire). Discard it here (left for normal hauling), never
                // walked to, so the producer never sends a suit-less pawn into space to scoop its own drop.
                // Self-heals existing saves.
                bool valid = t != null && t.Spawned && !t.Destroyed
                    && t.MapHeld == pawn.Map && !t.IsForbidden(pawn) && ExtraSweepReach.Allows(pawn, t);
                if (valid && !HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced: false))
                {
                    valid = false;
                    skippedUnreachable++; // leave it on the ground for normal hauling; never walk into a pathing failure
                }
                if (!valid)
                {
                    pendingSelfPickups.RemoveAt(i);
                    SelfPickupClaims.Release(t, pawn);
                    continue;
                }
                float distSq = (t.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIndex = i;
                }
            }
            if (skippedUnreachable > 0)
                HDLog.Dbg($"{pawn} self-pickup: skipped {skippedUnreachable} unreachable pending drop(s)"
                    + (bestIndex < 0 ? "; queue now empty." : "."));
            if (bestIndex < 0)
                return null;
            var best = pendingSelfPickups[bestIndex];
            pendingSelfPickups.RemoveAt(bestIndex);
            SelfPickupClaims.Release(best, pawn);
            return best;
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
            Scribe_Collections.Look(ref kept, "haulersDreamKept", LookMode.Reference);
            Scribe_Values.Look(ref lastYieldTick, "haulersDreamLastYieldTick", -99999);
            Scribe_Values.Look(ref autoHaulYields, "haulersDreamAutoHaulYields", true);
            if (takenToInventory == null)
                takenToInventory = new HashSet<Thing>();
            if (kept == null)
                kept = new HashSet<Thing>();
        }
    }
}
