namespace HaulersDream.Core
{
    /// <summary>Which corpse hauls auto-strip the body (see CorpseStripper).</summary>
    public enum AutoStripMode
    {
        Off,

        /// <summary>Every corpse haul strips — stockpile, grave, cremation, butchering (the default).</summary>
        AllHauls,

        /// <summary>Only hauls where the gear would otherwise be LOST strip: interment in a grave or
        /// casket, and corpse bills (cremation, butchering). A plain stockpile haul leaves the body
        /// dressed — useful when corpses are stored for later and you want the gear to travel with them.</summary>
        DisposalOnly,
    }

    /// <summary>What to do with a TAINTED apparel piece when stripping a hauled corpse.</summary>
    public enum TaintedApparelPolicy
    {
        /// <summary>Strip it and treat it like the rest of the loot (worth wearing in a pinch, selling, or smelting).</summary>
        Take,

        /// <summary>Don't strip it — it stays on the body and goes wherever the body goes (a cremated
        /// corpse takes it along, which disposes of it cleanly).</summary>
        LeaveOnCorpse,

        /// <summary>Strip it onto the ground and forbid it, so nobody hauls the rags home.</summary>
        DropAndForbid,

        /// <summary>Strip it and destroy it on the spot. The only place this mod ever destroys an item —
        /// an explicit player opt-in for "I never want to see tainted clothes again".</summary>
        Destroy,
    }

    /// <summary>
    /// Pure decision logic for stripping (unit-tested headlessly). Untainted gear is always loot; tainted
    /// apparel follows the player's per-category policy — smeltable pieces (metal armor: smelter value even
    /// when tainted) and non-smeltable pieces (cloth rags: the classic post-battle clutter) configured apart.
    /// </summary>
    public static class StripPolicy
    {
        /// <summary>The action for one apparel piece. Anything untainted is simply taken.</summary>
        public static TaintedApparelPolicy ApparelAction(bool tainted, bool smeltable,
            TaintedApparelPolicy smeltablePolicy, TaintedApparelPolicy nonSmeltablePolicy)
        {
            if (!tainted)
                return TaintedApparelPolicy.Take;
            return smeltable ? smeltablePolicy : nonSmeltablePolicy;
        }

        /// <summary>
        /// Should a stripped/loose piece be LEFT where it is (never hauled to storage) rather than taken home?
        /// True only for the two "keep it out of storage" resolutions of <see cref="ApparelAction"/> —
        /// <see cref="TaintedApparelPolicy.LeaveOnCorpse"/> and <see cref="TaintedApparelPolicy.DropAndForbid"/>.
        /// <see cref="TaintedApparelPolicy.Take"/> is hauled, and <see cref="TaintedApparelPolicy.Destroy"/>
        /// resolves false too: a still-loose Destroy piece fell through the strip loop's quest/relic/merged guard
        /// and is then treated as loot (Take), so HD should haul it like the guard's Take fallback does — not
        /// strand it. This is the decision every runtime intake gate consults for a loose tainted apparel piece
        /// (issue #187a: a keep-on-corpse piece that ended up on the ground was being re-hauled).
        /// </summary>
        public static bool LeaveWhereItIs(bool tainted, bool smeltable,
            TaintedApparelPolicy smeltablePolicy, TaintedApparelPolicy nonSmeltablePolicy)
            => IsLeavePolicy(ApparelAction(tainted, smeltable, smeltablePolicy, nonSmeltablePolicy));

        /// <summary>
        /// Would HD REFUSE to take this apparel piece OFF the body — i.e. does it stay worn, whoever orders the
        /// strip? True for exactly one resolution of <see cref="ApparelAction"/>,
        /// <see cref="TaintedApparelPolicy.LeaveOnCorpse"/>: the piece is never dropped, so it travels with the
        /// corpse. <see cref="TaintedApparelPolicy.DropAndForbid"/> and <see cref="TaintedApparelPolicy.Destroy"/>
        /// both come OFF first (they are then forbidden / destroyed on the ground), so they are false here — which
        /// is what separates this from <see cref="LeaveWhereItIs"/>, the LOOSE-piece question ("already on the
        /// ground: haul it home or not?").
        ///
        /// <para>This is the single definition of HD's per-piece "don't strip it" rule. The runtime drop filter
        /// (the <c>Pawn_ApparelTracker.DropAll</c> selector HD injects) rejects a piece iff this is true, and the
        /// "does a strip order still have anything to do?" probe asks the same question — both call HERE rather
        /// than re-deriving it, so the filter and the probe cannot drift apart and leave a strip order deleting its
        /// own designation for nothing (issue: manual Strip silently doing nothing).</para>
        /// </summary>
        /// <param name="tainted">The game's own taint test for this piece: worn by a corpse AND the apparel kind
        /// cares (<c>careIfWornByCorpse</c>). Untainted apparel is never left.</param>
        /// <param name="smeltable">Whether the INSTANCE is smeltable (which also excludes relics and
        /// non-smeltable stuff), picking which of the two configured categories applies.</param>
        /// <param name="smeltablePolicy">The player's policy for tainted smeltable apparel (metal armour).</param>
        /// <param name="nonSmeltablePolicy">The player's policy for tainted non-smeltable apparel (cloth rags).</param>
        public static bool StaysOnCorpse(bool tainted, bool smeltable,
            TaintedApparelPolicy smeltablePolicy, TaintedApparelPolicy nonSmeltablePolicy)
            => ApparelAction(tainted, smeltable, smeltablePolicy, nonSmeltablePolicy) == TaintedApparelPolicy.LeaveOnCorpse;

        /// <summary>
        /// Does EITHER tainted category resolve to a "leave where it is" policy? A cheap pre-gate for the runtime
        /// intake checks: when this is false (the Take/Smelt defaults) no loose piece is ever left, so the
        /// per-candidate apparel test can be skipped entirely — keeping the default config byte-identical.
        ///
        /// <para>Also the pre-gate for the <see cref="StaysOnCorpse"/> pair (the DropAll filter + the
        /// anything-left-to-strip probe). It is deliberately WIDER than they need — it also passes for
        /// <see cref="TaintedApparelPolicy.DropAndForbid"/>, which those two treat as strippable — because the two
        /// must share ONE gate: a probe that ran while the filter did not could report "nothing to strip" for a
        /// piece the filter would happily drop. Wider costs only a few reads in a config that already opted out of
        /// the defaults; narrower would be a correctness bug.</para>
        /// </summary>
        public static bool LeavesAnyTainted(TaintedApparelPolicy smeltablePolicy, TaintedApparelPolicy nonSmeltablePolicy)
            => IsLeavePolicy(smeltablePolicy) || IsLeavePolicy(nonSmeltablePolicy);

        /// <summary>True for the two policies that keep a piece out of colony storage (leave it on the corpse,
        /// or drop-and-forbid it in place).</summary>
        private static bool IsLeavePolicy(TaintedApparelPolicy p)
            => p == TaintedApparelPolicy.LeaveOnCorpse || p == TaintedApparelPolicy.DropAndForbid;
    }
}
