namespace HaulersDream.Core
{
    /// <summary>
    /// Why a haul job's inspector "current job" text should be rewritten to convey ROUTING INTENT, rather
    /// than the plain vanilla "Hauling X to Y". Faithful port of While You're Up's <c>DetourType</c> →
    /// report-key mapping in <c>BaseDetour.GetJobReport</c> (<c>BaseDetour.cs:107-120</c>), collapsed to the
    /// three player-visible CASES WYU actually distinguishes (its six detour types fold into three report
    /// keys: PUAH-vs-HTC variants share a key):
    /// <list type="bullet">
    ///   <item><see cref="Efficient"/> ⇐ <c>DetourType.Puah</c> → key <c>PickUpAndHaulPlus_*Report</c>.</item>
    ///   <item><see cref="EnRoute"/> ⇐ <c>DetourType.HtcOpportunity</c>/<c>PuahOpportunity</c> → key <c>Opportunity_*Report</c>.</item>
    ///   <item><see cref="CloserTo"/> ⇐ <c>DetourType.HtcBeforeCarry</c>/<c>PuahBeforeCarry</c> → key <c>HaulBeforeCarry_*Report</c>.</item>
    /// </list>
    /// <see cref="Normal"/> is WYU's <c>DetourType.Inactive</c> (and its <c>_ =&gt; text</c> default): no rewrite.
    /// </summary>
    public enum JobReportKind
    {
        /// <summary>No routing intent — leave the vanilla report unchanged (WYU <c>DetourType.Inactive</c>).</summary>
        Normal,

        /// <summary>
        /// The pawn grabbed this loose item because it lay roughly along the way to a job it was already
        /// heading to (HD's en-route pickup / opportunistic haul). WYU phrasing names the job the pawn is
        /// ultimately bound for: "X (on the way to {DESTINATION})". WYU <c>Opportunity_LoadReport</c> /
        /// <c>Opportunity_UnloadReport</c> (<c>BaseDetour.cs:114-115</c>), {DESTINATION} = the next job target
        /// (<c>opportunity.jobTarget.Label</c>).
        /// </summary>
        EnRoute,

        /// <summary>
        /// The pawn is relocating this material to storage CLOSER to where it will be used, before carrying it
        /// to the construction site / crafting bench (HD's storage routing / "haul before carry"). WYU phrasing
        /// names the eventual carry destination: "X (closer to {DESTINATION})". WYU
        /// <c>HaulBeforeCarry_LoadReport</c> / <c>HaulBeforeCarry_UnloadReport</c> (<c>BaseDetour.cs:116-117</c>),
        /// {DESTINATION} = the carry target (<c>beforeCarry.carryTarget.Label</c>).
        /// </summary>
        CloserTo,

        /// <summary>
        /// The pawn is consolidating many items into one storage trip (HD's bulk haul). WYU phrasing reads as
        /// "Efficiently hauling X" with no second target. WYU <c>PickUpAndHaulPlus_LoadReport</c> /
        /// <c>PickUpAndHaulPlus_UnloadReport</c> (<c>BaseDetour.cs:113</c>), only the {ORIGINAL} arg.
        /// </summary>
        Efficient,
    }

