namespace HaulersDream.Core
{
    /// <summary>
    /// The fair-share split for a bulk-load claim when SEVERAL pawns stand ready to load the same manifest (a portal
    /// or transporter boarding lord: everyone's only task is "load this, then enter"). Without it, the first asker's
    /// claim is bounded only by its own carry ceiling, and the smart-overload ceiling (275 percent of capacity by
    /// default, unbounded at level 0) routinely swallows an entire dungeon-loot manifest, so one pawn hauls while the
    /// rest idle. This was the previously-overlooked term in the claim sizing: every bound was per-PAWN (stack,
    /// manifest, ledger, carry, trip mass); none was per-PEER.
    ///
    /// The split divides MASS, not per-def units: per-def division cannot bound a claim over many small defs (dungeon
    /// loot is largely stackCount-1 uniques, where a per-def ceil quota of 1 still lets one pawn claim every def),
    /// while mass is comparable across defs and the sweep already runs a mass budget it can be clamped with.
    /// No game types; the runtime feeds it pool masses and a spawned-pawn count. Pure and deterministic (Multiplayer
    /// runs it in sim on every client).
    ///
    /// <para>DIRECTION OF THE CLAMP — the asymmetry that shapes this whole file (issue #167, reopened). The caller
    /// applies <see cref="ShareMassBudget"/> as a MIN against the asker's own trip budget, so a share can only ever
    /// make a trip SMALLER. It can never make one bigger, and it can never make the crew finish in fewer trips than
    /// the asker would have taken alone. Two rules follow, and BOTH exist because breaking either one costs real
    /// trips: (1) a remainder that already fits in ONE trip must never be divided — splitting it only converts one
    /// full trip into N partial ones, and an asker with NO trip bound fits everything, so it is never divided at all
    /// (issue #243); (2) a pawn that is not actually going to load this manifest must never be part of the divisor,
    /// because every phantom peer shrinks a real hauler's trip for nothing.</para>
    ///
    /// <para>What breaking those rules looked like: a reporter with exactly ONE able hauler mech and 33 units to load
    /// watched it carry 9 units, then 8, then 5, 4, 3, 2, 2, 2, 2, 1 — ten trips (with an all-but-empty pack on most
    /// of them) where vanilla would have taken two. One job is one trip, so each trip re-divided the already-shrunken
    /// remainder, and the divisor counted two mechs — a constructoid and a cleansweeper — that vanilla can never give
    /// a hauling job to at all. Hence the short-circuit in <see cref="ShareMassBudget"/> and the
    /// <see cref="CountsAsCoLoader"/> predicate: the "who counts" decision is now a pure, tested rule here rather
    /// than an ad-hoc scan in the runtime.</para>
    /// </summary>
    public static class LoadFairShare
    {
        /// <summary>
        /// The finite, honest "how much is one trip for this asker?" figure to hand <see cref="ShareMassBudget"/>,
        /// derived from the runtime's own trip budget.
        ///
        /// <para>The runtime budget is normally a real number and passes straight through. It arrives as the
        /// unbounded sentinel only when the pawn has NO carry ceiling (smart overload at "carry freely") AND the
        /// destination imposes no mass cap — a cave exit or other map portal. A pawn with no ceiling still makes
        /// TRIPS, and the size of one is a full pack.</para>
        ///
        /// <para>Deliberately does NOT subtract what the pawn is already carrying. That subtraction collapses to
        /// zero for an ordinarily-geared colonist — carried mass counts worn apparel and equipment, and a human's
        /// whole capacity is 35 kg — and a zero budget skips the fit-in-one-trip rule exactly as the sentinel did,
        /// reinstating the one-item-per-trip bug for that pawn permanently, since gear is never deposited. What a
        /// pawn already carries cannot shrink a trip it has no ceiling for.</para>
        /// </summary>
        /// <param name="runtimeTripBudgetKg">The planner's own per-trip mass budget; the unbounded sentinel
        /// (<see cref="float.MaxValue"/> or infinity) when the pawn has no ceiling and the destination no cap.</param>
        /// <param name="baseCapacityKg">One normal packful for this pawn, before any overload multiplier. Positive
        /// whenever the sentinel can occur (a non-positive base capacity yields a zero ceiling, never an infinite
        /// one), so the result is a usable bound rather than another zero.</param>
        /// <returns>The runtime budget unchanged, or one full pack in place of the sentinel.</returns>
        public static float AskerTripBudgetKg(float runtimeTripBudgetKg, float baseCapacityKg)
            => runtimeTripBudgetKg >= float.MaxValue ? baseCapacityKg : runtimeTripBudgetKg;

