using System;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Per-<c>(pawn, GenTicks.TicksGame)</c> READ memo of the two <c>MassUtility</c> numbers every overload
    /// decision needs: the pawn's true carry <see cref="PawnMass.Capacity"/> (<c>MassUtility.Capacity</c>) and
    /// its current gear+inventory mass (<c>MassUtility.GearAndInventoryMass</c>). Both are pure reads
    /// (decompile-verified: <c>Capacity</c> = <c>CanEverCarryAnything</c> + <c>BodySize*35</c>;
    /// <c>GearAndInventoryMass</c> sums <c>GetStatValue(Mass)</c> over apparel/equipment/inventory — neither
    /// mutates pawn state), so memoizing them within a tick changes no value, only avoids recomputation.
    ///
    /// WHY: the vanilla <c>MoveSpeed</c> StatDef is UNCACHED, so <see cref="StatPart_Overload"/> re-walks the
    /// full apparel+equipment+inventory mass once PER CELL a moving colonist enters; the same numbers are then
    /// re-read by the same-tick capacity gates. This memo collapses those N walks/tick/pawn to one.
    ///
    /// CORRECTNESS — read cache ONLY, never read across a same-tick inventory MUTATION by the same path:
    /// the memo is keyed on <see cref="Verse.TickManager.TicksGame"/> via the pure
    /// <see cref="TickKeyedMemo{TValue}"/> (auto-clears on tick change), so it self-invalidates every tick and,
    /// because the per-pawn key is the live pawn's IDENTITY (<c>thingIDNumber</c>), it cannot serve a value from
    /// a previous session after load (a deserialized pawn is a fresh entry; a same-id pawn from a quickload is
    /// dropped by <see cref="Clear"/> on FinalizeInit out of caution). A pawn that ADDS to its inventory mid-tick
    /// (e.g. the corpse-strip scoop loop) and re-reads in the SAME tick must NOT use this memo — it would see
    /// the pre-mutation mass. Those mutating callers keep reading live
    /// (<see cref="OverloadGate.CountToPickUp(Verse.Pawn,Verse.Thing,HaulersDreamSettings)"/> reads live for
    /// exactly that reason). Non-mutating callers — the per-cell MoveSpeed StatPart and the queue-only nearby
    /// sweep (which only records pending pickups, never touches inventory) — share this one read. The 1-tick
    /// staleness on a real mutation matches vanilla stat-cache semantics: the change is observed next tick.
    ///
    /// <c>[ThreadStatic]</c> + lazy-init mirrors the assembly's hook-reachable scratch convention
    /// (<see cref="BulkHaul"/>'s <c>planCache</c>, <see cref="OrganicInventoryShare"/>'s <c>countCache</c>):
    /// a stat/work-scan call on a worker thread gets its own per-tick memo; ThreadStatic field initializers
    /// only run on the static-ctor thread, so the backing dictionary is created lazily on first use per thread
    /// (handled inside <see cref="TickKeyedMemo{TValue}"/>).
    /// </summary>
    internal static class PawnMassCache
    {
        // One memo per thread. The struct holds the dictionary + tick stamp; default(struct) is a valid empty
        // memo (lazy-inits its dictionary on first use), so no ThreadStatic field initializer is needed.
        [ThreadStatic] private static TickKeyedMemo<PawnMass> memo;

        // Self-register the per-session memo clear with the game-load hygiene sweep (see CacheRegistry), so it can
        // never be forgotten. The static ctor runs once, the first time any member of this cache is touched (which
        // is also the only way the memo can hold cross-session data); Clear resets the FinalizeInit (main) thread's
        // slot, the same slot the registry runs on — other threads' memos are per-tick self-clearing.
        static PawnMassCache() => CacheRegistry.Register(Clear);

        /// <summary>
        /// The memoized <c>(Capacity, GearAndInventoryMass)</c> for <paramref name="pawn"/> this tick — computed
        /// once and reused for every subsequent same-tick read of the same pawn on this thread. Null pawn returns
        /// a zero <see cref="PawnMass"/> (matches <c>MassUtility</c>'s no-capacity result). NOT for callers that
        /// mutate the pawn's inventory and re-read within the same tick (they must read live — see the class doc).
        /// </summary>
        internal static PawnMass MassInfo(Pawn pawn)
        {
            if (pawn == null)
                return default;

            // TicksGame FREEZES while paused. That's fine here: mass cannot change while paused (no toils run),
            // so a frozen-tick memo serves the correct value, and the per-cell MoveSpeed read isn't exercised
            // while paused anyway. A new game / cross-session load resets the stamp via Clear (FinalizeInit).
            int tick = Find.TickManager?.TicksGame ?? -1;
            int key = pawn.thingIDNumber;
            if (memo.TryGet(tick, key, out var cached))
                return cached;

            // The two pure reads — the ONLY place this assembly walks mass for the memoized callers.
            var fresh = new PawnMass(CarryCapacity.Of(pawn), MassUtility.GearAndInventoryMass(pawn));
            memo.Store(tick, key, fresh);
            return fresh;
        }

        /// <summary>Memoized <c>MassUtility.Capacity(pawn)</c> for this tick (0 for a null / non-carrying pawn).</summary>
        internal static float Capacity(Pawn pawn) => MassInfo(pawn).Capacity;

        /// <summary>Memoized <c>MassUtility.GearAndInventoryMass(pawn)</c> for this tick (0 for a null pawn).</summary>
        internal static float CurrentMass(Pawn pawn) => MassInfo(pawn).CurrentMass;

        /// <summary>
        /// Drop the main thread's memo and reset the tick stamp — called on game load (FinalizeInit) so an equal
        /// tick number across a quickload cannot serve a stale cross-session entry. Other threads' memos are
        /// per-tick self-clearing, so a stale entry there dies on its next read regardless. Mirrors
        /// <see cref="BulkHaul.ClearPlanCache"/>.
        /// </summary>
        internal static void Clear() => memo.Clear();
    }
}
