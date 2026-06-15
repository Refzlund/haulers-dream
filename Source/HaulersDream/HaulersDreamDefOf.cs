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

        static HaulersDreamDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HaulersDreamDefOf));
    }
}
