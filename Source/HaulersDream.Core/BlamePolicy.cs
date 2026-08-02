namespace HaulersDream.Core
{
    /// <summary>
    /// How Hauler's Dream is involved in an exception observed at a method it patches, as far as the evidence
    /// available AT THAT SEAM can establish. The ordering of the members is the ordering of the evidence's
    /// strength, not a severity scale.
    /// </summary>
    public enum HdInvolvement
    {
        /// <summary>A Hauler's Dream frame is present in the exception's captured stack FRAMES — never in
        /// rendered trace text, see <c>HDFault.InvolvesHaulersDream</c>: HD's own code was on the path between
        /// the throw site and the observing method. Positive, self-contained evidence.</summary>
        FrameInTrace,

        /// <summary>No HD frame appears, but HD owns a TRANSPILER on the observing method: HD rewrote that
        /// method's IL in place, so a fault its edits introduced throws with only the original method's frames
        /// and can never produce a separate HD frame.</summary>
        Transpiled,

        /// <summary>No HD frame appears and HD does not transpile the method, but an HD wrapper is a live
        /// ANCESTOR on this thread's call stack — HD invoked the code that led here. A captured trace spans the
        /// throw site down to the observing method only, so it structurally cannot show a caller ABOVE that
        /// method; HD has to be told, and this is the value it is told with.</summary>
        AncestorWrapper,

        /// <summary>None of the above was OBSERVED. Deliberately not named "NotInvolved": the evidence a
        /// finalizer can see is incomplete by construction, so this value means "no involvement was detected",
        /// never "HD is innocent". Nothing may render it as a categorical denial.</summary>
        NotObserved
    }

    /// <summary>
    /// Decides how Hauler's Dream describes its OWN involvement in an exception observed at a method it patches.
    ///
    /// <para><b>The load-bearing rule (issue #236): the absence of an HD frame is not evidence of HD's
    /// innocence, and no verdict may assert that it is.</b> A Harmony finalizer observes the exception's
    /// CAPTURED stack trace, which spans the throw site down to the method the finalizer runs on. That trace can
    /// never contain HD's prefix on that method (it already returned), HD's postfix on it (it has not run yet),
    /// or any HD frame ABOVE it (a caller is never in a callee's captured trace). So "no HaulersDream. frame"
    /// is exactly as consistent with "HD is a bystander" as with "HD called this from six frames up" — which is
    /// precisely the #236 shape, where HD's haul-placement wrapper was the ancestor and HD nonetheless printed
    /// a categorical "This is NOT a Hauler's Dream bug".</para>
    ///
    /// <para><b>The consequence for design: prefer facts HD owns DETERMINISTICALLY over sniffing a stack that
    /// structurally cannot answer the question.</b> Whether HD transpiles the method is a registry lookup HD
    /// can always answer; whether an HD wrapper is currently on the call stack is something HD can record as it
    /// enters. Both are inputs here. The frame check stays, because a positive hit is real evidence — but only a
    /// positive hit is, and a miss downgrades the claim rather than inverting it. That check reads FRAME OBJECTS
    /// and never rendered trace text; see the <see cref="Classify"/> parameter doc for why text is unsound in
    /// both directions.</para>
    ///
    /// <para>Pure and allocation-free (bools in, an enum out); the Verse layer gathers the three facts, and the
    /// wording lives here in <see cref="Describe"/> so the #236 contract is testable.</para>
    /// </summary>
    public static class BlamePolicy
    {
        /// <summary>
        /// Classify HD's involvement from the three independent facts a finalizer can establish, strongest
        /// evidence first. Every input is a POSITIVE observation, so a false input only ever means "not
        /// observed", never "ruled out" — which is why <see cref="HdInvolvement.NotObserved"/> is the fallthrough
        /// and not a "definitely not HD" verdict.
        /// </summary>
        /// <param name="hdFrameInTrace">A Hauler's Dream frame is present in the exception's captured stack
        /// FRAMES. True is conclusive that HD's code ran on the observed path; false proves nothing (see the
        /// type remark).
        /// <para><b>Read this from frame objects, NEVER from rendered trace text</b> — the caller uses
        /// <c>HDFault.InvolvesHaulersDream</c>, which walks <c>new StackTrace(ex, false).GetFrames()</c>.
        /// Scanning text is unsound in BOTH directions and was the #236 defect: Harmony's deduplicating
        /// renderer returns a "duplicate stacktrace" placeholder for any render after the first (false
        /// negative — and HD's tagger runs at <c>Priority.Last</c>, so a foreign finalizer can take that first
        /// slot), while Harmony's enhanced trace ANNOTATES each frame with the mods that patch it, printing
        /// HD's own type names into the trace of every method HD patches (false positive, asserting a fact
        /// that is untrue). Frame objects are produced by neither.</para></param>
        /// <param name="hdTranspilesMethod">HD owns a transpiler on the observing method, so HD's edits are
        /// baked into that method's own frames and cannot appear separately.</param>
        /// <param name="hdWrapperIsAncestor">An HD wrapper is currently a live ancestor on this thread's call
        /// stack — the fact the captured trace structurally cannot carry, so HD records it as it enters.</param>
        /// <returns>The strongest form of involvement the evidence supports.</returns>
        public static HdInvolvement Classify(bool hdFrameInTrace, bool hdTranspilesMethod, bool hdWrapperIsAncestor)
        {
            if (hdFrameInTrace)
                return HdInvolvement.FrameInTrace;
            if (hdTranspilesMethod)
                return HdInvolvement.Transpiled;
            if (hdWrapperIsAncestor)
                return HdInvolvement.AncestorWrapper;
            return HdInvolvement.NotObserved;
        }

        /// <summary>
        /// The sentence the exception breadcrumb prints for a verdict. Developer-facing raw English (every
        /// HDLog channel is), so no translation keys.
        ///
        /// <para>These four strings ARE the deliverable of issue #236, which is why they live HERE and not in
        /// the Verse glue that prints them: the wording contract is only enforceable where the headless tests
        /// can read it. Before #236 the last branch read "This is NOT a Hauler's Dream bug" — asserted from a
        /// stack that structurally could not have carried the evidence, and printed while HD's own
        /// haul-placement wrapper was six frames out. NONE of these may assert a categorical negative; each
        /// describes what was OBSERVED and how strong that evidence is.</para>
        ///
        /// <para>Three phrasing rules learned the hard way. (a) In a PRINTED trace, callers appear BELOW the
        /// throw site, so "above" reads backwards — say "further out on the call stack" and point at the trace
        /// explicitly. (b) Only claim what was measured: the ancestor marker covers ONE wrapper, so the
        /// no-involvement text names that wrapper rather than implying HD tracks its whole call stack. (c) The
        /// trace printed underneath is Harmony's ENHANCED one, whose per-frame annotation lines
        /// (<c>- PREFIX/POSTFIX/TRANSPILER/FINALIZER {owner}: ...</c>) name the mods that PATCH each frame, not
        /// the code that ran — so HD's name appears on every HD-patched frame and a "no HD frame" sentence would
        /// read as a contradiction of the text right below it unless the difference is spelled out.</para>
        /// </summary>
        /// <param name="involvement">The verdict from <see cref="Classify"/>.</param>
        /// <returns>A complete sentence, never null or empty, for any input including an out-of-range cast.</returns>
        public static string Describe(HdInvolvement involvement)
        {
            switch (involvement)
            {
                case HdInvolvement.FrameInTrace:
                    return "Hauler's Dream's own code IS in this exception's stack, so it may be involved, though "
                        + "the original method or another mod's patch on it could still be the real cause.";
                case HdInvolvement.Transpiled:
                    return "Hauler's Dream TRANSPILES this method (it edits the method's IL), so even though its "
                        + "own code is not a separate frame in the stack it could still be involved; the original "
                        + "method or another mod's patch on it may also be the cause.";
                case HdInvolvement.AncestorWrapper:
                    return "Hauler's Dream is further out on the call stack than this method — one of its marked "
                        + "item-placement paths called into the code that threw — and it does not appear as a "
                        + "frame in the "
                        + "trace printed below, because a stack records what a method called, never who called "
                        + "it. (Lines starting with '- PREFIX', '- POSTFIX', '- TRANSPILER' or '- FINALIZER' are "
                        + "Harmony listing which mods PATCH that frame, not which code ran, so Hauler's Dream's "
                        + "name can appear there without any of its code having run.) That wrapper only invokes "
                        + "the original action unchanged, so this is not by itself blame; it means Hauler's Dream "
                        + "was involved in getting here.";
                case HdInvolvement.NotObserved:
                    return "Nothing here points at Hauler's Dream: no Hauler's Dream frame was found in this "
                        + "exception's stack, Hauler's Dream does not rewrite this method's code, and none of the "
                        + "item-placement paths it marks — the only ancestors it tracks — is further out on the "
                        + "call stack. That is NOT proof Hauler's Dream is uninvolved. A stack records what a method "
                        + "called, never who called it, so the trace printed below cannot show a Hauler's Dream "
                        + "prefix that already returned, a postfix that has not run yet, or any Hauler's Dream "
                        + "code further out — including Hauler's Dream's own jobs, which are not tracked. (Lines "
                        + "starting with '- PREFIX', '- POSTFIX', '- TRANSPILER' or '- FINALIZER' are Harmony "
                        + "listing which mods PATCH that frame, not which code ran, so Hauler's Dream's name "
                        + "appearing there is not a frame.) On the evidence available here the cause is most "
                        + "likely the original method itself or another mod patching it.";
                default:
                    // An out-of-range value (a future member, a bad cast). Say ONLY that, and never borrow
                    // NotObserved's text: that sentence asserts three specific negative findings — no HD frame,
                    // no transpiler, no tracked ancestor — none of which was established on this path.
                    return "Hauler's Dream could not classify its involvement in this fault, so nothing has been "
                        + "established either way — it may or may not be involved. The trace below is the only "
                        + "evidence here.";
            }
        }
    }
}
