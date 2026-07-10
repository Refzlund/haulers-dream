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

        /// <summary>
        /// CHEAP pre-gate run BEFORE the expensive Verse-side work (the per-stack mass walk + the
        /// <c>TryFindBestBetterStoreCellFor</c> spatial storage search): only those numbers that are free to
        /// read short-circuit a divert that the full <see cref="ShouldUnloadOnWay"/> / <see cref="ShouldUnloadOnRunEnd"/>
        /// math would reject anyway, so the storage search is deferred until a divert is actually plausible.
        /// Pure; mirrors the necessary conditions the full decision already enforces, so it can only short-circuit
        /// (never admit) a divert the full math would reject:
        /// <list type="bullet">
        /// <item><paramref name="capPositive"/> — a pawn with no carry capacity (<c>cap &lt;= 0</c>) can carry nothing,
        /// so its load fraction is meaningless; the full path bails on <c>cap &lt;= 0f</c>.</item>
        /// <item><paramref name="trackedCount"/> &gt; 0 — nothing tracked means nothing to divert (the full path
        /// bails on an empty tracked set).</item>
        /// <item><paramref name="cooldownElapsed"/> — a recent (possibly failed) divert is still cooling down; the
        /// full path bails when the cooldown has not elapsed.</item>
        /// <item><paramref name="loadFraction"/> &ge; <see cref="MinLoadFraction"/> — both
        /// <see cref="ShouldUnloadOnWay"/> and <see cref="ShouldUnloadOnRunEnd"/> reject a load below the minimum
        /// fraction outright, so a sub-threshold load can never produce a divert regardless of the geometry.</item>
        /// </list>
        /// Returns true only when a divert remains POSSIBLE and the storage search is therefore worth running.
        /// </summary>
        public static bool ShouldAttemptDivert(
            float loadFraction, bool cooldownElapsed, int trackedCount, bool capPositive,
            float minLoadFraction = MinLoadFraction)
        {
            if (!capPositive)
                return false;
            if (trackedCount <= 0)
                return false;
            if (!cooldownElapsed)
                return false;
            return loadFraction >= minLoadFraction;
        }

        /*
            ──────────────────────────────
             Downtime-swap severity gates
            ──────────────────────────────
            The rest / food / joy think-node postfixes may swap the pawn's downtime job for an
            unload trip ("put the load away before relaxing"), but NEVER when the need is
            already critical: a starving pawn is taking malnutrition damage and an exhausted
            pawn is about to collapse, so they must eat / sleep NOW and shed the load afterwards
            (the interval safety net catches it on wake). These gates are the single source of
            truth for that severity boundary; the Verse layer passes the vanilla enum as an int.

            → KEY: issue #122. A reading pawn's ONLY mid-job hunger rescue is the 600-tick
              CheckForJobOverride(9.1) reaching a food job. Anything that consistently costs the
              food node its job (a throw, or an unbounded swap) leaves the pawn reading until it
              starves to death. The Starving stand-down below is one of the two layers that keep
              the swap bounded (the other is the divert cooldown).
        */

        /// <summary>Vanilla <c>RimWorld.HungerCategory.Starving</c> (Fed=0, Hungry=1, UrgentlyHungry=2,
        /// Starving=3), the same int-pinned enum convention as <see cref="BatchYieldPolicy"/>.</summary>
        public const int HungerStarving = 3;

        /// <summary>Vanilla <c>RimWorld.RestCategory.Exhausted</c> (Rested=0, Tired=1, VeryTired=2,
        /// Exhausted=3).</summary>
        public const int RestExhausted = 3;

        /// <summary>Whether the unload-before-eating swap may replace a food job for a pawn at
        /// <paramref name="hungerCategory"/> (the vanilla <c>HungerCategory</c> as an int). False at
        /// Starving: the pawn is taking damage and must eat NOW, so the swap stands down and the vanilla
        /// food job runs unchanged.</summary>
        public static bool MaySwapFoodJobForUnload(int hungerCategory)
            => hungerCategory < HungerStarving;

        /// <summary>Whether the unload-before-sleep swap may replace a rest job for a pawn at
        /// <paramref name="restCategory"/> (the vanilla <c>RestCategory</c> as an int). False at
        /// Exhausted: the pawn is about to collapse and must sleep NOW.</summary>
        public static bool MaySwapRestJobForUnload(int restCategory)
            => restCategory < RestExhausted;

        /// <summary>Run-end detour-bar floor (tiles) — see <see cref="ShouldUnloadOnRunEnd"/>.</summary>
        public const int RunEndMinDetourTiles = 20;

        /// <summary>Run-end detour bar as a fraction of the trip length.</summary>
        public const float RunEndMaxDetourFraction = 1.0f;

        /// <summary>
        /// Run-END variant of <see cref="ShouldUnloadOnWay"/>: the pawn has FINISHED its yield-producing run
        /// and just picked an UNRELATED job, so the accumulate window is over and it should shed a worthwhile
        /// load at storage even on a SHORT hop — there is deliberately NO minimum-trip floor here (a pawn
        /// cleaning filth right next to storage should still drop its load). The only guard is that storage be
        /// reasonably near the path, not a cross-map detour. This is the "switched to non-yield work near
        /// storage" reconciler that lets a pawn stop carrying a deconstruct/mining load around while it does
        /// other things. Pure.
        /// </summary>
        public static bool ShouldUnloadOnRunEnd(
            int pawnToTarget, int pawnToStorage, int storageToTarget, float loadFraction,
            float minLoadFraction = MinLoadFraction, int minDetourTiles = RunEndMinDetourTiles,
            float maxDetourFraction = RunEndMaxDetourFraction)
        {
            if (loadFraction < minLoadFraction)
                return false; // not worth a trip for a trivial load, even at run-end
            int detour = pawnToStorage + storageToTarget - pawnToTarget;
            if (detour < 0)
                detour = 0;
            int bar = Math.Max(minDetourTiles, (int)(maxDetourFraction * pawnToTarget));
            return detour <= bar;
        }

        /// <summary>
        /// Max extra straight-line tiles a PROTECTED-work pass-by unload may add and still count as "zero detour".
        /// The whole point of the protected-work relaxation is to NEVER delay medical / rescue / firefighting work
        /// (issue #107), so storage must sit essentially ON the pawn's path to its next destination: a few steps off
        /// the line, no more. Deliberately tiny, unlike the normal <see cref="MaxDetourFraction"/> journey bar.
        /// </summary>
        /// <summary>Budget (extra tiles walked) for <see cref="OpportunisticDetour.Short"/>: only a near-free
        /// pass-by, roughly an item on the exact path. This was the original single fixed zero-detour budget.</summary>
        public const int DetourTilesShort = 4;

        /// <summary>Budget for <see cref="OpportunisticDetour.Standard"/> (the default): a short detour is worth
        /// saving a second trip.</summary>
        public const int DetourTilesStandard = 10;

        /// <summary>Budget for <see cref="OpportunisticDetour.Long"/>: take worthwhile detours (on protected work
        /// this trades a small, deliberate delay for fewer trips).</summary>
        public const int DetourTilesLong = 20;

        /// <summary>
        /// The extra-tiles budget for a detour-tolerance level. <see cref="OpportunisticDetour.Off"/> is not mapped
        /// here (the callers skip the behavior outright rather than pass a zero budget); it returns the Standard
        /// budget defensively so a caller that forgets the Off-gate degrades to the default rather than to 0.
        /// </summary>
        public static int DetourBudgetTiles(OpportunisticDetour tolerance)
        {
            switch (tolerance)
            {
                case OpportunisticDetour.Short:
                    return DetourTilesShort;
                case OpportunisticDetour.Long:
                    return DetourTilesLong;
                default:
                    return DetourTilesStandard;
            }
        }

        /// <summary>
        /// Protected-work variant of <see cref="ShouldUnloadOnWay"/>: a pawn on NON-emergency protected work (an
        /// elective surgery, a rescue, a warden task) may shed its scooped load ONLY when its storage is on the way
        /// to its next destination, within <paramref name="budgetTiles"/> extra tiles, so the drop stays a cheap
        /// pass-by (issue #107: at the default budget the delay is minor; the player tunes it via
        /// <see cref="OpportunisticDetour"/>). There is deliberately NO minimum-trip or load-fraction floor: if it
        /// is within budget, take it whatever the load size (the caller has confirmed something IS unloadable).
        /// </summary>
        /// <param name="pawnToTarget">Straight-line tiles from the pawn to its next destination.</param>
        /// <param name="pawnToStorage">Straight-line tiles from the pawn to the storage cell.</param>
        /// <param name="storageToTarget">Straight-line tiles from the storage cell to the destination.</param>
        /// <param name="budgetTiles">Max extra tiles the pass-by may add over going straight (the tolerance budget).</param>
        public static bool ShouldUnloadZeroDetour(int pawnToTarget, int pawnToStorage, int storageToTarget, int budgetTiles)
        {
            int detour = pawnToStorage + storageToTarget - pawnToTarget;
            if (detour < 0)
                detour = 0;
            return detour <= budgetTiles;
        }

        /// <summary>
        /// The pickup MIRROR of <see cref="ShouldUnloadZeroDetour"/>: a pawn ALREADY walking to storage may grab a
        /// loose haulable that sits on that path, so the item rides along on a trip it was making anyway and the
        /// offload is not meaningfully delayed. Reported case: a hauler carrying scooped goods to the shelves steps
        /// right over a loose organ and leaves it, because an HD unload trip cannot opportunistically scoop. Same
        /// <paramref name="budgetTiles"/> budget as the unload mirror: the extra tiles of pawn -&gt; thing -&gt;
        /// destination over going straight to the destination must stay within it. Floats because the en-route
        /// caller works in straight-line <c>DistanceTo</c> (no rounding step to lose).
        /// </summary>
        /// <param name="pawnToDest">Straight-line tiles from the pawn to the store destination it is heading to.</param>
        /// <param name="pawnToThing">Straight-line tiles from the pawn to the loose item.</param>
        /// <param name="thingToDest">Straight-line tiles from the loose item to the store destination.</param>
        /// <param name="budgetTiles">Max extra tiles the grab may add over going straight (the tolerance budget).</param>
        public static bool ShouldGrabOnWay(float pawnToDest, float pawnToThing, float thingToDest, int budgetTiles)
        {
            float detour = pawnToThing + thingToDest - pawnToDest;
            if (detour < 0f)
                detour = 0f;
            return detour <= budgetTiles;
        }
    }
}
