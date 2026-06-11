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
    }
}
