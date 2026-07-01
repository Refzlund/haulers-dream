using System.Collections.Generic;
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
        public static JobDef HaulersDream_KeepInInventory; // "Keep X in inventory": hold an item, never hauled/dropped
        public static JobDef HaulersDream_LoadPackAnimal; // load scooped loot onto a pack animal (caravan/away map)
        public static JobDef HaulersDream_UnloadCarrierInBulk; // bulk-empty a flagged pack animal into the hauler's backpack
        public static JobDef HaulersDream_LoadTransportersInBulk; // bulk-load a transporter/shuttle group from swept inventory
        public static JobDef HaulersDream_LoadPortalInBulk; // bulk-load a map portal (pit gate / cave / vault exit) from swept inventory
        public static JobDef HaulersDream_LoadVehicleInBulk; // bulk-load a Vehicle Framework vehicle from swept inventory (VF soft-dep)
        public static JobDef HaulersDream_BulkRefuel; // bulk-refuel a CompRefuelable (shuttle chemfuel, deep drill, …) from swept inventory in one trip

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

        // ---------------------------------------------------------------------------------------------------
        // SEMANTIC SUBSETS of the HD job defs. Each of these used to be an inline `jd == X || jd == Y` chain
        // at a single call site — and each was a place a newly-added HD driver had to be remembered (the exact
        // drift this class exists to prevent). They are promoted here, one named + commented set per distinct
        // meaning. They DELIBERATELY differ from one another (and from CustomDriverJobDefs): do NOT collapse or
        // merge them. Each is a HashSet for O(1) `Contains(jd)` at the (often hot) call sites. Lazy like the
        // array above — the DefOf fields are populated at game load before any tick/save/scan reads these.
        // ---------------------------------------------------------------------------------------------------

        private static HashSet<JobDef> _inTransitLoadJobs;
        private static HashSet<JobDef> _holdsUnshareablePreloadedStock;
        private static HashSet<JobDef> _noRecursionHaulJobs;
        private static HashSet<JobDef> _constructDeliverJobs;

        /// <summary>
        /// HD jobs that, while running, mean a genuine cannot-unload FAULT is being actively worked off — so the
        /// black-hole alert (<see cref="Alert_CannotUnloadInventory"/>) DEFERS surfacing the pawn this frame
        /// (the fault is real but a deliberate gather/craft/deliver/sweep/unload is in flight). Membership is the
        /// set of HD drivers that move/hold/flush tagged cargo on the pawn's CURRENT job. Note the alert's
        /// call site ORs an additional <c>PawnUnloadChecker.HasQueuedUnload(p)</c> predicate (a queued, not
        /// current, unload) — that is a separate runtime check, not a job def, so it stays at the call site.
        /// </summary>
        public static HashSet<JobDef> InTransitLoadJobs => _inTransitLoadJobs ??= new HashSet<JobDef>
        {
            HaulersDreamDefOf.HaulersDream_UnloadInventory,         // the unload itself is running -> obviously in flight
            HaulersDreamDefOf.HaulersDream_BillPrepGather,          // gathering ingredients into inventory for a bill
            HaulersDreamDefOf.HaulersDream_BatchCraft,              // crafting from pre-loaded (held) ingredients
            HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver,// delivering carried materials to a build
            HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild,   // same, with the build tethered after
            HaulersDreamDefOf.HaulersDream_BulkHaul,                // a multi-stack sweep that ends in an unload
            HaulersDreamDefOf.HaulersDream_SelfPickup,              // a self-pickup that ends in an unload
        };

        /// <summary>
        /// HD jobs whose holder is actively holding UNSHAREABLE pre-loaded stock — so another colonist must NOT
        /// pull from that holder's inventory (<see cref="InventoryShare.IsEligibleCarrier"/>). A pawn running one
        /// of these has deliberately pre-loaded (untagged) ingredients for its own recipe/delivery runs; letting
        /// a sharer drain them would starve the run mid-execution. Excludes BulkHaul/SelfPickup (those carry
        /// genuinely shareable swept stock) and the unload (it's flushing, not holding for itself). NOTE: this
        /// set still includes <c>HaulersDream_InventoryDoBill</c> (retired; def kept for save-compat) — keeping
        /// it is harmless (it's never started) and matches the exact prior membership of this guard.
        /// </summary>
        public static HashSet<JobDef> HoldsUnshareablePreloadedStock => _holdsUnshareablePreloadedStock ??= new HashSet<JobDef>
        {
            HaulersDreamDefOf.HaulersDream_BatchCraft,              // holding pre-loaded ingredients for its own batch runs
            HaulersDreamDefOf.HaulersDream_InventoryDoBill,         // retired def (save-compat); kept to preserve exact prior membership
            HaulersDreamDefOf.HaulersDream_BillPrepGather,          // mid-gather of its own bill ingredients
            HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver,// carrying materials earmarked for a specific build
            HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild,   // same, with the build tethered after
        };

        /// <summary>
        /// HD intake jobs that load loot INTO inventory — used by en-route pickup
        /// (<see cref="Patch_Pawn_JobTracker_EnRoutePickup"/>) as the G2 self-guard: if the pawn is already
        /// running (or has queued) one of these, it is already about to sweep loot into inventory, so en-route
        /// must not stack another pickup on top. DELIBERATELY only the two pure inventory-INTAKE drivers — never
        /// the gather/craft/deliver/unload jobs (those are not "another pickup to avoid duplicating").
        /// </summary>
        public static HashSet<JobDef> NoRecursionHaulJobs => _noRecursionHaulJobs ??= new HashSet<JobDef>
        {
            HaulersDreamDefOf.HaulersDream_BulkHaul,   // a multi-stack sweep into inventory
            HaulersDreamDefOf.HaulersDream_SelfPickup, // a single-stack pickup into inventory
        };

        /// <summary>
        /// The construct-deliver job PAIR (same driver, two defs — the plain delivery and the build-tethered
        /// variant). Used wherever a queue/route scan must recognize "this is one of HD's inventory
        /// construct-delivery jobs" (<see cref="ConstructTether"/> route-demand accumulation,
        /// <see cref="JobDriver_OverloadConstructDeliver"/>'s more-work-queued check). NOTE: that latter check
        /// also recognizes vanilla <c>JobDefOf.FinishFrame</c> — that is a vanilla def, not part of this HD
        /// pair, so it stays ORed at its call site.
        /// </summary>
        public static HashSet<JobDef> ConstructDeliverJobs => _constructDeliverJobs ??= new HashSet<JobDef>
        {
            HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver, // the plain inventory construct-delivery
            HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild,    // the variant that tethers the build after delivery
        };
    }
}
