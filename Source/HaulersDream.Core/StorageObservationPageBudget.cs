using System;

namespace HaulersDream.Core
{
    /// <summary>Bounds a fresh cell page before its demand/cell predicate cross product.</summary>
    public sealed class StorageObservationPageBudget
    {
        private readonly long limit, demands;
        private long reserved;

        public StorageObservationPageBudget(int predicateLimit, int demandCount, int memberCount)
        {
            if (predicateLimit <= 0 || demandCount < 0 || memberCount < 0)
                throw new ArgumentOutOfRangeException();
            limit = predicateLimit;
            demands = demandCount;
            // Demand enumeration plus initial and publication subject guards, and both
            // topology validations. Actual counters remain authoritative if callbacks
            // require more work than this conservative page estimate.
            reserved = 5L * demandCount + 2L * memberCount;
        }

        public bool TryInclude(int gridRecords, int predicatesAlreadyUsed)
        {
            if (gridRecords < 0 || predicatesAlreadyUsed < 0) throw new ArgumentOutOfRangeException();
            // Native/ASF acceptance, each possible resident compatibility edge, physical
            // provider checks and two freshness passes. Non-item records overestimate
            // resident edges deliberately; this estimate never grants capacity.
            long cost = 16L + demands * (10L + gridRecords) + 2L * gridRecords;
            if (reserved + cost > limit - (long)predicatesAlreadyUsed) return false;
            reserved += cost;
            return true;
        }
    }
}
