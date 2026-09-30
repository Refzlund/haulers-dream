using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Shared admission and click-time validation for the nearby menu and Misc. Robots targeter.
    /// Queries never build jobs, reserve items or alter a robot's work assignments. An explicit order
    /// retains the existing override of the automatic master/per-pawn preference switches.
    /// </summary>
    internal static class NearbyHaulCommand
    {
        [System.ThreadStatic] private static Pawn queuedPawn;
        [System.ThreadStatic] private static Job queuedJob;

        internal static bool PreservesQueue(Pawn pawn, Job job)
            => job != null && ReferenceEquals(pawn, queuedPawn) && ReferenceEquals(job, queuedJob);

        internal static WorkGiverDef Definition => HaulersDreamDefOf.HaulersDream_HaulNearby;

        internal static bool IsIdentifiedOrder(Job job) => job != null && job.playerForced
            && Definition != null && ReferenceEquals(job.workGiverDef, Definition)
            && (job.def == HaulersDreamDefOf.HaulersDream_BulkHaul
                || job.def == HaulersDreamDefOf.HaulersDream_NearbyDelivery
                || job.def == JobDefOf.HaulToCell || job.def == JobDefOf.HaulToContainer);

        internal static bool CanContinue(Pawn pawn, Job job) => IsIdentifiedOrder(job)
            && pawn?.Map != null && job.globalTarget.Map == pawn.Map
            && PawnBlockReason(pawn) == null
            && (!NearbyHaulDelivery.Applies(job) || NearbyHaulDelivery.ValidManifest(job));

        internal static bool Enabled => Definition?.directOrderable == true
            && HaulersDreamMod.Settings?.bulkHaul == true
            && HaulersDreamMod.Settings.haulNearbyOption;

        internal static string PawnBlockReason(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null
                || !pawn.Position.InBounds(pawn.Map) || pawn.Downed || pawn.InMentalState
                || pawn.CarriedBy != null || pawn.jobs == null)
                return "HD_NearbyHaulPawnUnavailable".Translate();

            var role = MiscRobotsStorageRole.Query(pawn);
            bool miscRobot = role.Role != MiscRobotStorageRole.Unrelated;
            // The custom robots support base-game orders through their own gizmos. This grants only
            // this HD action and never changes native CanTakeOrder or another provider's selection.
            if (pawn.Faction != Faction.OfPlayerSilentFail || pawn.Faction == null || pawn.IsQuestLodger()
                || (!pawn.CanTakeOrder && (!miscRobot || pawn.HostFaction != null)))
                return "HD_NearbyHaulPawnUnavailable".Translate();
            if (pawn.Drafted && Definition?.canBeDoneWhileDrafted != true)
                return "HD_NearbyHaulDrafted".Translate();
            if (!Enabled)
                return "Disabled".Translate() + ": " + "HaulersDream.Setting.HaulNearbyOption".Translate();
            if (!MapGate.HdActiveOnMap(pawn.Map))
                return "HD_NearbyHaulMapDisabled".Translate();
            if (!MultiplayerCompat.NearbyHaulAvailable)
                return "HD_NearbyHaulSyncUnavailable".Translate();
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory?.innerContainer == null
                || pawn.carryTracker == null)
                return "HD_NearbyHaulInventoryUnavailable".Translate();
            if (role.Role == MiscRobotStorageRole.Unsupported)
                return "HD_NearbyHaulRobotUnsupported".Translate();
            if (role.Role == MiscRobotStorageRole.Unassigned)
                return "CannotPrioritizeNotAssignedToWorkType".Translate(WorkTypeDefOf.Hauling.gerundLabel);

            var block = HaulOrderGate.BlockFor(pawn);
            if (block == HaulOrderBlock.Manipulation)
                return "IncapableOfCapacity".Translate(PawnCapacityDefOf.Manipulation.label);
            if (block == HaulOrderBlock.HaulingDisabled)
                return "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Hauling.gerundLabel);
            if (pawn.RaceProps?.IsMechanoid == true)
            {
                if (!HaulersDreamMod.Settings.allowMechanoids)
                    return "Disabled".Translate() + ": " + "HaulersDream.Setting.AllowMechanoids".Translate();
                // allowIncapable is a human work override, not permission to give combat mechs or
                // other specialists new work. Misc. Robots additionally uses the live role list above.
                if (WorkCapabilityProbe.IsDisabled(pawn, WorkTypeDefOf.Hauling)
                    || (!miscRobot && pawn.RaceProps.mechEnabledWorkTypes?.Contains(WorkTypeDefOf.Hauling) != true))
                    return "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Hauling.gerundLabel);
            }
            return null;
        }

        internal static bool IsGroundTarget(Pawn pawn, Thing thing)
            => pawn?.Map != null && thing?.def != null && !thing.Destroyed && thing.Spawned
                && thing.Map == pawn.Map && thing.Position.InBounds(pawn.Map) && thing.stackCount > 0
                && thing.def.category == ThingCategory.Item && thing.def.EverHaulable;

        internal static bool CanOffer(Pawn pawn, Thing thing, out string reason)
        {
            using var observational = StorageProgressWork.ObserveOnly();
            reason = PawnBlockReason(pawn);
            if (reason != null)
                return false;
            // Check the source/map before native code, which dereferences spawned map and position.
            if (!IsGroundTarget(pawn, thing) || !HaulAIUtility.PawnCanAutomaticallyHaul(pawn, thing, forced: true))
            {
                reason = "HD_NearbyHaulSourceUnavailable".Translate();
                return false;
            }
            bool hasStorage;
            // Match explicit HaulToStorageJob permission, without creating a Job. The balanced
            // scope preserves nested callers; false avoids native accurate-search RNG in local UI.
            StorageCommitments.PushForcedOrder();
            try
            {
                hasStorage = StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map,
                    StoreUtility.CurrentStoragePriorityOf(thing, true), pawn.Faction,
                    out _, out _, needAccurateResult: false);
            }
            finally
            {
                StorageCommitments.PopForcedOrder();
            }
            if (!hasStorage)
            {
                reason = "NoEmptyPlaceLower".Translate();
                return false;
            }
            return true;
        }

        private static bool LocalContextValid(Pawn pawn, int expectedMapId)
            => MultiplayerCompat.NearbyHaulLocalUi && pawn?.Map != null
                && pawn.Map.uniqueID == expectedMapId && pawn.Map == Find.CurrentMap
                && Find.Selector.SingleSelectedThing == pawn;

        // MP serializes arguments before entering the registered method. Check local actor/map/source
        // here too, so a retained cross-map menu action cannot fail inside the optional serializer.
        internal static void Dispatch(Pawn pawn, Thing source, int expectedMapId, bool queue)
        {
            if (!MultiplayerCompat.NearbyHaulLocalUi)
                return;
            if (!LocalContextValid(pawn, expectedMapId))
            {
                RejectLocal("HD_NearbyHaulPawnUnavailable".Translate());
                return;
            }
            if (!CanOffer(pawn, source, out var reason))
            {
                RejectLocal(reason);
                return;
            }
            // Retain the map/custody barrier immediately before optional argument serialization.
            if (!LocalContextValid(pawn, expectedMapId) || !IsGroundTarget(pawn, source))
            {
                RejectLocal("HD_NearbyHaulSourceUnavailable".Translate());
                return;
            }
            IssueSynced(pawn, source, expectedMapId, queue);
        }

        // Registered by name behind the MP API isolation boundary; no optional API attribute.
        public static void IssueSynced(Pawn pawn, Thing source, int expectedMapId, bool queue)
        {
            if (!MultiplayerCompat.NearbyHaulExecuting)
                return;
            if (pawn?.Map == null || pawn.Map.uniqueID != expectedMapId)
            {
                Reject("HD_NearbyHaulSourceUnavailable".Translate());
                return;
            }
            if (!CanOffer(pawn, source, out var reason))
            {
                Reject(reason);
                return;
            }
            // Both the sweep and legitimate single-stack/capacity fallback are behind the same
            // fresh permission check. A disabled robot role/feature can never fall through to vanilla.
            Job job = (Definition?.Worker as WorkGiver_HaulNearby)?.BuildCommandJob(pawn, source);
            if (job == null)
            {
                Reject("HaulersDream.HaulNearby.CouldNotStart".Translate());
                return;
            }
            job.playerForced = true;
            job.workGiverDef = Definition;
            job.globalTarget = new GlobalTargetInfo(pawn.Position, pawn.Map);
            var previousPawn = queuedPawn;
            var previousJob = queuedJob;
            queuedPawn = queue ? pawn : null;
            queuedJob = queue ? job : null;
            try
            {
                if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing: queue))
                    Reject("HaulersDream.HaulNearby.CouldNotStart".Translate());
            }
            finally
            {
                queuedPawn = previousPawn;
                queuedJob = previousJob;
            }
        }

        internal static Gizmo RobotGizmo(Pawn pawn, Texture2D icon)
        {
            // One actor per targeter, matching the menu's existing single-selection contract.
            if (!Enabled || !MultiplayerCompat.NearbyHaulLocalUi || Find.Selector.SingleSelectedThing != pawn
                || pawn?.Faction == null || pawn.Faction != Faction.OfPlayerSilentFail
                || MiscRobotsStorageRole.Query(pawn).Role == MiscRobotStorageRole.Unrelated)
                return null;
            int mapId = pawn.Map?.uniqueID ?? -1;
            var command = new Command_Action
            {
                defaultLabel = "HaulersDream.HaulNearby.Option".Translate(),
                defaultDesc = "HD_NearbyHaulGizmoDesc".Translate(),
                icon = icon,
                Order = float.MaxValue,
                action = () => BeginRobotTargeting(pawn, mapId)
            };
            string reason = PawnBlockReason(pawn);
            if (reason != null)
                command.Disable(reason);
            return command;
        }

        private static void BeginRobotTargeting(Pawn pawn, int expectedMapId)
        {
            if (!MultiplayerCompat.NearbyHaulLocalUi)
                return;
            if (!LocalContextValid(pawn, expectedMapId))
            {
                RejectLocal("HD_NearbyHaulPawnUnavailable".Translate());
                return;
            }
            string reason = PawnBlockReason(pawn);
            if (reason != null)
            {
                RejectLocal(reason);
                return;
            }
            var parameters = new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetPlants = false,
                canTargetItems = true,
                canTargetLocations = false,
                canTargetCorpses = true,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = target => LocalContextValid(pawn, expectedMapId)
                    && IsGroundTarget(pawn, target.Thing) && CanOffer(pawn, target.Thing, out _)
            };
            Find.DesignatorManager.Deselect();
            Find.Targeter.BeginTargeting(parameters,
                target => Dispatch(pawn, target.Thing, expectedMapId, KeyBindingDefOf.QueueOrder.IsDownEvent), pawn);
        }

        private static void RejectLocal(string reason)
            => Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);

        private static void Reject(string reason)
        {
            if (!MultiplayerCompat.InMultiplayerGame || MultiplayerCompat.ShouldShowLocalFeedback)
                Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
