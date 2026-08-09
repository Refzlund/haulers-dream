using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /*
        ═══════════════════════════════════════════════════════════════════════════════════════════════
                      The two seams the storage commitment ledger reaches vanilla through
        ═══════════════════════════════════════════════════════════════════════════════════════════════
        Two Harmony patches, and that is the whole surface. Between them they replace the ONE job vanilla's
        destination cell reservation used to do that HD took away: shrinking every other hauler's count.

          • HaulAIUtility.HaulToCellStorageJob — the COUNTER. Every vanilla cell-storage haul is produced
            here (WorkGiver_Haul.JobOnThing, JobGiver_Haul.TryGiveJob, Toils_Haul.StoreThingJob,
            JobDriver_Reading, HaulSourceUtility, Toils_Recipe's bench product, Pawn_JobTracker's
            opportunistic job, WorkGiver_CookFillHopper), so clamping here reaches all of them at once.

          • StoreUtility.IsGoodStoreCell — the GATE. Clamping the count alone is not enough: cell SELECTION
            runs through IsGoodStoreCell and nothing else, so a pawn would still be routed to a group that
            is fully spoken for and then handed a job.count of 0 — which vanilla answers with a red
            "Invalid count: 0, setting to 1" from Toils_Haul.ErrorCheckForCarry.

        → KEY: NEVER null the job and NEVER zero the count. Toils_Recipe passes HaulToCellStorageJob's
          result straight into StartJob with no null check, and StartJob(null) throws on curJob.startTick.
          The floor of 1 is safe precisely because the gate stops the destination being chosen at all.
        → GOTCHA: no Finalizer on either. Harmony already reds-out an uncaught patch exception with the
          patch's own name in the trace, and wrapping the hottest method in the haul system in a try/catch
          to re-log what is already visible would cost more than it buys.
        → NOTE: parameters are taken POSITIONALLY (__0, __1, …). Harmony throws at patch time on a name that
          does not exist, and this assembly's per-class patch loop turns that into a logged warning rather
          than a fatal — so a decompiler's parameter names must not be load-bearing.
        → KEY: these two classes and the reservation strip (Patch_JobDriver_HaulToCell_NoCellReservation) are
          applied INDEPENDENTLY by that loop, so "adapters missing, strip present" is expressible — and it is
          the original bug, shipped inert. HaulersDreamMod.VerifyStorageSeam checks all three at startup and
          calls StorageCommitments.Disable() if any is unaccounted for, which stands the strip down with
          them. Do not add a patch class to this seam without adding it to StorageSeamTargets.
    */

    /// <summary>
    /// The COUNTER: clamp a freshly-built haul's count to what the destination group genuinely still has
    /// free once other haulers' commitments are subtracted.
    /// </summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToCellStorageJob))]
    public static class Patch_HaulToCellStorageJob_ClampToCommitments
    {
        /// <summary>Clamp the job vanilla just built.</summary>
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
    /// so a forced hauler is visible to everyone else; it is only never BLOCKED by them.</para>
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
