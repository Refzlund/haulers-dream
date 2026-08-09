using System;
using System.Collections.Concurrent;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The three sanctioned fault boundaries for the vanilla job/work/carry seams HD patches. Those seams
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
    /// <para><b><see cref="SeamContained"/> (log + SUPPRESS)</b>, for the narrow case where vanilla itself left a
    /// seam unguarded AND the throw would cost the pawn an entire behaviour category every scan. Same report as
    /// SeamThrew, but it returns null so Harmony drops the exception. This is only correct where the caller has
    /// verified the fault is NOT HD's (see <c>HDFault.InvolvesHaulersDream</c>) — HD's own bugs must stay loud —
    /// and it mirrors the shipped <c>Patch_JobGiver_Work_WorkGiverResilient</c> trade from issue #7: contain a
    /// foreign failure at a controlled boundary, log it loudly and attributably, and keep the save playable.</para>
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
    /// HONEST ATTRIBUTION (all three): a caught throw may originate in HD's own hook, in a downstream
    /// vanilla/compat call HD makes, OR in vanilla / another mod patching the same method. The messages therefore
    /// do NOT assert HD is the cause. They name the innermost non-vanilla, non-HD frame when the stack has one
    /// (<c>HDFault.DescribeOrigin</c>), and say plainly that the origin could not be determined when it does not
    /// (<c>HDFault.OriginUnknown</c>) — never converting silence into a verdict, the #236 contract.
    /// (fix/mix #3b hardening; the SeamDegraded boundary is the #122 hardening; the report ORDERING below is the
    /// #235 hardening.)
    /// </summary>
    public static class HDGuard
    {
        // How often the same breadcrumb key may reach the disk trail. The trail is size-capped and a seam fault
        // can recur every scan (issue #235 logged 2673 throws in one session), so an unconditional line per
        // occurrence would evict the rest of HD's history from the tail a bug report ships. First occurrence in
        // full, then a handful of checkpoints — the same shape HDLog uses for the universal breadcrumb.
        private static readonly ConcurrentDictionary<string, int> breadcrumbCounts = new ConcurrentDictionary<string, int>();

        private static bool IsRepeatCheckpoint(int count) => count == 10 || count == 100 || count == 1000 || count % 10000 == 0;

        /// <summary>
        /// Report a throw observed at a seam HD patches and RETHROW it (the caller returns this from a Harmony
        /// <c>Finalizer</c>). Use when the fault must stay loud: HD's own code is implicated, or nothing about
        /// the seam justifies suppressing a real error.
        /// </summary>
        /// <param name="ex">The observed exception. No-op returning null when null.</param>
        /// <param name="seam">Stable seam name, also the per-session dedupe key.</param>
        /// <param name="pawn">The pawn being selected for; may be null.</param>
        /// <param name="consequence">What this costs the pawn, stated concretely.</param>
        /// <returns>The same exception, so Harmony re-raises it.</returns>
        public static Exception SeamThrew(Exception ex, string seam, Pawn pawn, string consequence)
        {
            if (ex == null)
                return null;
            Report(ex, seam, pawn, consequence, KeyFor(seam, "threw", ex));
            return ex; // rethrow: keep the fault visible, never swallow
        }

        /// <summary>
        /// Report a throw observed at a seam HD patches and CONTAIN it (the caller returns this from a Harmony
        /// <c>Finalizer</c>, which then drops the exception). Only valid once the caller has established the
        /// fault is not HD's own, and only where vanilla left the seam unguarded so the throw would otherwise
        /// cost the pawn a whole behaviour category on every scan.
        /// </summary>
        /// <param name="ex">The observed exception. No-op returning null when null.</param>
        /// <param name="seam">Stable seam name; the dedupe key is derived from it and is distinct from
        /// <see cref="SeamThrew"/>'s, so a contained foreign fault and a rethrown HD fault at the same seam
        /// cannot eat each other's one-shot report.</param>
        /// <param name="pawn">The pawn being selected for; may be null.</param>
        /// <param name="consequence">What HD did instead and what it costs, stated concretely.</param>
        /// <returns>Always null, so Harmony suppresses the exception.</returns>
        public static Exception SeamContained(Exception ex, string seam, Pawn pawn, string consequence)
        {
            if (ex == null)
                return null;
            Report(ex, seam, pawn, consequence, KeyFor(seam, "contained", ex));
            return null; // handled here: the caller has already substituted a safe result
        }

        /// <summary>
        /// The per-session one-shot key for a report: seam + channel + EXCEPTION TYPE.
        ///
        /// <para>The channel keeps a contained foreign fault and a rethrown HD fault at the same seam from eating
        /// each other's one-shot. The exception type keeps a SECOND, unrelated fault at that seam from being
        /// hidden by the first — the same per-(method, exception type) keying issue #236 uses for the universal
        /// breadcrumb, and the reason the occurrence-1 gate below does not cost diagnostics.</para>
        /// </summary>
        /// <param name="seam">The stable seam name.</param>
        /// <param name="channel">Which boundary reported it ("threw", "contained", "degraded").</param>
        /// <param name="ex">The exception, read for its type name only (never dereferenced further).</param>
        private static string KeyFor(string seam, string channel, Exception ex)
            => seam + "|" + channel + "|" + HDFault.ExceptionTypeName(ex);

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
            // Key: per seam, per CHANNEL and per EXCEPTION TYPE (see KeyFor). Log.ErrorOnce dedupes globally by
            // the int key, so sharing a key across channels would let whichever fires first eat the other's
            // one-shot for the session — a degraded HD enhancement hiding a later foreign fault's breadcrumb, or
            // vice versa — and sharing across exception types would hide a second, unrelated root cause.
            string key = KeyFor(seam, "degraded", ex);
            // ORDER IS LOAD-BEARING — see Report() for the full rationale. The minimal disk line first, so a
            // fault in the rich build cannot silence the evidence that this boundary ran at all.
            if (Breadcrumb(key) != 1)
                return;
            try
            {
                HDLog.ErrOnce("Hauler's Dream's enhancement at " + seam + " threw while selecting for "
                    + (pawn?.LabelShort ?? "a pawn") + " and stood down for this scan. " + kept
                    + " " + OriginClause(ex) + "\n" + HDFault.Render(ex),
                    key.GetHashCode()); // log once per session, never floods a per-scan repeat
            }
            catch (Exception reportFailed)
            {
                NoteReportFailure(key, reportFailed);
            }
        }

        /// <summary>
        /// The shared report for <see cref="SeamThrew"/> / <see cref="SeamContained"/>: a minimal disk
        /// breadcrumb, then the rich attributed error line.
        ///
        /// <para><b>THE ORDER IS THE FIX (issue #235).</b> In that report this guard produced ZERO output across
        /// 2673 throws while the disk trail was demonstrably alive throughout — and every cheap explanation was
        /// eliminated by the evidence (HDLog.ErrOnce writes to disk unconditionally BEFORE Log.ErrorOnce, and
        /// HDDebugLog drop / truncation / tagger-skip / Log.ErrorOnce dedupe were all ruled out). Two
        /// explanations survived: (a) Harmony never invoked the finalizer at all, or (b) building the rich
        /// message THREW — plausible, because it renders the exception through the same patched machinery that
        /// broke and dereferences a pawn whose state may be exactly what failed, and Harmony silently swallows a
        /// throw out of a finalizer. Emitting a format-free, allocation-light, pawn-free line to disk FIRST both
        /// FIXES (b) — the evidence survives a failed rich build — and DISCRIMINATES between the two: a minimal
        /// line present with the rich line absent means (b); neither line means (a).</para>
        /// </summary>
        /// <param name="ex">The observed exception; already null-checked by the caller.</param>
        /// <param name="seam">The human-readable seam name used in the message.</param>
        /// <param name="pawn">The pawn being selected for; may be null.</param>
        /// <param name="consequence">What this costs, stated concretely.</param>
        /// <param name="key">The dedupe key (breadcrumb key and, hashed, the ErrOnce key).</param>
        private static void Report(Exception ex, string seam, Pawn pawn, string consequence, string key)
        {
            // Only the FIRST occurrence builds the rich message. HDLog.ErrOnce dedupes the CONSOLE by key, but it
            // enqueues its message to the disk trail unconditionally — so on a per-scan repeat the old code wrote
            // a full report (stack included) to a size-capped trail every scan, evicting the rest of HD's history
            // exactly as issue #190 describes. Building the message is also the expensive and throw-prone half
            // (two frame walks + a render), so skipping it on repeats is both cheaper and safer. The recurrence
            // itself stays visible: Breadcrumb notes it at checkpoints.
            if (Breadcrumb(key) != 1)
                return;
            try
            {
                HDLog.ErrOnce("An exception surfaced at " + seam + " (a method HD patches) "
                    + "while selecting for " + (pawn?.LabelShort ?? "a pawn") + " - " + consequence
                    + " " + OriginClause(ex) + "\n" + HDFault.Render(ex),
                    key.GetHashCode()); // stable per-key (net48 string hashing isn't randomized) -> log once
            }
            catch (Exception reportFailed)
            {
                NoteReportFailure(key, reportFailed);
            }
        }

        // The blame clause, built from FRAME OBJECTS (HDFault) rather than rendered trace text, and never
        // categorical. Five cases, because "Hauler's Dream is the mod in this stack", "another mod is", "no mod
        // could be named" and "no frames at all" are different answers, and none of them may be reported as one
        // of the others. This is the #236 contract applied to the seam guards.
        //
        // HAULER'S DREAM IS ASKED ABOUT FIRST, and by name (#235's attribution review). DescribeOrigin skips HD
        // deliberately so it can answer "who ELSE is here?", but reporting only its answer meant an exception
        // whose one mod-owned frame was HD's own printed "no frame in this stack belongs to a mod" — evasive
        // about the single name that was actually present. Honesty about who threw has to cut both ways or it is
        // not honesty, it is a defence.
        private static string OriginClause(Exception ex)
        {
            string own = HDFault.DescribeOwnFrame(ex);
            string origin = HDFault.DescribeOrigin(ex);
            if (own != null && origin != null)
                return "Hauler's Dream's own code IS in this stack (" + own + "), and so is another mod's ("
                    + origin + "). Which of the two threw cannot be settled from here, so treat neither as "
                    + "established - reporting this to Hauler's Dream with the log attached is what lets it be "
                    + "traced. The full stack is below.";
            if (own != null)
                return "The only mod-owned frame in this stack is Hauler's Dream's own (" + own
                    + "), so treat this as a Hauler's Dream bug and report it to Hauler's Dream, with the log "
                    + "attached. The full stack is below.";
            if (origin != null)
                return "The innermost frame belonging to another mod is " + origin
                    + ", so that is the most likely source; please report it there. The full stack is below.";
            if (HDFault.OriginUnknown(ex))
                return "This exception carries no readable stack frames, so where it came from could not be "
                    + "determined here — the game's own error report shows the full call stack.";
            // Frames exist but none belongs to a mod. Deliberately NOT phrased as "only RimWorld and Hauler's
            // Dream frames": DescribeOrigin also skips the .NET runtime, Unity and Harmony's shared plumbing, so
            // that claim would be false for a fault thrown inside Dictionary`2.get_Item or a LINQ iterator — and
            // it would steer the report at Hauler's Dream, which is exactly the false blame #236 removed.
            string innermost = HDFault.DescribeInnermostFrame(ex);
            if (innermost != null)
                return "No frame in this stack belongs to a mod, so the mod at fault could not be identified "
                    + "here; the innermost readable frame is " + innermost + ". That is not proof Hauler's Dream "
                    + "caused it either (a stack records what a method called, never who called it) — the game's "
                    + "own error report shows the full call stack.";
            return "Where this exception came from could not be determined here; the game's own error report "
                + "shows the full call stack.";
        }

        /// <summary>
        /// The minimal, format-free, disk-only line. No exception formatting, no pawn dereference, no
        /// translation, no console call — the point is that NOTHING in it can fault while reporting a fault.
        /// Written on the first occurrence and at a few repeat checkpoints, so a per-scan repeat cannot evict
        /// HD's history from the size-capped trail while the recurrence still stays visible.
        /// </summary>
        /// <param name="key">The per-seam-and-channel key being counted.</param>
        /// <returns>This key's occurrence count, 1 on the first — the caller's gate for building the full
        /// report. Returns 1 if the counter itself failed, deliberately: the whole point of #235 is that the
        /// report went MISSING, so when the cheap channel fails the expensive one should still be attempted.</returns>
        private static int Breadcrumb(string key)
        {
            try
            {
                int count = breadcrumbCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
                if (count == 1)
                    HDDebugLog.Enqueue(DateTime.Now.ToString("MM-dd HH:mm:ss") + " ERR [seam] " + key);
                else if (IsRepeatCheckpoint(count))
                    HDDebugLog.Enqueue(DateTime.Now.ToString("MM-dd HH:mm:ss") + " ERR [seam] " + key
                        + " (recurred " + count + "x this session)");
                return count;
            }
            catch
            {
                // Total-function boundary, the same logger-never-throws policy HDDebugLog documents: this IS the
                // last-resort evidence path, so it must never itself become the thing that fails. There is no
                // remaining channel to report a failure on, and losing one breadcrumb must not cost the caller
                // its rethrow/degrade decision.
                return 1;
            }
        }

        // The rich build failed (the case #235's ordering was designed to survive). Record WHAT failed on the
        // channel that already proved it works, so the next report says "the message build threw" instead of
        // leaving a silent gap. Deliberately terse and self-contained: no HDLog, no Render, no pawn.
        private static void NoteReportFailure(string key, Exception reportFailed)
        {
            try
            {
                HDDebugLog.Enqueue(DateTime.Now.ToString("MM-dd HH:mm:ss") + " ERR [seam] " + key
                    + " - building the full report threw " + reportFailed.GetType().Name
                    + "; the minimal breadcrumb above is all that could be captured.");
            }
            catch
            {
                // As above: nothing further can be reported, and the caller's decision must still proceed.
            }
        }
    }
}
