using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// While a colonist is actively batch-crafting AT the bench, freeze the rot of the batch's carried
    /// ingredients — so a big cooking batch doesn't spoil the raw food it's working through. They rot normally
    /// while the pawn walks to/from the bench (the driver's <c>ActivelyCrafting</c> is false during those phases),
    /// and the freeze is scoped to THIS batch's ingredient defs only, so the pawn's unrelated personal stock is
    /// untouched. Stateless: nothing is scribed and nothing is left disabled, so a mid-batch save/load and other
    /// mods are unaffected (rot simply resumes from where it paused).
    ///
    /// Patches the single private <c>CompRottable.TickInterval(int)</c> — both <c>CompTickInterval</c> and
    /// <c>CompTickRare</c> route through it, so one prefix covers every rot tick path.
    /// </summary>
    [HarmonyPatch(typeof(CompRottable), "TickInterval", new[] { typeof(int) })]
    public static class Patch_CompRottable_BatchFreeze
    {
        static bool Prefix(CompRottable __instance)
        {
            var t = __instance?.parent;
            if (t == null)
                return true;
            if (t.ParentHolder is Pawn_InventoryTracker pit
                && pit.pawn?.jobs?.curDriver is JobDriver_BatchCraft driver
                && driver.ShouldFreezeRot(t))
                return false; // skip this rot tick — frozen while actively crafting
            return true;
        }
    }
}
