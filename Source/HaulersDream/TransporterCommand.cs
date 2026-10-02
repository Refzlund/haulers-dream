using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Local read-only offers and synchronized, freshly validated transporter mutations.</summary>
    internal static class TransporterCommand
    {
        internal static string UnloadReason(TransporterUnloadBlock block)
        {
            switch (block)
            {
                case TransporterUnloadBlock.None: return null;
                case TransporterUnloadBlock.Drafted: return "HaulersDream.UnloadTransporter.Drafted".Translate();
                case TransporterUnloadBlock.NoStorage: return "HaulersDream.UnloadTransporter.NoStorage".Translate();
                case TransporterUnloadBlock.Empty: return "HaulersDream.Gizmo.BulkUnloadAll.Empty".Translate();
                case TransporterUnloadBlock.Loading: return "HaulersDream.Gizmo.BulkUnloadAll.UnloadBlocked".Translate();
                case TransporterUnloadBlock.NoPath: return "NoPath".Translate();
                case TransporterUnloadBlock.HaulingDisabled: return "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Hauling.gerundLabel);
                case TransporterUnloadBlock.Disabled: return "Disabled".Translate();
                default: return "HaulersDream.UnloadTransporter.CouldNotStart".Translate();
            }
        }

        internal static string UnloadBlock(Pawn pawn, Thing target)
        {
            if (!MultiplayerCompat.TransporterAvailable) return "HaulersDream.Transporter.SyncUnavailable".Translate();
            return UnloadReason(BulkUnloadTransporterGate.StartBlock(pawn, target?.TryGetComp<CompTransporter>(), true));
        }

        internal static string LoadBlock(Pawn pawn, Thing target, bool probePlan = true)
        {
            if (!MultiplayerCompat.TransporterAvailable) return "HaulersDream.Transporter.SyncUnavailable".Translate();
            var settings = HaulersDreamMod.Settings;
            if (settings?.masterEnabled != true || !settings.enableBulkLoadTransporters) return "Disabled".Translate();
            var comp = target?.TryGetComp<CompTransporter>();
            if (comp == null || pawn == null || pawn.Dead || pawn.Downed || pawn.InMentalState || !pawn.Spawned || pawn.jobs == null
                || pawn.Faction != Faction.OfPlayerSilentFail || pawn.IsQuestLodger() || pawn.IsPrisoner
                || pawn.inventory == null || pawn.GetComp<CompHauledToInventory>() == null
                || !target.Spawned || target.Map != pawn.Map || VehicleFrameworkCompat.IsVehicle(target)
                || pawn.mindState?.duty?.def == DutyDefOf.LoadAndEnterTransporters
                || !MiscRobotsStorageRole.AllowsNewStorageIntake(pawn)) return "HaulersDream.LoadTransporter.CouldNotStart".Translate();
            if (HaulOrderGate.Blocks(pawn)) return "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Hauling.gerundLabel);
            if (BulkUnloadTransporterGate.AnyUnloadFlagInGroup(comp)) return "HaulersDream.Gizmo.BulkUnloadAll.LoadBlocked".Translate();
            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly)) return "NoPath".Translate();
            if (!comp.AnyInGroupHasAnythingLeftToLoad) return "HaulersDream.LoadTransporter.CouldNotStart".Translate();
            if (!probePlan) return null; // synchronized execution builds a fresh plan below, never trusts a menu memo
            var adapter = LoadTransportersAdapter.TryCreate(comp);
            return adapter != null && TransportLoad.WouldGiveBulkJobForMenu(pawn, adapter)
                ? null : "HaulersDream.LoadTransporter.CouldNotStart".Translate();
        }

        internal static void Dispatch(Pawn pawn, Thing target, int mapId, bool unload, bool queue, bool continuous = false)
        {
            if (!MultiplayerCompat.TransporterLocalUi || !MultiplayerCompat.TransporterAvailable) return;
            IssueSynced(pawn, target, mapId, unload, queue, continuous);
        }

        public static void IssueSynced(Pawn pawn, Thing target, int expectedMapId, bool unload, bool queue, bool continuous)
        {
            if (!MultiplayerCompat.TransporterExecuting) return;
            if (pawn?.Map == null || pawn.Map.uniqueID != expectedMapId || target?.Map != pawn.Map)
            { Reject("HaulersDream.LoadTransporter.CouldNotStart".Translate()); return; }
            if (continuous && (HaulersDreamMod.Settings?.enableContinuousLoading != true || pawn.Drafted))
            {
                Reject(pawn.Drafted ? "HaulersDream.UnloadTransporter.Drafted".Translate() : "Disabled".Translate());
                return;
            }
            string reason = unload ? UnloadBlock(pawn, target) : LoadBlock(pawn, target, probePlan: false);
            if (reason != null) { Reject(reason); return; }
            Job job = unload
                ? JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk, target)
                : TransportLoad.TryGiveBulkJob(pawn, LoadTransportersAdapter.TryCreate(target.TryGetComp<CompTransporter>()), playerOrder: true);
            if (job == null) { Reject("HaulersDream.LoadTransporter.CouldNotStart".Translate()); return; }
            job.playerForced = true;
            job.globalTarget = new GlobalTargetInfo(pawn.Position, pawn.Map);
            if (unload) job.workGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulersDream_BulkUnloadTransporters");
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing: queue))
                Reject((unload ? "HaulersDream.UnloadTransporter.CouldNotStart" : "HaulersDream.LoadTransporter.CouldNotStart").Translate());
        }

        public static void SetUnloadSynced(Thing target, int expectedMapId, bool on)
        {
            if (!MultiplayerCompat.TransporterExecuting || target == null) return;
            // Off only removes HD intent, and remains possible after departure or a disabled setting.
            if (on && (target.Map == null || target.Map.uniqueID != expectedMapId)) return;
            if (!(HaulersDreamGameComponent.Instance?.TrySetBulkUnloadAll(target, on) ?? false))
                Reject("HaulersDream.Gizmo.BulkUnloadAll.UnloadBlocked".Translate());
        }

        private static void Reject(string reason)
        {
            if (!MultiplayerCompat.InMultiplayerGame || MultiplayerCompat.ShouldShowLocalFeedback)
                Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
