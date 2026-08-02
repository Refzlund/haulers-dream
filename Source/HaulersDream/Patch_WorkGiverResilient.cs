using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// WORK-SCAN RESILIENCE (issue #7).
    ///
    /// <para>Vanilla <see cref="JobGiver_Work"/>.<c>PawnCanUseWorkGiver</c> decides whether a pawn may use a given
    /// <see cref="WorkGiver"/> this scan (its <c>ShouldSkip</c> / capacity / faction checks). It is called in a tight
    /// loop over EVERY work giver inside <c>TryIssueJobPackage</c>, and vanilla wraps NONE of those calls in a
    /// try/catch. So if a single work giver throws here, the exception propagates out of the whole loop and aborts the
    /// pawn's ENTIRE work selection — and if the fault is persistent it repeats every scan, which permanently stalls
    /// all of that pawn's dumb labor (hauling, cleaning, hauling corpses, etc.). A real report (issue #7) hit exactly
    /// this: a foreign hauling work giver (Haul Explicitly, reached via Vehicle Map Framework's transpiler on this
    /// method) threw a <c>NullReferenceException</c> in its <c>ShouldSkip</c>, bricking the colony's work.</para>
    ///
    /// <para>This Finalizer makes ONE broken work giver degrade to "skipped this scan" instead of bricking ALL work:</para>
    /// <list type="bullet">
    ///   <item>If the throwing work giver belongs to HAULER'S DREAM, RE-THROW it unchanged. HD's own faults must stay
    ///   loud and visible (the project's no-swallow rule) — they are real bugs to fix, not to hide.</item>
    ///   <item>If it belongs to ANY OTHER mod (or vanilla), log it ONCE per work-giver type — so it stays visible and
    ///   attributable, never silently swallowed — and return <c>__result = false</c> ("this pawn can't use this work
    ///   giver this scan"). The work loop then simply advances to the next giver, so the rest of the pawn's work still
    ///   runs.</item>
    /// </list>
    ///
    /// <para>This mirrors the mod's existing resilient-degrade stance (<see cref="HaulersDreamMod"/>'s
    /// <c>ApplyPatchesResilient</c>): contain a foreign failure at a controlled boundary, log it loudly, and keep the
    /// save playable — rather than let a third-party bug brick the colony. It only ever changes behaviour on the
    /// exception path; when nothing throws it is a pure no-op, so a normal work scan is byte-identical.</para>
    ///
    /// <para>The same method is ALSO the chokepoint for the sibling failure one method over (issue #235): a giver
    /// that throws from the work scan's UNGUARDED tail — <c>scannerWhoProvidedTarget.JobOnCell/JobOnThing</c>,
    /// which vanilla's per-giver try/catch does not cover — is quarantined for the session by
    /// <see cref="WorkGiverBlocklist"/>, and the <c>Prefix</c> below refuses it here so it never re-enters the
    /// scan. The <c>Postfix</c> records the last giver cleared for this pawn, used only as fallback attribution
    /// when a fault's own frames cannot name the culprit. Both are no-ops in a game where nothing has faulted.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), "PawnCanUseWorkGiver", new[] { typeof(Pawn), typeof(WorkGiver) })]
    public static class Patch_JobGiver_Work_WorkGiverResilient
    {
        /// <summary>
        /// QUARANTINE GATE (issue #235). A work giver that repeatedly threw out of the UNGUARDED tail of the work
        /// scan (<c>scannerWhoProvidedTarget.JobOnCell/JobOnThing</c>, which sits after vanilla's per-giver
        /// try/catch) is switched off here for the session. This is the correct chokepoint: returning false makes
        /// vanilla <c>continue</c> the WHOLE loop iteration, so the giver is never scanned, never becomes
        /// <c>scannerWhoProvidedTarget</c>, and can therefore never reach that unguarded call again — while every
        /// other work giver keeps running. See <see cref="WorkGiverBlocklist"/> for why containing the throw
        /// without removing the giver from the rotation would leave the colonist idle either way.
        /// </summary>
        /// <param name="giver">The work giver vanilla is about to consider.</param>
        /// <param name="__result">Set to false when the giver is quarantined.</param>
        /// <returns>False to skip the original (giver refused), true to run it normally.</returns>
        static bool Prefix(WorkGiver giver, ref bool __result)
        {
            if (!WorkGiverBlocklist.IsQuarantined(giver))
                return true;
            __result = false;
            return false;
        }

        /// <summary>
        /// Record the giver vanilla just CLEARED for this pawn, as the fallback attribution for a fault observed
        /// later at the work-selection seam (see <see cref="WorkGiverBlocklist.NoteGiverPassedGate"/>). Only a
        /// giver that passed this gate can go on to provide the target for the unguarded tail call, so a refused
        /// one (<paramref name="__result"/> false, including a quarantined one) is deliberately not recorded.
        /// </summary>
        static void Postfix(bool __result, WorkGiver giver)
        {
            if (__result)
                WorkGiverBlocklist.NoteGiverPassedGate(giver);
        }

        static Exception Finalizer(Exception __exception, ref bool __result, Pawn pawn, WorkGiver giver)
        {
            if (__exception == null)
                return null; // the common path — no fault, nothing to contain

            // HD's OWN fault -> keep it loud (no-swallow). Returning the exception tells Harmony to re-raise it,
            // so RimWorld still reports the fault (and HD's HDGuard finalizer on TryIssueJobPackage still adds
            // its breadcrumb). A real HD bug must never be hidden by this safety net.
            //
            // TWO tests, because "HD's own" now has two meanings here. The giver's assembly covers HD's own work
            // givers (the original #7 case, where HD supplies the throwing giver), and the frame test covers HD
            // code running INSIDE this method — which #235 introduced for the first time, since the Prefix and
            // Postfix above are HD's. Both bodies are provably non-throwing today, so the second test is
            // belt-and-braces; it costs one frame walk on an already-exceptional path and closes the hole for
            // good rather than depending on those bodies staying trivial.
            var giverType = giver?.GetType();
            if ((giverType != null && HDFault.IsHaulersDream(giverType.Assembly))
                || HDFault.InvolvesHaulersDream(__exception))
                return __exception;

            // A FOREIGN (or vanilla) work giver threw while RimWorld checked whether this pawn can use it. Contain it
            // at this per-giver boundary: vanilla has no guard, so letting it escape would abort the pawn's ENTIRE
            // work selection every scan. Log once per work-giver type (visible + attributable), then treat it as
            // "can't use this giver this scan" so only that one giver is skipped and all other work keeps running.
            //
            // WORDING: this used to assert "the fault is in that work giver", which is a categorical claim the
            // evidence here does not support — this method's own class doc says the giver may be vanilla's, and
            // WorkGiverQuarantinePolicy one method over explicitly refuses to blame a giver when nothing
            // implicates a mod (a vanilla giver choking on modded DATA). Name the innermost mod-owned frame when
            // there is one and say so plainly when there is not, matching HDGuard.OriginClause.
            //
            // RENDERING: HDFault.Render, never a raw ToString(). The 0Harmony runtime the game loads dedupes by
            // trace TEXT, so a repeating fault's second render returns "[Ref ...] Duplicate stacktrace" — and it
            // would land in exactly the line that tells the player whom to report to (issue #236).
            string giverName = giverType?.FullName ?? "an unknown WorkGiver";
            string origin = HDFault.DescribeOrigin(__exception);
            HDLog.ErrOnce(
                "the work giver '" + giverName + "' threw while RimWorld evaluated whether "
                + (pawn?.LabelShort ?? "a pawn") + " can use it. "
                // Only claim this when the type is actually known. With giverType == null there is nothing to
                // check it against, and asserting non-involvement you cannot establish is the #236 mistake.
                + (giverType != null ? "This is NOT a Hauler's Dream work giver. " : "")
                + (origin != null
                    ? "The innermost frame belonging to another mod is " + origin + ", so that is the most likely "
                        + "source; please report it there. "
                    : "No frame in this stack belongs to a mod, so the mod at fault could not be identified here — "
                        + "the stack below is what was available. ")
                + "Vanilla has no guard here, so this throw would "
                + "otherwise abort the pawn's ENTIRE work selection every scan (all hauling/cleaning/etc. would stall). "
                + "Hauler's Dream is skipping just that one work giver for this pawn this scan so the rest of its work "
                + "keeps running.\n" + HDFault.Render(__exception),
                ("HD.wgResilient." + giverName).GetHashCode());
            __result = false; // "pawn can't use this work giver" -> the work loop advances to the next giver
            return null;      // handled: suppress only this foreign giver's throw so the work scan isn't bricked
        }
    }
}
