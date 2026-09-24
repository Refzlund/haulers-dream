using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Editable forced command. The ordinary work scan never supplies work; the existing shared
    /// command alone enters its job factory after local and synchronized admission have succeeded.
    /// </summary>
    public sealed class WorkGiver_HaulNearby : WorkGiver_Scanner
    {
        [ThreadStatic] private static Pawn commandPawn;
        [ThreadStatic] private static Thing commandSource;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
            => !forced || NearbyHaulCommand.PawnBlockReason(pawn) != null;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
            => forced && NearbyHaulCommand.CanOffer(pawn, t, out _);

        internal static bool AllowsDraftedSweep(Pawn pawn, Thing source)
            => pawn != null && source != null && pawn.Drafted
                && ReferenceEquals(pawn, commandPawn) && ReferenceEquals(source, commandSource)
                && MultiplayerCompat.NearbyHaulExecuting
                && NearbyHaulCommand.PawnBlockReason(pawn) == null
                && YieldRouter.IsRaceEligible(pawn) && !PawnUnloadChecker.InDirectedActivity(pawn);

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // Native menu generation normally calls this method in local UI. An edited definition,
            // third-party query or automatic scanner must not turn that query into a Job allocation.
            if (!forced || pawn == null || t == null
                || !ReferenceEquals(pawn, commandPawn) || !ReferenceEquals(t, commandSource)
                || !MultiplayerCompat.NearbyHaulExecuting)
                return null;
            return BulkHaul.BuildBulkJobForced(pawn, t)
                ?? HaulAIUtility.HaulToStorageJob(pawn, t, forced: true);
        }

        internal Job BuildCommandJob(Pawn pawn, Thing source)
        {
            var previousPawn = commandPawn;
            var previousSource = commandSource;
            commandPawn = pawn;
            commandSource = source;
            try { return JobOnThing(pawn, source, forced: true); }
            finally
            {
                commandPawn = previousPawn;
                commandSource = previousSource;
            }
        }
    }

    // Only our exact definition is served by the existing custom provider. This avoids native
    // per-target duplicate queries/options and native local Job construction. Every other giver,
    // including ordinary hauling, keeps the native path. A missing patch still cannot allocate an
    // HD job through JobOnThing: its command scope above remains closed to local UI callers.
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption",
        new Type[] { typeof(Pawn), typeof(WorkGiverDef), typeof(LocalTargetInfo), typeof(FloatMenuContext) })]
    internal static class Patch_NearbyWorkGiverMenu
    {
        private static bool Prefix(WorkGiverDef workGiver, ref FloatMenuOption __result)
        {
            if (workGiver == null || !ReferenceEquals(workGiver, NearbyHaulCommand.Definition))
                return true;
            __result = null;
            return false;
        }
    }

    // Native ordered-job deduplication compares targets, not command authority. An ordinary
    // automatic/single-haul job cannot stand in for a newly authorized nearby command, even when
    // their target fields match. Same-authority orders retain native equality and queue behavior.
    [HarmonyPatch(typeof(Job), nameof(Job.JobIsSameAs), new Type[] { typeof(Pawn), typeof(Job) })]
    internal static class Patch_NearbyWorkGiverIdentity
    {
        private static bool Prefix(Job __instance, Job other, ref bool __result)
        {
            if (!NearbyHaulCommand.IsIdentifiedOrder(other)
                || NearbyHaulCommand.IsIdentifiedOrder(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    // Click-time native reservation entry. StartJob itself calls its driver's reservation method;
    // queued admission is separately guarded at CanBeginNow below, not inferred from this wrapper.
    [HarmonyPatch(typeof(Job), nameof(Job.TryMakePreToilReservations),
        new Type[] { typeof(Pawn), typeof(bool) })]
    internal static class Patch_NearbyWorkGiverReservations
    {
        private static bool Prefix(Job __instance, Pawn __0, ref bool __result)
        {
            if (!NearbyHaulCommand.IsIdentifiedOrder(__instance)
                || NearbyHaulCommand.CanContinue(__0, __instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Job), nameof(Job.CanBeginNow), new Type[] { typeof(Pawn), typeof(bool) })]
    internal static class Patch_NearbyWorkGiverQueuedAdmission
    {
        private static bool Prefix(Job __instance, Pawn __0, ref bool __result)
        {
            if (!NearbyHaulCommand.IsIdentifiedOrder(__instance)
                || NearbyHaulCommand.CanContinue(__0, __instance))
                return true;
            __result = false;
            return false;
        }
    }
}
