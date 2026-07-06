using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure diff math for the quest-pawn revert drop (issue #123): a temporary quest pawn (lodger / helper
    /// borrowed from another faction) joins the player faction, may accumulate colony goods in its inventory
    /// (meals, medicine, hauled stock), and normally walks away with all of it when it reverts to its home
    /// faction at quest end. The fix: snapshot the inventory at the moment control is GAINED, and at the moment
    /// control is LOST drop everything above that snapshot at the pawn's feet.
    ///
    /// The snapshot is (def, stuff) -> count, NEVER live Thing references or bare thingIDNumbers: inventory
    /// stacks merge and split freely while the pawn works, which invalidates both (the recurring HD trap:
    /// ownership tracked as Thing refs breaks on every merge). A count-per-kind diff survives any amount of
    /// stack churn.
    ///
    /// Equipped weapons and worn apparel are excluded BY CONSTRUCTION, not by filtering: the runtime mapper
    /// feeds only <c>Pawn_InventoryTracker.innerContainer</c>, and vanilla keeps equipment and apparel in
    /// separate ThingOwners (<c>Pawn_EquipmentTracker.equipment</c> / <c>Pawn_ApparelTracker.wornApparel</c>),
    /// so they can never appear in this diff, matching the issue's requirement that gear which "cannot be
    /// forcibly removed even while they are under the player's control" stays with the pawn.
    ///
    /// No Verse types: defs are opaque <c>object</c> tokens (the live ThingDef references at runtime; value
    /// equality via the tokens' own <c>Equals</c>, reference identity for defs, which are singletons). The
    /// runtime (<c>QuestPawnReversion</c>) gathers the inputs, sorted by thingIDNumber for MP determinism, and
    /// performs the drops.
    /// </summary>
    public static class QuestPawnDropPolicy
    {
        /// <summary>
        /// One inventory stack as the policy sees it, a flat value snapshot of a live Thing.
        /// </summary>
        public readonly struct InventoryStack
        {
            /// <summary>Opaque stable identity token for THIS stack object (thingIDNumber at runtime). Only
            /// echoed back in <see cref="DropOrder.Id"/> so the mapper can find the live Thing again; the
            /// policy never uses it for matching (merges/splits make stack identity meaningless over time).</summary>
            public readonly int Id;

            /// <summary>Opaque item-kind token (ThingDef at runtime). Never null for a real stack.</summary>
            public readonly object Def;

            /// <summary>Opaque material token (stuff ThingDef at runtime); null for an unstuffed item. Kept
            /// separate from <see cref="Def"/> so a granite-blocks arrival kit can't legitimize keeping
            /// marble blocks picked up later.</summary>
            public readonly object Stuff;

            /// <summary>Current stack count. Non-positive stacks are ignored by every method here.</summary>
            public readonly int Count;

            /// <summary>True when HD's own hauling tagged this stack (CompHauledToInventory). Tagged stacks
            /// are colony cargo by definition: they always drop in full and never consume snapshot allowance,
            /// and at snapshot time they are excluded (HD cargo is never "arrival kit").</summary>
            public readonly bool Tagged;

            public InventoryStack(int id, object def, object stuff, int count, bool tagged)
            {
                Id = id;
                Def = def;
                Stuff = stuff;
                Count = count;
                Tagged = tagged;
            }
        }

        /// <summary>
        /// One aggregated snapshot line: the pawn arrived holding <see cref="Count"/> of (<see cref="Def"/>,
        /// <see cref="Stuff"/>). The runtime persists these as (ThingDef, ThingDef, int) triples.
        /// </summary>
        public readonly struct SnapshotEntry
        {
            /// <summary>Opaque item-kind token (ThingDef at runtime).</summary>
            public readonly object Def;

            /// <summary>Opaque material token; null for an unstuffed item.</summary>
            public readonly object Stuff;

            /// <summary>How many of this kind the pawn held when control was gained (summed across stacks).</summary>
            public readonly int Count;

            public SnapshotEntry(object def, object stuff, int count)
            {
                Def = def;
                Stuff = stuff;
                Count = count;
            }
        }

        /// <summary>
        /// One drop instruction for the runtime: drop <see cref="Count"/> units out of the stack identified by
        /// <see cref="Id"/> (a partial count splits the stack; a full count drops the whole stack).
        /// </summary>
        public readonly struct DropOrder
        {
            /// <summary>The <see cref="InventoryStack.Id"/> this order applies to.</summary>
            public readonly int Id;

            /// <summary>Units to drop from that stack; always in (0, stack count].</summary>
            public readonly int Count;

            public DropOrder(int id, int count)
            {
                Id = id;
                Count = count;
            }
        }

        /// <summary>Dictionary key pairing an item-kind token with a material token, with value equality via
        /// the tokens' own <c>Equals</c> (reference identity for runtime defs; value equality for test strings).</summary>
        private readonly struct DefStuffKey : System.IEquatable<DefStuffKey>
        {
            private readonly object def;
            private readonly object stuff;

            public DefStuffKey(object def, object stuff)
            {
                this.def = def;
                this.stuff = stuff;
            }

            public bool Equals(DefStuffKey other) => Equals(def, other.def) && Equals(stuff, other.stuff);
            public override bool Equals(object obj) => obj is DefStuffKey other && Equals(other);
            public override int GetHashCode() => ((def?.GetHashCode() ?? 0) * 397) ^ (stuff?.GetHashCode() ?? 0);
        }

        /// <summary>
        /// Aggregate the pawn's inventory at the moment control is GAINED into snapshot lines ("what the pawn
        /// arrived with"). Sums counts per (def, stuff) across stacks; output order is first-seen input order
        /// (feed stacks sorted by thingIDNumber for a deterministic snapshot across MP clients).
        ///
        /// Skips non-positive counts and skips <see cref="InventoryStack.Tagged"/> stacks: an HD-tagged stack
        /// is colony cargo HD placed there (possible only on a re-joining pawn whose previous revert drop
        /// failed), and counting it as arrival kit would grant allowance that lets same-kind items picked up
        /// later walk away.
        /// </summary>
        /// <param name="stacks">The pawn's inventory stacks at gain time.</param>
        /// <param name="outSnapshot">Receives the aggregated lines; cleared first.</param>
        public static void BuildSnapshot(IReadOnlyList<InventoryStack> stacks, List<SnapshotEntry> outSnapshot)
        {
            outSnapshot.Clear();

            // Key -> index into outSnapshot, so repeated kinds fold into one line while preserving first-seen order.
            var indexByKey = new Dictionary<DefStuffKey, int>();
            for (int i = 0; i < stacks.Count; i++)
            {
                var stack = stacks[i];
                if (stack.Count <= 0 || stack.Def == null || stack.Tagged)
                    continue;

                var key = new DefStuffKey(stack.Def, stack.Stuff);
                if (indexByKey.TryGetValue(key, out int at))
                {
                    var existing = outSnapshot[at];
                    outSnapshot[at] = new SnapshotEntry(existing.Def, existing.Stuff, existing.Count + stack.Count);
                }
                else
                {
                    indexByKey[key] = outSnapshot.Count;
                    outSnapshot.Add(new SnapshotEntry(stack.Def, stack.Stuff, stack.Count));
                }
            }
        }

        /// <summary>
        /// Decide what to drop at the moment control is LOST: everything the pawn picked up while under player
        /// control, i.e. every unit above the gain-time snapshot.
        ///
        /// Rules, in stack input order (feed stacks sorted by thingIDNumber so every MP client keeps/drops the
        /// same units):
        /// <list type="number">
        /// <item>A <see cref="InventoryStack.Tagged"/> stack drops IN FULL and consumes no allowance, HD put
        /// it there, it is colony cargo regardless of counts.</item>
        /// <item>An untagged stack consumes remaining snapshot allowance for its (def, stuff); units above the
        /// allowance drop. Allowance is consumed first-stack-first, so with allowance 10 and stacks [7, 7] the
        /// first stack is kept whole and the second drops 4.</item>
        /// <item>A kind absent from the snapshot has zero allowance: the whole stack drops.</item>
        /// </list>
        /// Snapshot kinds the pawn no longer holds simply leave their allowance unused (eaten/used items are
        /// gone; nothing to do). Non-positive counts on either side are ignored; duplicate snapshot lines for
        /// one kind sum (tolerant of a hand-edited save).
        /// </summary>
        /// <param name="snapshot">The gain-time snapshot lines.</param>
        /// <param name="stacks">The pawn's inventory stacks at loss time.</param>
        /// <param name="outDrops">Receives one order per stack that must shed units, in stack input order; cleared first.</param>
        public static void SelectDrops(
            IReadOnlyList<SnapshotEntry> snapshot,
            IReadOnlyList<InventoryStack> stacks,
            List<DropOrder> outDrops)
        {
            outDrops.Clear();

            // Remaining keep-allowance per kind, seeded from the snapshot.
            var allowance = new Dictionary<DefStuffKey, int>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                if (entry.Count <= 0 || entry.Def == null)
                    continue;
                var key = new DefStuffKey(entry.Def, entry.Stuff);
                allowance.TryGetValue(key, out int have);
                allowance[key] = have + entry.Count;
            }

            for (int i = 0; i < stacks.Count; i++)
            {
                var stack = stacks[i];
                if (stack.Count <= 0 || stack.Def == null)
                    continue;

                if (stack.Tagged)
                {
                    outDrops.Add(new DropOrder(stack.Id, stack.Count));
                    continue;
                }

                var key = new DefStuffKey(stack.Def, stack.Stuff);
                allowance.TryGetValue(key, out int remaining);
                int kept = stack.Count < remaining ? stack.Count : remaining;
                if (kept > 0)
                    allowance[key] = remaining - kept;

                int drop = stack.Count - kept;
                if (drop > 0)
                    outDrops.Add(new DropOrder(stack.Id, drop));
            }
        }
    }
}
