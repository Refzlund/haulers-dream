using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Save identity only; never an admission bypass or an authority to pull cargo.</summary>
    internal static class TransporterOperation
    {
        internal static bool IsOperation(Job job) => job != null
            && (job.def == HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk
                || job.def == HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk)
            && job.targetA.Thing?.TryGetComp<RimWorld.CompTransporter>() != null;

        // A saved queued command must not disappear merely because a flag/setting/map/draft state changed.
        // Actual start/transfer admission remains live and may refuse it without moving anything.
        internal static bool IsExplicitOrder(Job job) => IsOperation(job) && job.playerForced;
        internal static bool IsCurrentOperation(Pawn pawn, Job job) => IsOperation(job)
            && pawn?.CurJob == job;
    }
}
