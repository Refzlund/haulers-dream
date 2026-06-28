using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Despawn-safe wrapper around the vanilla construction "space remaining" queries. Vanilla
    /// <see cref="EnrouteUtility.GetSpaceRemainingWithEnroute"/> dereferences <c>enroute.Map</c>
    /// unconditionally, and <see cref="Thing.Map"/> is null on a despawned-or-destroyed Thing (any
    /// <c>mapIndexOrState &lt; 0</c>). So HD code that queries a needer it captured into a Job earlier can NRE
    /// if another pawn finished or otherwise despawned that needer in the window before HD's own FailOn aborts
    /// the job (issue #88: a mech swarm completing a substructure area destroys one blueprint cell while a
    /// builder is still mid-load for it; the count getter then asks the gone blueprint for its remaining space).
    ///
    /// <para>This gates on <see cref="Thing.Spawned"/> (which implies <c>Map != null</c>) and reports 0
    /// ("no space") for a gone needer, so the delivery driver drains cleanly to the next toil where its FailOn
    /// ends the job. When the needer is alive the result is BYTE-IDENTICAL to calling vanilla directly. Nothing
    /// here patches or alters vanilla; it only decides whether HD makes the vanilla call at all.</para>
    /// </summary>
    public static class EnrouteSafety
    {
        /// <summary>
        /// Space remaining in <paramref name="needer"/> for <paramref name="def"/>, excluding
        /// <paramref name="pawn"/>'s own enroute claim. Returns 0 (never NREs) when the needer is null, not a
        /// constructible, or no longer spawned. Mirrors the construct-delivery driver's original branch order:
        /// the enroute-aware count when the needer is an <see cref="IHaulEnroute"/> (every real construct needer,
        /// a Blueprint_Build or Frame, is one), else the plain <see cref="IConstructible.ThingCountNeeded"/>.
        ///
        /// <para>The <see cref="IConstructible"/> check is first only as the "is this a real construct needer"
        /// test; it never zeroes a live construct delivery, because every needer HD passes here is a
        /// constructible. A non-constructible storage <see cref="IHaulEnroute"/> (a bookcase / stockpile
        /// building) is never a construct-delivery needer, so reporting 0 for one is correct, not a silent drop.</para>
        /// </summary>
        public static int SpaceRemainingSafe(Thing needer, ThingDef def, Pawn pawn)
        {
            // ShouldQueryNeederSpace pins the gate (the needer is spawned AND we know the material) in the
            // Verse-free Core so a refactor that re-weakens it back toward the old !DestroyedOrNull() guard —
            // the exact gap behind #88, which misses a plain-despawned needer — is caught by a unit test.
            bool spawned = needer != null && needer.Spawned;
            if (!Core.ConstructDeliveryPlan.ShouldQueryNeederSpace(spawned, def != null))
                return 0;
            if (!(needer is IConstructible ic))
                return 0;
            if (needer is IHaulEnroute enroute)
                return Mathf.Max(0, enroute.GetSpaceRemainingWithEnroute(def, pawn));
            return Mathf.Max(0, ic.ThingCountNeeded(def));
        }
    }
}
