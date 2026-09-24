using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>Retire a pickup without moving a saved cursor or promoting an extra to the ordered anchor.</summary>
    public static class SweepPickupPlan
    {
        /// <summary>Both lists are job-owned and must have the same shape before any mutation is safe.</summary>
        public static bool IsAligned<T>(IList<T> targets, IList<int> counts)
            => targets != null && counts != null && targets.Count > 0 && targets.Count == counts.Count;

        /// <summary>
        /// Replace every occurrence with an invalid target AND zero count. A zero count alone can still
        /// look like a full-stack pickup to evidence readers. Returns false without changing malformed plans.
        /// </summary>
        public static bool Retire<T>(IList<T> targets, IList<int> counts, T source, T invalid)
        {
            if (!IsAligned(targets, counts)) return false;
            var equality = EqualityComparer<T>.Default;
            if (equality.Equals(source, invalid)) return false;
            bool changed = false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!equality.Equals(targets[i], source)) continue;
                targets[i] = invalid;
                counts[i] = 0;
                changed = true;
            }
            return changed;
        }
    }
}
