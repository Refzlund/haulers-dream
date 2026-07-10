using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Shared visibility gate for HD's "Plan prioritized ..." float-menu providers, implementing the
    /// "plan for unassigned work" setting: whether a planner option must be hidden for a pawn that is CAPABLE of
    /// the work but has that work type UNASSIGNED (work priority 0) in its Work tab. Each provider already hides
    /// the option for a pawn truly INCAPABLE of the work (its own WorkTypeIsDisabled gate); this only adds the
    /// optional stricter "must also be assigned" rule. The multi-work-type bench case (a workbench can host bills
    /// of several work types) lives in <see cref="WorkOverride.HidePlanCraftForUnassigned"/>.
    /// </summary>
    internal static class PlannerGate
    {
        /// <summary>True if the planner option must be HIDDEN for <paramref name="pawn"/> because the "plan for
        /// unassigned work" setting is off and the pawn has <paramref name="wt"/> unassigned (work priority 0).
        /// Incapable pawns are already hidden by each provider's WorkTypeIsDisabled gate, so this only covers the
        /// capable-but-unassigned case. Null/absent workSettings (e.g. mechs) never hide (providers gate mechs out).</summary>
        internal static bool HideForUnassigned(Pawn pawn, WorkTypeDef wt)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || s.planForUnassignedWork || wt == null) return false;
            return pawn?.workSettings != null && pawn.workSettings.GetPriority(wt) == 0;
        }
    }
}
