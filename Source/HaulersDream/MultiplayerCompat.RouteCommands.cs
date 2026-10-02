using Multiplayer.API;
using RimWorld;
using Verse;

namespace HaulersDream
{
    public static partial class MultiplayerCompat
    {
        private static bool ordinaryRouteRegistered, sowRouteRegistered, removeFloorRouteRegistered;
        internal static bool CanSendOrdinaryRoute => !InMultiplayerGame || (ordinaryRouteRegistered && (MpHooks.InInterface() || MpHooks.ExecutingCommand()));
        internal static bool OrdinaryRouteExecuting => !InMultiplayerGame || (ordinaryRouteRegistered && MpHooks.ExecutingCommand());
        internal static bool CanSendSowRoute => !InMultiplayerGame || (sowRouteRegistered && (MpHooks.InInterface() || MpHooks.ExecutingCommand()));
        internal static bool SowRouteExecuting => !InMultiplayerGame || (sowRouteRegistered && MpHooks.ExecutingCommand());
        internal static bool CanSendRemoveFloorRoute => !InMultiplayerGame || (removeFloorRouteRegistered && (MpHooks.InInterface() || MpHooks.ExecutingCommand()));
        internal static bool RemoveFloorRouteExecuting => !InMultiplayerGame || (removeFloorRouteRegistered && MpHooks.ExecutingCommand());

        internal static void RejectRouteCommand()
        {
            if (InMultiplayerGame && MpHooks.InInterface())
                Messages.Message("HaulersDream.PlanRoute.SyncUnavailable".Translate(), MessageTypeDefOf.RejectInput, historical: false);
        }

        // Keep optional API types in method bodies only. Startup and other mods inspect method metadata
        // even without Multiplayer loaded. Delegate contravariance lets the serializer accept object here;
        // its SyncWorker cast is only JIT-compiled after the active Multiplayer API registers the worker.
        // Explicit delegate construction also avoids API-typed compiler cache fields.
        private static partial class MpHooks
        {
            internal static bool RegisterOrdinaryRoute()
            {
                MP.RegisterSyncWorker<RouteCommandArgs>(new SyncWorkerDelegate<RouteCommandArgs>(SyncOrdinaryRoute));
                return MP.RegisterSyncMethod(typeof(RouteExecutor), nameof(RouteExecutor.ExecuteRouteCommandSynced)) != null;
            }

            private static void SyncOrdinaryRoute(object worker, ref RouteCommandArgs args)
            {
                var sync = (SyncWorker)worker;
                sync.Bind(ref args.workGiverDefName);
                sync.Bind(ref args.mode);
                sync.Bind(ref args.amount);
                sync.Bind(ref args.radius);
                sync.Bind(ref args.maxDistance);
                sync.Bind(ref args.smart);
                sync.Bind(ref args.allowHarvest);
                sync.Bind(ref args.growthThreshold);
                sync.Bind(ref args.replace);
                sync.Bind(ref args.mustInclude);
                sync.Bind(ref args.selectionMethod);
                sync.Bind(ref args.distanceBasis);
                sync.Bind(ref args.exactMax);
                sync.Bind(ref args.startNode);
                sync.Bind(ref args.endNode);
                sync.Bind(ref args.alsoBuild);
                sync.Bind(ref args.roomAnchors);
                sync.Bind(ref args.extraDefs);
                sync.Bind(ref args.blightedOnly);
            }

            internal static bool RegisterSowRoute()
            {
                MP.RegisterSyncWorker<SowRouteCommandArgs>(new SyncWorkerDelegate<SowRouteCommandArgs>(SyncSowRoute));
                return MP.RegisterSyncMethod(typeof(SowRouteExecutor), nameof(SowRouteExecutor.ExecuteSowRouteCommandSynced)) != null;
            }

            private static void SyncSowRoute(object worker, ref SowRouteCommandArgs args)
            {
                var sync = (SyncWorker)worker;
                sync.Bind(ref args.anchor);
                sync.Bind(ref args.mode);
                sync.Bind(ref args.amount);
                sync.Bind(ref args.radius);
                sync.Bind(ref args.maxDistance);
                sync.Bind(ref args.smart);
                sync.Bind(ref args.replace);
                sync.Bind(ref args.mustInclude);
                sync.Bind(ref args.selectionMethod);
                sync.Bind(ref args.exactMax);
            }

            internal static bool RegisterRemoveFloorRoute()
            {
                MP.RegisterSyncWorker<RemoveFloorRouteCommandArgs>(new SyncWorkerDelegate<RemoveFloorRouteCommandArgs>(SyncRemoveFloorRoute));
                return MP.RegisterSyncMethod(typeof(RemoveFloorRouteExecutor), nameof(RemoveFloorRouteExecutor.ExecuteRemoveFloorRouteCommandSynced)) != null;
            }

            private static void SyncRemoveFloorRoute(object worker, ref RemoveFloorRouteCommandArgs args)
            {
                var sync = (SyncWorker)worker;
                sync.Bind(ref args.anchor);
                sync.Bind(ref args.mode);
                sync.Bind(ref args.amount);
                sync.Bind(ref args.radius);
                sync.Bind(ref args.maxDistance);
                sync.Bind(ref args.replace);
                sync.Bind(ref args.mustInclude);
                sync.Bind(ref args.selectionMethod);
                sync.Bind(ref args.exactMax);
            }

        }
    }
}
