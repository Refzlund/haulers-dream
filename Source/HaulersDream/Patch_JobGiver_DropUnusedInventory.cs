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
}
