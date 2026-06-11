namespace HaulersDream.Core
{
    /// <summary>
    /// Pure comparison primitive: optionally MARKED-first, then by squared distance to the anchor, then by a
    /// deterministic id tiebreak.
    /// <para>NOTE: the live route path NO LONGER uses the marked-first mode — <c>RouteSelection.SortByDistanceTo</c>
    /// calls this with both marked flags FALSE (pure distance + id), so a nearby UNMARKED plant ranks ahead of a
    /// farther marked one ("Chained — the nearest few" honours proximity, the user's expectation). Marked-first was
    /// originally added so the marked-only set (allow-harvest OFF) stayed a literal PREFIX of marked+unmarked
    /// (allow-harvest ON) — strictly monotone across that toggle — but it ranked nearby unmarked bushes BEHIND
    /// farther marked ones and the travel budget then trimmed them, which was the bug. Trade-off accepted: toggling
    /// allow-unmarked is no longer strictly count-monotone (a near unmarked plant opposite the cluster can, under a
    /// tight budget, reduce the routed count), but that's correct nearest-first behaviour. The marked-first branch
    /// is retained only for its unit tests / possible reuse.</para>
    /// </summary>
    public static class RouteOrdering
    {
        public static int CompareMarkedFirst(bool aMarked, long aDistSq, int aId, bool bMarked, long bDistSq, int bId)
        {
            if (aMarked != bMarked)
                return aMarked ? -1 : 1;
            int d = aDistSq.CompareTo(bDistSq);
            if (d != 0)
                return d;
            return aId.CompareTo(bId);
        }
    }
}
