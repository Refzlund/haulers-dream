using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// THE STORAGE-FILTER FUNNEL (plan G4) — a Harmony postfix on BOTH vanilla storage-search entry points
    /// (<see cref="StoreUtility.TryFindBestBetterStorageFor"/> and
    /// <see cref="StoreUtility.TryFindBestBetterStoreCellFor"/>) that VETOES a chosen storage building when an
    /// HD opportunistic/before-carry path has explicitly declared that purpose (via
    /// <see cref="StorageBuildingFilter.PushContext"/>) and the player's filter denies that building.
    ///
    /// <para><b>Byte-inert when OFF.</b> The very first check is the cheap feature gate: with the
    /// storage-filter master toggle off (the default) OR no storage mod present, the postfix returns
    /// immediately, so vanilla/normal play carries essentially zero overhead. Both <c>needAccurateResult</c>
    /// true and false are processed — a denied building must be vetoed on planning probes too, or a
    /// just-discarded-but-denied result could still leak into a follow-up.</para>
    ///
    /// <para><b>Why veto is correct (and can never strand an item).</b> The filter only ever acts when an HD
    /// scoop/sweep (Opportunistic) or before-carry routing (BeforeCarry) path explicitly pushed that context;
    /// the DEFAULT context (nothing pushed) is <see cref="StorageFilterContext.Unload"/> = allow-all, so
    /// vanilla hauls AND HD's own inventory-unload path are NEVER filtered (G4). Vetoing the best-but-denied
    /// storage in an Opportunistic/BeforeCarry context means "don't OPPORTUNISTICALLY route this item into a
    /// denied building" — the item is simply left for normal, unfiltered hauling instead. This is the
    /// faithful While-You're-Up behavior (e.g. keep opportunistic hauls out of slow LWM Deep Storage). It can
    /// never strand an item, because the actual put-it-away path (the Unload context) is allow-all: a carrying
    /// pawn can always find somewhere to set its load down.</para>
    /// </summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStorageFor))]
    public static class Patch_TryFindBestBetterStorageFor_StorageFilter
    {
        // Out params are `ref` in a postfix; __result is the method's bool return (ref so we can veto).
        static void Postfix(Map map, ref IntVec3 foundCell, ref IHaulDestination haulDestination,
            ref bool __result)
        {
            // 1. Cheap early-out: feature off (default) or no storage mod -> zero further work.
            if (!StorageBuildingFilter.Enabled || !StorageBuildingFilter.AnyStorageModPresent)
                return;
            // 2. Vanilla found nothing -> nothing to veto.
            if (!__result)
                return;
            // 3. Unload context (incl. the safe default when no HD path pushed a context) is allow-all (G4):
            //    a carrying pawn must always be able to put its load down. Vanilla hauls + HD unload land here.
            if (StorageBuildingFilter.CurrentContext == StorageFilterContext.Unload)
                return;

            // 4. Opportunistic / BeforeCarry context is active -> honor the player's per-building filter.
            var filter = HaulersDreamMod.Settings?.storageBuildingFilter;
            if (filter == null)
                return; // no shared filter object (should not happen) -> permit

            // The storage overload returns EITHER a slot-group cell (foundCell valid) OR a non-slot-group
            // haul destination (foundCell invalid, haulDestination set, e.g. a grave / modded container).
            bool allowed = foundCell.IsValid
                ? filter.IsCellAllowed(foundCell, map)
                : filter.IsHaulDestinationAllowed(haulDestination);
            if (allowed)
                return;

            // Denied: veto. Leave the item for normal (unfiltered) hauling.
            __result = false;
            foundCell = IntVec3.Invalid;
            haulDestination = null;
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellFor))]
    public static class Patch_TryFindBestBetterStoreCellFor_StorageFilter
    {
        // Store-cell overload: only a cell (a slot group) is returned, never a non-slot-group destination.
        static void Postfix(Map map, ref IntVec3 foundCell, ref bool __result)
        {
            if (!StorageBuildingFilter.Enabled || !StorageBuildingFilter.AnyStorageModPresent)
                return; // 1. cheap early-out (feature off / no storage mod)
            if (!__result)
                return; // 2. vanilla found nothing
            if (StorageBuildingFilter.CurrentContext == StorageFilterContext.Unload)
                return; // 3. Unload (and the safe default) is allow-all (G4)

            var filter = HaulersDreamMod.Settings?.storageBuildingFilter;
            if (filter == null)
                return;

            // 4. Opportunistic / BeforeCarry: veto a denied cell, leaving the item for normal hauling.
            if (filter.IsCellAllowed(foundCell, map))
                return;
            __result = false;
            foundCell = IntVec3.Invalid;
        }
    }
}
