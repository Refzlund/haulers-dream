using System;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Game-layer bridge that turns live <c>MassUtility</c> numbers into a pickup count via the pure
    /// <see cref="OverloadPolicy"/>. With the slider at "off" this returns exactly the old
    /// carry-limit count (no behaviour change); otherwise it lets the pawn load past 100% capacity up
    /// to the smart break-even ceiling so it makes fewer trips (paying the move-speed penalty from
    /// <see cref="StatPart_Overload"/> while overloaded).
    /// </summary>
    internal static class OverloadGate
    {
        // The pawn is actively producing/hauling during a work run, so more of this resource is coming:
        // it should fill to the smart overload ceiling rather than stopping at 100%. (Over-carrying on a
        // genuine last item is harmless — one slightly heavier trip — and self-limited by the ceiling.)
        private const int ActiveRunDemand = 1 << 20;

        /// <summary>
        /// True when inventory loading must NEVER pass the carry limit: the player chose strict carry
        /// weight, set the overload slider to "Off" — or Combat Extended is active. Under CE, CE's own
        /// encumbrance simulation (weight+bulk penalties via its MoveSpeed StatWorker) is the single
        /// source of slowdown truth, so the smart-overload feature stands down entirely (our StatPart
        /// would otherwise STACK with CE's penalty, and our break-even math assumes our slope).
        /// </summary>
        internal static bool NoOverload(HaulersDreamSettings s)
            => s == null || s.strictCarryWeight || OverloadTuning.IsOff(s.overloadLevel) || CECompat.IsActive;

        /// <summary>The overload slider level with the strict/Off/CE override applied.</summary>
        internal static int EffectiveLevel(HaulersDreamSettings s)
            => NoOverload(s) ? OverloadTuning.OffLevel : s.overloadLevel;

        /// <summary>
        /// The per-pawn half of the bargain: the overload deal is slowdown-FOR-capacity, so the pawns that
        /// may load past their carry limit must be exactly the pawns the <see cref="StatPart_Overload"/> can
        /// slow. Both this gate and the StatPart derive the shared strict/CE/level/race off-matrix from the
        /// SINGLE pure predicate <see cref="OverloadPolicy.ParticipatesInOverload"/>, so they can never
        /// silently diverge on those conditions (a future edit that breaks the lockstep fails the matrix
        /// test in OverloadPolicyTests).
        ///
        /// This gate is deliberately faction- and draft-AGNOSTIC (an overloaded pawn keeps the capacity it
        /// already loaded even once drafted); the StatPart layers the player-faction/undrafted asymmetry on
        /// top via <see cref="OverloadPolicy.AppliesSpeedPenalty"/>. Maps the live Verse pawn → the pure
        /// inputs; returns true (stand down to the plain carry limit) whenever the feature is not active for
        /// this pawn — including null settings or a pawn with no race.
        /// </summary>
        internal static bool NoOverloadFor(Pawn pawn, HaulersDreamSettings s)
        {
            if (s == null)
                return true;
            var race = pawn?.RaceProps;
            if (race == null)
                return true;
            return !OverloadPolicy.ParticipatesInOverload(
                s.strictCarryWeight, s.overloadLevel, CECompat.IsActive, race.Humanlike, race.IsMechanoid);
        }

        /// <summary>
        /// Pickup count for <paramref name="thing"/>, reading the pawn's capacity and current mass LIVE. This is
        /// the path for callers that MUTATE the pawn's inventory and re-read within the same tick (e.g. the
        /// corpse-strip scoop loop): each call must see the up-to-date mass, so it must NOT go through the
        /// per-(pawn,tick) memo. Non-mutating callers that sweep a fixed pawn (mass invariant across the loop)
        /// should hoist the capacity/current-mass ONCE — via <see cref="PawnMassCache"/> — and call the
        /// primitive <see cref="CountToPickUp(Pawn,Thing,HaulersDreamSettings,float,float,float)"/> overload.
        /// </summary>
        internal static int CountToPickUp(Pawn pawn, Thing thing, HaulersDreamSettings s)
        {
            if (pawn == null || thing == null || s == null)
                return 0;
            float maxCap = CarryCapacity.Of(pawn); // MassUtility.Capacity ×HD mech mult; under CE reads CE's CarryWeight
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float cur = MassUtility.GearAndInventoryMass(pawn);
            return CountToPickUp(pawn, thing, s, maxCap, baseCap, cur);
        }

        /// <summary>
        /// Pickup count for <paramref name="thing"/> given the pawn's capacity/current-mass already in hand —
        /// the loop-invariant ("hoisted") form. A queue-only sweep over many candidate things (the mass doesn't
        /// change because nothing is actually taken into inventory) computes <paramref name="maxCap"/> /
        /// <paramref name="baseCap"/> / <paramref name="cur"/> ONCE (ideally via <see cref="PawnMassCache"/>, so
        /// it shares the same-tick MoveSpeed read) and passes them here per candidate, paying only the
        /// per-thing unit-mass read + the pure <see cref="OverloadPolicy.UnitsToCarry"/> arithmetic instead of a
        /// full apparel+equipment+inventory mass walk per candidate. Behaviour is identical to the live 3-arg
        /// form when fed the live numbers — only the redundant per-candidate mass recompute is removed.
        /// </summary>
        /// <param name="maxCap">The pawn's true max carry capacity (<c>MassUtility.Capacity</c>; CE's CarryWeight under CE).</param>
        /// <param name="baseCap">The configured carry-limit mass (<see cref="CarryMath.EffectiveCapacity"/> of <paramref name="maxCap"/>).</param>
        /// <param name="cur">The pawn's current gear+inventory mass (<c>MassUtility.GearAndInventoryMass</c>).</param>
        internal static int CountToPickUp(Pawn pawn, Thing thing, HaulersDreamSettings s,
            float maxCap, float baseCap, float cur)
        {
            if (pawn == null || thing == null || s == null)
                return 0;
            float unit = thing.GetStatValue(StatDefOf.Mass);
            int count = OverloadPolicy.UnitsToCarry(
                NoOverloadFor(pawn, s) ? OverloadTuning.OffLevel : s.overloadLevel, maxCap, baseCap, cur, unit,
                demandUnits: ActiveRunDemand, availableUnits: thing.stackCount);
            // Combat Extended adds a BULK dimension vanilla mass math can't see — defer to CE's own
            // canonical fit check (weight AND bulk). No-ops (int.MaxValue) without CE.
            return Math.Min(count, CECompat.MaxFitCount(pawn, thing));
        }
    }
}
