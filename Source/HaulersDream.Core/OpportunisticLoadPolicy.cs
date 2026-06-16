namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision for the "opportunistic unload-first" load path (BLFT parity, gap #2 / plan B1; default OFF).
    /// A pawn that is ALREADY carrying HD-tagged inventory cargo, when a needy load target (transporter group /
    /// portal / vehicle) within the scan radius still wants some of that exact cargo, should DIVERT to deposit
    /// what it carries into that target — instead of walking past it and later starting a fresh, separate
    /// pickup-and-load trip. This is a DEPOSIT-ONLY decision: it never sweeps new ground stacks, it only sheds
    /// what the pawn happens to be carrying into a target that wants it on the way.
    ///
    /// No Verse types: the runtime gathers the live primitives (the carried surplus per def, each candidate
    /// target's still-needed / available-to-claim amount for those defs, the straight-line distances, the
    /// pawn's busy-state) and performs the divert. The runtime evaluates ONE candidate target/def pairing at a
    /// time via <see cref="DepositCount"/> (the per-pairing arithmetic) under the global <see cref="ShouldConsider"/>
    /// gate; <see cref="ShouldDivertTo"/> folds the two for the common single-pairing case + the radius test.
    ///
    /// CLAIM-LEDGER NOTE (why the policy reads "available to claim", not raw manifest-remaining): the runtime
    /// passes <c>targetAvailableForDef</c> = the target ledger's <c>AvailableToClaim</c> for the def (manifest
    /// remaining minus OTHER pawns' in-flight claims). Diverting toward a def that other bulk couriers have
    /// already fully claimed would be a wasted trip — the manifest is "needed" but no headroom is left to
    /// usefully deliver. So the deposit count is bounded by the available-to-claim, exactly like the normal
    /// planner's claimable slice. (The deposit itself records NO new claim — it is an over-deposit the ledger's
    /// Settle credits-then-decrements — so this is a divert-WORTHWHILE gate, not a reservation.)
    /// </summary>
    public static class OpportunisticLoadPolicy
    {
        /// <summary>
        /// The top-level gate: is this pawn a candidate to divert AT ALL right now (independent of any specific
        /// target)? Decision order (first failing condition wins, returns false):
        /// <list type="number">
        /// <item><paramref name="featureEnabled"/> false → no (the feature is OFF — byte-inert).</item>
        /// <item><paramref name="carriedSurplusUnits"/> &lt;= 0 → no (nothing tagged-and-surplus to shed).</item>
        /// <item><paramref name="alreadyOnHigherPriorityHdJob"/> → no (the pawn is already running an HD bulk
        /// load / unload / sweep / construct job that owns its carried cargo — never yank it off that to start a
        /// competing deposit; a player-forced order is also "higher priority" and must not be pre-empted).</item>
        /// <item>otherwise → yes (the pawn MAY divert if a needy target is found).</item>
        /// </list>
        /// </summary>
        /// <param name="featureEnabled">The <c>enableOpportunisticLoad</c> setting.</param>
        /// <param name="carriedSurplusUnits">Total units of HD-tagged SURPLUS the pawn currently carries (above
        /// its personal keep-stock). 0 ⇒ nothing to opportunistically deposit.</param>
        /// <param name="alreadyOnHigherPriorityHdJob">The pawn is already running an HD load/unload/sweep job (or a
        /// player-forced job) that owns its carried cargo — diverting would conflict with it.</param>
        public static bool ShouldConsider(
            bool featureEnabled,
            int carriedSurplusUnits,
            bool alreadyOnHigherPriorityHdJob)
        {
            if (!featureEnabled)
                return false;
            if (carriedSurplusUnits <= 0)
                return false;
            if (alreadyOnHigherPriorityHdJob)
                return false;
            return true;
        }

        /// <summary>
        /// How many units of one carried def the pawn should deposit into one candidate target — the binding
        /// minimum of (what the pawn carries as surplus of that def) and (what the target can still usefully take,
        /// i.e. its available-to-claim for that def). Returns 0 when either side is non-positive (nothing to shed,
        /// or the target's need for this def is already fully claimed / satisfied → not worth diverting for it).
        ///
        /// This is the per-(target, def) leaf arithmetic; the runtime calls it for each carried def against each
        /// candidate target and sums / picks to decide whether a divert pays off and what the resulting
        /// deposit-only job is worth. Clamping to the available-to-claim (not the raw manifest remaining) keeps a
        /// divert from being a wasted walk toward a def other couriers have already claimed in full.
        /// </summary>
        /// <param name="carriedSurplusOfDef">Units of this def the pawn carries as tagged surplus.</param>
        /// <param name="targetAvailableForDef">Units of this def the target can still usefully receive — its
        /// ledger available-to-claim (manifest remaining minus other pawns' in-flight claims), clamped ≥ 0 by the
        /// caller.</param>
        public static int DepositCount(int carriedSurplusOfDef, int targetAvailableForDef)
        {
            if (carriedSurplusOfDef <= 0 || targetAvailableForDef <= 0)
                return 0;
            return carriedSurplusOfDef < targetAvailableForDef ? carriedSurplusOfDef : targetAvailableForDef;
        }

        /// <summary>
        /// The combined single-pairing decision: should the pawn divert to deposit a carried def into a candidate
        /// target, and if so how many units? Returns the clamped deposit count (&gt; 0 ⇒ divert) or 0 (⇒ do not
        /// divert toward this target/def). Folds in the radius test and the top-level gate so a runtime caller that
        /// already knows the live numbers for one (target, def) pairing can get a single answer.
        ///
        /// Order: gate (<see cref="ShouldConsider"/>) → in-radius (<paramref name="distanceToTarget"/> ≤
        /// <paramref name="scanRadius"/>) → per-pairing count (<see cref="DepositCount"/>). A target exactly AT the
        /// radius counts as in-range (≤, matching <c>IntVec3.InHorDistOf</c>'s inclusive semantics). A
        /// non-positive radius rejects everything (a misconfigured 0 radius diverts nowhere).
        /// </summary>
        /// <param name="featureEnabled">The <c>enableOpportunisticLoad</c> setting.</param>
        /// <param name="carriedSurplusOfDef">Units of this def the pawn carries as tagged surplus.</param>
        /// <param name="targetAvailableForDef">Units of this def the target can still usefully receive
        /// (available-to-claim, clamped ≥ 0).</param>
        /// <param name="distanceToTarget">Straight-line cell distance from the pawn to the candidate target.</param>
        /// <param name="scanRadius">The <c>loadOpportunityScanRadius</c> setting (cells).</param>
        /// <param name="alreadyOnHigherPriorityHdJob">The pawn is already on an HD/forced job owning its cargo.</param>
        public static int ShouldDivertTo(
            bool featureEnabled,
            int carriedSurplusOfDef,
            int targetAvailableForDef,
            float distanceToTarget,
            float scanRadius,
            bool alreadyOnHigherPriorityHdJob)
        {
            if (!ShouldConsider(featureEnabled, carriedSurplusOfDef, alreadyOnHigherPriorityHdJob))
                return 0;
            if (scanRadius <= 0f)
                return 0;
            if (distanceToTarget > scanRadius)
                return 0;
            return DepositCount(carriedSurplusOfDef, targetAvailableForDef);
        }
    }
}
