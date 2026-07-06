using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Per-destination-GROUP storage budget for ONE bulk-haul plan. Models a stockpile/shelf group's
    /// remaining capacity so that several item defs bound for the SAME group draw from a SHARED pool of
    /// empty cells (one empty cell holds a stack of ONE def), while the room left in a cell that already
    /// holds a def stays that def's alone.
    ///
    /// <para>WHY (issue #138): the old budget was keyed per item def, so meat and harvest both headed for
    /// the same fridge each priced that fridge's full free space independently. The group was
    /// over-subscribed, the pawn pocketed more than it could hold, dropped what fit at the destination,
    /// and carried the excess back to the origin, the wasteful round trip the reporter saw. Empty cells
    /// are the coupling between defs: they must be spent once for the whole plan, not once per def.</para>
    ///
    /// <para>This type is pure (no game types) so the arithmetic is unit-tested headlessly; the caller
    /// (BulkHaul) does the Verse cell scan and feeds it the counts. A def is priced once, then
    /// <see cref="AvailableFor"/> reports what still fits and <see cref="Consume"/> books a commitment.</para>
    /// </summary>
    public sealed class StorageGroupBudget
    {
        /// <summary>
        /// Remaining fully-empty cells, shared across every def. <see cref="int.MaxValue"/> means unbounded
        /// (a group larger than any plan could fill, or a destination with no cell grid): no clamp applies
        /// and nothing is tracked.
        /// </summary>
        private int emptyCells;

        /// <summary>
        /// Remaining top-up room in cells that already hold a given def, plus the unfilled tail of any empty
        /// cell this plan has opened for that def. Priced lazily, keyed by the def object (reference-compared).
        /// </summary>
        private readonly Dictionary<object, int> partialByDef = new Dictionary<object, int>();

        /// <summary>
        /// How many units of a def one freshly-opened empty cell holds (its stack limit for vanilla storage,
        /// more for a deep-storage cell). Captured when the def is first priced; also serves as the "priced" set.
        /// </summary>
        private readonly Dictionary<object, int> perCellByDef = new Dictionary<object, int>();

        /// <summary>
        /// Create a budget for a group.
        /// </summary>
        /// <param name="emptyCells">The group's count of empty, acceptable cells (shared across all defs),
        /// or <see cref="int.MaxValue"/> for an unbounded destination that applies no clamp.</param>
        public StorageGroupBudget(int emptyCells)
        {
            this.emptyCells = emptyCells;
        }

        /// <summary>An unbounded destination applies no clamp and tracks nothing.</summary>
        public bool Unbounded => emptyCells == int.MaxValue;

        /// <summary>Whether <paramref name="def"/> has already had its partial room and per-cell capacity recorded.</summary>
        /// <param name="def">The item def key (reference-compared).</param>
        public bool IsPriced(object def) => perCellByDef.ContainsKey(def);

        /// <summary>
        /// Record a def's group-wide capacity, once. A later call for the same def is ignored: the first scan
        /// is the plan's baseline and <see cref="Consume"/> mutates the remaining room from there. No-op when
        /// the budget is unbounded.
        /// </summary>
        /// <param name="def">The item def key (reference-compared).</param>
        /// <param name="partialSpace">Units of this def that fit in cells which ALREADY hold it (top-up room),
        /// clamped to non-negative.</param>
        /// <param name="perCellCapacity">Units of this def one empty cell holds; clamped to at least 1 so a
        /// single stack always consumes exactly one empty cell.</param>
        public void PriceDef(object def, int partialSpace, int perCellCapacity)
        {
            if (Unbounded || perCellByDef.ContainsKey(def))
                return;
            partialByDef[def] = Math.Max(0, partialSpace);
            perCellByDef[def] = Math.Max(1, perCellCapacity);
        }

        /// <summary>
        /// Units of <paramref name="def"/> the plan may still send to this group: its remaining partial room
        /// plus the shared empty cells' worth of it. Returns <see cref="int.MaxValue"/> when the budget is
        /// unbounded or the def was never priced (fail-open: no clamp, so a scan gap never strands a haul, and
        /// the deposit re-gate stays the authority for any over-estimate).
        /// </summary>
        /// <param name="def">The item def key (reference-compared).</param>
        public int AvailableFor(object def)
        {
            if (Unbounded || !perCellByDef.TryGetValue(def, out int perCell))
                return int.MaxValue;
            // long math so a group whose room exceeds int.MaxValue is reported as unbounded, never overflowed.
            long avail = (long)partialByDef[def] + (long)emptyCells * perCell;
            return avail >= int.MaxValue ? int.MaxValue : (int)avail;
        }

        /// <summary>
        /// Book a commitment of <paramref name="count"/> units of <paramref name="def"/> to the group: spend
        /// its partial room first, then whole empty cells (the unfilled tail of the last opened cell becomes
        /// that def's partial room, so a later same-def stack can top it up). No-op when the budget is
        /// unbounded, the def is unpriced, or <paramref name="count"/> is not positive.
        /// </summary>
        /// <param name="def">The item def key (reference-compared).</param>
        /// <param name="count">Units committed (should be no more than <see cref="AvailableFor"/> returned).</param>
        public void Consume(object def, int count)
        {
            if (Unbounded || count <= 0 || !perCellByDef.TryGetValue(def, out int perCell))
                return;
            int fromPartial = Math.Min(count, partialByDef[def]);
            partialByDef[def] -= fromPartial;
            int remaining = count - fromPartial;
            if (remaining <= 0)
                return;
            // Whole empty cells opened for the rest; never spend cells the group does not have (a caller that
            // honoured AvailableFor never asks for more, but clamp defensively so the shared pool cannot go
            // negative and starve other defs).
            int cells = (remaining + perCell - 1) / perCell;
            if (cells > emptyCells)
                cells = emptyCells;
            emptyCells -= cells;
            int leftover = cells * perCell - remaining;
            if (leftover > 0)
                partialByDef[def] = partialByDef.TryGetValue(def, out int p) ? p + leftover : leftover;
        }
    }
}
