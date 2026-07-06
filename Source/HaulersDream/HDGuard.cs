using System;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The two sanctioned fault boundaries for the vanilla job/work/carry seams HD patches. Those seams
    /// (<c>JobGiver_*.TryGiveJob</c>, <c>JobGiver_Work.TryIssueJobPackage</c>,
    /// <c>FoodUtility.TryFindBestFoodSourceFor</c>, <c>WorkGiver_HaulGeneral.JobOnThing</c>,
    /// <c>Pawn_CarryTracker.TryStartCarry</c>) have no vanilla try/catch, so an unhandled throw from an HD hook
    /// (or a downstream vanilla/compat call it makes) propagates uncaught and can halt a whole CATEGORY of
    /// a pawn's behaviour (all hauling, all cleaning, even rest/eat) with nothing in the log pointing at HD.
    ///
    /// <para><b><see cref="SeamThrew"/> (log + RETHROW)</b>, for whole-method Harmony <c>Finalizer</c>s. The
    /// repo's standing rule is no-swallow: a real fault must stay visible as a red error. So this does NOT
    /// catch-and-continue. It LOGS once (deduped per seam, so a per-scan repeat can't flood the log) with the
    /// pawn + the concrete consequence, then RETURNS the exception so Harmony RE-THROWS it. Net effect: identical
    /// propagation to before (the fault still surfaces, RimWorld's own handler still logs it), but now there is a
    /// pawn-and-consequence breadcrumb at the seam instead of only an anonymous stack.</para>
    ///
    /// <para><b><see cref="SeamDegraded"/> (log + KEEP VANILLA)</b>, for a catch INSIDE an HD postfix, wrapped
    /// around HD's own ENHANCEMENT of a think-tree node whose vanilla result must survive. Issue #122 is why this
    /// exists: RimWorld's think infrastructure (<c>ThinkNode_Priority</c> / <c>ThinkNode_PrioritySorter</c>)
    /// catches a throwing child node, logs it (a single entry the log window collapses under its repeat
    /// counter, easy to miss), and SKIPS it, so a repeatable exception anywhere inside
    /// <c>JobGiver_GetFood.TryGiveJob</c>'s call graph costs the pawn its food job on EVERY think, while
    /// the joy node keeps issuing "read a book". The pawn then reads nonstop, refuses every other task, and
    /// starves to death. For such a seam, log-and-rethrow is the WRONG blast radius: the throw destroys vanilla's
    /// already-computed job even though HD only failed to ADD something optional (an unload swap, a carried-meal
    /// resolution). SeamDegraded reports the fault ONCE (deduped per seam) with full stack and HD attribution,
    /// and the caller keeps vanilla's result, so the pawn still eats/sleeps/works. This is RECOVER + REPORT, not
    /// suppression: the red error stays, only the collateral damage goes.</para>
    ///
    /// HONEST ATTRIBUTION (both): a caught throw may originate in HD's own hook, in a downstream vanilla/compat
    /// call HD makes, OR (for SeamThrew) in vanilla / another mod patching the same method. The messages therefore
    /// do NOT assert HD is the cause; they state HD's involvement and point at the stack trace (which names the
    /// real source). (fix/mix #3b hardening; the SeamDegraded boundary is the #122 hardening.)
    /// </summary>
    public static class HDGuard
    {
        public static Exception SeamThrew(Exception ex, string seam, Pawn pawn, string consequence)
        {
            if (ex != null)
                HDLog.ErrOnce("An exception surfaced at " + seam + " (a method HD patches) "
                    + "while selecting for " + (pawn?.LabelShort ?? "a pawn") + " - " + consequence
                    + " The stack trace below shows the actual source (HD, vanilla, or another mod on the same "
                    + "method); if it implicates HaulersDream, please report it.\n" + ex,
                    seam.GetHashCode()); // stable per-seam key (net48 string hashing isn't randomized) -> log once
            return ex; // rethrow: keep the fault visible, never swallow
        }

        /// <summary>
        /// Report a throw from an HD ENHANCEMENT at a think-node seam and degrade to vanilla: the caller catches,
        /// calls this, and returns with the vanilla result untouched (see the class doc for why rethrow is the
        /// wrong blast radius there, issue #122). Logs at ERROR level, once per <paramref name="seam"/> per
        /// session, with the pawn, what was preserved, and the full exception (whose stack names the real source:
        /// HD's own scan, or a vanilla/compat call it made).
        /// </summary>
        /// <param name="ex">The caught exception. No-op when null.</param>
        /// <param name="seam">Stable seam name (also the dedupe key), e.g.
        /// "JobGiver_GetFood.TryGiveJob (HD unload-before-eating)".</param>
        /// <param name="pawn">The pawn being selected for; may be null.</param>
        /// <param name="kept">What vanilla behaviour was preserved, stated concretely, e.g.
        /// "kept vanilla's food job, so the pawn still eats".</param>
        public static void SeamDegraded(Exception ex, string seam, Pawn pawn, string kept)
        {
            if (ex == null)
                return;
            // Key: per-seam AND distinct from SeamThrew's key for the same seam string. Log.ErrorOnce dedupes
            // globally by the int key, so sharing seam.GetHashCode() would let whichever of the two channels
            // fires first eat the other's one-shot breadcrumb for the session (a degraded HD enhancement would
            // then hide a later foreign fault's seam breadcrumb, or vice versa).
            HDLog.ErrOnce("Hauler's Dream's enhancement at " + seam + " threw while selecting for "
                + (pawn?.LabelShort ?? "a pawn") + " and stood down for this scan. " + kept
                + " The stack trace below shows the actual source (HD's own code, or a vanilla/compat call it "
                + "made); please report it.\n" + ex,
                (seam + "|degraded").GetHashCode()); // log once per session, never floods a per-scan repeat
        }
    }
}
