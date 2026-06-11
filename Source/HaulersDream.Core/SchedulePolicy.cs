using System;

namespace HaulersDream.Core
{
    /// <summary>Timing for periodic ("interval") unloading. Pure; the GameComponent calls it each tick.</summary>
    public static class SchedulePolicy
    {
        public const int DefaultTicksPerHour = 2500; // RimWorld: 2500 ticks per in-game hour

        /// <summary>
        /// True on the ticks that an interval-unload sweep should run. A non-positive interval
        /// disables it. The interval is clamped to at least 1 tick to avoid a divide-by-zero / spin.
        /// </summary>
        public static bool IntervalDueNow(int ticksGame, float intervalHours, int ticksPerHour = DefaultTicksPerHour)
        {
            if (intervalHours <= 0f || ticksPerHour <= 0)
                return false;
            int interval = Math.Max(1, (int)Math.Round(intervalHours * ticksPerHour));
            return ticksGame % interval == 0;
        }
    }
}
