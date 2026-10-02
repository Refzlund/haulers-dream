using System;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    internal static partial class StorageCommitments
    {
        // Lifecycle ownership only; all cargo quantities remain in storageClaims. Never scribed.
        internal sealed class LoadRecoveryTicket
        {
            internal readonly Game Game;
            internal readonly HaulersDreamGameComponent Component;
            internal bool Pending = true, Running;
            internal Exception Failure;
            internal LoadRecoveryTicket(Game game, HaulersDreamGameComponent component)
            { Game = game; Component = component; }
            internal bool Current => ReferenceEquals(activeLoadRecovery, this)
                && ReferenceEquals(Verse.Current.Game, Game)
                && ReferenceEquals(HaulersDreamGameComponent.Instance, Component);
        }

        private static LoadRecoveryTicket activeLoadRecovery;
        internal static bool ResourceLoadPending => UnityData.IsInMainThread
            && activeLoadRecovery?.Pending == true && activeLoadRecovery.Current;

        // A privilege is passed only down the writer's own call chain. Reentrant provider
        // callbacks call the public entries without this ticket and still see Deferred.
        internal static bool ResourceQueriesBlocked(LoadRecoveryTicket ticket = null)
            => ticket != null
                ? !UnityData.IsInMainThread || !ticket.Current || !ticket.Running || !ticket.Pending
                : ResourceLoadPending;

        internal static LoadRecoveryTicket BeginResourceLoad(Game game, HaulersDreamGameComponent component)
            => activeLoadRecovery = new LoadRecoveryTicket(game, component);

        internal static void ScheduleResourceLoad(LoadRecoveryTicket ticket)
            => LongEventHandler.ExecuteWhenFinished(() => CompleteResourceLoad(ticket));

        private static void CompleteResourceLoad(LoadRecoveryTicket ticket)
        {
            if (!UnityData.IsInMainThread || ticket == null || !ticket.Current
                || !ticket.Pending || ticket.Running || ticket.Failure != null) return;
            ticket.Running = true;
            try
            {
                RebuildResourceClaimsForLoad(ticket);
                if (ticket.Current) ticket.Pending = false;
            }
            catch (Exception error)
            {
                if (ticket.Current) ticket.Failure = error;
                throw; // LongEvent/native error handling owns the visible original exception.
            }
            finally { ticket.Running = false; }
        }

        internal static bool BeforeResourceLoadTick()
        {
            var ticket = activeLoadRecovery;
            if (ticket == null || !ticket.Pending) return true;
            if (!ticket.Current) return true;
            // Normal async load completed this in ExecuteWhenFinished. This covers the native
            // immediate/no-long-event callback case before MapPreTick can start any work.
            CompleteResourceLoad(ticket);
            // A visible restoration failure blocks storage admission, not the entire game.
            // The sticky error is not retried on every tick; native-exclusive and unrelated
            // gameplay can continue while public resource queries remain Deferred.
            return true;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class Patch_StorageLoadBeforeTick
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(out System.IDisposable __state)
        {
            __state = StorageProgressWork.Simulation();
            return StorageCommitments.BeforeResourceLoadTick();
        }
        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(System.IDisposable __state) => __state?.Dispose();
    }
}