        /// <summary>
        /// The mass budget one asker's claim may cover: the claimable pool mass divided evenly across the loaders,
        /// floored to one HEAVIEST unit so every single claimable item always fits inside one share (no starvation
        /// while unclaimed goods remain, and no item orphaned because every share is smaller than it) — but NOT
        /// divided at all when the whole pool already fits inside the asker's own single trip, nor when the asker
        /// has no per-trip bound for it to fit inside.
        /// </summary>
        /// <param name="claimableMassKg">Total mass (kg) of what THIS asker could claim right now: pool stacks of
        /// claimable defs, each counted up to the def's remaining claimable units. At most 0 when everything left is
        /// massless or already claimed.</param>
        /// <param name="heaviestUnitMassKg">Unit mass (kg) of the heaviest single claimable item in that pool, the
        /// no-starvation floor. Flooring to the HEAVIEST (not lightest) unit makes every claimable stack
        /// unit-affordable within one share, so the fairness clamp alone can never mass-starve a pick: a
        /// lightest-unit floor could leave a heavy item unclaimable by the whole crew when the raw share fell below
        /// its unit mass (say a sculpture heavier than the per-pawn split, with the light item that set the floor
        /// sitting unreachable behind a wall). The pawn's own trip budget still caps what it physically carries.
        /// Non-positive values (no massive item seen) disable the floor.</param>
        /// <param name="loaderCount">How many pawns the pool is split across: the asker plus every other ready
        /// co-loader that holds NO live claim on this task (claim holders' slices are already excluded from the
        /// claimable mass). Values below 2 mean the asker is alone. Only pawns that pass
        /// <see cref="CountsAsCoLoader"/> belong here — a divisor padded with pawns that will never load this
        /// manifest is the whole of issue #167.</param>
        /// <param name="askerTripBudgetKg">What the ASKER itself can move in ONE trip (kg): the very budget the
        /// caller is about to clamp with the returned share (its carry headroom, tightened by any destination mass
        /// cap). Must be a REAL number of kilograms — a pawn with no carry CEILING still makes trips, so the caller
        /// converts that case into an honest one-pack figure before asking (see <c>TransportLoad.TryGiveBulkJob</c>).
        /// The unbounded sentinels — <see cref="float.MaxValue"/> from an uncapped smart-overload ceiling,
        /// <see cref="float.PositiveInfinity"/> from an uncapped destination — are handled only as a fail-safe, and
        /// mean no clamp at all. A malformed value (NaN, zero, negative) is NOT unbounded: it simply never
        /// short-circuits, so a nonsense number can never widen a claim.</param>
        /// <returns><see cref="float.PositiveInfinity"/> when no clamp applies, else
        /// <c>max(claimableMassKg / loaderCount, heaviestUnitMassKg)</c>. No clamp applies when: the asker is alone
        /// (a lone loader keeps today's exact behavior); the pool has nothing measurable to divide; the asker has no
        /// real per-trip bound at all; or the whole claimable pool already fits inside
        /// <paramref name="askerTripBudgetKg"/>.</returns>
        public static float ShareMassBudget(float claimableMassKg, float heaviestUnitMassKg, int loaderCount,
            float askerTripBudgetKg)
        {
            // A lone loader is never clamped: the fair share of one is everything, and returning the sentinel keeps
            // the single-pawn planner byte-identical to the pre-fairness behavior.
            if (loaderCount <= 1)
                return float.PositiveInfinity;

            // Nothing measurable to divide (empty or all-massless pool): don't clamp. The sweep's other bounds
            // (claim units, carry ceiling, CE bulk) still apply; a 0 budget here would wrongly sweep NOTHING because
            // the sweep loop stops the moment its mass budget is spent.
            if (claimableMassKg <= 0f)
                return float.PositiveInfinity;

            // NO REAL PER-TRIP BOUND — never clamp (issue #243). Two sentinels mean "this asker has no ceiling":
            // float.MaxValue (smart overload at level 0, "carry freely") and positive infinity (an uncapped
            // destination). PositiveInfinity >= MaxValue, so the one comparison catches both, while NaN fails it —
            // malformed is not unbounded. Such an asker clears ANY pool in one trip, which is the rule below taken
            // to its limit, so the answer is the same: don't divide. The caller now converts an unbounded ceiling
            // into an honest one-pack figure before asking, so a sentinel reaching here is a CALLER bug — and
            // declining to clamp is the only safe way to fail it: for a lone or unbounded asker Hauler's Dream must
            // never move less per trip than vanilla, and vanilla hand-carries a whole stack with no mass term at
            // all. (For a genuine multi-pawn crew a single pawn's trip CAN be smaller than vanilla's — the crew
            // clears the pool together in one round — so this is a bound on the lone/unbounded case, not on every
            // trip.)
            //
            // This replaces the opposite rule, which excluded the unbounded case so that one pawn could not
            // "swallow the manifest and idle its peers". That reasoning was simply wrong: the caller applies this
            // result as a MIN against the same budget, so declining to clamp can never let a pawn carry past its
            // own capacity — and one pawn clearing the order in a single trip beats four pawns making nineteen
            // ever-shrinking ones. What the exclusion actually produced: with no ceiling the short-circuit below
            // was unreachable, the share decayed on every trip, and the no-starvation floor bottomed that decay
            // out at exactly ONE item per trip — colonists ordered out of a cave with the loot carrying insect
            // jelly one piece at a time, the same decay #167's short-circuit exists to end.
            if (askerTripBudgetKg >= float.MaxValue)
                return float.PositiveInfinity;

            // NEVER split a remainder one trip can already clear. The caller uses this result as a MIN against that
            // same trip budget, so declining to clamp here can never let a pawn carry more than its own capacity —
            // while clamping can only make its trip smaller. Dividing a pool that already fits therefore buys
            // nothing and costs trips: the asker comes back for the rest, re-divides the (now smaller) remainder,
            // and each round trip carries less than the last, down to a single item in an otherwise empty pack.
            // A nonsense budget (NaN, zero, negative) fails the positive test and falls through to the plain
            // division, unchanged — only a real, positive number of kilograms may skip the split.
            if (askerTripBudgetKg > 0f && claimableMassKg <= askerTripBudgetKg)
                return float.PositiveInfinity;

            float share = claimableMassKg / loaderCount;

            // No-starvation floor: every loader can always claim at least one unit of ANY claimable item, including
            // the heaviest. Without it a remainder split many ways yields a budget below an item's unit mass, that
            // item becomes unclaimable for the whole crew, and the haul stalls into the vanilla one-stack fallback.
            // (The floor is why an over-divided remainder bottomed out at one item per trip rather than at zero; the
            // short-circuit above is what stops a remainder from ever reaching it while one trip could clear it.)
            if (heaviestUnitMassKg > 0f && share < heaviestUnitMassKg)
                share = heaviestUnitMassKg;
            return share;
        }

