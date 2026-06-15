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
        /// <see cref="NoOverload"/> plus the per-pawn half of the bargain: the overload deal is
        /// slowdown-FOR-capacity, and <see cref="StatPart_Overload"/> slows player-faction HUMANLIKES AND
        /// player-faction MECHANOIDS — so both may load past their carry limit (and pay the matching
        /// slowdown). Only a non-mech non-humanlike (e.g. an animal hauler) stands down here: the StatPart
        /// never slows it, so it must not get the extra capacity penalty-free (a balance regression). The two
        /// halves stay in lockstep — the SAME race classes that overload here are the ones the StatPart slows.
        /// </summary>
        internal static bool NoOverloadFor(Pawn pawn, HaulersDreamSettings s)
        {
            if (NoOverload(s))
                return true;
            var race = pawn?.RaceProps;
            if (race == null)
                return true;
            return !race.Humanlike && !race.IsMechanoid;
        }

        internal static int CountToPickUp(Pawn pawn, Thing thing, HaulersDreamSettings s)
        {
            if (pawn == null || thing == null || s == null)
                return 0;
            float maxCap = MassUtility.Capacity(pawn); // under CE this reads CE's CarryWeight (CE postfix)
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float cur = MassUtility.GearAndInventoryMass(pawn);
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
