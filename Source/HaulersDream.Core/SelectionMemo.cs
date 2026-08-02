namespace HaulersDream.Core
{
    /// <summary>
    /// The STATEFUL half of the auto-open-inspect-tab selection gate (no game types, so the full click SEQUENCES
    /// are unit-tested headlessly rather than only the single-step predicate). The game layer
    /// <c>Patch_Selector_Select</c> owns exactly one instance and forwards the three Selector events to it:
    /// a selection was made, the selection was emptied, or the selection stopped being a single thing.
    ///
    /// <para>All decisions still route through <see cref="TabAutoOpenPolicy.IsNewSelection"/>; this type only
    /// holds the memo the predicate reads and applies the record/consume rules around it.</para>
    ///
    /// <para>A CLASS, not a struct, on purpose: the patch holds one <c>static readonly</c> instance and mutates it
    /// in place. A mutable struct in a static field silently copies on every pass-by-value, so a mutation could
    /// land on a temporary instead of the field.</para>
    /// </summary>
    public sealed class SelectionMemo
    {
        /// <summary>Identity (<c>thingIDNumber</c>) of the last single-selected thing, or -1 for none. An id rather
        /// than a reference so the memo can never keep a despawned thing alive.</summary>
        public int LastSelectedId { get; private set; } = -1;

        /// <summary>True when a selection-emptying event has been recorded and no selection has consumed it yet.</summary>
        public bool GapPending { get; private set; }

        /// <summary>The frame the pending gap was recorded on; only meaningful while <see cref="GapPending"/> is
        /// true. -1 when there is no pending gap.</summary>
        public int GapFrame { get; private set; } = -1;

        /// <summary>
        /// Drop the whole memo. Called on every game load: <c>thingIDNumber</c> counters restart per game, so a
        /// memo carried across a quickload could otherwise collide with a DIFFERENT thing in the new session and
        /// swallow one auto-open.
        /// </summary>
        public void Reset()
        {
            LastSelectedId = -1;
            GapPending = false;
            GapFrame = -1;
        }

        /// <summary>
        /// The selection is no longer a single thing (a multi-select, or a selection that is not a Thing at all):
        /// whatever is single-selected NEXT is a fresh selection.
        ///
        /// <para>It also drops any pending gap, because the gap has already served its purpose — the -1 id alone
        /// now guarantees the next selection reads as new, and keeping a stale gap frame around would only let it
        /// leak into a LATER decision it was never recorded for.</para>
        /// </summary>
        public void Invalidate()
        {
            LastSelectedId = -1;
            GapPending = false;
        }

        /// <summary>
        /// Record that the selection was emptied (the player clicked bare ground, or deselected their last
        /// selection) on <paramref name="frame"/>.
        ///
        /// <para>Deliberately does NOT overwrite an already-pending gap. Vanilla runs <c>ClearSelection()</c> then
        /// <c>Select(obj)</c> on EVERY plain click, so the clear belonging to the CURRENT click must not erase the
        /// frame of an earlier lone clear that no selection has consumed yet — that earlier frame is the only thing
        /// that distinguishes "deselected, then re-selected the same thing" from "re-clicked the same thing".</para>
        /// </summary>
        /// <param name="frame">The frame the emptying happened on.</param>
        public void NotifyCleared(int frame)
        {
            if (GapPending)
                return;
            GapPending = true;
            GapFrame = frame;
        }

        /// <summary>
        /// Record a single-thing selection and answer whether it is a genuinely NEW one (i.e. whether an auto-open
        /// should fire).
        ///
        /// <para>The id is recorded and the pending gap consumed EITHER WAY — the memo must stay coherent even when
        /// the caller discards the result, which is exactly what happens while both auto-open toggles are off (the
        /// patch keeps the memo current so flipping a toggle on mid-game is correct on the very next click).</para>
        /// </summary>
        /// <param name="selectedId">Identity (<c>thingIDNumber</c>) of the thing now selected.</param>
        /// <param name="frame">The frame this selection happened on, compared against a pending gap's frame to tell
        /// an EARLIER deselect apart from the clear-then-select pair of this very click.</param>
        /// <returns>True if this selection differs from the last one, or follows an emptied selection from an
        /// earlier frame.</returns>
        public bool NotifySelected(int selectedId, int frame)
        {
            bool isNew = TabAutoOpenPolicy.IsNewSelection(selectedId, LastSelectedId, GapPending, GapFrame, frame);
            LastSelectedId = selectedId;
            GapPending = false;
            return isNew;
        }
    }
}
