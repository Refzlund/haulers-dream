namespace HaulersDream.Core
{
    /*
        ──────────────────────────────────────────────
                   Haul commitment decision
        ──────────────────────────────────────────────
        The pluggable seam of the concurrency harness (see HaulConcurrencySim): what one hauler can SEE,
        and the rule that turns that into "how many units do I commit to this destination".

        → KEY: the harness deliberately owns NO rule of its own. The whole reason the over-haul bug has
          shipped three fixes and come back three times is that every fix was graded against the rule in
          isolation. Here the rule is an argument, so the SAME simulation grades today's behaviour and a
          candidate replacement, and the two can be compared instead of asserted about.
        → GOTCHA: HaulSight is the ENTIRE input surface. A candidate rule that needs a figure not listed
          here cannot be graded — and that missing figure is itself the finding, because the recurring
          failure has never been a wrong rule, it has been right rules fed wrong arguments.
    */

    /// <summary>
    /// Everything one hauler can see at the instant it decides how much cargo to commit toward a single
    /// destination.
    ///
    /// <para>Every field is a plain unit count with no game type behind it, so a rule written against this
    /// runs headlessly. The split between <see cref="FreeCapacity"/> (read live) and
    /// <see cref="UnitsEnroute"/> (possibly a stale snapshot) is not incidental — it mirrors the real
    /// asymmetry in the mod, where cell space is re-read on every plan while the in-flight figure is
    /// memoised per tick, and that asymmetry is where the residual over-haul lives.</para>
    /// </summary>
    public readonly struct HaulSight
    {
        /// <summary>Which hauler is deciding. Opaque: the simulation only ever compares ids for equality,
        /// never orders by them, so a trace cannot depend on how ids were assigned.</summary>
        public readonly int PawnId;

        /// <summary>The simulation clock when this decision is taken. Carried because the real in-flight
        /// figure is memoised per tick, so a rule may legitimately need to know whether it is reading a
        /// fresh observation or one frozen earlier in the same tick.</summary>
        public readonly int Tick;

        /// <summary>Units the destination can physically still take right now: its capacity minus what has
        /// already LANDED. Nothing in flight is subtracted here.
        ///
        /// <para>This is the number every previous fix decided from, and it is the same number for every
        /// pawn planning before the first delivery arrives — which is exactly why it cannot tell them
        /// apart. Zero means genuinely full.</para></summary>
        public readonly int FreeCapacity;

        /// <summary>Units that haulers OTHER than this one have committed to this destination and not yet
        /// delivered.
        ///
        /// <para>Excludes the deciding pawn's own commitment (that is <see cref="OwnUnitsEnroute"/>) so a
        /// re-plan is stable, matching how the mod's own accounting already works on both the storage side
        /// and the transport-manifest side. May be STALE — under a per-tick visibility model it is frozen
        /// at the tick's first decision, so a pawn handed work later in the same tick is invisible.</para></summary>
        public readonly int UnitsEnroute;

        /// <summary>This pawn's OWN live commitment, held out of <see cref="UnitsEnroute"/>.
        ///
        /// <para>Non-zero only when the pawn re-plans while an earlier commitment of its own is still live.
        /// Both mistakes are expressible and both are real: a rule that never adds it back shrinks its own
        /// allowance on every re-plan, and one that counts it twice makes the pawn compete with itself.</para></summary>
        public readonly int OwnUnitsEnroute;

        /// <summary>Units this pawn would take if the destination looked bottomless — a full source stack,
        /// or whatever its hands and mass budget allow. Deciding how much of this appetite to keep is the
        /// rule's entire job.</summary>
        public readonly int Desire;

        /// <summary>Assemble one hauler's view. Values are taken verbatim; nothing is clamped or repaired
        /// here, because a rule that mishandles a nonsensical input (a negative count, a sentinel that
        /// means "unbounded") must be able to fail visibly rather than be rescued by the harness.</summary>
        /// <param name="pawnId">Identity of the deciding hauler.</param>
        /// <param name="tick">Simulation clock at the decision.</param>
        /// <param name="freeCapacity">Capacity minus landed units, in-flight loads NOT subtracted.</param>
        /// <param name="unitsEnroute">Other haulers' undelivered commitments, as visible to this pawn.</param>
        /// <param name="ownUnitsEnroute">This hauler's own undelivered commitment, excluded from the above.</param>
        /// <param name="desire">What this hauler would take against an unlimited destination.</param>
        public HaulSight(int pawnId, int tick, int freeCapacity, int unitsEnroute, int ownUnitsEnroute, int desire)
        {
            PawnId = pawnId;
            Tick = tick;
            FreeCapacity = freeCapacity;
            UnitsEnroute = unitsEnroute;
            OwnUnitsEnroute = ownUnitsEnroute;
            Desire = desire;
        }
    }

    /// <summary>
    /// The rule under test: given one hauler's view of a destination, how many units does it commit?
    ///
    /// <para>Return 0 (or less) to stand down — the harness reads that as "no job was created", so the pawn
    /// never sets off. A positive return becomes both a live commitment other pawns may see and cargo the
    /// pawn will try to deposit.</para>
    ///
    /// <para><b>The harness never clamps the return against the destination's capacity.</b> Clamping is the
    /// bug being hunted; a simulation that quietly repaired an over-commitment could only ever report
    /// success. A negative return is recorded verbatim in the trace and booked as zero, since negative
    /// cargo is not physically expressible.</para>
    /// </summary>
    /// <param name="sight">Everything this hauler can see. Complete: no other input reaches the rule.</param>
    /// <returns>Units committed toward the destination.</returns>
    public delegate int HaulCommitDecision(HaulSight sight);
}
