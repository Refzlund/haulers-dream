using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Conservative quantity of one cargo origin within a physical fragment. Units of a merged
    /// commodity have no observable serial numbers: a partial split generally gives an interval,
    /// not an exact origin assignment. These are marginal bounds; callers must retain the shared
    /// origin total and all live/terminal fragments to preserve correlations between fragments.
    /// This arithmetic does not establish custody, delivery, an attempt, or recurrence.
    /// </summary>
    public readonly struct CargoOriginBounds
    {
        public long Total { get; }
        public long Minimum { get; }
        public long Maximum { get; }
        public bool IsExact => Minimum == Maximum;

        public CargoOriginBounds(long total, long minimum, long maximum)
        {
            if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
            if (minimum < 0 || minimum > total) throw new ArgumentOutOfRangeException(nameof(minimum));
            if (maximum < minimum || maximum > total) throw new ArgumentOutOfRangeException(nameof(maximum));
            Total = total;
            Minimum = minimum;
            Maximum = maximum;
        }

        /// <summary>
        /// Partition by an observed physical moved quantity. Both outputs describe the same
        /// partition: their upper bounds cannot be treated as simultaneously observed quantities.
        /// Whole-stack movement remains exact whenever the input origin quantity was exact.
        /// </summary>
        public void Partition(long moved, out CargoOriginBounds taken, out CargoOriginBounds remaining)
        {
            if (moved < 0 || moved > Total) throw new ArgumentOutOfRangeException(nameof(moved));
            var left = Total - moved;
            taken = new CargoOriginBounds(moved, Math.Max(0, Minimum - left), Math.Min(Maximum, moved));
            remaining = new CargoOriginBounds(left, Math.Max(0, Minimum - moved), Math.Min(Maximum, left));
        }

        /// <summary>
        /// Combine physically disjoint fragments for the same origin. This deliberately retains
        /// uncertainty; if the fragments share history, constrain their sum with the retained
        /// component origin total. Calling this twice for nested observations double-counts cargo.
        /// </summary>
        public CargoOriginBounds MergeDisjoint(CargoOriginBounds other)
        {
            return new CargoOriginBounds(checked(Total + other.Total),
                checked(Minimum + other.Minimum), checked(Maximum + other.Maximum));
        }

        /// <summary>
        /// Tighten using a conserved origin total and bounds for ALL other fragments, including
        /// accounted sinks. The component owns that total; it cannot be guessed from a dead input
        /// or from the sum of fragment maxima. Contradictory evidence is rejected, not clamped into
        /// an invented quantity. This supplies necessary bounds, not a full mixture feasibility solver.
        /// </summary>
        public CargoOriginBounds ConstrainByOriginTotal(long originTotal, long othersMinimum, long othersMaximum)
        {
            if (originTotal < 0) throw new ArgumentOutOfRangeException(nameof(originTotal));
            if (othersMinimum < 0) throw new ArgumentOutOfRangeException(nameof(othersMinimum));
            if (othersMaximum < othersMinimum) throw new ArgumentOutOfRangeException(nameof(othersMaximum));
            var minimum = Math.Max(Minimum, originTotal - othersMaximum);
            var maximum = Math.Min(Maximum, originTotal - othersMinimum);
            if (minimum > maximum)
                throw new ArgumentException("Fragment bounds contradict the conserved origin quantity.");
            return new CargoOriginBounds(Total, minimum, maximum);
        }
    }
}
