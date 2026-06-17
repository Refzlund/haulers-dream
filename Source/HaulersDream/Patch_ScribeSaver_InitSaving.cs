using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// A1 — Pre-save job cleanup (safe uninstall). BLFT-parity (faithful source:
    /// <c>SafeUnloadManager.CleanupBeforeSaving</c> + <c>ScribeSaver_InitSaving_Patch</c>).
    ///
    /// A save written while a pawn is mid-bulk-load otherwise embeds live references to HD's custom
    /// <c>JobDriver</c>s. If HD is then uninstalled, those driver types vanish and the save can't deserialize the
    /// dangling jobs. This PREFIX on <c>ScribeSaver.InitSaving</c> (which runs immediately before the game writes
    /// any save document) swaps every HD-driver job for vanilla <c>Wait</c> and strips queued HD jobs, so the
    /// written save contains no HD-driver references.
    ///
    /// Safety guards (mirror SafeUnloadManager):
    ///   • Abort off the MAIN THREAD — <c>InitSaving</c> can be reached from RimWorld's background autosave thread,
    ///     where touching job/pawn state is unsafe. We only clean on the main thread; a background-thread save will
    ///     simply persist the in-flight HD jobs, which is harmless while HD remains installed (they load back fine)
    ///     and is the same trade-off BLFT accepts.
    ///   • Abort if <see cref="HaulersDreamMod.Settings"/> is null (mod not fully initialized) or the
    ///     <c>cleanupOnSave</c> toggle is off → byte-inert.
    ///
    /// How the swap is safe + idempotent with the existing claim ledger:
    ///   • Swapping the current job is done via <c>TryTakeOrderedJob(Wait)</c>, exactly as BLFT. Internally that
    ///     ends the outgoing job with <c>JobCondition.InterruptForced</c> (verified in the decompiled 1.6
    ///     <c>Pawn_JobTracker.TryTakeOrderedJob</c>: <c>curDriver.EndJobWith(JobCondition.InterruptForced)</c>),
    ///     which routes through <c>EndCurrentJob</c>. So HD's existing defense-in-depth release
    ///     (<see cref="Patch_Pawn_JobTracker_EndCurrentJob_ReleaseClaim"/>) fires for the bulk-LOAD defs and returns
    ///     the pawn's <c>Core.LoadLedger</c> claims — no quota leak. <c>LoadLedger.Release</c> clamps ≥0 and drops
    ///     the pawn, so this is idempotent: if the job's own finish action already released, the re-release is a
    ///     no-op.
    ///   • Queued HD jobs are removed via <c>JobQueue.RemoveAll(pawn, predicate)</c>, which calls
    ///     <c>QueuedJob.Cleanup</c> per removed job (releases its pre-toil reservations) — the proper queue-removal
    ///     path, not a raw list edit.
    ///   • Swept/tagged stock stays in the pawn's inventory (tagged via <see cref="CompHauledToInventory"/>); we do
    ///     NOT clear the pawn's tags, only the job. After the swap the pawn is idle holding tagged cargo, which
    ///     rides HD's normal storage-unload exactly as it would after any interrupted load — never dropped on a
    ///     temp map, never stuck.
    /// </summary>
    [HarmonyPatch(typeof(ScribeSaver), nameof(ScribeSaver.InitSaving))]
    public static class Patch_ScribeSaver_InitSaving
    {
        static bool Prepare() => HaulersDreamMod.Settings?.cleanupOnSave ?? true;

        // HD JobDefs backed by a custom JobDriver that vanishes with the assembly. Any of these embedded in a save
        // would dangle after an uninstall. Built from the SINGLE canonical custom-driver set shared with the
        // softlock-drop skip-check (HdJobDefSets.CustomDriverJobDefs / plan A2) so the two can never drift.
        // (That set excludes HaulersDream_InventoryDoBill — retired; its def is kept only for save-compat and HD
        // never starts it.)
        private static HashSet<JobDef> _stripSet;

        private static HashSet<JobDef> StripSet =>
            _stripSet ??= new HashSet<JobDef>(HdJobDefSets.CustomDriverJobDefs);

        // No try/catch that swallows real errors: a genuine fault must surface. The two early-outs below
        // (main-thread + settings) are correct guards, not error suppression.
        static void Prefix()
        {
            if (!UnityData.IsInMainThread)
            {
                HDLog.Dbg("ScribeSaver.InitSaving reached off the main thread — skipping pre-save HD job cleanup.");
                return;
            }
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.cleanupOnSave)
                return;
            if (Current.Game == null || Find.Maps == null)
                return;

            var strip = StripSet;
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m]?.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                    continue;
                // Snapshot is unnecessary: swapping/clearing one pawn's jobs does not mutate the map's
                // spawned-pawn list. Indexed read avoids the IReadOnlyList enumerator boxing.
                for (int i = 0; i < pawns.Count; i++)
                    CleanupPawn(pawns[i], strip);
            }
        }

        private static void CleanupPawn(Pawn pawn, HashSet<JobDef> strip)
        {
            if (pawn == null || pawn.Destroyed || pawn.jobs == null)
                return;

            // 1) Strip QUEUED HD jobs (RemoveAll runs QueuedJob.Cleanup → releases their reservations). This is the
            //    load-bearing path for a pawn whose CURRENT job is NOT an HD job (we leave that job running, so the
            //    step-2 swap below never fires and never touches the queue) — it still loses any HD jobs queued
            //    behind it.
            var queue = pawn.jobs.jobQueue;
            if (queue != null && queue.Count > 0)
                queue.RemoveAll(pawn, job => job != null && job.def != null && strip.Contains(job.def));

            // 2) Swap the CURRENT job if it's an HD driver. TryTakeOrderedJob ends the outgoing job with
            //    InterruptForced (→ EndCurrentJob → HD's claim release fires for the bulk-load defs); the pawn is
            //    left idle on Wait. Tagged inventory cargo is untouched and rides the normal unload.
            //    NOTE: TryTakeOrderedJob internally ClearQueuedJobs() — so when the current job IS an HD job the
            //    ENTIRE remaining queue (incl. any non-HD player-queued orders behind it) is cleared, not just the
            //    HD entries. Acceptable for a save-time uninstall-safety net (no corruption; the pawn re-derives
            //    work from its think tree on load) and faithful to BLFT, but worth stating honestly.
            var cur = pawn.CurJob;
            if (cur != null && cur.def != null && strip.Contains(cur.def))
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 2), JobTag.Misc);
        }
    }
}
