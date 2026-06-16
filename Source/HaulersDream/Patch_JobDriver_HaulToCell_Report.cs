using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// COSMETIC inspector job-report rewrite (While You're Up parity). A Harmony POSTFIX on
    /// <see cref="Verse.AI.JobDriver.GetReport"/> — the single vanilla seam that produces the "current job" text
    /// shown under a selected pawn — that rewrites the report to convey ROUTING INTENT for the three cases HD's
    /// own hauling can detect, mirroring WYU's <c>BaseDetour.GetJobReport</c> (the key selection lives in the pure
    /// <see cref="JobReportPolicy"/>). The KIND is read entirely from the job INSTANCE (not the pawn), via the
    /// cosmetic instance markers the routing / en-route engines stash at emit time:
    /// <list type="bullet">
    ///   <item><b>CloserTo</b> ⇐ a <c>HaulToCell</c> job tagged <see cref="StorageRouting.IsRelocation"/> (the
    ///   before-carry relocation moves the stack CLOSER to where it will be used) — DESTINATION = the consumer
    ///   cell stashed on the relocation marker. Gated on <see cref="HaulersDreamSettings.storageRouting"/>.</item>
    ///   <item><b>EnRoute</b> ⇐ a <c>HaulersDream_BulkHaul</c> job tagged <see cref="EnRoutePickup.IsEnRoute"/>
    ///   (grabbed on the way to a job) — DESTINATION = the bound-for job cell stashed on the en-route marker.
    ///   Gated on <see cref="HaulersDreamSettings.enRoutePickup"/>.</item>
    /// </list>
    /// <para>The third WYU case, <see cref="JobReportKind.Efficient"/> (a plain bulk haul), is DELIBERATELY not
    /// fired: HD's bulk-haul job already self-describes via its own JobDef report ("hauling everything nearby."),
    /// which conveys the consolidation; wrapping it would read "Efficiently hauling hauling everything nearby". The
    /// <c>Efficient.*</c> keys are kept in XML for parity/completeness only.</para>
    ///
    /// <para><b>Purely cosmetic; degrade-to-no-rewrite everywhere.</b> Every classification path is gated on the
    /// owning FEATURE being enabled AND the job's own marker, so an OFF feature never rewrites anything; and if the
    /// kind is <see cref="JobReportKind.Normal"/> (no marker matched) the vanilla report is left UNTOUCHED. This is
    /// only the LOAD leg (the carry-toward-storage job the markers live on); HD's separate unload job is a distinct
    /// instance that carries no marker, so the <c>*.Unload</c> keys simply never fire — an accepted cosmetic gap
    /// (the keys exist for completeness / future parity). A null/empty vanilla report is also left untouched.</para>
    ///
    /// <para>Allocation-light: the common case (no marker) returns after one dictionary miss + an enum compare;
    /// only a matched, enabled kind allocates the single rewritten string the inspector was going to show anyway.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.GetReport))]
    public static class Patch_JobDriver_HaulToCell_Report
    {
        static void Postfix(JobDriver __instance, ref string __result)
        {
            // Cheap exits: nothing to rewrite, or no settings yet (very early init).
            if (__result.NullOrEmpty())
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return;
            var job = __instance?.job;
            if (job?.def == null)
                return;

            // Classify the job by its cosmetic marker, honoring the owning feature's enable flag. The order is
            // exclusive: a relocation is always a HaulToCell (never a bulk-haul), and an en-route bulk-haul is
            // checked before the plain (Efficient) bulk-haul.
            JobReportKind kind;
            IntVec3 destCell;
            if (s.storageRouting && job.def == JobDefOf.HaulToCell)
            {
                var info = StorageRouting.RelocationData(job);
                if (info == null)
                    return; // a normal HaulToCell, not one of ours -> leave vanilla report
                kind = JobReportKind.CloserTo;
                destCell = info.consumeCell;
            }
            else if (job.def == HaulersDreamDefOf.HaulersDream_BulkHaul)
            {
                // EN-ROUTE only. The plain (Efficient) bulk haul is DELIBERATELY left to its own JobDef report
                // ("hauling everything nearby."), which already conveys the consolidation — wrapping it as
                // "Efficiently hauling hauling everything nearby" would double the verb. The Efficient.* keys are
                // kept in XML for parity/completeness but not fired here (a clean degrade-to-no-rewrite). En-route
                // adds genuinely new info (the bound-for destination), so it IS rewritten.
                var enRoute = Patch_Pawn_JobTracker_EnRoutePickup.EnRouteData(job);
                if (enRoute == null || !s.enRoutePickup)
                    return; // not an en-route pickup, or the feature is off -> leave the bulk-haul report
                kind = JobReportKind.EnRoute;
                destCell = enRoute.jobCell;
            }
            else
            {
                return; // not a job HD rewrites
            }

            if (!JobReportPolicy.RewritesReport(kind))
                return;

            // LOAD leg: the markers live on the carry-toward-storage job (the only place HD knows the intent).
            string key = JobReportPolicy.ReportKeyFor(kind, isLoad: true);
            if (key == null)
                return;

            // {ORIGINAL} = the trimmed vanilla report (WYU text.Named("ORIGINAL")). A trailing period reads
            // oddly mid-sentence ("Hauling steel. (closer to …)"), so trim it like WYU does.
            string original = __result.TrimEnd().TrimEnd('.');

            if (JobReportPolicy.UsesDestination(kind))
            {
                string dest = DestinationLabel(__instance, destCell);
                __result = key.Translate(original.Named("ORIGINAL"), dest.Named("DESTINATION"));
            }
            else
            {
                __result = key.Translate(original.Named("ORIGINAL"));
            }
        }

        /// <summary>A short human label for the carry/consumer cell — the building/blueprint/zone standing there
        /// if any (e.g. "wall", "electric stove", a stockpile), else the bare cell. Best-effort and never throws:
        /// any failure falls back to the cell coordinates (this is cosmetic).</summary>
        private static string DestinationLabel(JobDriver driver, IntVec3 cell)
        {
            var map = driver?.pawn?.MapHeld;
            if (map != null && cell.IsValid && cell.InBounds(map))
            {
                // Prefer the most meaningful occupant: an edifice (building/blueprint/frame) first, then any
                // labelled thing, then the storage zone, then the bare cell.
                var edifice = cell.GetEdifice(map);
                if (edifice != null && !edifice.LabelShortCap.NullOrEmpty())
                    return edifice.LabelShortCap;

                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var t = things[i];
                    if (t != null && t.def != null && t.def.category != ThingCategory.Pawn
                        && !t.LabelShortCap.NullOrEmpty())
                        return t.LabelShortCap;
                }

                var zone = map.zoneManager?.ZoneAt(cell);
                if (zone != null && !zone.label.NullOrEmpty())
                    return zone.label;
            }
            return cell.IsValid ? cell.ToString() : "storage";
        }
    }
}
