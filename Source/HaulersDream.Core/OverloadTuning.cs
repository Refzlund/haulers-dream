using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Maps the "overload" slider level to the move-speed slowdown model. RimWorld has NO local
    /// move-speed penalty for inventory mass (verified: the MoveSpeed stat has no mass StatPart, and
    /// MassUtility's mass→speed term is caravan/world-map only), so this mod DEFINES the penalty — and
    /// the same model drives both the actual slowdown (a MoveSpeed StatPart) and the pawn's decision of
    /// how much to overload (<see cref="OverloadPolicy"/>), keeping them perfectly consistent.
    ///
    /// The slider is "how slowed you get past 100% capacity": level 0 = no slowdown (carry freely),
    /// the middle (<see cref="FairLevel"/>) = a fair balance (≈2× capacity break-even), and the far
    /// right = off (never overload). Because both the slowdown and the decision come from one slope,
    /// the pawn always overloads to the economically-correct point for the chosen punishment level.
    /// </summary>
    public static class OverloadTuning
    {
        /// <summary>Highest slider level (11 stops: 0..10).</summary>
        public const int MaxLevel = 10;

        /// <summary>Middle stop = the default "Fair" balance.</summary>
        public const int FairLevel = 5;

        /// <summary>Far-right stop: overload disabled (pawns never carry past their carry limit).</summary>
        public const int OffLevel = 10;

        /// <summary>Lowest the move-speed multiplier can fall to — overloaded pawns crawl, never freeze.</summary>
        public const float SpeedFloor = 0.25f;

        // Slowdown slope per level: how much the speed multiplier drops per +100% of capacity carried
        // past 100%. 0 = no slowdown. Higher = steeper (so overload stops paying off sooner). Level 10
        // is OFF (slope unused). Calibrated so Fair (5) breaks even around 2× capacity.
        private static readonly float[] Slopes =
        {
            0.00f, // 0 — no slowdown: carry freely
            0.10f, // 1
            0.20f, // 2
            0.30f, // 3
            0.38f, // 4
            0.45f, // 5 — Fair (break-even ≈ 2.0× capacity)
            0.60f, // 6
            0.85f, // 7
            1.20f, // 8
            1.80f, // 9 — barely overloads
            0.00f, // 10 — OFF (see IsOff)
        };

        public static int ClampLevel(int level) => level < 0 ? 0 : (level > MaxLevel ? MaxLevel : level);

        /// <summary>True at the far-right stop: overloading is disabled entirely.</summary>
        public static bool IsOff(int level) => ClampLevel(level) >= OffLevel;

        /// <summary>Slowdown slope for a level (0 at level 0; meaningless when <see cref="IsOff"/>).</summary>
        public static float SlopeForLevel(int level) => Slopes[ClampLevel(level)];

        /// <summary>
        /// The actual move-speed multiplier for a pawn at encumbrance ratio
        /// <paramref name="encumbranceRatio"/> = currentMass / maxCapacity. 1.0 at or under capacity
        /// (matches vanilla — no penalty up to 100%); declines past it; floored at
        /// <see cref="SpeedFloor"/>. Returns 1.0 when the feature is off or set to "no slowdown".
        /// </summary>
        public static float SpeedFactor(int level, float encumbranceRatio)
        {
            int lv = ClampLevel(level);
            if (IsOff(lv) || encumbranceRatio <= 1f)
                return 1f;
            float slope = Slopes[lv];
            if (slope <= 0f)
                return 1f; // "no slowdown" stop
            float f = 1f - slope * (encumbranceRatio - 1f);
            return f < SpeedFloor ? SpeedFloor : f;
        }

        /// <summary>
        /// The throughput-optimal "break-even" speed factor: f* = √(slope+2) − 1. Carrying extra raises
        /// throughput (units delivered per unit time) until the loaded-leg speed multiplier reaches f*,
        /// after which the slowdown costs more than the trip it saves. Derived by maximizing
        /// load / (1/factor + 1) for a load-deliver-return-empty cycle (distance and base speed cancel).
        /// </summary>
        public static float BreakEvenFactor(float slope) => (float)Math.Sqrt(slope + 2f) - 1f;

        /// <summary>
        /// The encumbrance ratio (mass / maxCapacity) at which overloading stops paying off, i.e. where
        /// the speed multiplier hits <see cref="BreakEvenFactor"/>. 1.0 = no overload (off);
        /// +∞ = carry freely (level 0). This is the smart ceiling the pawn loads up to.
        /// </summary>
        public static float MaxOverloadRatio(int level)
        {
            int lv = ClampLevel(level);
            if (IsOff(lv))
                return 1f; // no overload
            float slope = Slopes[lv];
            if (slope <= 0f)
                return float.PositiveInfinity; // carry freely
            float fStar = BreakEvenFactor(slope);
            float t = 1f + (1f - fStar) / slope;
            return t < 1f ? 1f : t;
        }
    }
}
