using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// (Issue #62) Protect HD's scooped work-yields from vanilla's "drop unused inventory" sweep.
    ///
    /// <para>HD scoops harvested crops / milk / wool (and other work yields) into the pawn's INVENTORY, tagged via
    /// <see cref="CompHauledToInventory"/>, to be hauled to storage by HD's own unload jobs. But vanilla
    /// <see cref="JobGiver_DropUnusedInventory"/> runs every think pass for a non-drafted player colonist standing
    /// in the Home area, and its first loop drops every inventory item that is
    /// <c>IsIngestible &amp;&amp; !IsDrug &amp;&amp; ingestible.preferability &lt;= 5</c> — i.e. raw crops / milk — once
    /// <c>pawn.mindState.lastInventoryRawFoodUseTick + 150000</c> (~2.5 in-game days) has elapsed. So on an
    /// established save, the moment a grower / animal handler finishes harvesting and picks its next job, vanilla
    /// drops the scooped yields at its feet instead of letting HD haul them to storage (issue #62). The
    /// 150000-tick gate is exactly why it surfaces only on older saves, never a fresh dev Quick Start.</para>
    ///
    /// <para>Combat Extended's own prefix on this same <c>Drop</c> explicitly PERMITS that category, and Pick Up
    /// And Haul's prefix protects only PUAH's own comp — so HD's tagged stock was unprotected by either. This
    /// prefix (mirroring PUAH's defensive pattern) SKIPS vanilla's drop for any thing HD has tagged, so the
    /// scooped yields stay in inventory for HD's haul-to-storage trip.</para>
    ///
    /// <para>It is a pure no-op for every UNTAGGED item (vanilla behaviour byte-identical — a packed lunch the
    /// pawn isn't keeping still drops normally), and only ever protects items HD itself scooped, which HD always
    /// has a storage destination for (its unload drivers empty the pack), so nothing is stranded. HD's universal
    /// exception tagger is auto-attached to this patched method, so a fault here is logged and re-thrown, never
    /// swallowed.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_DropUnusedInventory), "Drop")]
    public static class Patch_JobGiver_DropUnusedInventory_Drop
    {
        // Return false to SKIP vanilla's inventory drop for an HD-tagged stack (it belongs to HD's unload trip).
        // PeekHashSet is the read-only view — no self-heal / no CE re-notify / no Rand — correct on this hot
        // per-think-pass path: the item was tagged at scoop time (self-heal already linked it), and a rare
        // un-healed tag would at worst let one item drop and be re-scooped, never a crash and never stranding.
        static bool Prefix(Pawn pawn, Thing thing)
        {
            var comp = pawn?.GetComp<CompHauledToInventory>();
            return comp == null || thing == null || !comp.PeekHashSet().Contains(thing);
        }
    }

    /// <summary>
    /// (Issue #81) Keep an HD-hauled DRUG in inventory until HD's unload trip stores it.
    ///
    /// <para><see cref="JobGiver_DropUnusedInventory.TryGiveJob"/> runs a second, UN-gated loop every think pass that
    /// drops every inventory thing for which <see cref="JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory"/>
    /// returns false — i.e. any drug the colonist isn't scheduled to take, isn't carrying per its drug policy, and
    /// isn't addicted/dependent on. A recreational drug like a smokeleaf joint hits all of those, so the instant a
    /// pawn picks one up via HD's "Pick up X" order or an en-route grab, vanilla drops it back at the pawn's feet
    /// (reported in #81: only drugs are affected; every non-drug haulable rides to storage fine, because the loop
    /// only ever looks at <c>IsDrug</c> things).</para>
    ///
    /// <para>This postfix forces "keep" for any drug HD has tagged via <see cref="CompHauledToInventory"/>, so the
    /// joint stays in the pack for HD's storage-aware unload trip instead of being dumped. It complements the #62
    /// Prefix on the private <c>Drop</c> helper above: in pure vanilla either guard alone is enough (both loops in
    /// <c>TryGiveJob</c> funnel through <c>Drop</c>). But <c>ShouldKeepDrugInInventory</c> is the canonical
    /// keep-or-drop gate, so when another mod reimplements this drop loop — inlining the drop and bypassing the
    /// private <c>Drop</c> that #62 vetoes — HD's cargo is still kept as long as that mod consults the predicate
    /// (the usual pattern). That is the case the #81 report (a heavily-modded load order) hits, where a tagged drug
    /// is dropped despite #62. The same predicate also gates vanilla's "pick up drug" float-menu on a GROUND stack,
    /// which HD never has tagged, so that UI is unchanged.</para>
    ///
    /// <para>It only ever flips false -> true, and only for a thing already in HD's tagged set — a colonist's own
    /// recreational/personal drugs (never HD-tagged) keep dropping exactly as in vanilla. PeekHashSet is the
    /// read-only view (no self-heal / Rand / CE re-notify), correct on this per-think-pass gate.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_DropUnusedInventory), nameof(JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory))]
    public static class Patch_JobGiver_DropUnusedInventory_ShouldKeepDrug
    {
        static void Postfix(ref bool __result, Pawn pawn, Thing drug)
        {
            if (__result) return; // vanilla already keeps it — nothing to do
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (comp != null && drug != null && comp.PeekHashSet().Contains(drug))
                __result = true; // HD-hauled cargo: keep it for the unload trip instead of dropping
        }
    }
}
