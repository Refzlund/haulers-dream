using RimWorld;
using Verse;

namespace HaulersDream
{
    [DefOf]
    public static class HaulersDreamDefOf
    {
        public static JobDef HaulersDream_UnloadInventory;
        public static JobDef HaulersDream_SelfPickup;
        public static JobDef HaulersDream_OverloadConstructDeliver;
        public static JobDef HaulersDream_ConstructDeliverBuild; // same driver; this def also TETHERS the build
        public static JobDef HaulersDream_ClaimFromHauler;
        public static JobDef HaulersDream_BatchCraft;
        public static JobDef HaulersDream_InventoryDoBill; // retired (dup risk); def kept for save-compat
        public static JobDef HaulersDream_BillPrepGather;
        public static JobDef HaulersDream_BulkHaul;
        public static JobDef HaulersDream_LoadPackAnimal; // load scooped loot onto a pack animal (caravan/away map)
        public static JobDef HaulersDream_UnloadCarrierInBulk; // bulk-empty a flagged pack animal into the hauler's backpack
        public static JobDef HaulersDream_LoadTransportersInBulk; // bulk-load a transporter/shuttle group from swept inventory
        public static JobDef HaulersDream_LoadPortalInBulk; // bulk-load a map portal (pit gate / cave / vault exit) from swept inventory
        public static JobDef HaulersDream_LoadVehicleInBulk; // bulk-load a Vehicle Framework vehicle from swept inventory (VF soft-dep)

        static HaulersDreamDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HaulersDreamDefOf));
    }

    /// <summary>
    /// Single source of truth for the set of HD JobDefs backed by a custom <see cref="Verse.AI.JobDriver"/> that
    /// holds/manages tagged cargo (and that would dangle in a save after an uninstall). Shared by the pre-save
    /// cleanup (A1, <see cref="Patch_ScribeSaver_InitSaving"/>) and the softlock-drop skip-check (A2,
    /// <see cref="HaulersDreamGameComponent"/>) so the two lists can never drift — a drift previously let the
    /// softlock driver yank ingredients out from under a Hauling-priority-0 crafter running a (tagged) BatchCraft /
    /// BillPrepGather job. Excludes <c>HaulersDream_InventoryDoBill</c> (retired; def kept only for save-compat,
    /// never started). Lazy: the DefOf fields are populated at game load before any tick or save.
    /// </summary>
    public static class HdJobDefSets
    {
        private static JobDef[] _customDriverJobDefs;

        public static JobDef[] CustomDriverJobDefs => _customDriverJobDefs ??= new[]
        {
            HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk,
            HaulersDreamDefOf.HaulersDream_LoadPortalInBulk,
            HaulersDreamDefOf.HaulersDream_LoadVehicleInBulk,
            HaulersDreamDefOf.HaulersDream_LoadPackAnimal,
            HaulersDreamDefOf.HaulersDream_UnloadCarrierInBulk,
            HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver,
            HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild,
            HaulersDreamDefOf.HaulersDream_ClaimFromHauler,
            HaulersDreamDefOf.HaulersDream_BatchCraft,
            HaulersDreamDefOf.HaulersDream_BillPrepGather,
            HaulersDreamDefOf.HaulersDream_BulkHaul,
            HaulersDreamDefOf.HaulersDream_SelfPickup,
            HaulersDreamDefOf.HaulersDream_UnloadInventory,
        };
    }
}