    /// <summary>
    /// PURE (Verse-free) selection of the translation KEY for the inspector job-report rewrite. The Verse
    /// report patch (W6 <c>Patch_JobDriver_HaulToCell_Report</c>) classifies the pawn's current haul into a
    /// <see cref="JobReportKind"/> + whether it is the LOAD leg (scooping toward storage) or the UNLOAD leg,
    /// then calls <see cref="ReportKeyFor"/> to get the key it will <c>.Translate(...)</c> with the named
    /// args the XML expects. Keeping the key selection here (rather than inline switch-on-enum in the patch)
    /// gives it an oracle test and a 0-alloc guarantee, matching HD's Core-policy pattern.
    ///
    /// <para><b>Args contract (what the patch must pass to <c>.Translate</c>, mirroring WYU's
    /// <c>NamedArgument</c>s):</b> every non-<see cref="Normal"/> key takes <c>ORIGINAL</c> = the trimmed
    /// vanilla report text (WYU <c>text.Named("ORIGINAL")</c>). <see cref="EnRoute"/> and <see cref="CloserTo"/>
    /// additionally take <c>DESTINATION</c> = the destination label (WYU <c>...Label.Named("DESTINATION")</c>).
    /// <see cref="Efficient"/> takes ORIGINAL only. <see cref="Normal"/> selects no key
    /// (<see cref="ReportKeyFor"/> returns <c>null</c>) — the patch leaves the vanilla text untouched.</para>
    ///
    /// <para><b>XML keys the Languages file MUST define</b> (under the existing <c>HaulersDream.*</c>
    /// namespace; one per kind × leg). Suggested phrasings mirror WYU's "X (on the way to Y)" /
    /// "X (closer to Y)" / "Efficiently hauling…":</para>
    /// <list type="bullet">
    ///   <item><c>HaulersDream.JobReport.EnRoute.Load</c> / <c>HaulersDream.JobReport.EnRoute.Unload</c>  — args {ORIGINAL}, {DESTINATION}</item>
    ///   <item><c>HaulersDream.JobReport.CloserTo.Load</c> / <c>HaulersDream.JobReport.CloserTo.Unload</c> — args {ORIGINAL}, {DESTINATION}</item>
    ///   <item><c>HaulersDream.JobReport.Efficient.Load</c> / <c>HaulersDream.JobReport.Efficient.Unload</c> — arg {ORIGINAL}</item>
    /// </list>
    /// </summary>
    public static class JobReportPolicy
    {
        // Interned string literals (compile-time constants) — selecting one allocates nothing.
        const string EnRouteLoad    = "HaulersDream.JobReport.EnRoute.Load";
        const string EnRouteUnload  = "HaulersDream.JobReport.EnRoute.Unload";
        const string CloserToLoad   = "HaulersDream.JobReport.CloserTo.Load";
        const string CloserToUnload = "HaulersDream.JobReport.CloserTo.Unload";
        const string EfficientLoad   = "HaulersDream.JobReport.Efficient.Load";
        const string EfficientUnload = "HaulersDream.JobReport.Efficient.Unload";

        /// <summary>
        /// The translation key for <paramref name="kind"/> on the given leg, or <c>null</c> for
        /// <see cref="JobReportKind.Normal"/> (no rewrite — the patch keeps the vanilla report).
        /// </summary>
        /// <param name="kind">The routing intent of the current haul.</param>
        /// <param name="isLoad">
        /// <c>true</c> = the LOAD leg (the pawn is scooping / carrying toward storage; WYU's
        /// <c>_LoadReport</c>); <c>false</c> = the UNLOAD leg (emptying its inventory; WYU's <c>_UnloadReport</c>).
        /// </param>
        public static string ReportKeyFor(JobReportKind kind, bool isLoad)
        {
            switch (kind)
            {
                case JobReportKind.EnRoute:   return isLoad ? EnRouteLoad   : EnRouteUnload;
                case JobReportKind.CloserTo:  return isLoad ? CloserToLoad  : CloserToUnload;
                case JobReportKind.Efficient: return isLoad ? EfficientLoad : EfficientUnload;
                default:                      return null; // Normal: no rewrite
            }
        }

        /// <summary>Whether <paramref name="kind"/> rewrites the report (i.e. <see cref="ReportKeyFor"/> is non-null).</summary>
        public static bool RewritesReport(JobReportKind kind) => kind != JobReportKind.Normal;

        /// <summary>
        /// Whether the key for <paramref name="kind"/> takes a <c>DESTINATION</c> named arg (in addition to
        /// <c>ORIGINAL</c>). True for <see cref="JobReportKind.EnRoute"/> and <see cref="JobReportKind.CloserTo"/>
        /// (WYU passes the destination label to those two keys; <see cref="JobReportKind.Efficient"/> and
        /// <see cref="JobReportKind.Normal"/> do not). Lets the Verse patch skip resolving a destination label
        /// it would not use.
        /// </summary>
        public static bool UsesDestination(JobReportKind kind) =>
            kind == JobReportKind.EnRoute || kind == JobReportKind.CloserTo;
    }
}
