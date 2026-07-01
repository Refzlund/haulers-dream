using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Verse adapter over <see cref="ProtectedWorkPolicy"/> plus the "resting patient" predicate. These are the two
    /// guards that keep Hauler's Dream's automatic hauling/unloading from interfering with medical care:
    /// <list type="bullet">
    ///   <item><see cref="IsProtected"/> — the chosen work job is doctoring / rescue / firefighting (or any
    ///   emergency work), which must never be delayed by an opportunistic unload/haul divert.</item>
    ///   <item><see cref="IsRestingPatient"/> — the pawn should be lying in bed for medical care, so HD must never
    ///   hand it an automatic haul/unload job (that would yank it upright, the medical think tree lays it back
    ///   down, and it thrashes).</item>
    /// </list>
    /// Both are pure reads of state every multiplayer client already shares (deterministic, no side effects).
    /// </summary>
    public static class ProtectedWork
    {
        /// <summary>
        /// True if <paramref name="job"/> is protected work HD must not divert the pawn away from. Classified by the
        /// issuing work node's emergency flag plus the job's own <c>WorkGiverDef</c> emergency flag / worktype.
        /// </summary>
        /// <param name="job">The work job the vanilla scan chose.</param>
        /// <param name="nodeIsEmergency">The issuing <c>JobGiver_Work.emergency</c> flag (pass false when the caller
        /// doesn't have the node, e.g. a plain <c>ShouldDivert</c> call — the workgiver signals still catch it).</param>
        public static bool IsProtected(Job job, bool nodeIsEmergency)
        {
            var wg = job?.workGiverDef;
            return ProtectedWorkPolicy.IsProtectedWork(nodeIsEmergency, wg?.emergency ?? false, wg?.workType?.defName);
        }

        /// <summary>
        /// True if <paramref name="pawn"/> is (or should be) resting in bed for medical care, so HD must not give it
        /// an automatic haul/unload/self-fetch job. <see cref="HealthAIUtility.ShouldSeekMedicalRest"/> is the same
        /// predicate <c>JobGiver_PatientGoToBed</c> uses, so it stays true across the whole tend/surgery wait even
        /// while the pawn is momentarily upright; <c>InBed()</c> is the cheap belt-and-suspenders for any resting
        /// pawn already lying down.
        /// </summary>
        public static bool IsRestingPatient(Pawn pawn)
            => pawn != null && (HealthAIUtility.ShouldSeekMedicalRest(pawn) || pawn.InBed());
    }
}
