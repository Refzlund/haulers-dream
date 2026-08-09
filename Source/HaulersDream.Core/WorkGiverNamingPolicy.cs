namespace HaulersDream.Core
{
    /// <summary>
    /// Whether a fault contained at the work-selection seam may put a MOD'S NAME in front of the player.
    /// Two outcomes only: there is evidence that names someone, or there is not.
    /// </summary>
    public enum QuarantineNaming
    {
        /// <summary>The exception's own frames placed a mod's compiled code on the path between the throw site
        /// and the method that observed the fault. That mod may be named — as the most likely source, never as
        /// proven, because a trace records what a method called and not who called it.</summary>
        NameTheMod,

        /// <summary>Nothing in the exception placed any mod's code at the fault. The player is told the work type
        /// was switched off and that the source could not be identified from the error, and is pointed at the
        /// log. NOBODY is named.</summary>
        SourceUnknown
    }

    /// <summary>
    /// Decides WHO, if anyone, Hauler's Dream may name to the player as the source of a fault it contained at
    /// <c>JobGiver_Work.TryIssueJobPackage</c> (issue #235).
    ///
    /// <para><b>Separate from <see cref="WorkGiverQuarantinePolicy"/> on purpose.</b> That policy answers "may a
    /// work type be switched off?", and it is satisfied by INVOLVEMENT evidence — a mod is hooked into this
    /// giver's job call. This one answers "may a mod be blamed out loud?", and involvement is not enough for
    /// that. The two questions have different bars because they have different costs when wrong: switching a work
    /// type off is recoverable by restarting, while telling thousands of players that a named third-party mod
    /// crashed their colony is not. Hauler's Dream may therefore switch a hooked work giver off while saying it
    /// cannot tell who is at fault; that asymmetry is deliberate, not an oversight.</para>
    ///
    /// <para><b>Why only a frame may name anyone.</b> A resolvable mod-owned frame is the one fact that
    /// distinguishes <i>that mod's code ran on the path to this fault</i> from <i>that mod was merely installed
    /// nearby</i>. Everything else Hauler's Dream can observe is set membership: who is hooked into a method, who
    /// declared a type. In the report that produced this rule, the method that threw carried one mod's transpiler,
    /// two of Hauler's Dream's own postfixes and a Hauler's Dream finalizer, and no ordering between them is
    /// recoverable from ownership facts. The shipped code answered "who is responsible?" by listing the patch
    /// owners, dropping Hauler's Dream's own id from the list and printing whoever remained — which would have
    /// printed a third party's name even if Hauler's Dream's own postfix had thrown. Naming from a set that we
    /// removed ourselves from is not evidence; it is a process of elimination with the suspect excluded.</para>
    ///
    /// <para><b>The two rejected facts are PARAMETERS, not omissions.</b> They are passed in and deliberately not
    /// consulted, so that "a patch owner may not become a name" and "the giver's own assembly may not become a
    /// name" are properties a test can state and a future edit cannot quietly undo. A rule that simply took one
    /// bool could not be tested for what it refuses to do.</para>
    ///
    /// <para>Pure and allocation-free (bools in, an enum out); the Verse layer gathers the facts, resolves the
    /// mod's display name, and owns the wording.</para>
    /// </summary>
    public static class WorkGiverNamingPolicy
    {
        /// <summary>
        /// Decide whether this fault may be attributed to a named mod in what the player reads.
        /// </summary>
        /// <param name="exceptionCarriesModOwnedFrame">A frame in the exception's own captured stack resolved to
        /// an assembly some running mod ships, other than Hauler's Dream's two and other than Harmony's shared
        /// plumbing (which every patched call passes through and which therefore implicates nobody). THE ONLY
        /// FACT THAT NAMES ANYONE. False does not mean no mod was involved — a patched method runs as a
        /// <c>DynamicMethod</c> whose frame resolves to nothing, and any Exception-returning finalizer deeper in
        /// the chain has already reset the trace — which is exactly why a false leads to "unknown" and never to
        /// naming whoever is left.</param>
        /// <param name="aModPatchesTheGiver">Some mod owns a Harmony patch on this giver's job entry points.
        /// PASSED AND IGNORED: it proves who is HOOKED, never who threw, and several mods (Hauler's Dream
        /// included) are routinely hooked into the same method at once. It still carries the quarantine DECISION
        /// in <see cref="WorkGiverQuarantinePolicy"/>; it just may not become a name.</param>
        /// <param name="theGiverTypeBelongsToAMod">The work-giver type itself was declared by a mod rather than by
        /// RimWorld. PASSED AND IGNORED: shipping the type says nothing about whose code ran inside a call that
        /// several mods have patched, and the reported case was a vanilla type all along.</param>
        /// <returns><see cref="QuarantineNaming.NameTheMod"/> only under frame evidence; otherwise
        /// <see cref="QuarantineNaming.SourceUnknown"/>.</returns>
        public static QuarantineNaming Decide(bool exceptionCarriesModOwnedFrame, bool aModPatchesTheGiver,
            bool theGiverTypeBelongsToAMod)
            => exceptionCarriesModOwnedFrame ? QuarantineNaming.NameTheMod : QuarantineNaming.SourceUnknown;
    }
}
