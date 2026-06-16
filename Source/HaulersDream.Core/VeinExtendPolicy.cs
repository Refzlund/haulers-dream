namespace HaulersDream.Core
{
    /// <summary>
    /// What should happen to an in-flight vein route when the periodic "extend as fog clears" pass examines
    /// it and finds the route's last queued task is NO LONGER the pawn's last task (the tail check failed).
    /// A failed tail check is normally a sign the route was superseded — but there is one legitimate case
    /// (the flagship 1-2-visible-cell stub) where it is the route paying off rather than being diverted.
    /// </summary>
    public enum ExtendOutcome
    {
        /// <summary>Genuinely superseded / diverted: the player queued other real work after the route, or
        /// the tail cell was NOT mined (the tail moved for some other reason). Leave the route alone — drop
        /// the tracker.</summary>
        Drop,

        /// <summary>The flagship 1-2-visible-cell stub: the tail check failed precisely because the route's
        /// FINAL cell was just mined AND the pawn has nothing real queued after it. The reveal happened at
        /// the exact moment the route completed, so make ONE last extend attempt instead of dropping the
        /// tracker right when it was about to pay off. (A fruitless final attempt then drops it — no route
        /// jobs remain to mine more.)</summary>
        FinalAttempt,

        /// <summary>A normal extend: the route's last task is still the pawn's last task, so newly-revealed
        /// cells can simply be appended to the running route.</summary>
        Extend,
    }

    /// <summary>
    /// Pure decision logic for the deferred "vein reveal" extension (no game types — unit-tested headlessly).
    /// As a pawn mines a vein route, fog clears and uncovers more of the same vein; the runtime periodically
    /// re-floods the now-visible vein and appends the new cells to the running route. This class isolates the
    /// small PURE choices in that loop (when to stop, whether a failed tail check is a supersession or the
    /// final-cell payoff, when accumulating new stops has filled the route, and whether to keep the tracker
    /// after a fruitless re-flood) from the Verse queries that surround them — so the control flow is pinned
    /// by unit tests and can never silently drift.
    /// </summary>
    public static class VeinExtendPolicy
    {
        /// <summary>
        /// True once the route already holds its chosen Amount of stops — never grow a route past the count
        /// the player asked for. (Mirrors the runtime's <c>if (tr.included.Count &gt;= tr.cap) return false;</c>
        /// at the top of the extend pass: at or above the cap, stop extending.)
        /// </summary>
        public static bool AtCap(int includedCount, int cap) => includedCount >= cap;

        /// <summary>
        /// Decides what to do when the route's tail check has been evaluated. Only ever consulted in the
        /// branch where the tail did NOT match (the runtime short-circuits the expensive Verse queries that
        /// feed <paramref name="lastCellMined"/> / <paramref name="nothingElseQueued"/> when the tail still
        /// matches), but the full truth table is encoded here so the decision is exhaustively testable:
        /// <list type="bullet">
        /// <item>tail still matches the route → <see cref="ExtendOutcome.Extend"/> (the normal case: the
        ///   route's last task is still the pawn's last task, so just append more).</item>
        /// <item>tail does NOT match, the final cell WAS mined, and nothing real is queued after it →
        ///   <see cref="ExtendOutcome.FinalAttempt"/> (the 1-2-visible-cell payoff: try one last extend).</item>
        /// <item>tail does NOT match for any other reason (real work queued, or the tail cell wasn't mined) →
        ///   <see cref="ExtendOutcome.Drop"/> (genuinely superseded / diverted — leave the route alone).</item>
        /// </list>
        /// </summary>
        /// <param name="tailStillMatchesRoute">The route's last queued cell is still the pawn's last queued
        /// task (same cell AND a Mine job).</param>
        /// <param name="lastCellMined">The route's final cell no longer holds a vein thing of its kind — it
        /// was mined (only meaningful when the tail no longer matches).</param>
        /// <param name="nothingElseQueued">No REAL queued work follows the route (the mod's own self-pickup /
        /// unload housekeeping is ignored when computing this).</param>
        public static ExtendOutcome DecideSupersession(
            bool tailStillMatchesRoute, bool lastCellMined, bool nothingElseQueued)
        {
            if (tailStillMatchesRoute)
                return ExtendOutcome.Extend;
            if (lastCellMined && nothingElseQueued)
                return ExtendOutcome.FinalAttempt;
            return ExtendOutcome.Drop;
        }

        /// <summary>
        /// While accumulating newly-revealed cells to append, returns whether there is still room for one
        /// more stop — i.e. the already-included stops plus the new stops gathered so far have not yet
        /// reached the route's cap. (Mirrors the runtime's
        /// <c>if (tr.included.Count + newStops.Count &gt;= tr.cap) break;</c>: stop accumulating once the
        /// combined total would hit the cap.)
        /// </summary>
        public static bool CanAddStop(int includedCount, int alreadyAddedNewStops, int cap)
            => includedCount + alreadyAddedNewStops < cap;

        /// <summary>
        /// When the re-flood found NO new stops to append, decides whether to KEEP the tracker alive for a
        /// later pass: keep it only when this was NOT the final attempt AND there is still fog hiding more
        /// of the vein. A fruitless FINAL attempt is dropped (no route jobs remain to mine more, so nothing
        /// will ever reveal further cells); a non-final pass with no fog left is also dropped (the vein is
        /// fully revealed). (Mirrors the runtime's <c>return !finalAttempt &amp;&amp; stillFog;</c>.)
        /// </summary>
        public static bool KeepAfterNoNewStops(bool finalAttempt, bool stillFog) => !finalAttempt && stillFog;
    }
}
