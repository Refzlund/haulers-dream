namespace HaulersDream.Core
{
    /// <summary>
    /// How a fault observed at the work-selection seam was attributed to a work giver. The ordering is the
    /// ordering of the evidence's strength.
    /// </summary>
    public enum GiverAttribution
    {
        /// <summary>A frame in the exception's OWN captured stack named a type deriving from the work-giver
        /// base. Self-contained evidence that this giver was on the path between the throw site and the
        /// observing method — but NOT evidence that a mod is involved with it, which is a separate
        /// question the policy asks of both routes alike.</summary>
        FrameWalk,

        /// <summary>Nothing in the exception named a giver; the answer came from the recorded scan context (the
        /// last giver vanilla cleared for this pawn during THIS call). Circumstantial: vanilla carries
        /// <c>scannerWhoProvidedTarget</c> across loop iterations at one priority, so the giver that actually
        /// produced the target can be an earlier one — and a throw from BEFORE the loop, or from a postfix
        /// running after it, has no giver context at all.</summary>
        ScanContext,

        /// <summary>Neither route named a giver.</summary>
        None
    }

    /// <summary>Why a fault at the work-selection seam did, or did not, switch a work giver off for the session.</summary>
    public enum QuarantineVerdict
    {
        /// <summary>Switch this work giver off for the rest of the session.</summary>
        Quarantine,

        /// <summary>Nothing named a giver, so there is nothing to switch off. Never guess.</summary>
        NoGiverAttributed,

        /// <summary>The giver is Hauler's Dream's own. HD's bugs stay loud and stay in the rotation.</summary>
        OwnGiver,

        /// <summary>Nothing links a MOD to this fault: no mod patches the giver's job entry points, and the
        /// exception carries no mod-owned frame. Most likely an unpatched vanilla giver choking on modded DATA —
        /// a real fault, but not one to switch a whole work type off for. Containment alone already keeps the
        /// pawn working.</summary>
        NoModImplicated,

        /// <summary>The evidence is sufficient but this giver has not faulted often enough yet.</summary>
        BelowThreshold
    }

    /// <summary>
    /// Decides whether a fault observed at <c>JobGiver_Work.TryIssueJobPackage</c> may switch the responsible
    /// work giver off for the session (issue #235).
    ///
    /// <para><b>Why this is a policy and not an <c>if</c>.</b> Disabling a work giver is the most destructive
    /// thing Hauler's Dream does to a save — it also silently blocks the player's own right-click "prioritise"
    /// for that work, because vanilla gates the priority path on the same <c>PawnCanUseWorkGiver</c> — so getting
    /// it wrong reproduces the very symptom the feature exists to fix, with HD as the cause. The decision folds
    /// four independent facts, so it belongs where each refusal can be pinned by a named test rather than
    /// inferred from a chain of early returns inside a Harmony finalizer.</para>
    ///
    /// <para><b>The question is "is a MOD demonstrably implicated in THIS giver?", not "does this giver type
    /// belong to a mod?".</b> That distinction is the whole rule, and the wrong question fails in both
    /// directions:
    /// <list type="bullet">
    /// <item>Requiring the giver's own assembly to be a mod's locks out the reported shape itself. In #235 the
    /// giver that had to leave the rotation was <c>RimWorld.WorkGiver_ConstructFinishFrames</c> — a VANILLA type,
    /// owned by nobody — postfixed by a mod. A mod reaches a player through a vanilla giver far more often than
    /// through one of its own.</item>
    /// <item>Accepting a giver purely because the exception's frames named it blames vanilla for modded DATA: a
    /// modded <c>ThingDef</c> that makes an UNPATCHED vanilla giver throw resolves a real
    /// <c>RimWorld.WorkGiver_*</c> frame, and switching that work type off would tell the player RimWorld itself
    /// is broken.</item>
    /// </list>
    /// Both routes therefore clear the same bar: either a mod owns a Harmony patch on this giver's job entry
    /// points, or the exception carries a mod-owned frame. Each is a positive link between a mod and this giver;
    /// neither is satisfied by a vanilla giver merely being present in the stack.</para>
    ///
    /// <para>Pure and allocation-free (an enum plus bools in, an enum out); the Verse layer gathers the facts,
    /// owns the wording, and performs the switch-off.</para>
    /// </summary>
    public static class WorkGiverQuarantinePolicy
    {
        /// <summary>
        /// How many faults one work-giver type may produce before it is switched off for the session. Above 1 so
        /// a genuine one-off (a transient null during map generation, a single malformed thing) does not cost the
        /// player a whole work type; low enough that a giver which is actually broken reaches it within about a
        /// second of scanning, long before the colony notices.
        /// </summary>
        public const int FaultsBeforeQuarantine = 3;

        /// <summary>
        /// Decide what this fault does to the named work giver.
        /// </summary>
        /// <param name="attribution">How the giver was named — see <see cref="GiverAttribution"/>. Only
        /// <see cref="GiverAttribution.None"/> is decisive here; the other two face the same evidence bar,
        /// because a frame naming a giver says nothing about whether a mod is involved with it.</param>
        /// <param name="giverIsHaulersDream">The named giver's type lives in one of Hauler's Dream's own
        /// assemblies. Refused unconditionally: HD's bugs are HD's to fix, not to hide.</param>
        /// <param name="giverIsPatchedByAMod">Some mod other than Hauler's Dream owns a Harmony patch on this
        /// giver's job entry points (<c>JobOnThing</c> / <c>JobOnCell</c> / <c>HasJobOnThing</c>), so a mod's own
        /// code genuinely runs inside the call that threw — whoever declared the giver.</param>
        /// <param name="originIsModOwned">The exception's own frames contain at least one frame owned by a mod
        /// other than Hauler's Dream (and not by Harmony's shared plumbing, which every mod runs through).</param>
        /// <param name="faultCount">This giver type's fault tally for the session, counting this fault.</param>
        /// <returns>The verdict, with the refusal reason when it is not <see cref="QuarantineVerdict.Quarantine"/>.</returns>
        public static QuarantineVerdict Decide(GiverAttribution attribution, bool giverIsHaulersDream,
            bool giverIsPatchedByAMod, bool originIsModOwned, int faultCount)
        {
            if (attribution == GiverAttribution.None)
                return QuarantineVerdict.NoGiverAttributed;
            if (giverIsHaulersDream)
                return QuarantineVerdict.OwnGiver;
            if (!giverIsPatchedByAMod && !originIsModOwned)
                return QuarantineVerdict.NoModImplicated;
            // AT OR ABOVE, not exactly at. The evidence facts above genuinely vary from fault to fault (a later
            // throw can carry a mod-owned frame the first ones did not), so exact equality would permanently
            // exempt a giver whose first faults happened to be refused: fault four with good evidence would miss
            // the threshold and the giver could never be switched off again all session. Re-announcement is
            // prevented where it belongs — the caller inserts into the quarantine set once — not by arithmetic.
            if (faultCount < FaultsBeforeQuarantine)
                return QuarantineVerdict.BelowThreshold;
            return QuarantineVerdict.Quarantine;
        }
    }
}
