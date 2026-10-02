using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Ingredient Threshold bill-menu compatibility, without a hard assembly reference.
    /// IT's actual replacement enumerates every loaded repeat Def. Its own simple selection is retained;
    /// missing IT entries are supplied without duplicating entries made by its replacement prefix.
    /// Provider readiness includes registered patch/active assembly provenance and known action contracts.
    /// </summary>
    public static class IngredientThresholdCompat
    {
        public static bool IsActive => BillRepeatMenuProviders.Get(BillRepeatMenuKind.Threshold) != null;

        public static void TryInsertModes(List<FloatMenuOption> options, Bill_Production bill) =>
            BillRepeatMenuComposition.MergeProvider(options, bill, BillRepeatMenuKind.Threshold);
    }
}
