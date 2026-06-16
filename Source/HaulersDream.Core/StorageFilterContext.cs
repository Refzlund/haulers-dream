namespace HaulersDream.Core
{
    /// <summary>
    /// Which hauling situation is choosing a storage building — selects which curated default
    /// permit/deny set <see cref="StorageFilterPolicy.IsAllowed"/> applies (ported from While You're
    /// Up's two separate "Opportunity" / "Haul-before-carry" building filters; HD folds them into one
    /// filter object whose behavior is selected by this context — see plan G4/G7).
    /// </summary>
    public enum StorageFilterContext
    {
        /// <summary>
        /// A pawn would scoop/sweep a stray item as an incidental side-quest (WYU "Opportunity").
        /// Default-ALLOW everything EXCEPT the slow set (LWM Deep Storage), since a storing delay
        /// makes a stop there not actually "opportune".
        /// </summary>
        Opportunistic,

        /// <summary>
        /// A pawn is emptying its hauled inventory (the shared unload funnel). Must NEVER deny a
        /// destination on the slow set — a carrying pawn has to be able to put its load down somewhere
        /// (plan G4). Effectively allow-all.
        /// </summary>
        Unload,

        /// <summary>
        /// A pawn is about to carry one stack to a job and would first detour the haul to better storage
        /// (WYU "Haul before carry"). Default-DENY everything EXCEPT the curated container allow-list of
        /// mods determined to be real storage containers (not arbitrary <c>Building_Storage</c> reuse).
        /// </summary>
        BeforeCarry
    }
}
