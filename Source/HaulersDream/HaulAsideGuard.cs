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
    /// HAUL-ASIDE PING-PONG GUARD (issue #162). A distinct loop from the storage churn the other layers bound,
    /// and the reason four prior fixes missed it. When an item sits on a cell a vanilla work-giver wants cleared
    /// (a bill-giver's ingredient cell, a grower/mining/construction cell), vanilla issues
    /// <see cref="HaulAIUtility.HaulAsideJobFor"/>: a <c>HaulToCell</c> job with <c>HaulMode.ToCellNonStorage</c>
    /// and <c>count 99999</c> that moves the item to the NEAREST valid cell. In a cramped room the nearest valid
    /// cell is an adjacent cell the work-giver ALSO wants cleared, so the item ping-pongs between two cells forever,
    /// every haul reported <see cref="JobCondition.Succeeded"/> (decompile-confirmed against Assembly-CSharp:
    /// <c>HaulAsideJobFor</c> -> <c>CanHaulAside</c> -> <c>TryFindSpotToPlaceHaulableCloseTo</c>, picking the nearest
    /// <c>HaulablePlaceValidator</c> cell). The #162 report log captured it exactly: a kidney oscillating between two
    /// hospital cells, a fresh HaulToCell every ~30 ticks, forever, WHILE a bill on the adjacent operating table
    /// kept demanding its cell be cleared.
    ///
    /// <para><b>Storability is NOT the discriminator.</b> An earlier attempt only suppressed items with nowhere to
    /// store them, but the report log proves the looping kidney WAS storable: the instant the shuffle was broken,
    /// Hauler's Dream's own bulk-haul grabbed it and stored it (a stockpile had room). It bounces only because the
    /// work-giver's aside outranks the storage haul, so the pawn keeps clearing the cell and never gets to store the
    /// item. The fix is therefore about the BOUNCE, not storage: the moment a colonist hauls an item aside, further
    /// haul-aside for that same item is suppressed (<see cref="Patch_JobDriver_HaulToCell_NoteAside"/> stamps it,
    /// <see cref="Patch_HaulAsideJobFor_SuppressPingPong"/> denies the repeat). The one aside that clears the work
    /// cell still runs; only the second, RETURNING haul is denied, so the pawn does something productive instead
    /// (hauls the item to storage, or moves on) rather than pacing. Each further denial RE-ARMS the window, so while
    /// the work-giver keeps asking, the item is relocated ONCE and then never re-probed (no periodic ~40 s
    /// recurrence); the window (<see cref="HaulChurnPolicy.AsideBackoffTicks"/>) only lapses once those requests stop.</para>
    ///
    /// <para>The suppression is a Harmony PREFIX that forces <c>HaulAsideJobFor</c> to return null (the canonical
    /// "replace the return value" shape, more robust than a postfix on the same seam). A null aside is a state EVERY
    /// vanilla caller already handles (identical to <c>CanHaulAside</c> finding no spot), so the break is clean for
    /// all five callers, not just the bill-giver one the report hit. Scope is narrow: STORAGE hauling is never
    /// touched (only the aside path is denied), player-forced hauls never count, and it is unrelated to HaulToStack
    /// so it is always active (the loop is pure vanilla). Multiplayer: the stamp is written on the synced
    /// job-start path and read/refreshed on the synced job-giving path, keyed on thingIDNumber + TicksGame, so every
    /// client decides identically.</para>
    /// </summary>
    internal static class HaulAsideGuard
    {
        // Guards the stamp tables. Stamps are written on the synced sim path (job start) and read/refreshed on the
        // synced job-giving path via ShouldDenyAside; a threading mod could fan the haul scan that calls it onto
        // worker threads, so a plain lock keeps the tiny dictionary ops safe. Entries are pruned on stamp and
        // cleared on game load.
        private static readonly object sync = new object();

        // thingIDNumber -> tick until which further haul-aside for the thing is suppressed.
        private static readonly Dictionary<int, int> suppressedUntil = new Dictionary<int, int>();

        // Reused prune scratch (only ever touched under the lock).
        private static readonly List<int> expiredScratch = new List<int>();

        // Self-register the per-session clear with the game-load hygiene sweep (see CacheRegistry): thingIDNumbers
        // collide across saves, so a stale stamp could otherwise suppress an unrelated item after a quickload.
        static HaulAsideGuard() => CacheRegistry.Register(Clear);

        /// <summary>Drop every aside suppression stamp (game-load hygiene).</summary>
        internal static void Clear()
        {
            lock (sync)
                suppressedUntil.Clear();
        }

        /// <summary>Record that <paramref name="thing"/> was just hauled ASIDE, and stamp it so any further
        /// haul-aside for it is suppressed for <see cref="HaulChurnPolicy.AsideBackoffTicks"/>: the shuffle is
        /// stopped after this first relocation. Storability is irrelevant (a storable item bounces too, and breaking
        /// the bounce is exactly what lets it be hauled to storage); the one aside that ran already cleared the work
        /// cell. An item already inside its window is left as is. Read only for its thingIDNumber, so a
        /// destroyed/merged stack at call time is harmless.</summary>
        internal static void NoteAsideRun(Thing thing)
        {
            if (thing == null)
                return;
            int now = Find.TickManager?.TicksGame ?? 0;
            int id = thing.thingIDNumber;

            bool alreadySuppressed;
            lock (sync)
                alreadySuppressed = suppressedUntil.TryGetValue(id, out int existing)
                                    && HaulChurnPolicy.IsSuppressed(now, existing);
            if (alreadySuppressed)
                return;

            lock (sync)
            {
                PruneExpiredLocked(now);
                suppressedUntil[id] = HaulChurnPolicy.AsideSuppressUntil(now);
            }
        }

        /// <summary>Whether a just-requested haul-aside for <paramref name="thing"/> must be DENIED (the item was
        /// already relocated once and is inside its suppression window). SIDE EFFECT: on a match it pushes the
        /// window forward from now, so while a work-giver keeps asking to clear the item's cell the item stays
        /// suppressed (relocated ONCE, then never re-probed, so no periodic recurrence); the window only lapses once
        /// vanilla stops asking (the situation resolved), at which point the item is retried. Read-and-extend under
        /// the lock; unlocked fast path when the table is empty.</summary>
        internal static bool ShouldDenyAside(Thing thing)
        {
            // Unlocked fast path: the table is almost always empty, and a torn read of Count only defers one
            // denial by a scan tick (the next request re-checks).
            if (thing == null || suppressedUntil.Count == 0)
                return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            int id = thing.thingIDNumber;
            lock (sync)
            {
                if (!suppressedUntil.TryGetValue(id, out int until) || !HaulChurnPolicy.IsSuppressed(now, until))
                    return false;
                // Re-arm from now: a persistently requested aside keeps the window alive so it never re-bursts.
                suppressedUntil[id] = HaulChurnPolicy.AsideSuppressUntil(now);
            }
            return true;
        }

        // Drop every expired suppression stamp (the table only ever holds recently-aside'd things). Caller holds the lock.
        private static void PruneExpiredLocked(int now)
        {
            expiredScratch.Clear();
            foreach (var pair in suppressedUntil)
                if (!HaulChurnPolicy.IsSuppressed(now, pair.Value))
                    expiredScratch.Add(pair.Key);
            for (int i = 0; i < expiredScratch.Count; i++)
                suppressedUntil.Remove(expiredScratch[i]);
            expiredScratch.Clear();
        }
    }

    /// <summary>
    /// Note each AUTOMATIC haul-aside (a <c>HaulToCell</c> with <c>HaulMode.ToCellNonStorage</c>) as it starts, so
    /// the item that was just relocated is stamped and not shuffled again. Player-forced hauls never count. Runs at
    /// job start (the aside job's <see cref="JobDriver_HaulToCell.Notify_Starting"/>), the same seam the storage
    /// per-thing guard uses; unrelated to HaulToStack, so it is not gated on that feature.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.Notify_Starting))]
    public static class Patch_JobDriver_HaulToCell_NoteAside
    {
        static void Postfix(JobDriver_HaulToCell __instance)
        {
            var job = __instance?.job;
            if (job == null || job.playerForced)
                return;
            if (job.def != JobDefOf.HaulToCell || job.haulMode != HaulMode.ToCellNonStorage)
                return; // only the vanilla haul-ASIDE shape (storage hauls are bounded by the other layers)
            var thing = job.GetTarget(TargetIndex.A).Thing;
            if (thing?.def != null)
                HaulAsideGuard.NoteAsideRun(thing);
        }

        // Seam guard: a throw in this Notify_Starting postfix would break HaulToCell startup wholesale.
        static Exception Finalizer(Exception __exception)
            => HDGuard.SeamThrew(__exception, "JobDriver_HaulToCell.Notify_Starting (HD haul-aside note)",
                null, "aside suppression not stamped for this job; the other churn layers are unaffected.");
    }

    /// <summary>
    /// Break the ping-pong at its source: once a thing has been stamped (already hauled aside once), force
    /// <see cref="HaulAIUtility.HaulAsideJobFor"/> to return null so no further aside job is issued for it, and
    /// re-arm its window so a persistently-requested aside never re-bursts. Implemented as a PREFIX that skips the
    /// original and sets the result (the canonical, reliable "replace return value" shape). A null aside is a state
    /// every vanilla caller already handles (identical to <c>CanHaulAside</c> finding no spot), so the pawn simply
    /// does not haul it aside and moves on; the item sits where it is until its situation changes (it is hauled to
    /// storage, the player intervenes), at which point the item is retried. Inert for any thing not stamped (returns
    /// true so the original runs unchanged).
    /// </summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulAsideJobFor))]
    public static class Patch_HaulAsideJobFor_SuppressPingPong
    {
        static bool Prefix(Thing t, ref Job __result)
        {
            if (t != null && HaulAsideGuard.ShouldDenyAside(t))
            {
                __result = null; // already relocated once -> deny the returning aside, skip the original
                return false;
            }
            return true; // not stamped -> run vanilla HaulAsideJobFor unchanged
        }

        // Seam guard: a throw here would break aside-job creation for every caller; log attributably and rethrow.
        static Exception Finalizer(Exception __exception)
            => HDGuard.SeamThrew(__exception, "HaulAIUtility.HaulAsideJobFor (HD ping-pong break)",
                null, "haul-aside suppression skipped; vanilla's aside job stands for this call.");
    }
}
