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
    /// A save written while a pawn has QUEUED HD jobs otherwise embeds references to HD's custom <c>JobDriver</c>s
    /// that dangle if HD is later uninstalled. This PREFIX on <c>ScribeSaver.InitSaving</c> (which runs immediately
    /// before the game writes any save document) strips QUEUED HD jobs so the written save carries no dangling
    /// queued HD-driver references. (It deliberately leaves the CURRENT job alone — see the torn-snapshot note
    /// below — so a save taken mid-bulk-load still embeds that one running HD job; that deserializes fine while HD
    /// is installed and is released on the job's next normal end.)
    ///
    /// Safety guards (mirror SafeUnloadManager):
    ///   • Abort off the MAIN THREAD — <c>InitSaving</c> can be reached from RimWorld's background autosave thread,
    ///     where touching job/pawn state is unsafe. We only clean on the main thread; a background-thread save will
    ///     simply persist the in-flight HD jobs, which is harmless while HD remains installed (they load back fine)
    ///     and is the same trade-off BLFT accepts.
    ///   • Abort if <see cref="HaulersDreamMod.Settings"/> is null (mod not fully initialized) or the
    ///     <c>cleanupOnSave</c> toggle is off → byte-inert.
    ///
    /// Why we strip QUEUED HD jobs but DO NOT interrupt the CURRENT one (fix/mix — corrects a torn-snapshot bug):
    ///   • Queued HD jobs are removed via <c>JobQueue.RemoveAll(pawn, predicate)</c>, which calls
    ///     <c>QueuedJob.Cleanup</c> per removed job (releases its pre-toil reservations) — the proper queue-removal
    ///     path, not a raw list edit. A queued job has run NO toils, so removing it has no side effects beyond
    ///     releasing those reservations: safe to do at save time.
    ///   • We NO LONGER swap the CURRENT job via <c>TryTakeOrderedJob(Wait)</c>. That call ended the running job
    ///     with <c>JobCondition.InterruptForced</c> → <c>EndCurrentJob</c>, which fires the bulk-load drivers'
    ///     finish actions AND HD's claim-release (<c>Core.LoadLedger</c> mutations) — RIGHT IN THE MIDDLE of
    ///     <c>ScribeSaver.InitSaving</c>, i.e. while the save document is being written. Those releases mutate the
    ///     very ledger object <see cref="HaulersDreamGameComponent.ExposeData"/> serializes, producing a TORN
    ///     SNAPSHOT: on reload the persisted ledger holds phantom claims for a job that was Wait-swapped away, and
    ///     those phantom claims make the bulk-haul / load planners believe quota is already taken — so colony-wide
    ///     hauling, corpse/loot pickup and pollution cleanup quietly stop. (That matched the reported recovery:
    ///     loading WITHOUT HD discards every HD-scribed node, and a re-save writes a clean slate.)
    ///   • Dropping the interruption is SAFE for the uninstall case it was meant to guard: a save written while a
    ///     pawn is mid-HD-job embeds that HD-driver reference, which deserializes fine WHILE HD is installed; only a
    ///     SUBSEQUENT uninstall could dangle it, and the in-flight job's own <c>EndCurrentJob</c>/<c>DeSpawn</c>
    ///     lifecycle hooks release its claims on the next normal end either way. We accept that narrow uninstall-
    ///     mid-bulk-load edge (the player can let pawns idle before uninstalling) rather than corrupt EVERY save
    ///     taken while any pawn happens to be mid-load.
    ///   • Swept/tagged stock stays in the pawn's inventory (tagged via <see cref="CompHauledToInventory"/>) and
    ///     rides HD's normal storage-unload, exactly as before.
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

            // Strip QUEUED HD jobs only (RemoveAll runs QueuedJob.Cleanup → releases their pre-toil reservations).
            // A queued job has run no toils, so this is a pure, side-effect-free queue edit — safe at save time.
            // We deliberately do NOT touch the CURRENT job: interrupting it here ran HD's finish actions + ledger
            // releases mid-serialization and tore the saved ledger snapshot (see the class doc). The running HD job
            // is left intact; it serializes fine while HD is installed and releases its own claims on its next
            // normal end.
            var queue = pawn.jobs.jobQueue;
            if (queue != null && queue.Count > 0)
                queue.RemoveAll(pawn, job => job != null && job.def != null && strip.Contains(job.def));
        }
    }
}
