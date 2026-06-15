using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure best-match ranking for the manifest-decrement intercept (<c>FindBestMatchFor</c> /
    /// <c>SubtractFromToLoadList</c>): given the candidate manifest entries that match a deposited thing's def
    /// (each as its remaining <c>CountToTransfer</c>), pick which entry to decrement. Vanilla's
    /// <c>TransferableMatchingDesperate</c> is FUZZY and miscounts a multi-stack deposit; keeping the choice
    /// deterministic and testable (without Verse) is what makes the precise decrement reliable.
    ///
    /// The ranking, against the number of units actually being deposited (<c>depositedCount</c>):
    ///   1. EXACT — an entry whose remaining count equals <c>depositedCount</c> (the deposit fully empties it).
    ///   2. Among entries large enough to FULLY absorb the deposit (remaining ≥ depositedCount), the LARGEST.
    ///   3. Otherwise (every entry is a PARTIAL — remaining &lt; depositedCount), the SMALLEST remaining (so the
    ///      smallest entry is finished off first and the leftover rolls to the next deposit step).
    /// Ties broken by the FIRST occurrence (lowest index) for determinism. Returns -1 for an empty/no-match set.
    /// </summary>
    public static class TransferableMatchPolicy
    {
        /// <summary>One matching manifest entry as the policy sees it: its index in the caller's list and its
        /// remaining <c>CountToTransfer</c>.</summary>
        public struct Candidate
        {
            public int Index;
            public int Remaining;

            public Candidate(int index, int remaining)
            {
                Index = index;
                Remaining = remaining;
            }
        }

        /// <summary>
        /// Pick the index (the candidate's own <see cref="Candidate.Index"/>) of the manifest entry to decrement for
        /// a deposit of <paramref name="depositedCount"/> units, per the EXACT → largest-fully-absorbing →
        /// smallest-partial ladder above. Entries with <c>Remaining &lt;= 0</c> are ignored. Returns -1 when no
        /// positive-remaining candidate exists.
        /// </summary>
        public static int ChooseBestMatchIndex(IReadOnlyList<Candidate> candidates, int depositedCount)
        {
            if (candidates == null || candidates.Count == 0)
                return -1;

            int exactIndex = -1;
            int bestAbsorbIndex = -1, bestAbsorbRemaining = -1;     // largest remaining ≥ deposited
            int smallestPartialIndex = -1, smallestPartialRemaining = int.MaxValue; // smallest remaining < deposited

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.Remaining <= 0)
                    continue;
                if (c.Remaining == depositedCount)
                {
                    if (exactIndex < 0) // first exact wins (deterministic)
                        exactIndex = c.Index;
                    continue;
                }
                if (c.Remaining > depositedCount)
                {
                    if (c.Remaining > bestAbsorbRemaining) // strictly-greater → keeps the FIRST of equal-largest
                    {
                        bestAbsorbRemaining = c.Remaining;
                        bestAbsorbIndex = c.Index;
                    }
                }
                else // c.Remaining < depositedCount
                {
                    if (c.Remaining < smallestPartialRemaining) // strictly-less → keeps the FIRST of equal-smallest
                    {
                        smallestPartialRemaining = c.Remaining;
                        smallestPartialIndex = c.Index;
                    }
                }
            }

            if (exactIndex >= 0)
                return exactIndex;
            if (bestAbsorbIndex >= 0)
                return bestAbsorbIndex;
            return smallestPartialIndex; // -1 if there were no positive candidates at all
        }
    }
}
