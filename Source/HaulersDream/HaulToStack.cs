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
    /// Storage candidates use vanilla's own APIs — <c>IsGoodStoreCell</c> (which runs NoStorageBlockersIn,
    /// reachability, fire, forbiddance) and <c>CanStackWith</c>. Adaptive Storage Framework (and mods built
    /// on it, like Neat Storage) patch exactly those APIs (NoStorageBlockersIn transpiler,
    /// GetMaxItemsAllowedInCell, a worker prefix — source-verified in the ASF clone), so their per-building
    /// capacity and acceptance rules apply inside our calls automatically. Container-based storage
    /// (graves, modded ThingOwner units) goes through the untouched non-slot-group path, where stacking is
    /// inherent. Providers that gate the higher-level group search need an explicit compatibility check:
    /// Storage Refill Hysteresis controls whether a new refill may start, and RimIOT owns its consolidation.
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
            // Nothing to top up: a search for a partial stack of an unstackable can only ever come back
            // empty, so this is a pure cost saving on the corpse/weapon/minified-building haul path. Routed
            // through the Core policy rather than spelled out here, so the ONE definition of "can this def
            // share a cell" lives beside the search it scopes — and so the reservation decision below, which
            // used to carry a hand-written copy of the same test, can no longer drift away from it.
            if (!HaulToStackPolicy.CanTopUp(t.def.stackLimit))
                return;
            // No try/catch: a refinement failure is a real bug to surface as a red error, not a silent warning.
            var better = HaulToStack.FindStackCell(t, carrier, map, faction, foundCell);
            if (better.IsValid)
                foundCell = better;
        }
    }

    [HarmonyPatch(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.TryMakePreToilReservations))]
    public static class Patch_JobDriver_HaulToCell_NoCellReservation
    {
        static bool Prefix(JobDriver_HaulToCell __instance, bool errorOnFailed, ref bool __result,
            out NativeFallbackReservation __state)
        {
            __state = null;
            // A queued/preselected native haul must not skip another current explicit trip's real
            // destination reservation, including through the forced-order arbitration bypass.
            if (StorageCommitments.ExplicitShelfCellHeldByOther(__instance.pawn, __instance.job?.targetB.Cell ?? IntVec3.Invalid))
            { __result = false; return false; }
            var job = __instance.job;
            if (job == null || job.haulMode != HaulMode.ToCellStorage)
                return true; // non-storage cell hauls keep their reservation semantics
            var pawn = __instance.pawn;
            var hauled = job.GetTarget(TargetIndex.A).Thing;
            var map = pawn?.Map;
            if (hauled?.def == null || map == null)
                return true;
            var group = BulkHaul.BudgetGroupOf(
                map.haulDestinationManager.SlotGroupAt(job.GetTarget(TargetIndex.B).Cell));
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.haulToStack || __instance.GetType() != typeof(JobDriver_HaulToCell)
                || job.def != JobDefOf.HaulToCell)
                return NativeFallback(__instance, group, ref __result, out __state);
            // A pre-existing hand passenger is not this activation's incoming parcel. Keep
            // native whole-cell ownership until native work has bound its own actual cargo.
            var held = pawn.carryTracker?.CarriedThing;
            if (held != null && !ReferenceEquals(held, hauled)
                && !map.reservationManager.ReservedBy(held, pawn, job))
                return NativeFallback(__instance, group, ref __result, out __state);
            int requested = job.count > 0 ? Math.Min(job.count, hauled.stackCount) : hauled.stackCount;
            bool priority = StorageCommitments.MayPrioritizeNative(__instance);
            int available;
            var resource = priority
                ? StorageCommitments.ForcedNativeUnitsFor(__instance, group, hauled, requested, false, out available)
                : StorageCommitments.ResourceUnitsFor(pawn, group, hauled, requested, job.targetB.Cell, job, out available);
            if (resource != StorageCommitments.ResourceAllowance.Unsupported)
            {
                if (resource != StorageCommitments.ResourceAllowance.Observed || available <= 0)
                { __result = false; return false; }
                var source = job.GetTarget(TargetIndex.A);
                bool alreadyOwned = map.reservationManager.ReservedBy(source, pawn, job);
                var reservingJob = pawn.CurJob;
                var reservingDriver = pawn.jobs.curDriver;
                if (!pawn.Reserve(source, job, 1, -1, null, errorOnFailed))
                { __result = false; return false; }
                // Reservation callbacks may change the world. Publish only a fresh, successful
                // admission after source ownership, never a cached-driver/preflight proposal.
                bool actual = ReferenceEquals(pawn.CurJob, job) && ReferenceEquals(pawn.jobs.curDriver, __instance);
                resource = priority
                    ? StorageCommitments.ForcedNativeUnitsFor(__instance, group, hauled, requested, actual, out available)
                    : StorageCommitments.ResourceUnitsFor(pawn, group, hauled, requested, job.targetB.Cell, job, out available);
                if (resource != StorageCommitments.ResourceAllowance.Observed || available <= 0)
                {
                    if (!alreadyOwned && (!priority || (ReferenceEquals(pawn.CurJob, reservingJob)
                        && ReferenceEquals(pawn.jobs.curDriver, reservingDriver) && job.targetA == source
                        && StorageCommitments.MayPrioritizeNative(__instance))))
                        map.reservationManager.Release(source, pawn, job);
                    __result = false; return false;
                }
                // Native count is the remaining trip budget, including later opportunistic
                // sources. Only this concrete reserved source has been admitted so far; a
                // pickup hook admits each later source before it enters the pawn's hands.
                if (!priority && ReferenceEquals(pawn.CurJob, job) && ReferenceEquals(pawn.jobs.curDriver, __instance))
                    StorageCommitments.CommitNativeResource(pawn, job, group, hauled, Math.Min(requested, available));
                __result = true;
                return false;
            }
            // Unobserved provider semantics keep native source AND destination reservations.
            // A preselected or forced job must also respect older numerical owners whose
            // native cell reservation was removed while that provider was supported.
            return NativeFallback(__instance, group, ref __result, out __state);
        }

        private static bool NativeFallback(JobDriver_HaulToCell driver, ISlotGroup group,
            ref bool result, out NativeFallbackReservation state)
        {
            state = group == null ? null : new NativeFallbackReservation(driver, group);
            if (state == null || state.Allowed()) return true;
            state = null; result = false; return false;
        }

        // Native source/destination reservation callbacks may change admission after the prefix.
        // Recheck the complete original call; only reservations acquired by this call are undone.
        static Exception Finalizer(ref bool __result, Exception __exception,
            NativeFallbackReservation __state)
            => __state == null ? __exception : __state.Finish(ref __result, __exception);
    }

    public static class HaulToStack
    {
        // Per-tick memo: the work scan probes HasJobOnThing (= JobOnThing != null) per candidate, and each
        // probe runs the full vanilla storage search INCLUDING this refinement — same lesson as the
        // bulk-haul planner. Key = (thing, CARRIER, vanilla's chosen cell); null/Invalid results cached
        // too, except while SRH is present because its refill toggle can change within one tick.
        // The carrier is part of the key because IsGoodStoreCell validates per CARRIER (allowed
        // area, its own reservations, reachability) — serving one pawn's cell to another hands out a job
        // that fails synchronously and re-scans the same tick ("started 10 jobs in one tick").
        // [ThreadStatic] to match the sibling BulkHaul.planCache: FindStackCell runs on the per-candidate
        // HasJobOnThing probe, which a threading mod (e.g. RimThreaded) may fan onto worker threads, and one
        // shared Dictionary mutated concurrently would tear. Each worker thread keeps its own per-tick memo,
        // lazily built at the read site (ThreadStatic field initializers only run on the static-ctor thread).
        // Correctness is unchanged single-threaded: the per-tick clear self-scopes per thread, and the
        // IsGoodStoreCell re-validation on every cached hit is the real stale/cross-session guard (the tick
        // stamp is belt-and-braces), exactly as BulkHaul.planCache leans on its loadID re-check.
        [ThreadStatic] private static int cacheTick;
        [ThreadStatic] private static Dictionary<(int thingId, int carrierId, int cellIdx), IntVec3> cellCache;

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
            cellCache?.Clear();
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
            if (tick != -1)
            {
                // Lazy per-thread init (ThreadStatic initializers only run on the static-ctor thread), so every
                // cellCache access below stays under this tick != -1 guard where it is guaranteed non-null.
                cellCache ??= new Dictionary<(int thingId, int carrierId, int cellIdx), IntVec3>();
                if (tick != cacheTick)
                {
                    cellCache.Clear();
                    cacheTick = tick;
                }
            }
            var key = (t.thingIDNumber, carrier.thingIDNumber, map.cellIndices.CellToIndex(vanillaCell));
            if (tick != -1 && cellCache.TryGetValue(key, out var cached))
            {
                // Belt and braces: even a same-carrier hit can go stale within the tick (an earlier job
                // this tick reserved the thing or filled the cell) — re-validate before serving it.
                // SRH can toggle within this tick. Recheck a cached destination, and don't
                // retain a negative search across a toggle that could reopen a partial stack.
                if (!cached.IsValid ? !StorageRefillHysteresisCompat.IsPresent
                    : StorageRefillHysteresisCompat.AllowsRefill(cached.GetSlotGroup(map))
                        && StoreUtility.IsGoodStoreCell(cached, map, t, carrier, faction))
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
            // RimIOT compat (#177): if vanilla routed this deposit INTO a RimIOT logistic-network group, HD must
            // NOT re-steer the cell. RimIOT owns cell selection and consolidation inside its own network, and HD's
            // steer-toward-a-partial refinement is exactly what defeats RimIOT's TickRebalance convergence (the
            // stack-size-mod infinite haul loop). Keep vanilla's pick unchanged (return Invalid); RimIOT converges
            // the partials itself. This must run BEFORE the ScanGroup skip below, or a network-bound deposit could
            // be steered OUT to an equal-priority non-network partial (moving items out of the player's network).
            // IsActive short-circuits before any reflection when RimIOT is absent (byte-identical then).
            if (RimIOTCompat.IsActive && RimIOTCompat.IsNetworkManagedGroup(map, chosenGroup))
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
                // RimIOT compat (#177): never reroute a deposit INTO a RimIOT-network-managed group (let RimIOT own
                // consolidation within its network). Skipping the WHOLE group is correct (network membership is a
                // per-SlotGroup property: all its cells share one parent) and cheap (one check per candidate group,
                // not per cell). Returns true = "not a budget exhaustion, keep scanning the other groups". Covers
                // the case where vanilla chose a NON-network cell but a network partial of the same def sits nearby;
                // the chosen-network case is already handled by the early-out above. Inert when RimIOT is absent.
                if (RimIOTCompat.IsActive && RimIOTCompat.IsNetworkManagedGroup(map, group))
                    return true;
                var cells = group.CellsList;
                bool refillChecked = false;
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
                    // SRH gates vanilla's group worker, not IsGoodStoreCell. Ask once for a
                    // relevant partial; out-of-scope groups never trigger its usage measurement.
                    if (!refillChecked)
                    {
                        if (!StorageRefillHysteresisCompat.AllowsRefill(group))
                            return true;
                        refillChecked = true;
                    }
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
                    // A modded SlotGroup can momentarily expose a null Settings (half-built, or a mod tearing
                    // storage down off the main thread). HD already guards the CHOSEN group's Settings in this
                    // method's preamble (chosenGroup?.Settings); mirror that EXACTLY here so HD's own storage
                    // loop skips such a group rather than NRE on group.Settings below — a group with no settings
                    // is not a valid destination anyway. (Settings == parent.GetStoreSettings(), so a non-null
                    // Settings also guarantees a non-null parent for group.parent below.) Issue #58 robustness.
                    if (group?.Settings == null)
                        continue;
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
