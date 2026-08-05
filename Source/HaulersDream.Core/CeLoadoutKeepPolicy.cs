using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure allocation policy for the inventory units Hauler's Dream must keep on behalf of Combat Extended.
    /// CE loadouts have two materially different slot modes: <see cref="SlotMode.PickupDrop"/> is a target CE
    /// actively refills, while <see cref="SlotMode.DropExcess"/> is only a ceiling. HD shields only the former:
    /// unloading below a ceiling does not make CE fetch the item again, so counting that ceiling as personal stock
    /// would strand HD's temporary cargo in the pawn's pack.
    ///
    /// Generic slots share one count across every matching def. Allocation therefore consumes one ordered copy of
    /// CE's storage listing, in loadout-slot order and then storage-def order, exactly like CE's pickup calculation.
    /// <see cref="Stock.TotalCount"/> includes equipment and loaded magazines; <see cref="Stock.InventoryCount"/>
    /// includes only units HD could unload. The final keep is the inventory portion that is not left over after
    /// satisfying refill slots, so an equipped weapon or rounds already loaded in it satisfy the loadout before an
    /// inventory duplicate is pinned.
    ///
    /// Game types remain outside this assembly: defs and generic matchers are opaque reference tokens, and the
    /// runtime wrapper supplies the generic predicate. Inputs must already be unique per def and preserve CE's
    /// enumeration order. The output is cleared first and contains only positive inventory keeps.
    /// </summary>
    public static class CeLoadoutKeepPolicy
    {
        public enum SlotMode : byte
        {
            PickupDrop,
            DropExcess,
        }

        /// <summary>One unique def from CE's storage dictionary, in its original enumeration order.</summary>
        public readonly struct Stock
        {
            public readonly object Def;
            public readonly int TotalCount;
            public readonly int InventoryCount;

            public Stock(object def, int totalCount, int inventoryCount)
            {
                Def = def;
                TotalCount = totalCount;
                InventoryCount = inventoryCount;
            }
        }

        /// <summary>
        /// One loadout slot. Exactly one of <see cref="ExactDef"/> and <see cref="GenericMatcher"/> should be
        /// non-null. GenericMatcher is an opaque token interpreted by the supplied match callback.
        /// </summary>
        public readonly struct Slot
        {
            public readonly object ExactDef;
            public readonly object GenericMatcher;
            public readonly int Count;
            public readonly SlotMode Mode;

            public Slot(object exactDef, object genericMatcher, int count, SlotMode mode)
            {
                ExactDef = exactDef;
                GenericMatcher = genericMatcher;
                Count = count;
                Mode = mode;
            }

            public static Slot Exact(object def, int count, SlotMode mode = SlotMode.PickupDrop)
                => new Slot(def, null, count, mode);

            public static Slot Generic(object matcher, int count, SlotMode mode = SlotMode.PickupDrop)
                => new Slot(null, matcher, count, mode);
        }

        /// <summary>Positive inventory units of one def covered by CE refill slots.</summary>
        public readonly struct Keep
        {
            public readonly object Def;
            public readonly int Count;

            public Keep(object def, int count)
            {
                Def = def;
                Count = count;
            }
        }

        /// <summary>
        /// Allocate CE refill slots over its ordered storage view and return the inventory units those slots cover.
        /// Drop-only ceilings contribute nothing. Counts are defensively floored and inventory counts are capped at
        /// total counts, so malformed reflected data can never produce a negative or phantom keep.
        /// </summary>
        public static void Allocate(
            IReadOnlyList<Stock> stocksInCeOrder,
            IReadOnlyList<Slot> slotsInLoadoutOrder,
            Func<object, object, bool> genericMatches,
            List<Keep> output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (stocksInCeOrder == null || stocksInCeOrder.Count == 0 || slotsInLoadoutOrder == null)
                return;

            var remaining = new int[stocksInCeOrder.Count];
            var inventory = new int[stocksInCeOrder.Count];
            for (int i = 0; i < stocksInCeOrder.Count; i++)
            {
                int total = stocksInCeOrder[i].TotalCount;
                if (total < 0)
                    total = 0;
                int inPack = stocksInCeOrder[i].InventoryCount;
                if (inPack < 0)
                    inPack = 0;
                if (inPack > total)
                    inPack = total;
                remaining[i] = total;
                inventory[i] = inPack;
            }

            for (int slotIndex = 0; slotIndex < slotsInLoadoutOrder.Count; slotIndex++)
            {
                var slot = slotsInLoadoutOrder[slotIndex];
                if (slot.Mode == SlotMode.DropExcess || slot.Count <= 0)
                    continue;

                if (slot.ExactDef != null)
                {
                    for (int i = 0; i < stocksInCeOrder.Count; i++)
                    {
                        if (!ReferenceEquals(stocksInCeOrder[i].Def, slot.ExactDef))
                            continue;
                        remaining[i] = Math.Max(0, remaining[i] - slot.Count);
                        break;
                    }
                    continue;
                }

                if (slot.GenericMatcher == null || genericMatches == null)
                    continue;
                int wanted = slot.Count;
                for (int i = 0; i < stocksInCeOrder.Count && wanted > 0; i++)
                {
                    if (remaining[i] <= 0 || stocksInCeOrder[i].Def == null
                        || !genericMatches(slot.GenericMatcher, stocksInCeOrder[i].Def))
                        continue;
                    int consumed = Math.Min(wanted, remaining[i]);
                    remaining[i] -= consumed;
                    wanted -= consumed;
                }
            }

            for (int i = 0; i < stocksInCeOrder.Count; i++)
            {
                // CE can only drop leftovers from the pack. If more units remain than exist in the pack, all
                // inventory units are excess; otherwise the difference is the inventory portion the slots cover.
                int keep = inventory[i] - Math.Min(inventory[i], remaining[i]);
                if (keep > 0 && stocksInCeOrder[i].Def != null)
                    output.Add(new Keep(stocksInCeOrder[i].Def, keep));
            }
        }
    }
}
