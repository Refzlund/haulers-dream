using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure arithmetic for the planned-route dialog: how many stops fit inside a max-travel-distance budget,
    /// the total estimated time (walking + working, plus an optional return-to-storage leg), and a ticks→hours
    /// conversion. Fed raw per-leg / per-stop numbers by the game layer (which does the pathfinding and work
    /// lookups), so the budgeting and totalling stay unit-testable with no Verse dependency.
    /// </summary>
    public static class RouteEstimate
    {
        /// <summary>Ticks in one in-game hour (Verse.GenDate.TicksPerHour).</summary>
        public const float TicksPerHour = 2500f;

        /// <summary>
        /// The largest number of leading stops whose cumulative arrival-leg distance stays within
        /// <paramref name="maxDistance"/>. <paramref name="legDistances"/>[i] is the travel distance of the leg
        /// that arrives at stop i (index 0 = pawn → first stop, index 1 = first → second, …). A non-positive or
        /// infinite budget means "no limit" (returns the full count). This is the ceiling that trims the chosen
        /// amount when the targets can't all be gathered within the distance.
        /// </summary>
        public static int StopsWithinBudget(IReadOnlyList<float> legDistances, float maxDistance)
        {
            if (legDistances == null)
                return 0;
            int n = legDistances.Count;
            if (maxDistance <= 0f || float.IsPositiveInfinity(maxDistance))
                return n;
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                acc += Pos(legDistances[i]);
                if (acc > maxDistance)
                    return i; // stops 0..i-1 fit; arriving at stop i would exceed the budget
            }
            return n;
        }

        /// <summary>
        /// Total estimated ticks for the first <paramref name="keptStops"/> stops: the walk into each stop plus
        /// the work done there, plus an optional final return-to-storage leg.
        /// </summary>
        public static float TotalTicks(IReadOnlyList<float> legWalkTicks, IReadOnlyList<float> stopWorkTicks,
            int keptStops, float returnLegTicks)
        {
            float t = 0f;
            if (legWalkTicks != null)
                for (int i = 0; i < keptStops && i < legWalkTicks.Count; i++)
                    t += Pos(legWalkTicks[i]);
            if (stopWorkTicks != null)
                for (int i = 0; i < keptStops && i < stopWorkTicks.Count; i++)
                    t += Pos(stopWorkTicks[i]);
            return t + Pos(returnLegTicks);
        }

        public static float HoursFromTicks(float ticks) => ticks <= 0f ? 0f : ticks / TicksPerHour;

        /// <summary>
        /// Ticks to harvest or cut one plant (vanilla JobDriver_PlantWork): workDone accrues at
        /// <c>plantWorkSpeed · Lerp(3.3, 1, growth)</c> per tick until it reaches <paramref name="harvestWork"/>.
        /// A fully-grown plant (growth=1) is the slowest case; an immature one is faster.
        /// </summary>
        public static float PlantWorkTicks(float harvestWork, float plantWorkSpeed, float growth)
        {
            if (plantWorkSpeed <= 0f || harvestWork <= 0f)
                return 0f;
            float rate = plantWorkSpeed * Lerp(3.3f, 1f, Clamp01(growth));
            return rate <= 0f ? 0f : harvestWork / rate;
        }

        /// <summary>
        /// Ticks to mine one rock (vanilla JobDriver_Mine): a pick-hit lands every <c>100/miningSpeed</c> ticks
        /// dealing 80 damage to natural rock / 40 to other mineables, until hit points reach zero. Continuous
        /// approximation (good enough for a UI estimate): <c>hitPoints · 100 / (damagePerHit · miningSpeed)</c>.
        /// </summary>
        public static float MineWorkTicks(int hitPoints, bool isNaturalRock, float miningSpeed)
        {
            if (miningSpeed <= 0f || hitPoints <= 0)
                return 0f;
            float damagePerHit = isNaturalRock ? 80f : 40f;
            return hitPoints * 100f / (damagePerHit * miningSpeed);
        }

        private static float Pos(float v) => v > 0f ? v : 0f;
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }
}
