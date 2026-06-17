namespace HaulersDream.Core
{
    /// <summary>
    /// The pure decision core for While You're Up's consumer-aware STORAGE ROUTING ("relocate a supply/
    /// ingredient stack to storage CLOSER to the job that will consume it, and grab same-/equal-priority
    /// extras"). This class owns ONLY the priority-eligibility rule and the distance ranking; the Verse layer
    /// enumerates slot groups / cells and supplies their priorities and squared distances. Faithful port of
    /// WYU's two relocation gates:
    ///
    /// <list type="bullet">
    ///   <item>The slot-group priority-eligibility loop in
    ///   <c>StoreUtility.TryFindBestBetterStoreCellFor_MidwayToTarget</c>
    ///   (<c>StoreUtility.cs:214-242</c>) — which priorities a relocation may target.</item>
    ///   <item>The "store cell closest to the thing↔job MIDWAY" ranking
    ///   (<c>StoreUtility.cs:247-266</c>) — reused via the existing
    ///   <see cref="EnRoutePickupPolicy.Midway"/> / <see cref="EnRoutePickupPolicy.MidwayDistanceSquared"/>
    ///   helpers (this policy does NOT duplicate the midway math — see <see cref="MidwayBetter"/>).</item>
    ///   <item>The "is this relocation worth it" gate from the before-carry detour
    ///   (<c>BeforeCarryDetour.cs:100-104</c>): only relocate when the candidate store is STRICTLY closer to
    ///   the carry target than the thing's current position — see <see cref="WorthRelocating"/>.</item>
    /// </list>
    ///
    /// <para><b>StoragePriority is a Verse enum</b> (<c>Unstored=0 &lt; Low &lt; Normal &lt; …</c>); the Verse
    /// layer casts it to <c>int</c> before calling here (higher int = higher priority). The "Unstored" guard
    /// (<c>StoreUtility.cs:219</c>) lives in the Verse layer's slot-group enumeration (a candidate with the
    /// Unstored priority is never offered to this policy); HD never relocates INTO Unstored because a routed
    /// relocation is, by construction, going to a real storage destination.</para>
    ///
    /// <para>Allocation-light: every method is primitives in / bool out, no per-call allocation (see
    /// <c>StorageRoutingPolicyPerfTests</c>).</para>
    /// </summary>
    public static class StorageRoutingPolicy
    {
        /// <summary>
        /// Is a candidate slot group's PRIORITY eligible as a routing-relocation destination for a stack
        /// currently stored at <paramref name="currentPriority"/>? Faithful port of WYU's slot-group entry
        /// gate in <c>TryFindBestBetterStoreCellFor_MidwayToTarget</c> combined with its before-carry
        /// equal-priority break.
        ///
        /// <para><b>WYU rule (the two gates this reproduces):</b></para>
        /// <list type="number">
        ///   <item>The base loop never considers a LOWER priority than the stack's current one:
        ///   <c>if (slotGroup.Settings.Priority &lt; currentPriority) break;</c>
        ///   (<c>StoreUtility.cs:218</c>) — so a strictly-LOWER candidate is always rejected.</item>
        ///   <item>EQUAL priority is gated by the relocation kind. For an OPPORTUNITY relocation (no
        ///   before-carry target) equal priority is broken out immediately:
        ///   <c>if (slotGroup.Settings.Priority == currentPriority &amp;&amp; !beforeCarryTarget.IsValid) break;</c>
        ///   (<c>StoreUtility.cs:220</c>, <c>:ToEqualPriority</c>). For a BEFORE-CARRY relocation equal
        ///   priority is allowed ONLY when the <c>routeToEqualPriority</c> toggle is on:
        ///   <c>if (!settings.HaulBeforeCarry_ToEqualPriority &amp;&amp; slotGroup.Settings.Priority == currentPriority) break;</c>
        ///   (<c>StoreUtility.cs:232</c>). WYU's <c>HaulBeforeCarry_ToEqualPriority</c> default is
        ///   <c>true</c> (<c>Settings.cs:478</c>); HD exposes it as <c>routeToEqualPriority</c>.</item>
        ///   <item>A candidate that IS the stack's own slot group is skipped:
        ///   <c>if (… slotGroup == map.haulDestinationManager.SlotGroupAt(thing.Position)) continue;</c>
        ///   (<c>StoreUtility.cs:233</c>) — never relocate within the SAME group. The Verse layer can't
        ///   always cheaply pre-exclude the own group (priorities tie), so this policy enforces it via the
        ///   <paramref name="isOwnGroup"/> flag (the C3 shouldFix).</item>
        /// </list>
        ///
        /// <para>STRICTLY-HIGHER priority is therefore always eligible (it never trips any break); EQUAL is
        /// eligible only on the before-carry path with the equal-priority toggle on; LOWER is never eligible;
        /// the OWN group is never eligible regardless of priority.</para>
        /// </summary>
        /// <param name="candidatePriority">The candidate slot group's StoragePriority cast to int.</param>
        /// <param name="currentPriority">The stack's current StoragePriority cast to int.</param>
        /// <param name="beforeCarryActive">True for a BEFORE-CARRY relocation (the pawn is about to carry one
        /// stack to a job and would first re-store it closer); false for an OPPORTUNITY relocation. WYU keys
        /// the equal-priority allowance on whether a before-carry target is valid
        /// (<c>StoreUtility.cs:220 vs :232</c>).</param>
        /// <param name="allowEqualPriority">WYU's <c>HaulBeforeCarry_ToEqualPriority</c> (HD
        /// <c>routeToEqualPriority</c>) — only consulted on the before-carry path.</param>
        /// <param name="isOwnGroup">True when the candidate group is the stack's CURRENT slot group — never
        /// relocate within the same group (<c>StoreUtility.cs:233</c>).</param>
        /// <returns>True iff the candidate priority is an eligible relocation destination.</returns>
        public static bool PriorityEligibleForRoute(
            int candidatePriority,
            int currentPriority,
            bool beforeCarryActive,
            bool allowEqualPriority,
            bool isOwnGroup)
        {
            // StoreUtility.cs:233 — never relocate within the SAME group (the C3 shouldFix). Wins over any
            // priority test: even an equal/higher-priority candidate that IS the own group is rejected.
            if (isOwnGroup)
                return false;

            // StoreUtility.cs:218 — a strictly-LOWER priority candidate is never a relocation destination.
            if (candidatePriority < currentPriority)
                return false;

            // Strictly-HIGHER priority is always eligible (it trips none of WYU's breaks).
            if (candidatePriority > currentPriority)
                return true;

            // EQUAL priority. WYU:
            //   - OPPORTUNITY (no before-carry target): always broken out (StoreUtility.cs:220) -> never.
            //   - BEFORE-CARRY: allowed only when the equal-priority toggle is on (StoreUtility.cs:232).
            return beforeCarryActive && allowEqualPriority;
        }

        /// <summary>
        /// Is relocating the stack to <paramref name="storeToTargetDistSquared"/> actually WORTH it versus
        /// leaving it where it is? Faithful port of the before-carry detour's worthwhileness gate
        /// (<c>BeforeCarryDetour.cs:100-104</c>): relocate only when the candidate store is STRICTLY closer
        /// to the carry/consume target than the stack's current position is.
        ///
        /// <code>
        /// var fromHereSquared  = thing.Position.DistanceToSquared(carryTarget.Cell); // current → target
        /// var fromStoreSquared = storeCell.DistanceToSquared(carryTarget.Cell);      // candidate → target
        /// if (fromStoreSquared &lt; fromHereSquared) { … relocate … }                // strictly closer
        /// </code>
        ///
        /// Strict <c>&lt;</c> (no equal): a relocation that doesn't bring the supply at least one squared
        /// tile closer to the consumer buys nothing, so it's declined (and avoids relocation churn between
        /// equidistant stores). The Verse layer passes the SQUARED int distances (no <c>Sqrt</c>), matching
        /// WYU's <c>DistanceToSquared</c> comparison exactly.
        /// </summary>
        /// <param name="currentToTargetDistSquared">Squared distance from the stack's current position to
        /// the consume/carry target (WYU <c>fromHereSquared</c>).</param>
        /// <param name="storeToTargetDistSquared">Squared distance from the candidate store cell to the
        /// consume/carry target (WYU <c>fromStoreSquared</c>).</param>
        /// <returns>True iff the candidate store is strictly closer to the target than the current position.</returns>
        public static bool WorthRelocating(int currentToTargetDistSquared, int storeToTargetDistSquared)
            => storeToTargetDistSquared < currentToTargetDistSquared;

        /// <summary>
        /// Rank two candidate store cells by closeness to the thing↔job MIDWAY (lower squared midway
        /// distance is better) — the ranking WYU uses to pick the store cell "closest to halfway to target"
        /// (<c>StoreUtility.cs:254-266</c>). This DELEGATES to the existing
        /// <see cref="EnRoutePickupPolicy.MidwayBetter"/> so en-route pickup and storage routing share ONE
        /// midway ranking (the Verse layer computes each cell's midway-squared distance via
        /// <see cref="EnRoutePickupPolicy.Midway"/> + <see cref="EnRoutePickupPolicy.MidwayDistanceSquared"/>,
        /// the same helpers en-route uses — no divergent duplicate). Returns true when
        /// <paramref name="candidateMidwayDistSquared"/> is STRICTLY closer than the running best, so the
        /// FIRST cell among equals wins (stable), exactly as WYU's
        /// <c>if (distSquared &gt; closestDistSquared) continue;</c> replace-on-strictly-smaller does.
        /// </summary>
        /// <param name="candidateMidwayDistSquared">The candidate cell's squared distance to the midway
        /// (from <see cref="EnRoutePickupPolicy.MidwayDistanceSquared"/>).</param>
        /// <param name="bestMidwayDistSquared">The running best's squared midway distance.</param>
        public static bool MidwayBetter(int candidateMidwayDistSquared, int bestMidwayDistSquared)
            => EnRoutePickupPolicy.MidwayBetter(candidateMidwayDistSquared, bestMidwayDistSquared);

        /// <summary>
        /// Rank two candidate store cells by raw squared distance to the consuming job / carry target —
        /// the <see cref="CompareByDestinationDistance"/> contract named in the plan. Returns true when
        /// <paramref name="distSqA"/> is STRICTLY closer than <paramref name="distSqB"/> (strict, so the
        /// running best is replaced only on a strictly-smaller distance — first-among-equals stays, the same
        /// stable replace rule WYU uses). Provided for callers ranking by direct destination distance rather
        /// than the midway (e.g. the before-carry "closest to the carry target" choice); for the midway
        /// ranking use <see cref="MidwayBetter"/>.
        /// </summary>
        public static bool CompareByDestinationDistance(int distSqA, int distSqB)
            => distSqA < distSqB;
    }
}
