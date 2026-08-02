using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// "How many units of this def are OTHER pawns already bringing to that storage group?" — the colony-wide
    /// scan behind <see cref="DestinationEnroutePolicy"/> and the cross-pawn half of issue #114.
    ///
    /// <para>Hauler's Dream reserves the stacks it PICKS UP but never the space it will DROP INTO (see
    /// <c>JobDriver_BulkHaul.TryMakePreToilReservations</c>, which reserves source stacks only), and vanilla's
    /// haul-to-cell reservation covers a single cell, not a group. So without this, every hauler planning in the
    /// same tick prices the same free space and they collectively pocket N times what fits. This is the missing
    /// term: what is already committed but has not landed yet.</para>
    ///
    /// <para><b>What counts as in flight</b>, in the three shapes a committed delivery can take:</para>
    /// <list type="number">
    /// <item>ALREADY POCKETED — the pawn's HD-tagged inventory SURPLUS (<see cref="InventorySurplus.SurplusOf"/>),
    /// never its keep-stock/sidearms/drug-policy stock, because only the surplus is what an unload actually
    /// deposits. Attributed by one storage probe per carrying pawn and def.</item>
    /// <item>PLANNED BUT NOT YET PICKED UP — the still-SPAWNED entries of a running bulk-haul job's pickup queue
    /// (an unspawned entry is already inside the pawn and counted above, so counting it here would double it).</item>
    /// <item>VANILLA HAULERS — a pawn on vanilla's haul-to-cell, attributed by that job's own destination cell
    /// (exact: the job already names where it is going, so no probe is needed).</item>
    /// </list>
    ///
    /// <para><b>Deliberately an ESTIMATE.</b> Nothing here reserves anything, so a load that never arrives can
    /// never hold a group hostage; the cost is that the answer can be up to one tick stale (see the memo below),
    /// which can still allow a rare extra trip. That is the failure this trades for, on purpose.</para>
    ///
    /// <para><b>Multiplayer determinism.</b> Every figure is an integer SUM over a per-(map, tick) snapshot, so
    /// the result does not depend on the order contributions were collected in — which matters because the
    /// tagged-stack scan iterates a <c>HashSet</c> whose order differs between clients. The one place order
    /// COULD have leaked in (which stack of a def gets probed) is pinned to the lowest <c>thingIDNumber</c>, the
    /// same tiebreak <c>BulkHaul.TakeNearestEligible</c> uses. The probes run with
    /// <c>needAccurateResult:false</c>, which consumes no <c>Rand</c>.</para>
    /// </summary>
    internal static class StorageEnroute
    {
        /// <summary>
        /// One pawn's committed delivery of one def into one storage group. A flat list of these is the whole
        /// per-tick snapshot: contributions are few (only pawns actually hauling produce one), and a linear
        /// scan keyed on three reference/int compares is both cheaper and safer than a composite-key dictionary
        /// (no hash collisions to reason about, and the asker's own rows are trivially skippable).
        /// </summary>
        private struct Delivery
        {
            /// <summary>The delivering pawn's <c>thingIDNumber</c>. The asker's OWN rows are excluded from its
            /// answer — it is about to plan that load itself, so counting it would make the pawn compete with
            /// its own commitment.</summary>
            public int pawnId;

            /// <summary>The destination's budget identity (<see cref="BulkHaul.BudgetGroupOf"/>) — a linked
            /// storage group when there is one, else the slot group. Must be resolved the SAME way the budget
            /// being adjusted was, or the two would key on different objects and never match.</summary>
            public ISlotGroup group;

            /// <summary>The def being delivered. Reference-compared, like every other def key here.</summary>
            public ThingDef def;

            /// <summary>Units of <see cref="def"/> this pawn will put into <see cref="group"/>. Where the exact
            /// figure is unknown the WHOLE stack is recorded: over-stating what is coming only makes the asker
            /// take less, which is the safe direction for this bug.</summary>
            public int units;
        }

        // Per-(map, tick) snapshot of every committed delivery on the map. Rebuilt at most once per tick per
        // map, which is what makes the storage probes below affordable at all — the haul work scan calls into
        // BulkHaul for every candidate it considers, and probing the whole colony per candidate would be far
        // worse than the bug. [ThreadStatic] because a threading mod may fan the work scan to worker threads
        // (mirrors RimIOTCompat.interfaceCellsMemo): each thread keeps its own per-tick snapshot.
        //
        // STALENESS, stated honestly: the snapshot is taken at the FIRST query of a tick, so a pawn that is
        // handed a job later in that same tick is invisible to a pawn planning after it. That costs at most one
        // extra in-flight load per tick, against the sustained N this fixes.
        [ThreadStatic] private static TickKeyedMemo<List<Delivery>> memo;

        // Per-pawn scratch for the pocketed-surplus pass: units summed per def, and the representative stack of
        // each def that gets the storage probe. Reused instead of allocated per pawn (the snapshot build walks
        // every colonist). Cleared at the point of use, never trusted empty from a prior call. SAFETY: one
        // AddPocketedSurplus call runs to completion before the next — the storage probe it makes cannot re-enter
        // this class (UnitsEnrouteTo is reached only from BulkHaul's plan build, which no storage search calls).
        [ThreadStatic] private static Dictionary<ThingDef, int> scratchUnits;
        [ThreadStatic] private static Dictionary<ThingDef, Thing> scratchSample;

        // Self-register the per-session clear with the game-load hygiene sweep (see CacheRegistry), like every
        // other per-tick memo here. This one genuinely needs it: the snapshot holds ISlotGroup/ThingDef/Thing
        // references, so an equal tick number across a quickload could otherwise serve a previous session's
        // objects. The static ctor runs once, the first time any member is touched.
        static StorageEnroute() => CacheRegistry.Register(Clear);

        /// <summary>Drop this thread's snapshot and scratch — game-load hygiene (<see cref="CacheRegistry"/>).
        /// Clears the FinalizeInit (main) thread's slots; worker threads' memos are per-tick self-clearing.</summary>
        private static void Clear()
        {
            memo.Clear();
            scratchUnits?.Clear();
            scratchSample?.Clear();
        }

        /// <summary>
        /// Units of <paramref name="def"/> that pawns OTHER than <paramref name="asker"/> are already committed
        /// to deposit into <paramref name="group"/>. 0 when nothing is coming, when the inputs are incomplete,
        /// or when there is no game clock (a defensive path — this is only ever called mid-game).
        /// </summary>
        /// <param name="asker">The pawn whose haul is being planned; its own committed loads are excluded so it
        /// never competes with itself. Its map is the map that gets scanned.</param>
        /// <param name="group">The destination's budget identity, as produced by
        /// <see cref="BulkHaul.BudgetGroupOf"/> — reference-compared, so it must come from that same helper.</param>
        /// <param name="def">The item def in question. Reference-compared.</param>
        internal static int UnitsEnrouteTo(Pawn asker, ISlotGroup group, ThingDef def)
        {
            var map = asker?.Map;
            if (map == null || group == null || def == null)
                return 0;

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (!memo.TryGet(tick, map.uniqueID, out var deliveries))
            {
                deliveries = BuildSnapshot(map);
                memo.Store(tick, map.uniqueID, deliveries);
            }

            int askerId = asker.thingIDNumber;
            int units = 0;
            for (int i = 0; i < deliveries.Count; i++)
            {
                var delivery = deliveries[i];
                if (delivery.pawnId != askerId && delivery.def == def && delivery.group == group)
                    units += delivery.units;
            }
            return units;
        }

        /// <summary>Collect every committed delivery on <paramref name="map"/>, once per tick. Player pawns
        /// only — a raider or a wild animal is not hauling into our stockpiles. Colony ANIMALS are deliberately
        /// included: a haul-trained animal on vanilla's haul-to-cell is a real load in flight (unlike a route
        /// CLAIM, where animals are excluded because their jobs are not work claims).</summary>
        /// <param name="map">The map to snapshot; never null (the caller resolved it from a spawned pawn).</param>
        private static List<Delivery> BuildSnapshot(Map map)
        {
            var deliveries = new List<Delivery>();
            var player = Faction.OfPlayerSilentFail;
            if (player == null)
                return deliveries;
            var pawns = map.mapPawns.SpawnedPawnsInFaction(player);

            // The probes below PREDICT the unload, so they must be answered allow-all even if an
            // opportunistic / before-carry path happens to sit on this call stack — exactly the reasoning in
            // InventorySurplus.HasUnloadDestination (plan G4: the unload context is the allow-all sentinel). One
            // scope around the whole build rather than one per probe, so it costs a single object per tick.
            using (StorageBuildingFilter.PushContext(StorageFilterContext.Unload))
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p == null)
                        continue;
                    AddPocketedSurplus(deliveries, map, p);
                    AddPlannedPickups(deliveries, map, p);
                    AddVanillaHaul(deliveries, map, p);
                }
            }
            return deliveries;
        }

        /// <summary>Record what <paramref name="p"/> is ALREADY carrying toward storage: the HD-tagged surplus
        /// of each def in its inventory. Only the surplus counts, because only the surplus is what the unload
        /// deposits — a pawn's kept food, sidearms and drug-policy stock never reach a stockpile and would
        /// permanently inflate the estimate.</summary>
        /// <param name="into">The snapshot being built; one row is appended per (def, destination group).</param>
        /// <param name="map">The map the pawn and its destination are on.</param>
        /// <param name="p">The carrying pawn.</param>
        private static void AddPocketedSurplus(List<Delivery> into, Map map, Pawn p)
        {
            var comp = p.GetComp<CompHauledToInventory>();
            var tagged = comp?.PeekHashSet();
            if (tagged == null || tagged.Count == 0)
                return; // the cheap gate: most colonists carry nothing, and this skips the probe entirely
            var inner = p.inventory?.innerContainer;
            if (inner == null)
                return;

            var units = scratchUnits ?? (scratchUnits = new Dictionary<ThingDef, int>());
            var sample = scratchSample ?? (scratchSample = new Dictionary<ThingDef, Thing>());
            units.Clear();
            sample.Clear();
            // PeekHashSet is the read-only view (no self-heal), so this stays safe on the scan path.
            foreach (var t in tagged)
            {
                if (t == null || t.Destroyed || t.def == null || !inner.Contains(t))
                    continue; // a tag whose stack has left the inventory is not carrying anything anywhere
                int surplus = InventorySurplus.SurplusOf(p, t, comp, null);
                if (surplus <= 0)
                    continue;
                units[t.def] = units.TryGetValue(t.def, out int running) ? running + surplus : surplus;
                // Only ONE stack per def is probed, so WHICH stack must not depend on HashSet iteration order
                // (it differs between multiplayer clients, and two stacks of a def can differ in stuff/quality
                // enough to resolve to different storage). Lowest thingIDNumber wins, as elsewhere in HD.
                if (!sample.TryGetValue(t.def, out var best) || t.thingIDNumber < best.thingIDNumber)
                    sample[t.def] = t;
            }

            foreach (var pair in units)
            {
                var group = DestinationGroupFor(map, p, sample[pair.Key]);
                if (group != null)
                    into.Add(new Delivery { pawnId = p.thingIDNumber, group = group, def = pair.Key, units = pair.Value });
            }
        }

        /// <summary>Record what <paramref name="p"/> has COMMITTED to pick up but has not reached yet: the
        /// still-spawned entries of a running bulk-haul job's pickup queue. This is the part that matters most
        /// for the reported bug — several pawns get their plans within a few ticks of each other, long before
        /// any of them has anything in its pockets.</summary>
        /// <param name="into">The snapshot being built; one row is appended per queued stack.</param>
        /// <param name="map">The map the pawn and its destination are on.</param>
        /// <param name="p">The planning pawn.</param>
        private static void AddPlannedPickups(List<Delivery> into, Map map, Pawn p)
        {
            var job = p.CurJob;
            if (job == null || job.def != HaulersDreamDefOf.HaulersDream_BulkHaul)
                return;
            var queue = job.targetQueueB;
            if (queue == null)
                return;
            var counts = job.countQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                var t = queue[i].Thing;
                // An UNSPAWNED queued target has already been picked up — it is inside the pawn now, so the
                // pocketed pass above counts it. Counting it here as well would double this pawn's claim.
                if (t == null || !t.Spawned || t.def == null)
                    continue;
                int planned = counts != null && i < counts.Count ? counts[i] : 0;
                int units = planned > 0 ? Math.Min(planned, t.stackCount) : t.stackCount;
                if (units <= 0)
                    continue;
                var group = DestinationGroupFor(map, p, t);
                if (group != null)
                    into.Add(new Delivery { pawnId = p.thingIDNumber, group = group, def = t.def, units = units });
            }
        }

        /// <summary>Record a plain vanilla haul-to-cell in progress. Exact rather than predicted: the job
        /// already names the destination cell, so the group is read straight off it and no storage probe is
        /// needed.</summary>
        /// <param name="into">The snapshot being built; at most one row is appended.</param>
        /// <param name="map">The map the pawn and its destination are on.</param>
        /// <param name="p">The hauling pawn.</param>
        private static void AddVanillaHaul(List<Delivery> into, Map map, Pawn p)
        {
            var job = p.CurJob;
            if (job == null || job.def != JobDefOf.HaulToCell)
                return;
            // Vanilla retargets targetA to the CARRIED thing once the stack is in hands
            // (Toils_Haul.StartCarryThing), so this resolves the item both before and after pickup.
            var t = job.targetA.Thing;
            if (t == null || t.def == null)
                return;
            var group = GroupAt(map, job.targetB.Cell);
            if (group == null)
                return;
            int units = job.count > 0 ? Math.Min(job.count, t.stackCount) : t.stackCount;
            if (units > 0)
                into.Add(new Delivery { pawnId = p.thingIDNumber, group = group, def = t.def, units = units });
        }

        /// <summary>Where <paramref name="thing"/> will be put down when <paramref name="carrier"/> unloads it,
        /// or null when it has nowhere to go (then it is not in flight to anywhere). DELIBERATELY the same probe
        /// the unload itself makes — same <see cref="StoragePriority.Unstored"/> floor, same carrier, same map —
        /// so what this predicts is what that will actually do. <c>needAccurateResult:false</c> stops at the
        /// first acceptable cell and, unlike the accurate form, rolls no <c>Rand</c>, which is what makes it
        /// safe to run from a work scan under multiplayer.</summary>
        /// <param name="map">The map to search.</param>
        /// <param name="carrier">The pawn that will deliver it (its faction and reachability shape the search).</param>
        /// <param name="thing">The stack whose destination is wanted.</param>
        private static ISlotGroup DestinationGroupFor(Map map, Pawn carrier, Thing thing)
        {
            if (!StoreUtility.TryFindBestBetterStorageFor(thing, carrier, map, StoragePriority.Unstored,
                    carrier.Faction, out IntVec3 cell, out _, needAccurateResult: false))
                return null;
            // A CONTAINER destination (grave, transport pod, modded container) comes back with an invalid cell:
            // its capacity is coordinated by the enroute/reservation system rather than by a cell budget, so
            // there is no group here to attribute the load to.
            return GroupAt(map, cell);
        }

        /// <summary>The budget identity of the storage at <paramref name="cell"/>, or null when the cell is
        /// invalid or holds no slot group. Routed through <see cref="BulkHaul.BudgetGroupOf"/> so this and the
        /// budget it adjusts can never key on different objects.</summary>
        /// <param name="map">The map the cell is on.</param>
        /// <param name="cell">The destination cell; may be invalid (a container destination).</param>
        private static ISlotGroup GroupAt(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid)
                return null;
            return BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(cell));
        }
    }
}
