using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Everybody Gets One bill-menu compatibility, without a hard assembly reference.
    /// The cooperative bill menu retains EGO's own actions and supplies them through its actual insertion
    /// factory only when missing (or replacing an inadequate Ingredient Threshold proxy).
    /// Provider readiness includes registered patch/active assembly provenance and known action contracts.
    /// </summary>
    public static class EverybodyGetsOneCompat
    {
        public static bool IsActive => BillRepeatMenuProviders.Get(BillRepeatMenuKind.Everybody) != null;

        public static void TryInsertModes(List<FloatMenuOption> options, Bill_Production bill) =>
            BillRepeatMenuComposition.MergeProvider(options, bill, BillRepeatMenuKind.Everybody);
    }
}
