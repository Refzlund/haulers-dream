using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Decides whether a pawn carrying scooped goods should divert to unload at storage on its way to a
    /// new work target — i.e. when storage is roughly "on the way", so dropping off now is cheaper than
    /// carrying the load onward and making a dedicated trip later. Pure; the game layer supplies the
    /// straight-line distances and the fraction of capacity that is scooped goods.
    /// </summary>
    public static class OpportunisticUnloadPolicy
    {
        /// <summary>Only divert for a real journey, not local work right next to the pawn.</summary>
        public const int MinTripTiles = 16;

        /// <summary>And only when carrying a worthwhile load (fraction of max carry capacity).</summary>
        public const float MinLoadFraction = 0.15f;

        /// <summary>A detour up to this many tiles always counts as "on the way".</summary>
        public const int MinDetourTiles = 10;

        /// <summary>...plus this fraction of the trip length, so long hauls tolerate a bigger nudge.</summary>
        public const float MaxDetourFraction = 0.25f;

        /// <summary>Carrying at least this fraction of capacity makes the load HEAVY: shedding it is now
        /// worth more than the walking saved, so the trip/detour bars relax (below).</summary>
        public const float HeavyLoadFraction = 0.5f;

        /// <summary>Heavy load: even a short hop counts as a journey worth diverting from.</summary>
        public const int HeavyMinTripTiles = 8;

        /// <summary>Heavy load: accept a detour up to half the trip length.</summary>
        public const float HeavyMaxDetourFraction = 0.5f;

        /// <param name="pawnToTarget">Straight-line tiles from the pawn to its next work target.</param>
        /// <param name="pawnToStorage">Straight-line tiles from the pawn to the storage.</param>
        /// <param name="storageToTarget">Straight-line tiles from the storage to the work target.</param>
        /// <param name="loadFraction">Scooped-goods mass carried, as a fraction of max carry capacity.</param>
        public static bool ShouldUnloadOnWay(
            int pawnToTarget, int pawnToStorage, int storageToTarget, float loadFraction,
            int minTripTiles = MinTripTiles, float minLoadFraction = MinLoadFraction,
            int minDetourTiles = MinDetourTiles, float maxDetourFraction = MaxDetourFraction,
            float heavyLoadFraction = HeavyLoadFraction, int heavyMinTripTiles = HeavyMinTripTiles,
            float heavyMaxDetourFraction = HeavyMaxDetourFraction)
        {
            if (loadFraction < minLoadFraction)
                return false;
            // A pawn lugging half its capacity (or more) around pays a tax on everything it does —
            // relax the journey/detour bars so it sheds the load far sooner.
            bool heavy = loadFraction >= heavyLoadFraction;
            if (pawnToTarget < (heavy ? heavyMinTripTiles : minTripTiles))
                return false; // local work — not worth a detour

            // Extra distance walked by going pawn -> storage -> target instead of straight there.
            int detour = pawnToStorage + storageToTarget - pawnToTarget;
            if (detour < 0)
                detour = 0;
            int bar = Math.Max(minDetourTiles, (int)((heavy ? heavyMaxDetourFraction : maxDetourFraction) * pawnToTarget));
            return detour <= bar;
        }
    }
}
