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
        // Per-def amount the player pinned this pawn to KEEP in inventory (issue #197: "keep N of a def", set by the
        // "Keep X in inventory" order's slider or the Gear-tab keep button). keptCounts[def] = N means HD holds up to
        // N units of def: the unload never sheds the first N (InventorySurplus treats only held-above-N as surplus)
        // and vanilla's drop-unused never touches a kept def. Distinct from takenToInventory (which HD unloads).
        //
        // This REPLACES the old whole-stack `kept` HashSet<Thing> (a Thing-ref set that auto-released when the stack
        // left inventory). The amount model persists a def→count instead, so the player can dial and SEE the amount;
        // it is pruned when the pawn no longer holds ANY of the def (see the heal below), which preserves the old
        // "un-keep when it's gone" behavior without pinning a specific Thing. Pre-#197 saves are migrated on the
        // first heal (see legacyKept).
        private Dictionary<ThingDef, int> keptCounts = new Dictionary<ThingDef, int>();

        // MIGRATION (transient): a pre-#197 save scribed the old whole-stack `kept` Thing-set under
        // "haulersDreamKept". We load it here (Reference mode) and, on the first heal after load, fold each still-held
        // kept Thing into keptCounts[def] (keep at least what the pawn holds of that def), then clear it — so an
        // in-progress "keep this stack" order survives the upgrade as a def keep-count. Null once migrated/absent.
        [System.NonSerialized] private HashSet<Thing> legacyKept;
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

        /// <summary>Cell of this pawn's PREVIOUS plant-work yield drop, with <see cref="lastPlantHarvestTick"/> —
        /// the marker the cluster test compares the next harvest against (near + recent = continuing a cluster =
        /// hold for a section; else isolated = collect now). Transient (a fresh run-local hint, not scribed; after
        /// load the first harvest is simply treated as isolated, which is the safe default).</summary>
        public IntVec3 lastPlantHarvestCell = IntVec3.Invalid;
        public int lastPlantHarvestTick = -99999;

        /// <summary>Whether the pawn's NEXT self-pickup job should pace its pickups as an ISOLATED direct harvest
        /// (<see cref="Core.PickupDelayContext.DirectHarvest"/>) rather than an ordinary auto-haul sweep — set when
        /// an isolated harvest enqueues the job, read once when the job builds its toils. Transient; on a mid-job
        /// reload it defaults to the ordinary auto-haul pacing, and the pause is toil-count-stable either way.</summary>
        public bool selfPickupDirectHarvest;

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
            // Migrate a pre-#197 whole-stack keep set into per-def keep-counts, once, on the first heal after load:
            // pin each still-held kept def to what the player pinned as whole stacks, EXCLUDING any HD-tagged haul
            // cargo of the same def (#225). Runs on the synced heal path so every MP client migrates identically.
            //
            // SCOPE LIMITATION (#225): this only repairs a save that STILL carries the legacyKept signal (i.e. one
            // loaded from a pre-#197 save). A keep pin that was ALREADY inflated by the shipped v1.19-v1.20 buggy
            // migration is not retroactively fixed: that migration dropped the "haulersDreamKept" scribe key on
            // re-save, so no signal remains to migrate from, and an inflated 9 is indistinguishable from a
            // deliberately chosen 9. The player corrects such a pin directly via the Gear-tab keep control.
            if (legacyKept != null && owner != null)
            {
                foreach (var t in legacyKept)
                {
                    if (t == null || t.Destroyed || !owner.Contains(t) || t.def == null)
                        continue;
                    // Migrate to what the player actually pinned as whole stacks (the sum of the still-held
                    // legacy-kept stacks of this def), CAPPED at the non-tagged units (held - taggedUnits) so
                    // freshly-scooped HD-tagged haul cargo of the same def is NEVER folded into the keep (#225).
                    // CountOfDef sums the tagged haul units too, so the old `keptCounts[def] = held` pinned the
                    // surplus (held == kept -> nothing ever unloaded: the "holds 9, keep 7, unloads nothing" bug).
                    int held = CountOfDef(owner, t.def);
                    int taggedUnits = TaggedUnitsOfDef(owner, t.def);
                    int sumOfKeptStackCounts = SumLegacyKeptStackCounts(t.def, owner);
                    int migrated = KeepCountPolicy.MigratedKeep(sumOfKeptStackCounts, held, taggedUnits);
                    // Accumulate toward `migrated`, but never STOMP a live pin (a keep the player already dialed
                    // this session) upward past it; another still-held stack of the same def is then a no-op. The
                    // per-def sums make `migrated` identical across MP clients regardless of iteration order.
                    if (migrated > 0 && (!keptCounts.TryGetValue(t.def, out int cur) || cur < migrated))
                        keptCounts[t.def] = migrated;
                }
                // Only discard the legacy set once the inventory was actually available to fold from, so a heal that
                // somehow ran before the inventory tracker resolved retries on the next heal instead of losing the
                // pre-#197 keep.
                legacyKept = null;
            }
            // Maintain the KEEP counts on the same heal beat: drop a def's pin once the pawn holds NONE of it (used
            // up, dropped from the gear tab, consumed in a recipe). That prune IS the release path — a keep stops the
            // moment the def is gone, matching the old Thing-ref auto-release. Runs on the synced/decision path only
            // (GetHashSet), so it never mutates from a render/alert read; identical across MP clients.
            if (keptCounts.Count > 0)
                PruneEmptyKeptCounts(owner);
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

        /// <summary>The units of <paramref name="def"/> this pawn is pinned to keep in inventory (issue #197), or 0
        /// if none. Side-effect-free — safe on the render/alert/surplus path. Read by <see cref="InventorySurplus"/>
        /// (keep the first N, unload the rest) and the drop-unused guards (never drop a kept def).</summary>
        /// <param name="def">The item def to query. Null yields 0.</param>
        public int KeptCountOf(ThingDef def)
            => def != null && keptCounts.TryGetValue(def, out int n) && n > 0 ? n : 0;

        /// <summary>True iff this pawn keeps any amount of <paramref name="def"/> (a fast, allocation-free gate for
        /// the drop-unused guards and the Gear-tab "is kept" display).</summary>
        /// <param name="def">The item def to query.</param>
        public bool IsKeptDef(ThingDef def) => KeptCountOf(def) > 0;

        /// <summary>The whole keep-count map WITHOUT side effects — for the Gear-tab UI to show every active pin. The
        /// caller must not mutate it (writes go through <see cref="SetKeptCount"/> / <see cref="AddKeptCount"/>, which
        /// are MP-synced).</summary>
        public Dictionary<ThingDef, int> PeekKeptCounts() => keptCounts;

        /// <summary>ADD <paramref name="count"/> to the keep-count for <paramref name="def"/> (the "Keep N in
        /// inventory" order: each order raises the amount held-and-kept by what it pocketed). Called from
        /// <see cref="JobDriver_KeepInInventory"/> on every MP client (the ordered job replicates), so the map stays
        /// deterministic without an explicit sync method. No-op on null/non-positive count.</summary>
        /// <param name="def">The item def being kept.</param>
        /// <param name="count">Units to add to the keep pin (the amount just pocketed).</param>
        public void AddKeptCount(ThingDef def, int count)
        {
            if (def == null || count <= 0)
                return;
            keptCounts.TryGetValue(def, out int cur);
            keptCounts[def] = cur + count;
        }

        /// <summary>SET the absolute keep-count for <paramref name="def"/> (the Gear-tab slider / toggle: the player
        /// dials an exact amount). A count &lt;= 0 removes the pin entirely. NOT MP-safe on its own — the Gear-tab
        /// caller routes through <c>MultiplayerCompat.SetKeptCount</c> so every client applies the same write.</summary>
        /// <param name="def">The item def to pin. Null is a no-op.</param>
        /// <param name="count">The absolute amount to keep; 0 or less clears the pin.</param>
        public void SetKeptCount(ThingDef def, int count)
        {
            if (def == null)
                return;
            if (count <= 0)
                keptCounts.Remove(def);
            else
                keptCounts[def] = count;
        }

        /// <summary>Total units of <paramref name="def"/> across every stack in <paramref name="owner"/>. Small
        /// helper for the keep-count prune + migration (kept defs are few).</summary>
        private static int CountOfDef(ThingOwner owner, ThingDef def)
        {
            if (owner == null || def == null)
                return 0;
            int n = 0;
            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t != null && t.def == def)
                    n += t.stackCount;
            }
            return n;
        }

        /// <summary>Sum of the still-held legacy-kept stacks of <paramref name="def"/> (issue #225 migration): the
        /// units the player pinned as whole stacks under the pre-#197 model, the amount to migrate into the per-def
        /// keep BEFORE the tagged-haul cap. Reads <see cref="legacyKept"/> (a handful of entries, so the scan is
        /// trivial); counts only entries still in <paramref name="owner"/>, so a used-up / dropped kept stack adds 0.</summary>
        /// <param name="def">The kept def being migrated.</param>
        /// <param name="owner">The pawn's inventory container.</param>
        private int SumLegacyKeptStackCounts(ThingDef def, ThingOwner owner)
        {
            if (legacyKept == null || def == null || owner == null)
                return 0;
            int n = 0;
            foreach (var t in legacyKept)
                if (t != null && !t.Destroyed && t.def == def && owner.Contains(t))
                    n += t.stackCount;
            return n;
        }

        /// <summary>Units of <paramref name="def"/> across the pawn's HD-TAGGED inventory stacks still held (the haul
        /// cargo tracked in <see cref="takenToInventory"/>). The #225 migration subtracts these from the held total so
        /// a pre-#197 whole-stack keep never folds freshly-scooped haul units into the keep pin.</summary>
        /// <param name="owner">The pawn's inventory container (a tag OUT of inventory, e.g. in hands mid-craft, is
        /// not counted, matching <see cref="CountOfDef"/>, which also only sees the container).</param>
        /// <param name="def">The def whose tagged units to total.</param>
        private int TaggedUnitsOfDef(ThingOwner owner, ThingDef def)
        {
            if (owner == null || def == null)
                return 0;
            int n = 0;
            foreach (var t in takenToInventory)
                if (t != null && !t.Destroyed && t.def == def && owner.Contains(t))
                    n += t.stackCount;
            return n;
        }

        // Reused scratch for the keep-count prune, so the per-tick heal allocates nothing when pruning.
        [System.ThreadStatic] private static List<ThingDef> tmpKeptToPrune;

        /// <summary>Drop every keep pin whose def the pawn no longer holds ANY of — the release path (a keep ends
        /// when the def is gone). Called from the synced heal only.</summary>
        /// <param name="owner">The pawn's inventory container.</param>
        private void PruneEmptyKeptCounts(ThingOwner owner)
        {
            var toPrune = tmpKeptToPrune ?? (tmpKeptToPrune = new List<ThingDef>());
            toPrune.Clear();
            foreach (var kv in keptCounts)
                if (kv.Value <= 0 || CountOfDef(owner, kv.Key) <= 0)
                    toPrune.Add(kv.Key);
            for (int i = 0; i < toPrune.Count; i++)
                keptCounts.Remove(toPrune[i]);
            toPrune.Clear();
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
            // Track the Thing REFERENCE, not its list index. The cleanup pass below removes stale entries with
            // RemoveAt(i), which shifts every entry above i down by one — so a `bestIndex` captured earlier in
            // the scan (at a higher index) silently drifts past the end of the now-shorter list, and the
            // post-loop `pendingSelfPickups[bestIndex]` throws ArgumentOutOfRangeException (issue #172).
            // The Thing reference is immune to index shifts; Remove(thing) finds it in O(n) (trivial for a
            // pending queue that holds at most a handful of drops from one work run), and the list never holds
            // duplicates (every intake path — SelfPickupClaims.Claim, YieldRouter — guards with Contains).
            Thing best = null;
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
                    best = t;
                }
            }
            if (skippedUnreachable > 0)
                HDLog.Dbg($"{pawn} self-pickup: skipped {skippedUnreachable} unreachable pending drop(s)"
                    + (best == null ? "; queue now empty." : "."));
            if (best == null)
                return null;
            pendingSelfPickups.Remove(best);
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
            // #197: the per-def keep-count map (new single source of truth). A pre-#197 save has no such key, so this
            // starts empty and is filled by the legacy migration below.
            Scribe_Collections.Look(ref keptCounts, "haulersDreamKeptCounts", LookMode.Def, LookMode.Value);
            // Pre-#197 whole-stack keep set: read + resolve ONLY on load (both load phases, never on save) so an
            // in-progress "keep this stack" order migrates into keptCounts on the first heal (see GetHashSet), and the
            // stale key is never written back. LookMode.Reference needs both LoadingVars (record IDs) and
            // ResolvingCrossRefs (resolve to Things) to bind the refs.
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                Scribe_Collections.Look(ref legacyKept, "haulersDreamKept", LookMode.Reference);
            Scribe_Values.Look(ref lastYieldTick, "haulersDreamLastYieldTick", -99999);
            Scribe_Values.Look(ref autoHaulYields, "haulersDreamAutoHaulYields", true);
            if (takenToInventory == null)
                takenToInventory = new HashSet<Thing>();
            if (keptCounts == null)
                keptCounts = new Dictionary<ThingDef, int>();
        }
    }
}
