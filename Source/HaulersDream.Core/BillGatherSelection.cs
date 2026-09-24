using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>An observed transfer to one surviving stack, not an intended pickup.</summary>
    public readonly struct BillGatherReceipt<T> where T : class
    {
        public readonly T Thing;
        public readonly int Count;
        public BillGatherReceipt(T thing, int count) { Thing = thing; Count = count; }
    }

    /// <summary>Quantity-preserving updates to a selected recipe. No ingredient reselection occurs here.</summary>
    public static class BillGatherSelection
    {
        /// <summary>Keep original row indices stable while a one-pass sweep appends transferred portions.</summary>
        public static bool TryRemap<T>(List<T> targets, List<int> counts, int row,
            IReadOnlyList<BillGatherReceipt<T>> receipts) where T : class
        {
            if (targets == null || counts == null || targets.Count != counts.Count
                || row < 0 || row >= counts.Count || counts[row] <= 0 || receipts == null)
                return false;
            long moved = 0;
            for (int i = 0; i < receipts.Count; i++)
            {
                if (receipts[i].Thing == null || receipts[i].Count <= 0) return false;
                moved += receipts[i].Count;
            }
            if (moved > counts[row]) return false;
            if (moved == 0) return true;
            int first = 0;
            if (moved == counts[row])
            {
                targets[row] = receipts[0].Thing;
                counts[row] = receipts[0].Count;
                first = 1;
            }
            else counts[row] -= (int)moved;
            for (int i = first; i < receipts.Count; i++)
            {
                targets.Add(receipts[i].Thing);
                counts.Add(receipts[i].Count);
            }
            return true;
        }

        /// <summary>Coalesce by physical identity, rejecting lost/over-allocated stock instead of shrinking a recipe.</summary>
        public static bool TryNormalize<T>(IReadOnlyList<T> targets, IReadOnlyList<int> counts,
            Func<T, int> available, out List<T> normalized, out List<int> quantities) where T : class
        {
            normalized = new List<T>();
            quantities = new List<int>();
            if (targets == null || counts == null || targets.Count == 0 || targets.Count != counts.Count)
                return false;
            for (int i = 0; i < targets.Count; i++)
            {
                T t = targets[i];
                if (t == null || counts[i] <= 0) return false;
                int index = normalized.FindIndex(x => ReferenceEquals(x, t));
                long total = counts[i];
                if (index >= 0) total += quantities[index];
                if (total > int.MaxValue || total > available(t)) return false;
                if (index < 0) { normalized.Add(t); quantities.Add((int)total); }
                else quantities[index] = (int)total;
            }
            return true;
        }

        /// <summary>
        /// Check that retained whole units can satisfy all ingredient slots without allocating a unit twice.
        /// requiredUnits[slot, stack] is the units needed to fill that slot using that stack alone; <=0 means
        /// disallowed. Non-mixing slots may use multiple stacks, but only of one kind. This validates a retained
        /// selection; it neither chooses new stock nor changes the amounts native crafting will consume.
        /// </summary>
        public static bool SatisfiesRecipe(int[] available, int[] kinds, double[,] requiredUnits,
            bool[] mixing, int searchBudget = 20000)
        {
            if (available == null || kinds == null || requiredUnits == null || mixing == null
                || available.Length != kinds.Length || requiredUnits.GetLength(0) != mixing.Length
                || requiredUnits.GetLength(1) != available.Length || searchBudget <= 0)
                return false;
            // The node budget bounds work; this separately bounds recursive depth for unusually large recipes.
            if ((long)mixing.Length * (available.Length + 1L) > 512) return false;
            var left = (int[])available.Clone();
            for (int i = 0; i < left.Length; i++) if (left[i] < 0) return false;
            return Fill(0, 0, 1d, -1);

            bool Fill(int slot, int stack, double remaining, int kind)
            {
                if (--searchBudget < 0) return false; // bounded validation; caller can safely leave native work
                if (slot == mixing.Length) return true;
                if (remaining <= 0.000001d) return Fill(slot + 1, 0, 1d, -1);
                if (stack == left.Length) return false;
                double room = 0;
                for (int i = stack; i < left.Length; i++)
                {
                    double required = requiredUnits[slot, i];
                    if (required > 0 && !double.IsInfinity(required) && !double.IsNaN(required)
                        && (mixing[slot] || kind < 0 || kinds[i] == kind)) room += left[i] / required;
                }
                if (room + 0.000001d < remaining) return false;
                double units = requiredUnits[slot, stack];
                int take = 0;
                if (units > 0 && !double.IsInfinity(units) && !double.IsNaN(units)
                    && (mixing[slot] || kind < 0 || kinds[stack] == kind))
                {
                    double needed = Math.Ceiling(remaining * units - 0.000001d);
                    take = needed >= left[stack] ? left[stack] : (int)Math.Max(0, needed);
                }
                for (int n = take; n >= 0; n--)
                {
                    left[stack] -= n;
                    bool fits = Fill(slot, stack + 1, remaining - (n > 0 ? n / units : 0),
                        n > 0 && kind < 0 ? kinds[stack] : kind);
                    left[stack] += n;
                    if (fits) return true;
                    if (searchBudget < 0) return false;
                }
                return false;
            }
        }
    }
}
