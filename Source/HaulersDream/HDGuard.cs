using System;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Shared log-and-RETHROW body for the Harmony <c>Finalizer</c>s that guard the vanilla job/work/carry seams
    /// HD postfixes (and one prefix) hook. Those seams — <c>JobGiver_*.TryGiveJob</c>,
    /// <c>JobGiver_Work.TryIssueJobPackage</c>, <c>WorkGiver_HaulGeneral.JobOnThing</c>,
    /// <c>Pawn_CarryTracker.TryStartCarry</c> — have no vanilla try/catch, so an unhandled throw from an HD hook
    /// (or a downstream vanilla/compat call it makes) propagates uncaught and can silently halt a whole CATEGORY of
    /// a pawn's behaviour (all hauling, all cleaning, even rest/eat) with nothing in the log pointing at HD.
    ///
    /// The repo's standing rule is no-swallow: a real fault must stay visible as a red error. So this does NOT
    /// catch-and-continue — it LOGS once (deduped per seam, so a per-scan repeat can't flood the log) with the
    /// pawn + the concrete consequence, then RETURNS the exception so Harmony RE-THROWS it. Net effect: identical
    /// propagation to before (the fault still surfaces, RimWorld's own handler still logs it), but now there is a
    /// pawn-and-consequence breadcrumb at the seam instead of only an anonymous stack.
    ///
    /// HONEST ATTRIBUTION: a Harmony Finalizer wraps the WHOLE patched method, so the caught throw may originate
    /// in HD's own hook, in a downstream vanilla/compat call HD makes, OR in vanilla / another mod patching the
    /// same method. The message therefore does NOT assert HD is the cause — it states HD patches the method and
    /// points at the stack trace (which names the real source). (fix/mix #3b hardening — the "unguarded throw at
    /// a shared seam" bad-pattern the global-hauling-stall report flagged.)
    /// </summary>
    public static class HDGuard
    {
        public static Exception SeamThrew(Exception ex, string seam, Pawn pawn, string consequence)
        {
            if (ex != null)
                Log.ErrorOnce("[Hauler's Dream] An exception surfaced at " + seam + " (a method HD patches) "
                    + "while selecting for " + (pawn?.LabelShort ?? "a pawn") + " — " + consequence
                    + " The stack trace below shows the actual source (HD, vanilla, or another mod on the same "
                    + "method); if it implicates HaulersDream, please report it.\n" + ex,
                    seam.GetHashCode()); // stable per-seam key (net48 string hashing isn't randomized) -> log once
            return ex; // rethrow: keep the fault visible, never swallow
        }
    }
}
