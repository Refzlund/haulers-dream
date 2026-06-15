using System;
using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Per-<c>(pawn, GenTicks.TicksGame)</c> READ memo of the two "does this pawn hold anything to unload"
    /// booleans the inspect-pane gizmo reads EVERY FRAME a player pawn is selected:
    /// <see cref="InventorySurplus.HasAnySurplus"/> and <see cref="InventorySurplus.HasAnyRuledSurplus"/>.
    ///
    /// WHY: <c>Pawn.GetGizmos</c> runs once per frame per selected pawn; each of those booleans is a full
    /// inventory scan (now O(n) after the HD-GIZMO comp/count hoist, but still a walk + a <c>SurplusOf</c> per
    /// stack). Surplus changes SLOWLY versus the frame rate — a pawn's inventory only changes when it scoops or
    /// unloads (a tick event), never between two frames of the same tick — so recomputing per frame is pure
    /// waste. Memoizing per tick collapses the per-frame scans to one per tick (≈1 vs 60+ per second selected).
    ///
    /// CORRECTNESS — keyed on <see cref="Verse.TickManager.TicksGame"/> via the pure
    /// <see cref="TickKeyedMemo{TValue}"/> (auto-clears on tick change), so a value is at most one tick old. The
    /// pawn doing an actual unload/scoop mutates its inventory on a TICK, so the very next tick re-stamps and the
    /// cache recomputes — the gizmo's show/hide flip is at most one tick (one frame) delayed, a purely cosmetic
    /// staleness identical in spirit to vanilla stat-cache semantics. The per-pawn key is the live pawn's
    /// IDENTITY (<c>thingIDNumber</c>), so a deserialized pawn is a fresh entry and a same-id pawn from a
    /// quickload is dropped by <see cref="Clear"/> on FinalizeInit.
    ///
    /// <c>[ThreadStatic]</c> + lazy-init mirrors <see cref="PawnMassCache"/> (the gizmo path is main-thread, but
    /// this keeps the convention so a stray off-thread call can't race a shared map).
    /// </summary>
    internal static class SurplusCache
    {
        [ThreadStatic] private static TickKeyedMemo<bool> anyMemo;
        [ThreadStatic] private static TickKeyedMemo<bool> ruledMemo;

        /// <summary>Memoized <see cref="InventorySurplus.HasAnySurplus"/> for this tick (computed once, reused
        /// across same-tick frames). Null pawn -> false.</summary>
        internal static bool HasAnySurplus(Pawn pawn)
        {
            if (pawn == null)
                return false;
            int tick = Find.TickManager?.TicksGame ?? -1;
            int key = pawn.thingIDNumber;
            if (anyMemo.TryGet(tick, key, out var cached))
                return cached;
            bool fresh = InventorySurplus.HasAnySurplus(pawn);
            anyMemo.Store(tick, key, fresh);
            return fresh;
        }

        /// <summary>Memoized <see cref="InventorySurplus.HasAnyRuledSurplus"/> for this tick. Null pawn -> false.</summary>
        internal static bool HasAnyRuledSurplus(Pawn pawn)
        {
            if (pawn == null)
                return false;
            int tick = Find.TickManager?.TicksGame ?? -1;
            int key = pawn.thingIDNumber;
            if (ruledMemo.TryGet(tick, key, out var cached))
                return cached;
            bool fresh = InventorySurplus.HasAnyRuledSurplus(pawn);
            ruledMemo.Store(tick, key, fresh);
            return fresh;
        }

        /// <summary>Drop both memos on game load (FinalizeInit) so an equal tick number across a quickload cannot
        /// serve a stale cross-session entry. Mirrors <see cref="PawnMassCache.Clear"/>.</summary>
        internal static void Clear()
        {
            anyMemo.Clear();
            ruledMemo.Clear();
        }
    }
}
