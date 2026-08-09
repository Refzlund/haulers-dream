using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// ONE pawn's cargo, in the three shapes a committed delivery can take. This is the "amount" half of the
    /// storage claim ledger: <see cref="StorageCommitments"/> records WHERE a pawn is taking a def, and this
    /// answers HOW MUCH of it that pawn can actually be seen to be moving right now.
    ///
    /// <para><b>What counts as in flight:</b></para>
    /// <list type="number">
    /// <item>ALREADY POCKETED — the pawn's HD-tagged inventory SURPLUS
    /// (<see cref="InventorySurplus.SurplusOf"/>), never its keep-stock / sidearms / drug-policy stock,
    /// because only the surplus is what an unload actually deposits.</item>
    /// <item>PLANNED BUT NOT YET PICKED UP — the still-SPAWNED entries of a running bulk-haul job's pickup
    /// queue. An unspawned entry is already inside the pawn and counted above, so counting it here would
    /// double it.</item>
    /// <item>VANILLA HAULERS — a pawn on vanilla's haul-to-cell, or holding a stack in its hands. The job
    /// already names its destination cell, so that case needs no storage probe at all.</item>
    /// </list>
    ///
    /// <para><b>Why this replaced a colony-wide per-tick snapshot.</b> The previous shape of this file built
    /// one list of every delivery on the map, memoised for the tick. That memo is precisely why the #114 fix
    /// did not hold: two haulers planning in the SAME tick both read a snapshot taken before either of them
    /// committed. Per-pawn evidence has no such freeze — it is measured on demand, from live state, and the
    /// authoritative "who promised what" now lives in the ledger rather than being re-derived each tick.</para>
    ///
    /// <para><b>Multiplayer determinism.</b> Every figure is an integer sum, so the VALUES do not depend on
    /// the order contributions were collected in — which matters because the tagged-stack scan iterates a
    /// <c>HashSet</c>. Which stack of a def gets the destination probe is pinned to the lowest
    /// <c>thingIDNumber</c>, the same tiebreak <c>BulkHaul.TakeNearestEligible</c> uses, and probes run with
    /// <c>needAccurateResult:false</c>, which consumes no <c>Rand</c>.</para>
    ///
    /// <para>→ GOTCHA: order-independent VALUES are not the same as an order-independent LIST, and the
    /// difference is a desync. A consumer that walks <see cref="Collect"/>'s output performing a side effect
    /// each step can see — the janitor's adoption pass — turns the enumeration order into game state, and this
    /// class does not fix that order. <see cref="ByDefName"/> exists for exactly those consumers.</para>
    /// </summary>
    internal static class StorageEvidence
    {
        /// <summary>
        /// One def's worth of a single pawn's cargo, with the sample stack a destination probe should use.
        /// </summary>
        internal struct PawnCargo
        {
            /// <summary>The def being moved. Reference-compared everywhere downstream.</summary>
            public ThingDef def;

            /// <summary>Units of <see cref="def"/> this pawn is carrying or committed to fetch. Where the
            /// exact figure is unknown the WHOLE stack is recorded: over-stating what is coming only makes
            /// other pawns take less, which is the safe direction for this bug.</summary>
            public int units;

            /// <summary>A representative stack of <see cref="def"/> to probe storage with. Lowest
            /// <c>thingIDNumber</c> of the candidates, so two multiplayer clients probe the same one.</summary>
            public Thing sample;

            /// <summary>The destination read straight off a running vanilla haul job, when there is one.
            /// Null means "probe for it" — <see cref="sample"/> is then the stack to probe with.</summary>
            public ISlotGroup knownGroup;
        }

        /// <summary>
        /// Total order over <see cref="PawnCargo"/> entries by <c>defName</c>, for a consumer whose EARLIER
        /// iterations change what its LATER ones see (the janitor's adoption pass: each commit is visible to
        /// the next entry's storage probe). Such a consumer must sort first, because
        /// <see cref="Collect"/>'s own output order is not something two multiplayer clients need to agree on.
        ///
        /// <para>→ GOTCHA: ORDINAL, never <c>string.Compare</c>. The default overload is CULTURE-sensitive, so
        /// a Turkish-locale client and an English one would order the same two defNames differently — a desync
        /// introduced by the very code meant to prevent one. <c>defName</c> is unique among ThingDefs, so this
        /// is a total order with no ties left to break.</para>
        ///
        /// <para>Cached as a field rather than passed as a method group: every method-group conversion
        /// allocates a fresh delegate, and this one is handed to <c>List.Sort</c> on a per-pawn path.</para>
        /// </summary>
        internal static readonly Comparison<PawnCargo> ByDefName = CompareByDefName;

        /// <summary>Order two cargo entries by their def's <c>defName</c>, ordinally.</summary>
        /// <param name="a">First entry.</param>
        /// <param name="b">Second entry.</param>
        /// <returns>Negative, zero or positive per <see cref="Comparison{T}"/>. An entry with no def sorts
        /// last; every consumer skips such an entry anyway, so where it lands only has to be consistent.</returns>
        private static int CompareByDefName(PawnCargo a, PawnCargo b)
        {
            string x = a.def?.defName;
            string y = b.def?.defName;
            if (x == null)
                return y == null ? 0 : 1;
            if (y == null)
                return -1;
            return string.CompareOrdinal(x, y);
        }

        // Per-pawn scratch for the pocketed-surplus pass: units summed per def, and the representative stack
        // of each def. Reused instead of allocated per pawn (a collect runs per pawn per janitor sweep and on
        // every evidence miss). Cleared at the point of use, never trusted empty from a prior call.
        // SAFETY: one Collect call runs to completion before the next on a given thread — Collect makes no
        // storage probe and therefore cannot re-enter itself; the janitor's probe happens AFTER Collect
        // returns, over the caller's own list.
        [ThreadStatic] private static Dictionary<ThingDef, int> scratchUnits;
        [ThreadStatic] private static Dictionary<ThingDef, Thing> scratchSample;

        // Self-register the per-session clear with the game-load hygiene sweep (see CacheRegistry): the
        // scratch holds Thing references, so an equal tick number across a quickload could otherwise leave a
        // previous session's objects reachable. The static ctor runs once, on first touch of any member.
        static StorageEvidence() => CacheRegistry.Register(Clear);

        /// <summary>Drop this thread's scratch — game-load hygiene (<see cref="CacheRegistry"/>).</summary>
        private static void Clear()
        {
            scratchUnits?.Clear();
            scratchSample?.Clear();
        }

        /// <summary>
        /// Everything <paramref name="p"/> is currently moving, one entry per def.
        ///
        /// <para>→ GOTCHA: the ORDER of the entries is not part of the contract. The pocketed pass below walks
        /// a <c>HashSet</c> into a <c>Dictionary</c>, and a host and a mid-game joiner rebuild that set from
        /// different insert/remove histories, so two multiplayer clients can enumerate the same cargo in
        /// different orders. Summing the entries is safe (integer addition is commutative); WALKING them with
        /// a side effect each step sees is not, and such a consumer must sort by
        /// <see cref="ByDefName"/> first. Sorting here instead would put a delegate allocation and a sort on
        /// the per-evidence-miss path to fix one cold caller.</para>
        /// </summary>
        /// <param name="p">The pawn to weigh. A null pawn, or one with no map, contributes nothing.</param>
        /// <param name="into">The caller's list, CLEARED first and then filled. The caller owns it, which is
        /// what lets the janitor keep iterating while its storage probes re-enter the evidence path on a
        /// different buffer.</param>
        internal static void Collect(Pawn p, List<PawnCargo> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (p == null || p.Map == null)
                return;

            AddPocketedSurplus(into, p);
            AddPlannedPickups(into, p);
            AddCarriedHaul(into, p);
        }

        /// <summary>
        /// Units of one def <paramref name="p"/> is moving. The clamp behind every claim in the ledger.
        /// </summary>
        /// <param name="p">The pawn to weigh.</param>
        /// <param name="def">The def in question, reference-compared.</param>
        /// <param name="scratch">A caller-owned buffer to collect into, so this allocates nothing per call.</param>
        /// <returns>Units of that def in the pawn's hands, tagged inventory surplus, or pickup queue.</returns>
        internal static int UnitsOf(Pawn p, ThingDef def, List<PawnCargo> scratch)
        {
            if (p == null || def == null || scratch == null)
                return 0;
            Collect(p, scratch);
            int units = 0;
            for (int i = 0; i < scratch.Count; i++)
                if (scratch[i].def == def)
                    units += scratch[i].units;
            return units < 0 ? 0 : units;
        }

        /// <summary>Record what <paramref name="p"/> is ALREADY carrying toward storage: the HD-tagged
        /// surplus of each def in its inventory. Only the surplus counts, because only the surplus is what
        /// the unload deposits — a pawn's kept food, sidearms and drug-policy stock never reach a stockpile
        /// and would permanently inflate the figure.</summary>
        /// <param name="into">The list being filled; one entry per def.</param>
        /// <param name="p">The carrying pawn.</param>
        private static void AddPocketedSurplus(List<PawnCargo> into, Pawn p)
        {
            var comp = p.GetComp<CompHauledToInventory>();
            var tagged = comp?.PeekHashSet();
            if (tagged == null || tagged.Count == 0)
                return; // the cheap gate: most colonists carry nothing
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
                // Only ONE stack per def is probed, so WHICH stack must not depend on HashSet iteration
                // order (it differs between multiplayer clients, and two stacks of a def can differ in
                // stuff/quality enough to resolve to different storage). Lowest thingIDNumber wins.
                if (!sample.TryGetValue(t.def, out var best) || t.thingIDNumber < best.thingIDNumber)
                    sample[t.def] = t;
            }

            foreach (var pair in units)
                Accumulate(into, pair.Key, pair.Value, sample[pair.Key], null);
        }

        /// <summary>Record what <paramref name="p"/> has COMMITTED to pick up but has not reached yet: the
        /// still-spawned entries of a running bulk-haul job's pickup queue. This is the part that matters
        /// most for the reported bug — several pawns get their plans within a few ticks of each other, long
        /// before any of them has anything in its pockets.</summary>
        /// <param name="into">The list being filled.</param>
        /// <param name="p">The planning pawn.</param>
        private static void AddPlannedPickups(List<PawnCargo> into, Pawn p)
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
                Accumulate(into, t.def, units, t, null);
            }
        }

        /// <summary>Record a hand-carried haul: a plain vanilla haul-to-cell in progress, or the stack an
        /// HD unload has just pulled out of the pawn's pockets. The vanilla job names its destination cell
        /// outright, so that group is EXACT and needs no probe.
        ///
        /// <para>→ GOTCHA: the carried stack is read ONLY for those two jobs, never as a general fallback.
        /// <c>carryTracker</c> is how vanilla carries every kind of non-haul cargo — steel to a blueprint
        /// (<c>JobDriver_HaulToContainer</c>), shells to a turret, components to a broken machine, ingredients
        /// to a bill, food to a barrel — and treating any of those as storage-bound cargo would have the
        /// janitor ADOPT it as a real claim against the stockpile that def normally lives in, holding a shelf
        /// against every hauler because a builder walked past with a load of steel. The colony-wide snapshot
        /// this replaced had no carried-stack pass at all, so the fallback would have been a regression, not
        /// a port.</para></summary>
        /// <param name="into">The list being filled.</param>
        /// <param name="p">The hauling pawn.</param>
        private static void AddCarriedHaul(List<PawnCargo> into, Pawn p)
        {
            var job = p.CurJob;
            if (job == null)
                return;

            // Vanilla retargets targetA to the CARRIED thing once the stack is in hands
            // (Toils_Haul.StartCarryThing), so the job's own target resolves the item both before and after
            // pickup — which is why the carried stack itself is never consulted on this path.
            if (job.def == JobDefOf.HaulToCell)
            {
                var t = job.targetA.Thing;
                if (t?.def == null)
                    return;
                int units = job.count > 0 ? Math.Min(job.count, t.stackCount) : t.stackCount;
                if (units > 0)
                    Accumulate(into, t.def, units, t, GroupAt(p.Map, job.targetB.Cell));
                return;
            }

            // HD's own unload takes one tagged stack OUT of the inventory and into the hands to walk it to
            // storage, at which point the pocketed pass above can no longer see it. This is the one moment
            // that stack would otherwise vanish from the accounting mid-delivery.
            if (job.def != HaulersDreamDefOf.HaulersDream_UnloadInventory)
                return;
            var carried = p.carryTracker?.CarriedThing;
            if (carried?.def != null && carried.stackCount > 0)
                Accumulate(into, carried.def, carried.stackCount, carried, null);
        }

        /// <summary>Fold one contribution into the per-def list, merging with an existing entry for the same
        /// def rather than appending a second (a reader sums per def, and a duplicate entry would make the
        /// janitor probe twice for one load).</summary>
        /// <param name="into">The list being filled.</param>
        /// <param name="def">The def contributed.</param>
        /// <param name="units">Units contributed.</param>
        /// <param name="sample">The stack to probe storage with; kept only if it is the lowest-id candidate
        /// so far, for multiplayer stability.</param>
        /// <param name="knownGroup">An exact destination when one is known; it wins over a probe.</param>
        private static void Accumulate(List<PawnCargo> into, ThingDef def, int units, Thing sample, ISlotGroup knownGroup)
        {
            for (int i = 0; i < into.Count; i++)
            {
                if (into[i].def != def)
                    continue;
                var merged = into[i];
                merged.units += units;
                if (merged.sample == null
                    || (sample != null && sample.thingIDNumber < merged.sample.thingIDNumber))
                    merged.sample = sample;
                if (merged.knownGroup == null)
                    merged.knownGroup = knownGroup;
                into[i] = merged;
                return;
            }
            into.Add(new PawnCargo { def = def, units = units, sample = sample, knownGroup = knownGroup });
        }

        /// <summary>Where <paramref name="thing"/> will be put down when <paramref name="carrier"/> unloads
        /// it, or null when it has nowhere to go (then it is not in flight to anywhere). DELIBERATELY the
        /// same probe the unload itself makes — same <see cref="StoragePriority.Unstored"/> floor, same
        /// carrier, same map — so what this predicts is what that will actually do.
        /// <c>needAccurateResult:false</c> stops at the first acceptable cell and, unlike the accurate form,
        /// rolls no <c>Rand</c>, which is what makes it safe to run under multiplayer.</summary>
        /// <param name="map">The map to search.</param>
        /// <param name="carrier">The pawn that will deliver it (its faction and reachability shape the search).</param>
        /// <param name="thing">The stack whose destination is wanted.</param>
        /// <returns>The destination's budget identity, or null.</returns>
        internal static ISlotGroup DestinationGroupFor(Map map, Pawn carrier, Thing thing)
        {
            if (map == null || carrier == null || thing == null)
                return null;
            // The probe PREDICTS the unload, so it must be answered allow-all even if an opportunistic /
            // before-carry path happens to sit on this call stack — the same reasoning as
            // InventorySurplus.HasUnloadDestination (the unload context is the allow-all sentinel).
            using (StorageBuildingFilter.PushContext(StorageFilterContext.Unload))
            {
                if (!StoreUtility.TryFindBestBetterStorageFor(thing, carrier, map, StoragePriority.Unstored,
                        carrier.Faction, out IntVec3 cell, out _, needAccurateResult: false))
                    return null;
                // A CONTAINER destination (grave, transport pod, modded container) comes back with an
                // invalid cell: its capacity is coordinated by vanilla's own enroute/reservation system
                // rather than by a cell budget, so there is no group here to attribute the load to.
                return GroupAt(map, cell);
            }
        }

        /// <summary>The budget identity of the storage at <paramref name="cell"/>, or null when the cell is
        /// invalid or holds no slot group. Routed through <see cref="BulkHaul.BudgetGroupOf"/> so this and
        /// the ledger it feeds can never key on different objects.</summary>
        /// <param name="map">The map the cell is on.</param>
        /// <param name="cell">The destination cell; may be invalid (a container destination).</param>
        /// <returns>The group, or null.</returns>
        private static ISlotGroup GroupAt(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid)
                return null;
            return BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(cell));
        }
    }
}
