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
    /// the middle (<see cref="FairLevel"/>) = a fair balance (≈275% capacity break-even), and the far
    /// right = off (never overload). Because both the slowdown and the decision come from one curve,
    /// the pawn always overloads to the economically-correct point for the chosen punishment level.
    ///
    /// SPEED CURVE (convex quadratic): the multiplier is gentle just past 100% and steepens the further
    /// you overload — <c>f(r) = max(<see cref="SpeedFloor"/>, g(r))</c> with the un-floored curve
    /// <c>g(r) = 1 − α·(r−1)²</c>. The break-even encumbrance (the smart ceiling a pawn loads up to) is the
    /// INTERIOR (pre-floor) throughput optimum: the ratio that maximizes the UN-FLOORED carry throughput
    /// <c>r·g(r)/(1+g(r))</c> over a load→deliver→return-empty cycle (distance and base speed cancel).
    /// The <see cref="SpeedFloor"/> is a safety backstop deliberately EXCLUDED from this optimization —
    /// with the floor applied, raw throughput <c>r·f(r)/(1+f(r))</c> becomes monotone-increasing once the
    /// floor clamps the speed, so its global max would be the far edge of the slider's domain (a pawn
    /// crawling under a giant load), which is never the intent. The meaningful optimum is therefore the
    /// peak of the un-floored curve, which for the tuned αs sits comfortably below the floor knee — so
    /// pawns load to the moderate sweet spot and never into the floor region. The quadratic has no clean
    /// closed form for that argmax, so it is computed NUMERICALLY once per level at static init into
    /// <see cref="BreakEvenRatios"/> — derived straight from <see cref="Alphas"/>, so the curve and the
    /// break-even ceiling can never desync.
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

        // Convexity coefficient per level (p = 2): f(r) = max(SpeedFloor, 1 − α·(r−1)²). 0 = no slowdown.
        // Larger α = steeper, so overload stops paying off sooner. Level 10 is OFF (α unused). Each α was
        // solved numerically (bisection) so the level's interior (pre-floor) throughput optimum hits the
        // design spectrum below (450%→150%, Fair = 275%). The break-evens are recomputed from these at
        // static init (see BreakEvenRatios) and pinned by tests, so a re-tune here can never desync the ceiling.
        //   level | label    | α         | break-even
        //     0   | Free     | 0.00000   | ∞   (carry freely)
        //     1   | Eager    | 0.03148   | 450%
        //     2   | Eager    | 0.04777   | 380%
        //     3   | Eager    | 0.06866   | 330%
        //     4   | Eager    | 0.08856   | 300%
        //     5   | Fair     | 0.11264   | 275%  (default)
        //     6   | Cautious | 0.15727   | 245%
        //     7   | Cautious | 0.23527   | 215%
        //     8   | Cautious | 0.39153   | 185%
        //     9   | Cautious | 0.91199   | 150%  (barely overloads)
        //    10   | Off      | 0.00000   | 1.0   (never overloads — see IsOff)
        private static readonly float[] Alphas =
        {
            0.00000f, // 0 — Free: no slowdown, carry freely
            0.03148f, // 1 — Eager     (break-even ≈ 450%)
            0.04777f, // 2 — Eager     (break-even ≈ 380%)
            0.06866f, // 3 — Eager     (break-even ≈ 330%)
            0.08856f, // 4 — Eager     (break-even ≈ 300%)
            0.11264f, // 5 — Fair      (break-even ≈ 275%)
            0.15727f, // 6 — Cautious  (break-even ≈ 245%)
            0.23527f, // 7 — Cautious  (break-even ≈ 215%)
            0.39153f, // 8 — Cautious  (break-even ≈ 185%)
            0.91199f, // 9 — Cautious  (break-even ≈ 150%)
            0.00000f, // 10 — OFF (see IsOff)
        };

        // The interior (pre-floor) throughput-optimum encumbrance ratio per level, derived ONCE from
        // Alphas[] at static init via a numerical argmax of the un-floored curve (so curve ↔ ceiling are
        // always in sync). Level 0 is +∞ (carry freely); the off level resolves to 1.0 (no overload)
        // through MaxOverloadRatio.
        private static readonly float[] BreakEvenRatios = BuildBreakEvenTable();

        static OverloadTuning()
        {
            // Defensive: the two tables (and the model's special levels) must line up. A mismatched
            // edit to Alphas should fail loudly here rather than index out of range at runtime.
            if (Alphas.Length != MaxLevel + 1)
                throw new InvalidOperationException(
                    $"OverloadTuning.Alphas must have {MaxLevel + 1} entries, has {Alphas.Length}.");
        }

        private static float[] BuildBreakEvenTable()
        {
            var table = new float[MaxLevel + 1];
            for (int lv = 0; lv <= MaxLevel; lv++)
            {
                float alpha = Alphas[lv];
                table[lv] = alpha <= 0f ? float.PositiveInfinity : ComputeBreakEven(alpha);
            }
            return table;
        }

        public static int ClampLevel(int level) => level < 0 ? 0 : (level > MaxLevel ? MaxLevel : level);

        /// <summary>True at the far-right stop: overloading is disabled entirely.</summary>
        public static bool IsOff(int level) => ClampLevel(level) >= OffLevel;

        /// <summary>The convexity coefficient α for a level (0 at level 0; meaningless when <see cref="IsOff"/>).</summary>
        public static float AlphaForLevel(int level) => Alphas[ClampLevel(level)];

        /// <summary>
        /// The unfloored speed factor of the quadratic at ratio <paramref name="r"/> for coefficient
        /// <paramref name="alpha"/>: <c>1 − α·(r−1)²</c> (no floor / no r≤1 clamp). Internal helper for the
        /// break-even search and the speed-at-break-even diagnostics.
        /// </summary>
        private static double RawSpeed(double alpha, double r)
        {
            double d = r - 1.0;
            return 1.0 - alpha * d * d;
        }

        /// <summary>
        /// The ratio at which the floored quadratic reaches <see cref="SpeedFloor"/>:
        /// <c>1 + √((1 − floor)/α)</c>. This is the "floor knee" — past it the EXPERIENCED speed is constant
        /// (the floor backstop), so the floored throughput would turn monotone-increasing (a pawn crawling
        /// under an ever-bigger load), which is never the intended ceiling. The interior optimum we search
        /// for always sits below this knee for the tuned αs, so the search caps its bracket here to stay in
        /// the meaningful (un-floored) region.
        /// </summary>
        private static double RatioAtFloor(double alpha)
            => 1.0 + Math.Sqrt((1.0 - SpeedFloor) / alpha);

        /// <summary>
        /// Throughput per load→deliver→return-empty cycle at ratio <paramref name="r"/> (distance cancels):
        /// <c>r·f(r) / (1 + f(r))</c> using the FLOORED speed. Across the search bracket [1, ratioAtFloor]
        /// the floored speed equals the un-floored <c>g(r) = 1 − α(r−1)²</c> (the floor has not yet bitten),
        /// so maximizing this over that bracket finds the INTERIOR peak of the un-floored carry curve — the
        /// break-even. (Maximizing the floored curve over the FULL domain instead would just walk to the far
        /// edge, since past the knee the floor makes throughput monotone-increasing; that is deliberately
        /// excluded — see <see cref="RatioAtFloor"/> and <see cref="ComputeBreakEven"/>.)
        /// </summary>
        private static double Throughput(double alpha, double r)
        {
            double raw = RawSpeed(alpha, r);
            double f = raw < SpeedFloor ? SpeedFloor : raw;
            return (r * f) / (1.0 + f);
        }

        /// <summary>
        /// Numerically find the INTERIOR (pre-floor) throughput optimum for the quadratic with the given
        /// <paramref name="alpha"/> — the peak of the UN-FLOORED carry curve <c>r·g(r)/(1+g(r))</c>,
        /// <c>g(r) = 1 − α(r−1)²</c>, which is the meaningful break-even ceiling. Ternary (golden-style)
        /// search over r in [1, min(8, ratioAtFloor)]: the objective is unimodal there (it rises to the
        /// interior optimum, which always sits below the floor knee for the tuned αs). The upper cap
        /// deliberately stops at the floor knee so the search never walks into the floored region, where
        /// raw throughput turns monotone-increasing and its global max would be the slider's far edge (a
        /// pawn crawling under a giant load) rather than the intended sweet spot. ~ <c>(2/3)^iters</c>
        /// bracket shrink; 100 iterations give far better than 0.001 precision over the ≤7-wide bracket.
        /// </summary>
        private static float ComputeBreakEven(float alpha)
        {
            double lo = 1.0;
            double hi = Math.Min(8.0, RatioAtFloor(alpha));
            for (int i = 0; i < 100; i++)
            {
                double m1 = lo + (hi - lo) / 3.0;
                double m2 = hi - (hi - lo) / 3.0;
                if (Throughput(alpha, m1) < Throughput(alpha, m2))
                    lo = m1;
                else
                    hi = m2;
            }
            return (float)((lo + hi) / 2.0);
        }

        /// <summary>
        /// The actual move-speed multiplier for a pawn at encumbrance ratio
        /// <paramref name="encumbranceRatio"/> = currentMass / maxCapacity. 1.0 at or under capacity
        /// (matches vanilla — no penalty up to 100%); declines convexly past it (gentle near 100%,
        /// steeper the further you overload); floored at <see cref="SpeedFloor"/>. Returns 1.0 when the
        /// feature is off or set to "no slowdown".
        /// </summary>
        public static float SpeedFactor(int level, float encumbranceRatio)
        {
            int lv = ClampLevel(level);
            if (IsOff(lv) || encumbranceRatio <= 1f)
                return 1f;
            float alpha = Alphas[lv];
            if (alpha <= 0f)
                return 1f; // "no slowdown" stop
            float d = encumbranceRatio - 1f;
            float f = 1f - alpha * d * d;
            return f < SpeedFloor ? SpeedFloor : f;
        }

        /// <summary>
        /// The move-speed multiplier AT the interior (pre-floor) break-even for coefficient
        /// <paramref name="alpha"/> — i.e. <see cref="SpeedFactor(int,float)"/> evaluated at the break-even
        /// ratio. Retained as a standalone pure helper (used by the alloc-guard tests); not on the runtime
        /// hot path. Returns 1.0 when α ≤ 0 (the "no slowdown" curve never slows).
        /// </summary>
        public static float BreakEvenFactor(float alpha)
        {
            if (alpha <= 0f)
                return 1f;
            float r = ComputeBreakEven(alpha);
            float d = r - 1f;
            float f = 1f - alpha * d * d;
            return f < SpeedFloor ? SpeedFloor : f;
        }

        /// <summary>
        /// The encumbrance ratio (mass / maxCapacity) at which overloading stops paying off — the interior
        /// (pre-floor) throughput optimum (the peak of the un-floored carry curve), read from the per-level
        /// table derived from <see cref="Alphas"/>. 1.0 = no overload (off); +∞ = carry freely (level 0).
        /// This is the smart ceiling the pawn loads up to; the <see cref="SpeedFloor"/> backstop lies well
        /// past it and is excluded from the optimization (see <see cref="ComputeBreakEven"/>).
        /// </summary>
        public static float MaxOverloadRatio(int level)
        {
            int lv = ClampLevel(level);
            if (IsOff(lv))
                return 1f; // no overload
            return BreakEvenRatios[lv];
        }
    }
}
