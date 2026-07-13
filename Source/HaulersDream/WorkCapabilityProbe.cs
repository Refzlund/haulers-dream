using System;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// A fault-isolating wrapper around vanilla's work-type capability query
    /// (<see cref="Pawn.WorkTypeIsDisabled"/>, which routes through <see cref="Pawn.GetDisabledWorkTypes"/>),
    /// used everywhere Hauler's Dream probes whether a pawn can do a work type.
    ///
    /// WHY (issue #197): vanilla's GetDisabledWorkTypes reads pawn fields with NO null guard. Its
    /// LifeStageWorkSettings.IsDisabled path is literally <c>pawn.ageTracker.AgeBiologicalYears &lt; minAge</c>,
    /// and FillList walks <c>RaceProps.lifeStageWorkSettings</c>, so a malformed MODDED pawn throws a
    /// NullReferenceException there. A Dead Man's Switch "humanoid mech" summoned by WVC's voidlink is the
    /// reported case (it reaches HD with a null ageTracker). Vanilla never asks an arbitrary mech whether a
    /// work type is disabled, but HD does (it offers mech hauling), so HD is the mod that trips the latent
    /// vanilla/foreign defect. The throw is NOT HD's to fix (it lives in vanilla + the other mod's pawn data),
    /// but one broken pawn must never crash a whole haul/work scan or a right-click menu build.
    ///
    /// RECOVER + REPORT, never swallow (the <see cref="HDGuard"/> philosophy): on a throw HD reports it ONCE
    /// (a deduped ERROR whose stack names the real source) and answers as if the work were DISABLED — the
    /// conservative branch at each call site (a scan skips the pawn, a menu hides the option, a job builder
    /// falls through to vanilla). The returned value is not always the deciding factor: at the mech-reachable
    /// haul-eligibility site, whether a mech may haul is governed by the allowMechanoids setting, not by this
    /// probe (<see cref="Core.EligibilityPolicy"/> discards <c>incapableOfHauling</c> for mechs), so there
    /// "disabled" simply contains the crash while argument evaluation completes and the mech's eligibility is
    /// unchanged. Either way the red error stays visible and one broken pawn never aborts the scan.
    ///
    /// SCOPE: this wraps the work-TYPE query (<see cref="Pawn.WorkTypeIsDisabled"/> ->
    /// <see cref="Pawn.GetDisabledWorkTypes"/>), the path #197's null-ageTracker defect lives on. The work-TAG
    /// query (<c>WorkTagIsDisabled</c> -> <c>CombinedDisabledWorkTags</c>) is a separate getter with its own
    /// unguarded field read; it is NOT wrapped here (no report implicates it) and shares only the universal-tagger
    /// exclusion in <see cref="HaulersDreamMod"/>.
    /// </summary>
    internal static class WorkCapabilityProbe
    {
        /// <summary>
        /// <see cref="Pawn.WorkTypeIsDisabled"/> for <paramref name="pawn"/> / <paramref name="work"/>, returning
        /// <c>true</c> (treat the work as disabled, so HD stands down for this pawn) when the vanilla query throws
        /// for a malformed pawn. On the non-throwing path it is exactly vanilla's answer, so normal behaviour is
        /// unchanged. The failure is reported once per (pawn race, work type) so it stays visible without flooding
        /// a per-scan repeat.
        /// </summary>
        /// <param name="pawn">The pawn whose capability is probed; a null pawn is treated as disabled.</param>
        /// <param name="work">The work type to test; a null work type is treated as disabled.</param>
        /// <returns>Vanilla's <see cref="Pawn.WorkTypeIsDisabled"/> result, or <c>true</c> if that query threw.</returns>
        internal static bool IsDisabled(Pawn pawn, WorkTypeDef work)
        {
            if (pawn == null || work == null)
                return true;
            try
            {
                return pawn.WorkTypeIsDisabled(work);
            }
            catch (Exception e)
            {
                // A malformed (almost always modded) pawn's vanilla work-type query threw. Report once per
                // (race, work) with the real stack, then stand down for this pawn. def?.defName is read (not
                // LabelShort) precisely because the pawn is malformed — a label lookup could throw again and
                // escape this catch, defeating the guard.
                HDLog.ErrOnce(
                    "vanilla's work-type query (Pawn.WorkTypeIsDisabled -> GetDisabledWorkTypes) threw for a "
                    + (pawn.def?.defName ?? "pawn") + " while Hauler's Dream checked whether it can do "
                    + (work.defName ?? "a work type") + ". This is a defect in that pawn's setup, NOT in Hauler's "
                    + "Dream: vanilla reads fields like ageTracker / lifeStageWorkSettings with no null guard, and a "
                    + "malformed modded pawn (for example a custom 'mech') can be missing them. The stack trace below "
                    + "names the real source. Hauler's Dream is treating this pawn as unable to do that work so one "
                    + "broken pawn does not stop the whole scan.\n" + e,
                    unchecked(((pawn.def?.shortHash ?? 0) * 397) ^ work.shortHash));
                return true;
            }
        }
    }
}
