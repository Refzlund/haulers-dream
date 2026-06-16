using System;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Per-<c>(pawn, GenTicks.TicksGame)</c> READ memo of a pawn's total SCOOPED (tracked) mass — the sum of
    /// <c>stackCount * GetStatValue(Mass)</c> over the live <see cref="CompHauledToInventory"/> tracked set. This
    /// is the load-fraction numerator the opportunistic-unload pre-gate
    /// (<see cref="Core.OpportunisticUnloadPolicy.ShouldAttemptDivert"/>) needs BEFORE the expensive
    /// <c>TryFindBestBetterStoreCellFor</c> storage search — so it must be cheap to read every work scan.
    ///
    /// WHY a memo: <see cref="OpportunisticUnload.ShouldDivert"/> runs per-pawn on every work scan, and the load
    /// fraction it computes walks <c>GetStatValue(Mass)</c> over every tracked stack. The fraction is the cheap
    /// precheck that gates the storage search, so re-walking it per scan would defeat the deferral. Memoizing it
    /// within a tick collapses repeated same-tick reads (e.g. the work-found divert check and the end-of-run
    /// path on the same pawn) to one walk.
    ///
    /// CORRECTNESS — read cache ONLY, keyed on <see cref="Verse.TickManager.TicksGame"/> via the pure
    /// <see cref="TickKeyedMemo{TValue}"/> (auto-clears on tick change), exactly like <see cref="PawnMassCache"/>:
    /// the tracked set / per-stack mass only changes when the pawn SCOOPS or UNLOADS, both of which run in their
    /// own tick and advance <c>TicksGame</c>, so a stale entry is dropped on its next read next tick. A
    /// deserialized pawn after load is a fresh key (per-pawn key = the live pawn's <c>thingIDNumber</c>); a
    /// same-id pawn from a quickload is dropped by <see cref="Clear"/> on FinalizeInit out of caution. The scan
    /// postfix that reads this does NOT mutate inventory, so it never reads across a same-tick mutation. The
    /// 1-tick staleness on a real mutation matches vanilla stat-cache semantics (observed next tick).
    ///
    /// <c>[ThreadStatic]</c> + lazy-init mirrors <see cref="PawnMassCache"/>: a work-scan call on a worker thread
    /// gets its own per-tick memo; the backing dictionary is created lazily on first use per thread inside
    /// <see cref="TickKeyedMemo{TValue}"/>.
    /// </summary>
    internal static class TrackedMassCache
    {
        [ThreadStatic] private static TickKeyedMemo<float> memo;

        // Self-register the per-session memo clear with the game-load hygiene sweep (see CacheRegistry), so it can
        // never be forgotten. The static ctor runs once, the first time any member is touched (the only way the memo
        // can hold cross-session data); Clear resets the FinalizeInit (main) thread's slot — other threads' memos
        // are per-tick self-clearing.
        static TrackedMassCache() => CacheRegistry.Register(Clear);

        /// <summary>
        /// The total tracked (scooped) mass for <paramref name="pawn"/> this tick — computed once from the live
        /// tracked set and reused for every subsequent same-tick read of the same pawn on this thread. Pass the
        /// already-fetched comp (the caller has it in hand) to avoid a redundant <c>GetComp</c>; a null pawn or
        /// comp returns 0. Destroyed / null tracked stacks are skipped, matching the live walk in
        /// <see cref="OpportunisticUnload.ShouldDivert"/>.
        /// </summary>
        internal static float TrackedMass(Pawn pawn, CompHauledToInventory comp)
        {
            if (pawn == null || comp == null)
                return 0f;

            int tick = Find.TickManager?.TicksGame ?? -1;
            int key = pawn.thingIDNumber;
            if (memo.TryGet(tick, key, out var cached))
                return cached;

            float mass = 0f;
            foreach (var t in comp.PeekHashSet())
            {
                if (t == null || t.Destroyed)
                    continue;
                mass += t.stackCount * t.GetStatValue(StatDefOf.Mass);
            }
            memo.Store(tick, key, mass);
            return mass;
        }

        /// <summary>Drop the main thread's memo and reset the tick stamp — called on game load (FinalizeInit) so an
        /// equal tick number across a quickload cannot serve a stale cross-session entry. Mirrors
        /// <see cref="PawnMassCache.Clear"/>.</summary>
        internal static void Clear() => memo.Clear();
    }
}
