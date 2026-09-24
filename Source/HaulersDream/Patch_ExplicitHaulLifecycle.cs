using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal static class ExplicitHaulLifecycle
    {
        // Ephemeron keys do not keep an aborted/replaced Game or its loaded pawns alive.
        private static readonly ConditionalWeakTable<Game, List<CompHauledToInventory>> loaded
            = new ConditionalWeakTable<Game, List<CompHauledToInventory>>();
        internal static void RegisterLoaded(CompHauledToInventory comp)
        {
            if (Current.Game == null) return;
            var entries = loaded.GetOrCreateValue(Current.Game);
            if (!entries.Contains(comp)) entries.Add(comp);
        }
        internal static void FinishLoad(Game game)
        {
            if (!loaded.TryGetValue(game, out var entries)) return;
            loaded.Remove(game);
            foreach (var comp in entries.OrderBy(c => c.parent.thingIDNumber)) comp.ReconcileExplicitOrders();
        }
        internal static void AbortLoad(Game game) => loaded.Remove(game);

        internal sealed class Placement
        {
            internal Placement previous;
            internal Cleanup cleanup;
            internal Map map;
            internal IntVec3 cell;
            internal int attempted;
            internal readonly Dictionary<Thing, int> before = new Dictionary<Thing, int>();
            internal readonly HashSet<Thing> receivers = new HashSet<Thing>();
        }
        internal sealed class Capture
        {
            internal Pawn pawn;
            internal Capture previous;
        }
        internal sealed class Cleanup
        {
            internal Cleanup previous;
            internal Pawn pawn;
            internal Job job;
            internal ExplicitHaulOrder order;
            internal Thing piece;
            internal ThingOwner owner;
            internal int units;
            internal readonly Dictionary<Thing, int> placed = new Dictionary<Thing, int>();
        }
        [ThreadStatic] internal static Placement placement;
        [ThreadStatic] internal static Capture capture;
        [ThreadStatic] internal static Cleanup cleanup;
        internal static bool Capturing(Pawn pawn)
        { for (var s = capture; s != null; s = s.previous) if (s.pawn == pawn) return true; return false; }

        internal static void DropRetained(Pawn pawn, ExplicitHaulOrder order)
        {
            var owner = pawn.GetComp<CompHauledToInventory>().ExplicitRecovery;
            if (!ExplicitHaulTransfer.Owns(owner, order.piece)) return;
            var state = new Cleanup { previous = cleanup, pawn = pawn, order = order,
                piece = order.piece, units = order.piece.stackCount, owner = owner };
            cleanup = state;
            Exception primary = null;
            try { owner.TryDrop(state.piece, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _); }
            catch (Exception error) { primary = error; }
            try { FinishCleanup(state); }
            catch (Exception recovery)
            {
                if (primary == null) primary = recovery;
                else primary.Data["HaulersDream.ExplicitHaul.CancelRecovery"] = recovery.ToString();
            }
            finally { cleanup = state.previous; }
            if (primary != null) ExceptionDispatchInfo.Capture(primary).Throw();
        }

        internal static void FinishCleanup(Cleanup c)
        {
            var o = c.order;
            StorageCommitments.ReleaseExplicitShelf(c.pawn, o);
            if (c.piece != null)
            {
                int placed = 0;
                foreach (var move in c.placed.OrderBy(p => p.Key.thingIDNumber))
                {
                    if (move.Key.Destroyed || !move.Key.Spawned || move.Key.Map.uniqueID != o.mapId
                        || move.Value <= 0 || move.Value > move.Key.stackCount)
                    { o.Block("Native cleanup receiver changed custody"); continue; }
                    o.remainders.Add(new ExplicitHaulRemainder { thing = move.Key, units = move.Value,
                        observedCount = move.Key.stackCount, cell = move.Key.Position });
                    placed += move.Value;
                }
                bool held = ExplicitHaulTransfer.Owns(c.owner, c.piece);
                o.piece = held ? c.piece : null;
                o.tripUnits = held ? c.piece.stackCount : 0;
                if (placed + o.tripUnits != c.units) o.Block("Native cleanup parcel accounting changed");
                // Drop/merge is recovery custody only, even when the drop cell equals destination.
            }
            bool suspended = c.job != null && c.pawn.jobs.jobQueue.Any(q => q?.job == c.job);
            if (!o.Terminal)
            {
                if (suspended && o.state != ExplicitHaulState.Blocked) o.state = ExplicitHaulState.Queued;
                else if (!suspended) { o.Block(o.reason ?? "Order interrupted; resume explicitly"); o.jobId = -1; }
            }
        }
    }

    [HarmonyPatch(typeof(ReservationManager), nameof(ReservationManager.Reserve))]
    internal static class Patch_ExplicitHaulNoSteal
    {
        static bool Prefix(ReservationManager __instance, Pawn claimant, Job job, LocalTargetInfo target, int maxPawns, int stackCount,
            ReservationLayerDef layer, bool ignoreOtherReservations, ref bool __result)
        {
            if (!ExplicitHaulCommand.IsJob(job)) return true;
            if (ExplicitHaulCommand.Identified(claimant, job) && __instance == claimant.Map?.reservationManager && claimant.CurJob == job
                && claimant.jobs.curDriver?.GetType() == typeof(JobDriver_ExplicitHaul)
                && maxPawns == 1 && stackCount == -1 && layer == null && !ignoreOtherReservations
                && ExplicitHaulCommand.CanReserve(claimant, job, target)) return true;
            __result = false; return false; // refuse before native playerForced fallback can cancel a claimant
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
    internal static class Patch_ExplicitHaulCurrentCleanup
    {
        static void Prefix(Pawn ___pawn, out ExplicitHaulLifecycle.Cleanup __state)
        {
            __state = null;
            var job = ___pawn?.CurJob;
            var order = ___pawn?.GetComp<CompHauledToInventory>()?.ExplicitOrder(job);
            if (order == null) return;
            var piece = order.piece;
            if (!ExplicitHaulTransfer.Owns(___pawn.carryTracker.innerContainer, piece)) piece = null;
            __state = new ExplicitHaulLifecycle.Cleanup { previous = ExplicitHaulLifecycle.cleanup,
                pawn = ___pawn, job = job, order = order, piece = piece, units = piece?.stackCount ?? 0,
                owner = ___pawn.carryTracker.innerContainer };
            ExplicitHaulLifecycle.cleanup = __state;
        }
        static Exception Finalizer(Exception __exception, ExplicitHaulLifecycle.Cleanup __state)
        {
            if (__state == null) return __exception;
            try { ExplicitHaulLifecycle.FinishCleanup(__state); }
            catch (Exception recovery)
            {
                __state.order.Block("Native cleanup reconciliation failed");
                if (__exception == null) __exception = recovery;
                else __exception.Data["HaulersDream.ExplicitHaul.Cleanup"] = recovery.ToString();
            }
            finally { ExplicitHaulLifecycle.cleanup = __state.previous; }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class Patch_ExplicitHaulLoaded
    {
        static void Postfix(Game __instance) => ExplicitHaulLifecycle.FinishLoad(__instance);
    }
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    internal static class Patch_ExplicitHaulAbortedLoad
    {
        static Exception Finalizer(Game __instance, Exception __exception)
        {
            if (__exception != null) ExplicitHaulLifecycle.AbortLoad(__instance);
            return __exception;
        }
    }

    // Native Near cleanup may attempt several cells. Observe only this exact parcel's native
    // attempt, including a merge that throws before placedAction, never all nearby stock changes.
    [HarmonyPatch(typeof(GenPlace), "TryPlaceDirect")]
    internal static class Patch_ExplicitHaulCleanupPlacement
    {
        static void Prefix(Thing thing, IntVec3 loc, Map map, out ExplicitHaulLifecycle.Placement __state)
        {
            __state = null;
            var c = ExplicitHaulLifecycle.cleanup;
            if (c?.piece != thing || thing == null || map?.uniqueID != c.order.mapId || !loc.InBounds(map)) return;
            __state = new ExplicitHaulLifecycle.Placement { previous = ExplicitHaulLifecycle.placement,
                cleanup = c, cell = loc, map = map, attempted = thing.stackCount };
            foreach (var t in loc.GetThingList(map))
                if (t.def.category == ThingCategory.Item) __state.before.Add(t, t.stackCount);
            ExplicitHaulLifecycle.placement = __state;
        }
        static Exception Finalizer(Exception __exception, ExplicitHaulLifecycle.Placement __state)
        {
            if (__state == null) return __exception;
            var c = __state.cleanup;
            try
            {
                var moves = new Dictionary<Thing, int>();
                foreach (var t in __state.cell.GetThingList(__state.map))
                {
                    if (t.def != c.piece.def || (!ReferenceEquals(t, c.piece) && !__state.receivers.Contains(t))) continue;
                    int delta = t.stackCount - (__state.before.TryGetValue(t, out int old) ? old : 0);
                    if (delta > 0) moves.Add(t, delta);
                }
                int retained = !c.piece.Destroyed && !c.piece.Spawned ? c.piece.stackCount : 0;
                // This is custody reconciliation, not requested-destination delivery credit.
                HaulersDream.Core.ExplicitHaulAmount.Credit(__state.attempted, 0, __state.attempted, retained, moves.Values.Sum());
                foreach (var move in moves)
                    c.placed[move.Key] = checked((c.placed.TryGetValue(move.Key, out int old) ? old : 0) + move.Value);
            }
            catch (Exception recovery)
            {
                c.order.Block("Native cleanup placement changed custody");
                if (__exception == null) __exception = recovery;
                else __exception.Data["HaulersDream.ExplicitHaul.CleanupPlacement"] = recovery.ToString();
            }
            finally { ExplicitHaulLifecycle.placement = __state.previous; }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TryAbsorbStack))]
    internal static class Patch_ExplicitHaulCleanupReceiver
    {
        static void Prefix(Thing __instance, Thing other)
        {
            var s = ExplicitHaulLifecycle.placement;
            if (s != null && ReferenceEquals(other, s.cleanup.piece) && __instance.Spawned
                && __instance.Map == s.map && __instance.Position == s.cell && s.before.ContainsKey(__instance))
                s.receivers.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(QueuedJob), nameof(QueuedJob.Cleanup))]
    internal static class Patch_ExplicitHaulQueueCleanup
    {
        static void Prefix(QueuedJob __instance, Pawn pawn)
        {
            var o = pawn?.GetComp<CompHauledToInventory>()?.ExplicitOrder(__instance.job);
            if (o == null || o.Terminal) return;
            if (ExplicitHaulLifecycle.Capturing(pawn)) o.state = ExplicitHaulState.Captured;
            else { o.state = ExplicitHaulState.Cancelled; o.reason = "Queued order removed"; }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.CaptureAndClearJobQueue))]
    internal static class Patch_ExplicitHaulCaptureQueue
    {
        static void Prefix(Pawn ___pawn, out ExplicitHaulLifecycle.Capture __state)
        {
            __state = new ExplicitHaulLifecycle.Capture { pawn = ___pawn, previous = ExplicitHaulLifecycle.capture };
            ExplicitHaulLifecycle.capture = __state;
        }
        static Exception Finalizer(Exception __exception, ExplicitHaulLifecycle.Capture __state)
        { if (__state != null) ExplicitHaulLifecycle.capture = __state.previous; return __exception; }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.RestoreCapturedJobs))]
    internal static class Patch_ExplicitHaulRestoreQueue
    {
        static void Prefix(Pawn ___pawn, JobQueue incomming, out ExplicitHaulOrder[] __state)
            => __state = incomming.Select(q => ___pawn.GetComp<CompHauledToInventory>()?.ExplicitOrder(q.job))
                .Where(o => o != null).Distinct().ToArray();
        static Exception Finalizer(Pawn ___pawn, Exception __exception, ExplicitHaulOrder[] __state)
        {
            foreach (var o in __state ?? Array.Empty<ExplicitHaulOrder>())
            {
                if (o.Terminal) continue;
                bool present = ExplicitHaulCommand.IsJob(___pawn.CurJob) && ___pawn.CurJob.loadID == o.jobId
                    || ___pawn.jobs.jobQueue.Any(q => ExplicitHaulCommand.IsJob(q?.job) && q.job.loadID == o.jobId);
                if (!present) { o.Block("Captured job was not restored"); o.jobId = -1; }
                else if (o.state == ExplicitHaulState.Captured) o.state = ExplicitHaulState.Queued;
            }
            return __exception;
        }
    }
}
