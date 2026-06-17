using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// THE fix for "batch crafting ignores 'pause when satisfied' / 'unpause at'": make vanilla's product count
    /// see the products Hauler's Dream banks in pawn INVENTORY.
    ///
    /// A "Do until you have X" (<see cref="BillRepeatModeDefOf.TargetCount"/>) bill decides whether to keep crafting,
    /// and whether to PAUSE (and stay paused until the unpause-at threshold), entirely from
    /// <see cref="RecipeWorkerCounter.CountProducts"/> — read inside <see cref="Bill_Production.ShouldDoNow"/> and
    /// <c>Bill_Production.CanUnpause</c>. But CountProducts counts world + storage + the HANDS only (via
    /// <c>GetCarriedCount</c>); it never counts a pawn's INVENTORY unless the bill has <c>includeEquipped</c> set.
    /// HD's scoop ("while you're up") and batch driver bank freshly-made products in inventory to deliver a whole
    /// batch in one trip — so for a "do until you have X" bill the made products are INVISIBLE to the count, the
    /// target is never observed, <c>paused</c> is never latched, and pawns overproduce and never pause (exactly the
    /// reported bug). HD's own batch paths already corrected for this via <c>EffectiveProductCount</c>, but vanilla's
    /// autonomous crafting (the ordinary work scan that vanilla drives) had no such correction.
    ///
    /// This postfix adds the HD-banked in-flight products to the count at the SOURCE, so vanilla's own ShouldDoNow,
    /// the unpause-at hysteresis, and the bill UI all observe the true colony count and behave exactly as they would
    /// if the products were already sitting in storage. It is deliberately MODE-AGNOSTIC: it corrects the count for
    /// ANY count-based repeat mode, not just vanilla TargetCount — notably Everybody Gets One's count-based modes
    /// ("one per person" / "X per person"), whose own ShouldDoNow reads CountProducts and would otherwise overproduce
    /// and never pause for the same reason. RepeatCount / Forever bills never read CountProducts for their gate, so
    /// no scan runs for them in practice; the only real cost is on the count-based modes that need it. The gate is
    /// single-counted-product bills (CanCountProducts) that do NOT already count inventory themselves
    /// (<c>!includeEquipped</c>, where vanilla's own loop counts free colonists' inventory — adding here would
    /// double-count). When no products are banked (HD's banking features off / nothing in flight) the added count is
    /// 0, so this is inert by construction. Mirrors how vanilla's <c>GetCarriedCount</c> folds the hands into the
    /// count — this just also folds in the inventory HD put the products in.
    /// </summary>
    [HarmonyPatch(typeof(RecipeWorkerCounter), nameof(RecipeWorkerCounter.CountProducts))]
    public static class Patch_CountProducts_BankedInventory
    {
        static void Postfix(Bill_Production bill, ref int __result)
        {
            // !includeEquipped: when set, vanilla's CountProducts already counts colonists' inventory, so adding the
            // banked products again would double-count. No repeatMode gate — count-based modded modes (e.g.
            // Everybody Gets One's "one per person") need this correction just as much as vanilla TargetCount, and
            // non-count modes never call CountProducts for their gate, so this stays inert for them.
            if (bill == null || bill.includeEquipped)
                return;
            // BankedInFlightProductCount applies the bill's own CanCountProducts + per-thing validity, and returns 0
            // when nothing relevant is banked — so this is a no-op for multi-product recipes and when HD holds none.
            __result += CraftBatchPlanner.BankedInFlightProductCount(bill);
        }
    }
}
