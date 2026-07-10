using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Decides whether a chosen work Job is "protected" — i.e. work Hauler's Dream must NEVER delay by diverting
    /// the pawn to an opportunistic unload/haul first. Verse-free so it is unit-testable; the Verse adapter
    /// (<c>HaulersDream.ProtectedWork</c>) maps a live <c>Job</c>/<c>JobGiver_Work</c> into these plain inputs.
    ///
    /// <para>Motivation (reported, game-breaking): a colonist carrying HD-scooped cargo that the work scan assigns
    /// a tend / rescue / firefight job was being swapped to an unload trip instead — so after a fight nobody tended
    /// the bleeding, downed pawns were never rescued, and fires were ignored, even at work priority 1. Protecting
    /// these job classes from the divert fixes that for good while leaving the opportunistic-unload feature working
    /// for ordinary work (mining/hauling/cleaning/etc.).</para>
    /// </summary>
    public static class ProtectedWorkPolicy
    {
        /// <summary>
        /// The <c>WorkTypeDef.defName</c>s whose work must never be delayed by an HD unload/haul divert. Verified
        /// against vanilla Core WorkGiver defs: rescuing a downed colonist (<c>WorkGiver_RescueDowned</c>,
        /// def <c>DoctorRescue</c>) and taking a patient to a surgery bed (<c>WorkGiver_TakeToBedToOperate</c>) are
        /// both <c>Doctor</c> worktype but NOT flagged <c>emergency</c> — so the emergency flags alone would MISS
        /// rescue; the worktype set is load-bearing. Firefighting (<c>FightFires</c>) is Firefighter + emergency;
        /// Warden covers prisoner rescue/care. Single source of truth (add a worktype here to protect it).
        /// </summary>
        private static readonly HashSet<string> ProtectedWorkTypeDefNames = new HashSet<string>
        {
            "Doctor", "Firefighter", "Warden"
        };

        /// <summary>True if <paramref name="workTypeDefName"/> is a protected worktype (null-safe: null =&gt; false).</summary>
        public static bool IsProtectedWorkType(string workTypeDefName)
            => workTypeDefName != null && ProtectedWorkTypeDefNames.Contains(workTypeDefName);

        /// <summary>
        /// True when the chosen work job must not be delayed by an opportunistic unload/haul divert.
        /// </summary>
        /// <param name="nodeIsEmergency">The issuing <c>JobGiver_Work</c> node's <c>emergency</c> flag — vanilla runs
        /// the work-selection seam once for the high-priority emergency node and once for the normal node; the
        /// emergency node issues tend/rescue/firefight/take-to-bed, so this alone covers everything it produces
        /// (including future/modded emergency givers) with no enumeration.</param>
        /// <param name="workGiverEmergency">The chosen job's <c>WorkGiverDef.emergency</c> flag — catches an
        /// emergency giver reached via the normal node too.</param>
        /// <param name="workTypeDefName">The chosen job's <c>WorkGiverDef.workType.defName</c> — catches the
        /// non-emergency Doctor/Firefighter/Warden work (notably rescue) the flags miss.</param>
        public static bool IsProtectedWork(bool nodeIsEmergency, bool workGiverEmergency, string workTypeDefName)
            => nodeIsEmergency || workGiverEmergency || IsProtectedWorkType(workTypeDefName);

        /// <summary>
        /// The #107 divert-gate decision for protected work: is this a TRUE emergency whose divert must be
        /// HARD-BLOCKED? True emergencies (tend the bleeding, firefight, emergency take-to-bed) are the
        /// emergency-node / emergency-workgiver jobs; they are never diverted for any reason, exactly as issue #107
        /// established. (Splitting this out from <see cref="MayZeroDetourUnload"/> so the safety-critical
        /// emergency-vs-non-emergency boundary is pinned by tests, not left implicit in the Verse postfix.)
        /// </summary>
        /// <param name="isProtected">The job is protected work (see <see cref="IsProtectedWork"/>).</param>
        /// <param name="isEmergency">The job is emergency-flagged (its issuing node or its workgiver).</param>
        public static bool MustHardBlockDivert(bool isProtected, bool isEmergency)
            => isProtected && isEmergency;

        /// <summary>
        /// The #107 divert-gate decision for protected work: may this job take a ZERO-DETOUR pass-by unload (shed a
        /// scooped load ONLY when storage is already on the way, never adding travel, so the work is never delayed)?
        /// Deliberately narrow: ONLY a NON-emergency <paramref name="isDoBill"/> (the reported "a doctor carries
        /// scooped organs through an elective-surgery queue" case). Rescue and warden work, though non-emergency,
        /// keep the hard block because their urgency should not be delayed even by a free drop; a true emergency is
        /// already excluded by <paramref name="isEmergency"/>.
        /// </summary>
        /// <param name="isProtected">The job is protected work.</param>
        /// <param name="isEmergency">The job is emergency-flagged (its issuing node or its workgiver).</param>
        /// <param name="isDoBill">The job is a DoBill (a surgery when the bill-giver is a patient).</param>
        public static bool MayZeroDetourUnload(bool isProtected, bool isEmergency, bool isDoBill)
            => isProtected && !isEmergency && isDoBill;
    }
}
