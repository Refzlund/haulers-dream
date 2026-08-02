using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The single place every Hauler's Dream right-click HAUL order asks "may this pawn be ordered to haul?".
    /// It pulls the live primitives off the pawn and delegates the decision to the pure
    /// <see cref="HaulOrderPolicy"/>, so the ordered side and the automatic side (<c>YieldRouter.IsEligible</c>
    /// → <see cref="EligibilityPolicy"/>) apply the SAME incapable clause and can never drift apart.
    ///
    /// <para>ISSUE #229 — the hole this closes. Every HD haul order used to gate on the work TAG
    /// (<c>Pawn.WorkTagIsDisabled(WorkTags.Hauling)</c>). Vanilla's <c>WorkTypeDef Hauling</c> carries
    /// <c>workTags = {ManualDumb, Hauling, Commoner, AllWork}</c>, and <c>BackstoryDef.AllowsWorkType</c> is
    /// <c>(workDisables &amp; workType.workTags) == 0</c> — so a backstory that disables <c>ManualDumb</c> or
    /// <c>Commoner</c> puts the Hauling work TYPE into the pawn's <c>DisabledWorkTypes</c> while leaving the
    /// <c>Hauling</c> BIT clear in <c>CombinedDisabledWorkTags</c>. The tag query therefore answered "can haul"
    /// for exactly the pawns vanilla refuses to give hauling work to, and HD offered them the order. Vanilla's
    /// own equivalent (<c>FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption</c>) greys its option out with
    /// <c>CannotPrioritizeWorkTypeDisabled</c>, which reads the work TYPE.</para>
    ///
    /// <para>The probe is <see cref="WorkCapabilityProbe"/> (not a raw <c>WorkTypeIsDisabled</c> call) — the
    /// same fault-isolating call the plan-route / plan-sow providers already make, so a malformed modded pawn
    /// whose vanilla work-type query throws hides the option instead of killing the whole float menu (#197).
    /// It also composes with the "all pawns can haul" override for free: that override is a postfix on
    /// <c>Pawn.GetDisabledWorkTypes</c> (see <see cref="WorkOverride"/>), which is what
    /// <c>WorkTypeIsDisabled</c> reads.</para>
    /// </summary>
    internal static class HaulOrderGate
    {
        /// <summary>
        /// Why <paramref name="pawn"/> must not be offered an ordered HD hauling task, or
        /// <see cref="HaulOrderBlock.None"/>.
        /// </summary>
        /// <param name="pawn">The pawn the float menu is being built for. A null pawn is reported as
        /// <see cref="HaulOrderBlock.Manipulation"/> (the most conservative block).</param>
        /// <returns>The blocking reason, or <see cref="HaulOrderBlock.None"/> when the order may be offered.</returns>
        internal static HaulOrderBlock BlockFor(Pawn pawn)
        {
            if (pawn == null)
                return HaulOrderBlock.Manipulation;
            var s = HaulersDreamMod.Settings;
            return HaulOrderPolicy.BlockFor(
                capableOfManipulation: pawn.health?.capacities != null
                                       && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation),
                incapableOfHauling: WorkCapabilityProbe.IsDisabled(pawn, WorkTypeDefOf.Hauling),
                allowIncapable: s != null && s.allowIncapable);
        }

        /// <summary>True when an ordered HD hauling task must be hidden for <paramref name="pawn"/>.</summary>
        internal static bool Blocks(Pawn pawn) => BlockFor(pawn) != HaulOrderBlock.None;
    }
}
