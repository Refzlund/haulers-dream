using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal enum TransporterUnloadBlock { None, Disabled, Unavailable, Drafted, HaulingDisabled, Forbidden, Loading, Empty, NoRoom, NoPath, NoStorage }

    /// <summary>Live, read-only admission shared by the toggle, scanner, order and each physical transfer.
    /// Adapted from nullpat's GH267 contribution; no tick cache authorizes custody changes.</summary>
    internal static class BulkUnloadTransporterGate
    {
        internal static bool Enabled => HaulersDreamMod.Settings?.masterEnabled == true
            && HaulersDreamMod.Settings.enableBulkUnloadTransporters;

        internal static bool IsSupported(CompTransporter comp)
            => comp?.parent != null && comp.parent.Spawned && comp.Map != null && comp.innerContainer != null
                && !VehicleFrameworkCompat.IsVehicle(comp.parent) && !comp.parent.IsInCaravan()
                && (comp.Shuttle == null || comp.Shuttle.ShowLoadingGizmos);

        internal static bool UnloadFlagActive(CompTransporter comp)
            => comp?.parent != null && (HaulersDreamGameComponent.Instance?.BulkUnloadAllActive(comp.parent.thingIDNumber) ?? false);

        internal static bool AnyUnloadFlagInGroup(CompTransporter comp)
        {
            if (!Enabled || comp?.parent == null) return false;
            if (UnloadFlagActive(comp)) return true;
            if (comp.groupID < 0 || comp.Map == null) return false;
            // Consume the native shared list immediately; no nested group query and no retained reference.
            var group = comp.TransportersInGroup(comp.Map);
            for (int i = 0; group != null && i < group.Count; i++)
                if (UnloadFlagActive(group[i])) return true;
            return false;
        }

        internal static bool BlocksLoad(IManagedLoadable loadable)
            => loadable is LoadTransportersAdapter adapter && AnyUnloadFlagInGroup(adapter.Primary);

        internal static bool HasPullableContents(CompTransporter comp)
        {
            var hold = comp?.innerContainer;
            for (int i = 0; hold != null && i < hold.Count; i++)
                if (BulkUnloadPull.IsCargo(hold[i])) return true;
            return false;
        }

        internal static bool HasEligibleContents(Pawn pawn, CompTransporter comp, bool playerOrdered)
        {
            var hold = comp?.innerContainer;
            for (int i = 0; hold != null && i < hold.Count; i++)
                if (BulkUnloadPull.Eligible(hold[i], pawn, !playerOrdered)) return true;
            return false;
        }

        internal static bool ConflictActive(CompTransporter comp)
        {
            if (comp?.parent == null || !comp.parent.Spawned || comp.Map == null || comp.parent.IsInCaravan()) return true;
            var ship = comp.Shuttle?.shipParent;
            if (ship?.curJob is ShipJob_Unload drain && drain.dropMode != TransportShipDropMode.PawnsOnly) return true;
            // Old saves have no session history. An extant manifest is conservatively authoritative;
            // fulfil/cancel it before unloading. Short-circuit also avoids a map-wide courier scan for it.
            if (comp.AnyInGroupHasAnythingLeftToLoad) return true;
            // Never let an empty manifest hide an actual lord, courier or queued deposit.
            bool custody = LoadFlowActive(comp);
            return custody || (HaulersDreamGameComponent.Instance?.TransporterLoadOwns(comp.groupID,
                comp.AnyInGroupHasAnythingLeftToLoad, custody) ?? false);
        }

        internal static bool LoadFlowActive(CompTransporter comp)
        {
            if (comp?.parent == null || !comp.parent.Spawned || comp.Map == null) return false;
            if (comp.groupID >= 0 && TransporterUtility.FindLord(comp.groupID, comp.Map) != null) return true;
            var pawns = comp.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (LoadsGroup(pawn?.CurJob, comp)) return true;
                var queue = pawn?.jobs?.jobQueue;
                for (int j = 0; queue != null && j < queue.Count; j++)
                    if (LoadsGroup(queue[j].job, comp)) return true;
            }
            return false;
        }

        private static bool LoadsGroup(Job job, CompTransporter target)
        {
            if (job == null) return false;
            Thing destination;
            if (job.def == HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk) destination = job.targetA.Thing;
            else if (job.def == JobDefOf.HaulToTransporter) destination = job.targetB.Thing;
            else return false;
            var comp = destination?.TryGetComp<CompTransporter>();
            return comp == target || (comp?.Map == target.Map && target.groupID >= 0 && comp.groupID == target.groupID);
        }

        internal static TransporterUnloadBlock TargetBlock(CompTransporter comp, bool requireFlag)
        {
            if (!Enabled) return TransporterUnloadBlock.Disabled;
            if (!IsSupported(comp)) return TransporterUnloadBlock.Unavailable;
            if (requireFlag && !UnloadFlagActive(comp)) return TransporterUnloadBlock.Disabled;
            if (ConflictActive(comp)) return TransporterUnloadBlock.Loading;
            return TransporterUnloadBlock.None;
        }

        // The per-transfer version does not reapply the initial free-backpack percentage: filling the pack is
        // the job's purpose. Hands must still be empty before a new pull. Drafting ends further pulls.
        internal static TransporterUnloadBlock ActorAndTargetBlock(Pawn pawn, CompTransporter comp, bool playerOrdered)
        {
            if (!Enabled || !UnloadFlagActive(comp)) return TransporterUnloadBlock.Disabled;
            if (!IsSupported(comp)) return TransporterUnloadBlock.Unavailable;
            if (pawn == null || !pawn.Spawned || pawn.Map != comp.Map || pawn.Dead || pawn.Downed || pawn.InMentalState || pawn.jobs == null
                || pawn.Faction != Faction.OfPlayerSilentFail || pawn.IsQuestLodger() || pawn.IsPrisoner
                || pawn.inventory == null || pawn.carryTracker?.innerContainer == null || pawn.carryTracker.innerContainer.Count != 0
                || pawn.GetComp<CompHauledToInventory>() == null) return TransporterUnloadBlock.Unavailable;
            if (pawn.Drafted) return TransporterUnloadBlock.Drafted;
            if (HaulOrderGate.Blocks(pawn) || !MiscRobotsStorageRole.AllowsNewStorageIntake(pawn)) return TransporterUnloadBlock.HaulingDisabled;
            if (pawn.RaceProps?.IsMechanoid == true)
            {
                if (!HaulersDreamMod.Settings.allowMechanoids) return TransporterUnloadBlock.Disabled;
                var role = MiscRobotsStorageRole.Query(pawn);
                if (WorkCapabilityProbe.IsDisabled(pawn, WorkTypeDefOf.Hauling)
                    || (role.Role == MiscRobotStorageRole.Unrelated && pawn.RaceProps.mechEnabledWorkTypes?.Contains(WorkTypeDefOf.Hauling) != true))
                    return TransporterUnloadBlock.HaulingDisabled;
            }
            if (!playerOrdered && (!YieldRouter.IsEligible(pawn) || comp.parent.Faction != Faction.OfPlayerSilentFail))
                return TransporterUnloadBlock.Unavailable;
            if (!playerOrdered && comp.parent.IsForbidden(pawn)) return TransporterUnloadBlock.Forbidden;
            // Only eligible actors on supported flagged holds pay for the live custody scan.
            return TargetBlock(comp, requireFlag: true);
        }

        internal static TransporterUnloadBlock StartBlock(Pawn pawn, CompTransporter comp, bool playerOrdered)
        {
            if (pawn == null || comp == null) return TransporterUnloadBlock.Unavailable;
            // Most scanner candidates are unflagged or empty: do not scan all pawn queues for them.
            if (!Enabled || !UnloadFlagActive(comp)) return TransporterUnloadBlock.Disabled;
            if (!HasEligibleContents(pawn, comp, playerOrdered)) return TransporterUnloadBlock.Empty;
            var block = ActorAndTargetBlock(pawn, comp, playerOrdered);
            if (block != TransporterUnloadBlock.None) return block;
            if (!playerOrdered && !Core.BulkUnloadCarrierPolicy.HasEnoughBackpackRoom(MassUtility.EncumbrancePercent(pawn),
                HaulersDreamMod.Settings.minFreeSpaceToUnloadCarrierPct)) return TransporterUnloadBlock.NoRoom;
            if (!pawn.CanReach(comp.parent, PathEndMode.Touch, Danger.Deadly)) return TransporterUnloadBlock.NoPath;
            // Otherwise a full store could repeatedly dispatch zero-progress automatic visits to the same hold.
            var contents = comp.innerContainer;
            for (int i = 0; i < contents.Count; i++)
                if (BulkUnloadPull.Eligible(contents[i], pawn, !playerOrdered) && HasStorage(pawn, contents[i]))
                    return TransporterUnloadBlock.None;
            return TransporterUnloadBlock.NoStorage;
        }

        internal static bool HasStorage(Pawn pawn, Thing cargo)
        {
            if (cargo == null || pawn?.Map == null) return false;
            using (StorageBuildingFilter.PushContext(Core.StorageFilterContext.Unload))
            {
                // This also runs in menus/scanners. The inaccurate probe is sufficient for availability
                // and consumes no Rand; the actual in-tick destination plan remains a fresh native search.
                if (!StoreUtility.TryFindBestBetterStorageFor(cargo, pawn, pawn.Map, StoragePriority.Unstored,
                    pawn.Faction, out var cell, out var destination, needAccurateResult: false)) return false;
                return cell.IsValid || (destination is Thing building && building.TryGetInnerInteractableThingOwner() != null);
            }
        }

        /// <summary>The driver calls this after its per-stack wait. A stale selection never grants permission
        /// to move goods: feature/flag, actor, draft state, forbidden policy and current load custody are live.</summary>
        internal static int Transfer(Pawn pawn, CompTransporter comp, Thing selected, int count, bool toHands,
            bool playerOrdered, out Thing movedThing, out bool movedToHands)
        {
            movedThing = null; movedToHands = toHands;
            if (ActorAndTargetBlock(pawn, comp, playerOrdered) != TransporterUnloadBlock.None) return 0;
            int moved = BulkUnloadPull.Transfer(pawn, comp.innerContainer, selected, count, toHands,
                respectForbidden: !playerOrdered, playerOrdered, out movedThing, out movedToHands);
            if (moved > 0) comp.Notify_ThingRemoved(selected);
            return moved;
        }
    }
}