        /// <summary>
        /// Does another pawn belong in <see cref="ShareMassBudget"/>'s divisor — is it genuinely going to load THIS
        /// manifest alongside the asker? Every pawn counted here shrinks a real hauler's trip, and the clamp is
        /// one-directional (it can only remove capacity), so the bar is "already committed to this load and able to
        /// act on that right now", never "might plausibly help". A pawn that fails ANY fact is simply not counted,
        /// which at worst leaves the asker with a larger share than a perfectly even split — the harmless direction.
        ///
        /// <para>Deliberately NOT counted: free haulers with no tie to this manifest. They have a colony of other
        /// work and no board gate holds them, so dividing for a pawn that may never come shrinks every real trip for
        /// nothing. Issue #167's reopening was exactly that mistake — a non-home-map carve-out counted any pawn that
        /// passed the generic "could this pawn have bulk work" gate, which admits mechanoids regardless of hauling
        /// capability, so a constructoid and a cleansweeper (vanilla gives neither a hauling job, ever) divided a
        /// lone hauler mech's trips by three.</para>
        /// </summary>
        /// <param name="isBoardingPassengerOfThisLoadable">The pawn is a boarding passenger of THIS exact loadable:
        /// its lord duty is "load this transporter group / portal, then enter it". This is the contractual tie that
        /// makes it a certainty rather than a maybe — such a pawn has no other work and cannot leave until the
        /// manifest empties.</param>
        /// <param name="canDoHaulingWorkType">Vanilla would let this pawn do the Hauling WORK TYPE (not merely the
        /// work tag — see the #229 gate; for a colony mech the work type is whatever its
        /// <c>mechEnabledWorkTypes</c> allows, which is how a constructoid or cleansweeper is excluded). A pawn the
        /// game will not give hauling work to cannot be relied on to carry a share.</param>
        /// <param name="hasClaimableWork">The live ledger still has something for this pawn to claim on this task.
        /// A passenger that is already aboard, or that cannot reach anything claimable, is done loading — counting
        /// it would shrink everyone else's share permanently, for a pawn that will never ask again.</param>
        /// <param name="downed">Incapacitated: it will not run its load duty at all.</param>
        /// <param name="drafted">Under player control: it is not taking jobs from the work/duty tree.</param>
        /// <param name="inMentalState">Berserk, wandering, binging: same, it will not load.</param>
        /// <param name="capableOfManipulation">Has working manipulation — vanilla's own gate for handing out a
        /// loading job.</param>
        /// <param name="hasCarrierComp">Can physically run the bulk-load driver (Hauler's Dream's carrier comp and a
        /// pawn inventory are both present). Without them the pawn falls back to vanilla one-stack loading and never
        /// draws from the shared claim pool.</param>
        /// <returns>True only when every fact holds; the pawn then counts once toward the divisor.</returns>
        public static bool CountsAsCoLoader(
            bool isBoardingPassengerOfThisLoadable,
            bool canDoHaulingWorkType,
            bool hasClaimableWork,
            bool downed,
            bool drafted,
            bool inMentalState,
            bool capableOfManipulation,
            bool hasCarrierComp)
            => isBoardingPassengerOfThisLoadable
               && canDoHaulingWorkType
               && hasClaimableWork
               && capableOfManipulation
               && hasCarrierComp
               && !downed
               && !drafted
               && !inMentalState;
    }
}
