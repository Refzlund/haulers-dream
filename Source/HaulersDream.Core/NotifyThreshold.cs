namespace HaulersDream.Core
{
    /// <summary>
    /// How noisy the main-menu report notifications are. Ordered from most to least: the further down
    /// the list, the fewer events surface. Maps to the "Any event / Comment / Fixed in update / Never"
    /// setting (the further right the slider, the higher this value).
    /// </summary>
    public enum NotifyThreshold
    {
        /// <summary>Show every kind of event: new comments, status changes, and fixes.</summary>
        All = 0,

        /// <summary>Show new comments and fixes, but not bare status changes (reopen / close-as-wontfix).</summary>
        Comments = 1,

        /// <summary>Show only "fixed" events (an issue closed as completed), plus the out-of-date warning.</summary>
        FixedOnly = 2,

        /// <summary>Never show notifications (also the privacy opt-out).</summary>
        Never = 3
    }
}
