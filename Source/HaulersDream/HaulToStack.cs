using System;
using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// HAUL TO STACK — haulers prefer topping up an EXISTING partial stack over starting a new one, and
    /// they no longer reserve the destination cell, so several pawns can deliver to the same tile at once.
    ///
    /// WHICH storage wins stays vanilla's call (priority, then distance — a different room may still win on
    /// those grounds); this only refines the CELL within what vanilla chose: a postfix on
    /// <see cref="StoreUtility.TryFindBestBetterStoreCellFor"/> swaps the closest-valid-cell pick for the
    /// nearest cell holding a partial stack of the same thing, searched in the chosen cell's ROOM across
    /// every equal-priority storage there — ground stockpiles, shelves, and modded storage units alike.
    /// When the destination is outside (no room, or the room touches the map edge — the unbounded
    /// outdoors), the search scopes to a radius around the chosen cell instead, so haulers consolidate
    /// across nearby outdoor stockpiles without wandering the map.
    ///
    /// STORAGE-MOD COMPATIBILITY BY CONSTRUCTION (no references, no reflection): candidates are validated
    /// exclusively through vanilla's own APIs — <c>IsGoodStoreCell</c> (which runs NoStorageBlockersIn,
    /// reachability, fire, forbiddance) and <c>CanStackWith</c>. Adaptive Storage Framework (and mods built
    /// on it, like Neat Storage) patch exactly those APIs (NoStorageBlockersIn transpiler,
    /// GetMaxItemsAllowedInCell, a worker prefix — source-verified in the ASF clone), so their per-building
    /// capacity and acceptance rules apply inside our calls automatically. Container-based storage
    /// (graves, modded ThingOwner units) goes through the untouched non-slot-group path, where stacking is
    /// inherent.
    ///
    /// NO-RESERVE: vanilla's <c>JobDriver_HaulToCell</c> reserves the destination cell, which makes
    /// <c>IsGoodStoreCell</c> (via CanReserveNew) hide that cell from every other hauler — the classic
    /// "ten haulers spread one item type across ten cells". For STORAGE hauls only (haulMode
    /// ToCellStorage; ritual/non-storage cell hauls keep vanilla reservations), the cell reservation is
    /// skipped. Races resolve by vanilla's own machinery, three ways: the goto toil fails the job while
    /// the pawn is still walking to the item (nothing picked up yet); CarryHauledThingToCell's own fail
    /// condition (ToCellStorage + cell no longer valid storage) ends the job Incompletable mid-carry,
    /// where CleanupCurrentJob floor-drops the full carried stack at the pawn's feet — it gets re-hauled
    /// (bounded churn, never loss); and PlaceHauledThingInCell's storage mode re-targets any remainder
    /// on arrival.
    /// </summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellFor))]
    public static class Patch_TryFindBestBetterStoreCellFor_HaulToStack
    {
        static void Postfix(Thing t, Pawn carrier, Map map, Faction faction, ref IntVec3 foundCell,
            bool needAccurateResult, bool __result)
        {
            // needAccurateResult false = a planning/availability probe (this mod's own planners use it);
            // refining a discarded result would be pure waste.
            if (!__result || !needAccurateResult)
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.haulToStack)
                return;
            if (carrier == null || map == null || t == null || faction != Faction.OfPlayerSilentFail)
                return;
            if (t.def.stackLimit <= 1)
                return; // unstackables have nothing to top up
            // No try/catch: a refinement failure is a real bug to surface as a red error, not a silent warning.
            var better = HaulToStack.FindStackCell(t, carrier, map, faction, foundCell);
            if (better.IsValid)
                foundCell = better;
        }
    }

    [HarmonyPatch(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.TryMakePreToilReservations))]
    public static class Patch_JobDriver_HaulToCell_NoCellReservation
    {
        static bool Prefix(JobDriver_HaulToCell __instance, bool errorOnFailed, ref bool __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.haulToStack)
                return true; // feature off -> vanilla (reserve cell + thing)
            var job = __instance.job;
            if (job == null || job.haulMode != HaulMode.ToCellStorage)
                return true; // non-storage cell hauls keep their reservation semantics
            // Unstackables (corpses, minified buildings, weapons — stackLimit 1) can NEVER stack onto a shared
            // cell, so leaving the destination cell unreserved gains them nothing — but it removes vanilla's
            // CanReserveNew(B) throttle. With B unreserved, the work scan keeps re-selecting the SAME cell every
            // tick when a second hauler contends that 1-capacity cell, re-issuing the identical HaulToCell until
            // "started 10 jobs in one tick" fires (the reported corpse HaulToCell loop). Mirror the cell-refine
            // postfix's own stackLimit<=1 guard (Patch_TryFindBestBetterStoreCellFor_HaulToStack) and keep
            // vanilla's cell reservation for them — byte-identical for stackables (the only things stacking helps).
            var hauled = job.GetTarget(TargetIndex.A).Thing;
            if (hauled?.def == null || hauled.def.stackLimit <= 1)
                return true; // vanilla reserves both cell + thing
            // Storage haul of a STACKABLE: reserve only the THING being hauled. The destination cell stays
            // unreserved so other haulers can pick (and stack onto) the same cell.
            __result = __instance.pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
            return false;
        }
    }

    public static class HaulToStack
    {
        // Per-tick memo: the work scan probes HasJobOnThing (= JobOnThing != null) per candidate, and each
        // probe runs the full vanilla storage search INCLUDING this refinement — same lesson as the
        // bulk-haul planner. Key = (thing, CARRIER, vanilla's chosen cell); null/Invalid results cached
        // too. The carrier is part of the key because IsGoodStoreCell validates per CARRIER (allowed
        // area, its own reservations, reachability) — serving one pawn's cell to another hands out a job
        // that fails synchronously and re-scans the same tick ("started 10 jobs in one tick").
        private static int cacheTick = -1;
        private static readonly Dictionary<(int thingId, int carrierId, int cellIdx), IntVec3> cellCache
            = new Dictionary<(int thingId, int carrierId, int cellIdx), IntVec3>();

        // Self-register the per-session cell-memo clear with the game-load hygiene sweep (see CacheRegistry), so it
        // can never be forgotten. The static ctor runs once, the first time any member is touched (the only way the
        // memo can hold cross-session data); the `tick != -1` populate guard in FindStackCell is the actual
        // cross-session safeguard.
        static HaulToStack() => CacheRegistry.Register(Clear);

        /// <summary>Drop the per-tick stack-cell memo and reset the tick stamp — called on game load
        /// (FinalizeInit) so an equal tick number across a quickload cannot serve a stale cross-session entry
        /// (the (thingId, carrierId, cellIdx) key collides across saves). Mirrors
        /// <see cref="BulkHaul.ClearPlanCache"/>; the `tick != -1` populate guard in <see cref="FindStackCell"/>
        /// is the cross-session safeguard, this is consistency with the existing FinalizeInit list.</summary>
        internal static void Clear()
        {
            cacheTick = -1;
            cellCache.Clear();
        }

        /// <summary>The best same-room (or, outside, in-radius) cell holding a partial stack
        /// <paramref name="t"/> can merge into, or Invalid to keep vanilla's pick. PURE — no reservations,
        /// no world mutation (the storage search runs speculatively during work scans and menu builds).</summary>
        internal static IntVec3 FindStackCell(Thing t, Pawn carrier, Map map, Faction faction, IntVec3 vanillaCell)
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            // tick == -1 (TickManager briefly null across a load): don't trust or populate the memo — a
            // cross-session quickload can land on the same tick number, and the (thingId, carrierId, cellIdx)
            // key collides across saves. Guard the stamp update on `tick != -1` (mirrors
            // CompHauledToInventory.lastHealTick); when -1 we recompute live and never cache. (The cached-hit
            // path is already re-validated by IsGoodStoreCell below, but the tick guard closes the populate side
            // and is consistent with the count caches.)
            if (tick != -1 && tick != cacheTick)
            {
                cellCache.Clear();
                cacheTick = tick;
            }
            var key = (t.thingIDNumber, carrier.thingIDNumber, map.cellIndices.CellToIndex(vanillaCell));
            if (tick != -1 && cellCache.TryGetValue(key, out var cached))
            {
                // Belt and braces: even a same-carrier hit can go stale within the tick (an earlier job
                // this tick reserved the thing or filled the cell) — re-validate before serving it.
                if (!cached.IsValid || StoreUtility.IsGoodStoreCell(cached, map, t, carrier, faction))
                    return cached;
            }
            var result = FindStackCellUncached(t, carrier, map, faction, vanillaCell);
            if (tick != -1)
                cellCache[key] = result; // only memoize a real tick (see the -1 guard above)
            return result;
        }

        private static IntVec3 FindStackCellUncached(Thing t, Pawn carrier, Map map, Faction faction, IntVec3 vanillaCell)
        {
            // Vanilla's pick already tops up a stack? Nothing to improve.
            if (CellHasPartialStackOf(vanillaCell, map, t))
                return IntVec3.Invalid;
            var chosenGroup = vanillaCell.GetSlotGroup(map);
            if (chosenGroup?.Settings == null)
                return IntVec3.Invalid;
            var chosenPriority = chosenGroup.Settings.Priority;

            var room = vanillaCell.GetRoom(map);
            bool radiusScan = HaulToStackPolicy.UseRadiusScan(room != null, room?.TouchesMapEdge ?? true);

            // Distance metric matches vanilla's worker: from where the ITEM currently is (the carry leg).
            IntVec3 origin = t.SpawnedOrAnyParentSpawned ? t.PositionHeld : carrier.PositionHeld;

            IntVec3 best = IntVec3.Invalid;
            float bestDistSq = float.MaxValue;
            int scanned = 0;

            // One group's cells; returns false when the scan budget runs out (the caller stops scanning).
            bool ScanGroup(SlotGroup group)
            {
                var cells = group.CellsList;
                for (int i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    // Cheap scope filters first — and FREE: out-of-scope cells never touch the budget, so
                    // a big colony's irrelevant groups can't exhaust it before the relevant one is reached.
                    if (radiusScan)
                    {
                        if ((cell - vanillaCell).LengthHorizontalSquared > HaulToStackPolicy.OutsideScanRadiusSquared)
                            continue;
                    }
                    else if (cell.GetRoom(map) != room)
                    {
                        continue;
                    }
                    if (++scanned > HaulToStackPolicy.MaxCellsScanned)
                        return false; // huge storage: keep whatever we have rather than stall the scan
                    float distSq = (cell - origin).LengthHorizontalSquared;
                    if (best.IsValid && !HaulToStackPolicy.IsBetter(true, distSq, true, bestDistSq))
                        continue;
                    if (!CellHasPartialStackOf(cell, map, t))
                        continue;
                    // Vanilla's own full gate: storage blockers (incl. modded per-building capacity rules),
                    // forbiddance, reachability for this carrier, fire, reservations.
                    if (!StoreUtility.IsGoodStoreCell(cell, map, t, carrier, faction))
                        continue;
                    best = cell;
                    bestDistSq = distSq;
                }
                return true;
            }

            // The vanilla-chosen cell's OWN group first — the partial stack is most likely right there,
            // so it gets the budget before any other group. (Vanilla just chose a cell in it, so
            // enabled/accepts hold by construction; IsGoodStoreCell still gates every candidate.)
            if (ScanGroup(chosenGroup))
            {
                // Then the remaining equal-priority groups: anything higher either rejected the thing or
                // had no good cell (else vanilla would have chosen it), anything lower would silently
                // DOWNGRADE the storage.
                var groups = map.haulDestinationManager.AllGroupsListInPriorityOrder;
                for (int g = 0; g < groups.Count; g++)
                {
                    var group = groups[g];
                    if (group == chosenGroup)
                        continue; // already scanned first
                    var priority = group.Settings.Priority;
                    if ((int)priority > (int)chosenPriority)
                        continue;
                    if ((int)priority < (int)chosenPriority)
                        break; // list is priority-sorted
                    if (group.parent is Thing parentThing && parentThing.Faction != faction)
                        continue;
                    if (!group.parent.HaulDestinationEnabled || !group.Settings.AllowedToAccept(t))
                        continue;
                    if (!ScanGroup(group))
                        break;
                }
            }
            return best;
        }

        // A spawned item stack in this cell that t can merge into and that has room left. CanStackWith
        // covers def/stuff/quality/hit points rules, so a "same def, wrong stuff" stack never matches.
        private static bool CellHasPartialStackOf(IntVec3 cell, Map map, Thing t)
        {
            if (!cell.InBounds(map))
                return false;
            var things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                var other = things[i];
                if (other.def.category == ThingCategory.Item
                    && other.stackCount < other.def.stackLimit
                    && other != t
                    && t.CanStackWith(other))
                    return true;
            }
            return false;
        }
    }
}
