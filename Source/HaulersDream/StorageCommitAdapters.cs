using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // The two value adapters below share the storage ledger with actual reservation and
    // pickup admission. StorageQueryScopeHooks additionally owns the inspected native
    // search/factory lifetimes, using void finalizers for validation and exception-safe
    // cleanup. Those scopes never publish a claim or replace the original count loop.
    //
    // Native count is the remaining trip budget, including opportunistic duplicates. Keep
    // its nonnull/minimum-one contract: several native callers immediately StartJob with
    // the factory result. Admission at reservation/start and every real pickup checks the
    // concrete source. The retained unsupported-provider scalar route remains separate.
    //
    // Bind parameters positionally to the installed native signatures. All required patches
    // (including query scopes and reservation stripping) are listed in StorageSeamTargets;
    // a missing binding disables the seam rather than allowing an unprotected strip.

    /// <summary>
    /// Preserve the native trip budget for resource admission; retain the existing scalar
    /// count fallback only where that provider contract remains in use.
    /// </summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToCellStorageJob))]
    public static class Patch_HaulToCellStorageJob_ClampToCommitments
    {
        /// <summary>Preserve the factory contract and its supported admission route.</summary>
        /// <param name="__result">The haul job; left entirely alone when null, and never nulled here.</param>
        /// <param name="__0">The hauling pawn.</param>
        /// <param name="__1">The stack being hauled.</param>
        /// <param name="__2">The destination cell vanilla chose.</param>
        static void Postfix(Job __result, Pawn __0, Thing __1, IntVec3 __2)
        {
            if (__result == null || __0 == null || __1?.def == null)
                return;
            // Measuring a group calls IsGoodStoreCell, which can reach this method through vanilla's own
            // search; and an explicit player order overrides the standing arbitration by design.
            if (StorageCommitments.InsideSpaceScan || StorageCommitments.InForcedOrder)
                return;
            if (!StorageCommitments.AnyClaims)
                return;
            var map = __0.Map;
            if (!StorageCommitments.GatesVanillaStorage(map) || __1.def.category != ThingCategory.Item)
                return;

            var group = BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(__2));
            if (group == null)
                return; // a container or a bare cell — not a destination this ledger arbitrates

            if (StorageCommitments.QueryFactoryHasView(__0, map, __1, group))
            {
                // The original count loop already used this factory's scoped gate. Its
                // finalizer certifies the view; actual pickup still admits the real source.
                __result.count = Math.Max(1, __result.count);
                return;
            }

            var resource = StorageCommitments.ResourceUnitsFor(__0, group, __1,
                Math.Max(1, Math.Min(__result.count > 0 ? __result.count : __1.stackCount, __1.stackCount)),
                __2, __result, out int witnessed);
            if (resource != StorageCommitments.ResourceAllowance.Unsupported)
            {
                // count is native's entire remaining pickup budget, not a commitment to this
                // first Thing. Keeping it lets native discover actual compatible duplicates.
                // Reservation/start and each actual pickup admit their concrete source anew.
                // Keep native's nonzero contract for callers which immediately StartJob.
                __result.count = Math.Max(1, __result.count);
                return;
            }
            int free = StorageCommitments.FreeUnitsFor(__0, group, __1.def, __1, out bool truncated);
            if (free == int.MaxValue)
                return;

            // Floor of 1, never 0 and never null. Two reasons it has to be a floor and not a clamp:
            //   • Toils_Recipe passes this job straight into StartJob with no null check, and StartJob(null)
            //     throws on curJob.startTick;
            //   • vanilla's own count is the sum over cells that pass IsGoodStoreCell, and the GATE below is
            //     now on that loop — so a fully-committed group can hand us a count of ZERO, which
            //     Toils_Haul.ErrorCheckForCarry answers with a red "Invalid count: 0, setting to 1". Vanilla
            //     already repairs that number; HD must not widen the window it appears in.
            int allowed = Math.Max(1, free);
            // An incomplete look must not clamp either. The cell walk is budgeted, so a huge nearly-full
            // group can report far less room than it has, and clamping a haul to 1 on an under-estimate is
            // the "colonists carry one item at a time" symptom this mod has shipped before.
            int next = truncated
                ? Math.Max(__result.count, 1)
                : (__result.count > 0 ? Math.Min(__result.count, allowed) : allowed);
            if (next == __result.count)
                return;
            StorageCommitments.Trace("count", __0, group, __1.def, next, free);
            __result.count = next;
        }
    }

    /// <summary>
    /// The GATE: hide a storage cell whose group's units for this def are already fully spoken for, so the
    /// pawn is routed elsewhere instead of being sent to a destination with nothing left for it.
    /// </summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell))]
    public static class Patch_IsGoodStoreCell_HonourCommitments
    {
        /// <summary>Refuse a cell the ledger has already given away.</summary>
        /// <param name="__result">Vanilla's verdict; only ever turned from true to false.</param>
        /// <param name="__0">The candidate cell.</param>
        /// <param name="__1">The map.</param>
        /// <param name="__2">The stack being placed.</param>
        /// <param name="__3">The pawn that would carry it. NULL for a availability query rather than a real
        /// haul, and that distinction is load-bearing — see below.</param>
        static void Postfix(ref bool __result, IntVec3 __0, Map __1, Thing __2, Pawn __3)
        {
            if (!__result)
                return;
            // → GOTCHA: this null check is the only thing between this gate and the colony-wide haulable
            //   lister. StoreUtility.IsInValidBestStorage asks TryFindBestBetterStorageFor(t, null, …), and
            //   its answer feeds ListerHaulables and the storage alerts. Throttling THAT would make items
            //   vanish from the haul list and the "things are deteriorating" alerts lie, colony-wide,
            //   because one pawn is mid-trip. A commitment must never change what is HAULABLE, only who is
            //   sent to carry it.
            if (__3 == null)
                return;
            if (StorageCommitments.InsideSpaceScan || StorageCommitments.InForcedOrder)
                return;
            if (!StorageCommitments.AnyClaims)
                return;
            if (__2?.def == null || __2.def.category != ThingCategory.Item)
                return; // a downed pawn (the caravan gather search passes one as the "thing") is not cargo
            if (!StorageCommitments.GatesVanillaStorage(__1))
                return;

            var group = BulkHaul.BudgetGroupOf(__1.haulDestinationManager.SlotGroupAt(__0));
            if (group == null)
                return; // the desperate radial leg and the caravan search are cell-only: inert here

            var resource = StorageCommitments.SearchCellUnitsFor(__3, __1, group, __2,
                __0, out int witnessed);
            if (resource != StorageCommitments.ResourceAllowance.Unsupported)
            {
                __result = resource == StorageCommitments.ResourceAllowance.Observed && witnessed > 0;
                return;
            }
            int free = StorageCommitments.FreeUnitsFor(__3, group, __2.def, __2, out bool truncated);
            // An incomplete look must never become a hard refusal. The cell walk is budgeted, so a huge
            // nearly-full group can report less room than it has; clamping a COUNT on an under-estimate is
            // conservative, but REFUSING the whole destination on one would strand haulers at a stockpile
            // that is only too large to finish measuring.
            if (truncated)
                return;
            if (free <= 0)
                __result = false;
        }
    }

    /// <summary>
    /// The forced-order scope. A player clicking "Prioritize hauling" is overriding the standing
    /// arbitration, exactly as every other HD toggle treats a forced order, so both adapters stand down for
    /// the duration of the search this order runs.
    ///
    /// <para>The flag has to be carried as a scope rather than read off the job, because at the moment the
    /// destination is chosen the job does not exist yet — <c>playerForced</c> is stamped on it afterwards,
    /// by <c>Pawn_JobTracker.TryTakeOrderedJob</c>. The claim itself is still recorded when the job starts,
    /// so a forced hauler is visible to everyone else. Actual startup admits a finite primary
    /// against protected cargo/leases before reducing eligible automatic pending work.</para>
    /// </summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToStorageJob))]
    public static class Patch_HaulToStorageJob_ForcedScope
    {
        /// <summary>Open the scope for a forced order.</summary>
        /// <param name="__2">Vanilla's <c>forced</c> argument.</param>
        static void Prefix(bool __2)
        {
            if (__2)
                StorageCommitments.PushForcedOrder();
        }

        /// <summary>Close it, however the search ended. A Finalizer rather than a Postfix so a throw inside
        /// vanilla's search cannot leave the scope stuck open, which would disable the gate for the rest of
        /// the session.</summary>
        /// <param name="__2">Vanilla's <c>forced</c> argument.</param>
        static void Finalizer(bool __2)
        {
            if (__2)
                StorageCommitments.PopForcedOrder();
        }
    }
}
