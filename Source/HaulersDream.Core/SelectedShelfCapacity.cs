using System;

namespace HaulersDream.Core
{
    /// <summary>A lower bound from measured cells of one selected shelf, using the existing group claims.</summary>
    public static class SelectedShelfCapacity
    {
        /// <summary>
        /// Price compatible resident deficits and vacant native slots without adding overlapping group
        /// observations. All ordinary group commitments still apply; physically exclusive cells were
        /// excluded by the caller and their metadata rows must not be subtracted a second time.
        /// </summary>
        public static int Available(int compatibleDeficits, int vacantSlots, int stackLimit,
            StorageClaimRow[] rows, object group, object def, object asker, bool delivering,
            StorageClaimEvidence evidence, Func<object, int> stackLimitFor)
        {
            if (group == null || def == null || stackLimit <= 0 || stackLimitFor == null) return 0;
            var budget = new StorageGroupBudget(Math.Max(0, vacantSlots));
            budget.PriceDef(def, Math.Max(0, compatibleDeficits), stackLimit);
            foreach (var row in rows ?? StorageClaimLedger.Empty)
            {
                if (!ReferenceEquals(row.Group, group) || ReferenceEquals(row.Def, def) || budget.IsPriced(row.Def)) continue;
                int claimed = StorageClaimLedger.ClaimedTotal(rows, group, row.Def, evidence);
                if (claimed <= 0) continue;
                int limit = stackLimitFor(row.Def);
                if (limit <= 0) return 0; // An unpriceable competing definition cannot grant room.
                budget.PriceDef(row.Def, 0, limit);
                budget.Consume(row.Def, claimed);
            }
            int others = StorageClaimLedger.ClaimedByOthers(rows, group, def, asker, evidence);
            int mine = delivering ? 0 : StorageClaimLedger.ClaimedByPawn(rows, group, def, asker, evidence);
            return StorageCommitPolicy.Commit(new HaulSight(0, 0, budget.AvailableFor(def), others, mine, int.MaxValue), delivering);
        }
    }
}
