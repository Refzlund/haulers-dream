using HarmonyLib;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Migration self-heal (fix/mix): when a save is loaded after a mod that contributed a JobDef + JobDriver is
    /// removed — most commonly a <b>Pick Up And Haul</b> save migrated to Hauler's Dream — a pawn's in-progress
    /// job deserializes with a <b>null def</b> and <b>null driver</b> (the def name no longer resolves; the driver
    /// class is gone). Vanilla's own PostLoadInit cleanup
    /// (<c>Pawn_JobTracker.ExposeData</c> → <c>EndCurrentJob(JobCondition.Errored)</c>) then <b>crashes</b> on it:
    /// <c>EndCurrentJob</c> reads <c>curJob.def.collideWithPawns</c>, which is a NullReferenceException — so the
    /// broken job is never cleared and the pawn re-throws every tick from
    /// <c>PatherTick → Job.MakeDriver</c> (<c>this.def.driverClass</c>).
    ///
    /// <para>This prefix intercepts <see cref="Pawn_JobTracker.EndCurrentJob"/> ONLY for a null-def current job,
    /// performs the minimal <b>def-safe</b> cleanup (release the job's reservations by reference, stop a stale
    /// path, null the fields), and skips the crashing vanilla body. Every other call — <c>curJob == null</c>, or a
    /// normal job with a real def — passes straight through unchanged. It is always-on and self-contained, so it
    /// repairs orphaned jobs from ANY removed mod, not just Pick Up And Haul.</para>
    ///
    /// <para>This is NOT exception suppression: a null-def job is corrupt, unrunnable state RimWorld itself tries
    /// (and fails) to clear — this completes that cleanup correctly instead of crashing. The once-per-load
    /// <c>HaulersDreamGameComponent.RepairOrphanedJobsAfterLoad</c> sweep is the companion that also clears null-def
    /// QUEUED jobs and stranded reservations this prefix never sees.</para>
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_Pawn_JobTracker_EndCurrentJob_NullDefGuard
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> PawnOf =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        static bool Prefix(Pawn_JobTracker __instance)
        {
            var job = __instance.curJob;
            // Only act on an orphaned, defless job. A normal job (def != null) or no job runs vanilla untouched.
            if (job == null || job.def != null)
                return true;

            // Clear it without dereferencing curJob.def (which vanilla EndCurrentJob / CleanupCurrentJob / StopAll
            // all do, and which would NRE). ClearReservationsForJob matches by job REFERENCE, so it is def-safe
            // (and a no-op when the pawn is not yet spawned, e.g. during PostLoadInit — the GameComponent sweep
            // releases those reservations once the map is finalized).
            var pawn = PawnOf(__instance);
            pawn?.ClearReservationsForJob(job);
            pawn?.pather?.StopDead();
            __instance.curDriver = null;
            __instance.curJob = null;
            return false; // skip the vanilla body (it would NRE on curJob.def)
        }
    }
}
