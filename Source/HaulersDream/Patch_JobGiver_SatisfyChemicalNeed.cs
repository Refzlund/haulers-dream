using System;
using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// (Issue #229) Let a colonist in withdrawal reach a drug HAULER'S DREAM pinned in another colonist's
    /// inventory — the one lockout HD itself creates.
    ///
    /// <para>THE EXPLOIT. Every vanilla drug search is spawned-only or colony-ANIMAL-only:
    /// <c>JobGiver_SatisfyChemicalNeed.FindDrugFor</c> and <c>JobGiver_TakeDrugsForDrugPolicy.FindDrugFor</c> each
    /// check (1) the seeker's own inventory, (2) <c>GenClosest.ClosestThingReachable</c> over
    /// <c>ThingRequestGroup.Drug</c> (spawned things only), (3) <c>mapPawns.SpawnedColonyAnimals</c>; and
    /// <c>JobGiver_BingeDrug.BestIngestTarget</c> / <c>AddictionUtility.CanBingeOnNow</c> are spawned-only. A drug
    /// in a COLONIST's inventory is invisible to all of them. HD then pins it there permanently — its keep-count
    /// surplus rule (<see cref="InventorySurplus"/>) plus the two #81 guards that veto vanilla's drop-unused loop
    /// for a kept drug (<see cref="Patch_JobGiver_DropUnusedInventory_Drop"/> /
    /// <see cref="Patch_JobGiver_DropUnusedInventory_ShouldKeepDrug"/>) — so "Keep in inventory" on one colonist
    /// locks an addict out of that drug forever. Vanilla's own invariant is the opposite: its drop loop sheds any
    /// drug a colonist has no policy or addiction reason to hold, and <c>FloatMenuOptionProvider_PickUpItem</c>
    /// refuses to even offer "Pick up" for such a drug on the home map. Those HD guards stay exactly as they are
    /// (#81 is a real bug they fix); this postfix opens the ONE door HD closed.</para>
    ///
    /// <para>SCOPE — only stacks HD ITSELF pinned. A drug vanilla put in an inventory — a drug-policy
    /// <c>takeToInventory</c> supply, an addicted holder's own stash — stays exactly as invisible as it is in
    /// vanilla. HD fixes what HD caused and rebalances nothing. That claim rests on <see cref="IsPinnedByHd"/>
    /// being TWO clauses, not one: the player's keep-in-inventory pin by def, OR a tagged stack that
    /// <see cref="InventorySurplus.SurplusOf"/> still reports as surplus. The surplus clamp is load-bearing —
    /// membership in the tag set alone would leak a colonist's policy stash, because the tag self-heal adopts any
    /// untagged stack whose DEF matches something HD scooped (see <see cref="IsPinnedByHd"/> for the full
    /// argument). The player's rehab lever is untouched too: the re-implemented validator below keeps vanilla's
    /// drug-policy clause, so a drug set "not allowed for addiction" is as unreachable through this leg as through
    /// vanilla's.</para>
    ///
    /// <para>MECHANISM. A <see cref="Priority.Low"/> postfix that acts only when vanilla found nothing, mirroring
    /// vanilla's own job construction verbatim: <c>JobDefOf.TakeFromOtherInventory</c> with the stack as target A,
    /// the holder as target B, and <c>count = 1</c>. Vanilla's driver then walks the seeker to the holder and moves
    /// the dose into the SEEKER's inventory; the next think finds it in the seeker's own-inventory pass and ingests
    /// it. The returned Thing MUST stay in the holder's inventory (never dropped/respawned) or
    /// <c>JobDriver_TakeFromOtherInventory.ItemHoldingInventory</c>'s
    /// <c>TargetThingA.ParentHolder as Pawn_InventoryTracker</c> lookup breaks and the chain never fires — the same
    /// constraint <see cref="Patch_TryFindBestFoodSourceFor"/> documents for meals-on-wheels.</para>
    ///
    /// <para>ONE DOSE PER TRIP. Both vanilla <c>TakeFromOtherInventory</c> sites set <c>job.count = 1</c>, so a take
    /// never drains a holder: the addict comes back for the next dose and every trip re-tests eligibility.</para>
    ///
    /// <para>TAKER/DONOR ASYMMETRY (deliberate). The DONOR must be safe to interrupt —
    /// <see cref="InventoryShare.IsEligibleCarrier"/> excludes self / unspawned / dead / downed / DRAFTED /
    /// MENTAL, and this scan adds caravan / vehicle / forbidden / unreachable on top. The TAKER is gated only on
    /// <c>IsColonist</c>: it is deliberately NOT required to be undrafted or sane, because the taker is the one in
    /// trouble. A drafted or berserk addict may still walk over and take its dose.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_SatisfyChemicalNeed), "TryGiveJob")]
    public static class Patch_JobGiver_SatisfyChemicalNeed
    {
        // Priority.Low so HD runs AFTER any other drug-finder postfix; whichever produced a job wins and this
        // early-outs on it.
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.drugsForWithdrawal)
                return;
            if (__result != null)
                return;                          // vanilla found a drug — NEVER override it
            if (pawn == null || !pawn.IsColonist)
                return;                          // mirrors vanilla's own extra colonist leg
            if (pawn.Map == null || pawn.needs == null || pawn.inventory == null)
                return;                          // caravan / world map is out of scope (mapPawns would NRE)

            // #122 SEAM BOUNDARY (degrade to pure vanilla). This scan runs inside a think node, whose enclosing
            // ThinkNode_Priority catches a throwing child, logs one collapsed entry and SKIPS it — so a repeatable
            // throw here would cost the pawn its whole drug-satisfaction node on every think. The scan reads OTHER
            // pawns' inventories through comps and compat shims (code vanilla never runs on this path), so on a
            // throw: report once with attribution, restore vanilla's result, and behave as if HD found nothing.
            var vanillaResult = __result;
            try
            {
                Job job = TryBuildTakeFromHolderJob(pawn, s);
                if (job != null)
                    __result = job;
            }
            catch (Exception ex)
            {
                __result = vanillaResult;
                HDGuard.SeamDegraded(ex, "JobGiver_SatisfyChemicalNeed.TryGiveJob (HD kept-drug withdrawal access)",
                    pawn, "kept vanilla's result (no drug found by HD), so drug selection itself keeps working.");
            }
        }

        // Seam guard: covers throws HD's self-degrading postfix above did NOT cause (the vanilla body, another
        // mod's patch on this method); a throw there breaks this pawn's drug selection for the scan.
        static Exception Finalizer(Exception __exception, Pawn pawn)
            => HDGuard.SeamThrew(__exception, "JobGiver_SatisfyChemicalNeed.TryGiveJob (HD kept-drug withdrawal access)",
                pawn, "this pawn's drug search failed this scan.");

        /// <summary>
        /// A <c>TakeFromOtherInventory</c> job for one dose of a drug an HD-pinned holder carries, or null when no
        /// unsatisfied chemical need has a reachable, reservable, policy-legal, HD-pinned source. Chemical needs
        /// are tried most urgent first, so a pawn addicted to two chemicals rescues the worse one when only one
        /// source exists.
        /// </summary>
        private static Job TryBuildTakeFromHolderJob(Pawn pawn, HaulersDreamSettings s)
        {
            var needs = pawn.needs.AllNeeds;
            if (needs == null || needs.Count == 0)
                return null;

            float triedLevel = float.NegativeInfinity;
            int triedIndex = -1;
            // HARD ITERATION BOUND, not just the null exit. Each pass is meant to consume one qualifying need, but
            // a MODDED Need_Chemical with a NaN CurLevel makes EVERY float comparison false — including the
            // "already tried" guard in NextMostUrgentNeed — so that need would be re-selected forever. That is a
            // HANG inside a think node, which the postfix's seam try/catch cannot rescue (it catches throws, not
            // spins). One pass per need is the true ceiling, so this can never cut a legitimate scan short.
            for (int attempt = 0; attempt < needs.Count; attempt++)
            {
                var need = NextMostUrgentNeed(needs, triedLevel, triedIndex, out int index);
                if (need == null)
                    return null;
                triedLevel = need.CurLevel;
                triedIndex = index;

                Job job = TryBuildForNeed(pawn, need, s);
                if (job != null)
                    return job;
            }
            return null;
        }

        /// <summary>
        /// The next unsatisfied chemical need to try, in ascending <c>CurLevel</c> order (most urgent first),
        /// strictly after the (level, index) pair already tried. A selection scan rather than a sorted list: a pawn
        /// has at most a handful of chemical needs and this think path should not allocate per scan. The index
        /// tiebreak makes the order total — and therefore identical on every multiplayer client — when two needs
        /// sit at exactly the same level.
        /// </summary>
        /// <param name="needs">The pawn's live need list.</param>
        /// <param name="afterLevel">The <c>CurLevel</c> of the last need tried; needs at or below it are skipped
        /// unless their index is higher. <c>float.NegativeInfinity</c> starts the scan.</param>
        /// <param name="afterIndex">The index of the last need tried, breaking an equal-level tie.</param>
        /// <param name="index">Receives the returned need's index, to feed the next call.</param>
        /// <returns>The next need to try, or null when every qualifying need has been tried.</returns>
        private static Need_Chemical NextMostUrgentNeed(List<Need> needs, float afterLevel, int afterIndex, out int index)
        {
            Need_Chemical best = null;
            index = -1;
            for (int i = 0; i < needs.Count; i++)
            {
                // Vanilla's own trigger for this node: Withdrawal (0) or Desire (1), never Satisfied (2).
                if (!(needs[i] is Need_Chemical chemical) || chemical.CurCategory > DrugDesireCategory.Desire)
                    continue;
                if (chemical.AddictionHediff == null)
                    continue;                    // no addiction to match a drug against
                float level = chemical.CurLevel;
                if (level < afterLevel || (level == afterLevel && i <= afterIndex))
                    continue;                    // already tried
                if (best == null || level < best.CurLevel || (level == best.CurLevel && i < index))
                {
                    best = chemical;
                    index = i;
                }
            }
            return best;
        }

        /// <summary>
        /// The take job for ONE chemical need: scan eligible holders for the closest HD-pinned, policy-legal,
        /// reservable stack of a drug that treats this addiction, and build vanilla's own job for it.
        /// </summary>
        private static Job TryBuildForNeed(Pawn pawn, Need_Chemical need, HaulersDreamSettings s)
        {
            var addiction = need.AddictionHediff;
            var map = pawn.Map;

            // The full clause set — and its ORDER — lives in the pure policy, which is where it is tested. The
            // three clauses that do not depend on a candidate are hoisted into locals here so the postfix can
            // short-circuit them before any colony walk, and are then handed to the policy as the values they
            // actually hold rather than re-derived at the decision point.
            bool featureEnabled = s.drugsForWithdrawal;
            bool vanillaFoundDrug = false;   // the postfix returns early when vanilla produced a job
            bool seekerHasChemicalNeed = addiction != null;

            Thing best = null;
            int bestDist = int.MaxValue;

            var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                var holder = pawns[i];
                bool holderEligible = IsEligibleHolder(holder, pawn);
                if (!holderEligible)
                    continue;                // cost short-circuit; the policy states the same clause below
                var comp = holder.GetComp<CompHauledToInventory>();
                var inv = holder.inventory?.innerContainer;
                if (comp == null || inv == null || inv.Count == 0)
                    continue;

                // Best stack WITHIN this holder first (cheap): all its stacks share one distance, so the
                // thingIDNumber tiebreak decides. MP determinism — every client must pick the same physical stack.
                Thing candidate = null;
                for (int j = 0; j < inv.Count; j++)
                {
                    var stack = inv[j];
                    // CHEAPEST FILTER FIRST. IsDrugForAddiction opens on def.IsDrug, which rejects apparel, meals,
                    // weapons and everything else a colonist carries in one field read — so the HD-pin test below,
                    // whose GetHashSet() self-heal MUTATES the holder's tag set, only ever runs for a stack that is
                    // already a drug treating this addiction. Otherwise an unsatisfied need would heal every
                    // colonist's comp every tick for as long as the withdrawal lasted. Evaluate's own internal
                    // clause order (what the tests pin) is untouched: this is caller-side short-circuiting, the
                    // same thing the postfix does for the three hoisted clauses above.
                    if (!IsDrugForAddiction(pawn, addiction, stack))
                        continue;
                    var verdict = DrugSharePolicy.Evaluate(
                        featureEnabled: featureEnabled,
                        vanillaFoundDrug: vanillaFoundDrug,
                        seekerHasChemicalNeed: seekerHasChemicalNeed,
                        holderEligible: holderEligible,
                        stackPinnedByHaulersDream: IsPinnedByHd(holder, comp, stack),
                        heldUnits: stack.stackCount);
                    if (verdict != DrugShareVerdict.Allow)
                        continue;
                    if (!pawn.CanReserve(stack))
                        continue;
                    if (candidate == null || stack.thingIDNumber < candidate.thingIDNumber)
                        candidate = stack;
                }
                if (candidate == null)
                    continue;

                // Reach last (a pathfind) — only for a holder that actually carries a usable, reservable dose.
                // PathEndMode.Touch, NOT the OnCell vanilla's colony-animal leg uses: JobDriver_TakeFromOtherInventory's
                // own goto is Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch), so Touch is the mode that
                // actually decides whether this job can complete.
                if (!pawn.CanReach(holder, PathEndMode.Touch, Danger.Some))
                    continue;

                int dist = IntVec3Utility.ManhattanDistanceFlat(pawn.Position, holder.Position);
                if (best == null || dist < bestDist
                    || (dist == bestDist && candidate.thingIDNumber < best.thingIDNumber))
                {
                    best = candidate;
                    bestDist = dist;
                }
            }

            if (best == null)
                return null;

            // Vanilla's own construction, verbatim (JobGiver_SatisfyChemicalNeed.TryGiveJob): the stack stays IN
            // the holder's inventory so the driver's ParentHolder lookup resolves.
            Pawn holderPawn = (best.ParentHolder as Pawn_InventoryTracker)?.pawn;
            if (holderPawn == null || holderPawn == pawn)
                return null;
            Job job = JobMaker.MakeJob(JobDefOf.TakeFromOtherInventory, best, holderPawn);
            job.count = DrugSharePolicy.UnitsToTake(best.stackCount);

            // Meet-in-the-middle: nudge an IDLE holder toward the addict so they converge (free; self-skips for a
            // busy holder). Vanilla's driver already walks the taker to the holder, so this is pure polish.
            if (s.shareMeetInMiddle)
                SharedInventoryApproach.MaybeApproach(best, pawn);

            HDLog.Dbg($"KeptDrugShare: {pawn} -> {best.def?.defName ?? "?"} x{job.count} carried by {holderPawn} " +
                      $"(addiction {addiction.def?.defName ?? "?"}).");
            return job;
        }

        /// <summary>
        /// A colonist whose HD-pinned stock a withdrawing seeker may draw from. Layers this leg's own guards on the
        /// shared carrier liveness (<see cref="InventoryShare.IsEligibleCarrier"/>: not self / unspawned / dead /
        /// downed / drafted / mental / mid-HD-batch).
        /// </summary>
        /// <param name="holder">The candidate donor.</param>
        /// <param name="seeker">The pawn in withdrawal.</param>
        /// <returns>True when the holder's inventory may be scanned for a dose.</returns>
        private static bool IsEligibleHolder(Pawn holder, Pawn seeker)
        {
            // Animals are vanilla's OWN leg (SpawnedColonyAnimals) — it already reaches their inventories, so this
            // patch has no gap to fill there and must not double up on it.
            if (holder?.RaceProps == null || holder.RaceProps.Animal)
                return false;
            if (!InventoryShare.IsEligibleCarrier(holder, seeker))
                return false;
            // A VF vehicle's cargo is a player-curated loadout VF manages; a holder riding INSIDE one is
            // unreachable. Both return false with VF absent, so this is inert without it.
            if (VehicleFrameworkCompat.IsVehicle(holder) || VehicleFrameworkCompat.InVehicle(holder))
                return false;
            // A pawn packing for a caravan is mid-commitment; don't pull its cargo back out.
            if (holder.IsFormingCaravan())
                return false;
            if (holder.IsForbidden(seeker))
                return false;
            return true;
        }

        /// <summary>
        /// Whether HAULER'S DREAM is why <paramref name="stack"/> sits in that inventory: a keep-in-inventory pin on
        /// its def, or HD haul cargo the holder is carrying to storage. This is the scoping rule that makes the whole
        /// feature safe — a drug vanilla put there stays as invisible as it is in vanilla.
        ///
        /// <para>THE SURPLUS CLAMP IS WHAT MAKES THE TAGGED BRANCH HONEST. "In the tag set" is NOT the same as "HD
        /// is why this is here": the tag self-heal (<c>CompHauledToInventory.GetHashSet</c> →
        /// <c>Core.TagHealPolicy.SelectStacksToTag</c>) adopts EVERY untagged inventory stack whose DEF is in the
        /// scooped union, excluding only Simple Sidearms / Grab Your Tool weapons. So once a colonist hauls one
        /// go-juice stack, the 2 go-juice their DRUG POLICY keeps in the same pack get tagged too, permanently — and
        /// an unclamped test would hand an addict that emergency dose. Every other consumer of the tag set is
        /// immune because it goes through <see cref="InventorySurplus.SurplusOf"/>, whose <c>KeepCountOf</c>
        /// subtracts the drug policy's <c>takeToInventory</c>; this was the first consumer to bypass it. Clamping
        /// the tagged branch on <c>SurplusOf &gt; 0</c> means a take only ever comes off units the holder carries
        /// ABOVE what its own policy wants, so the policy stash can never be drained. Evaluation order is
        /// load-bearing: <c>GetHashSet()</c> heals first, so the <c>PeekHashSet</c> view <c>SurplusOf</c> reads is
        /// already current (and <c>SurplusOf</c> itself never mutates).</para>
        ///
        /// <para>The KEPT branch stays UNCLAMPED on purpose: for a def with a player keep-count,
        /// <see cref="InventorySurplus.SurplusOf"/> returns 0 by design once the holder is at or below the pin
        /// (<c>InventorySurplus</c>'s keep-count branch), so clamping it would refuse exactly the case #229 exists
        /// for — the player's "Keep in inventory" pin, which is the lockout HD itself created.</para>
        ///
        /// <para>Reads the HEALED view (<see cref="CompHauledToInventory.GetHashSet"/>), not the peek view, because
        /// the two pins read the healed view too — a kept stack that merged or split since must be recognised here
        /// as well, or the lockout survives its own fix. The heal mutates synced world state, which is safe here:
        /// this is a think path, exactly like the #81 drop guards.</para>
        /// </summary>
        /// <param name="holder">The carrying colonist, whose keep-stock the surplus is measured against.</param>
        /// <param name="comp">That colonist's HD carry comp.</param>
        /// <param name="stack">The candidate inventory stack.</param>
        /// <returns>True only when HD is genuinely why the stack is held and a dose may come off it.</returns>
        private static bool IsPinnedByHd(Pawn holder, CompHauledToInventory comp, Thing stack)
            => comp != null && stack?.def != null
               && (comp.IsKeptDef(stack.def)
                   || (comp.GetHashSet().Contains(stack) && InventorySurplus.SurplusOf(holder, stack) > 0));

        /// <summary>
        /// Vanilla's private <c>DrugValidator</c> closure from <c>JobGiver_SatisfyChemicalNeed.TryGiveJob</c>,
        /// re-implemented for an UNSPAWNED inventory stack — its <c>drug.Spawned &amp;&amp;</c> clause is dropped
        /// (dead for one) and every other clause is kept verbatim. Every member it reads is public, so this is an
        /// exact restatement rather than a guess.
        ///
        /// <para>The drug-policy clause is KEPT on purpose: it is the player's own rehab lever. A drug the policy
        /// marks not "allowed for addiction" is as unreachable through this leg as through vanilla's own search
        /// (unless the pawn has the drug-desire trait or is in a policy-ignoring mental state, both of which
        /// vanilla itself excepts).</para>
        ///
        /// <para>TWO DEVIATIONS (issue #232), pointing in OPPOSITE directions — read them together. Vanilla asks
        /// <c>drugPolicy[drug.def]</c>, whose no-match path is a bare <c>throw new ArgumentException();</c>, and
        /// here that would fire on a def taken from an arbitrary colonist's inventory. So (1) the entry is read
        /// through <see cref="DrugPolicyLookup.EntryFor"/> and the policy question is answered by
        /// <see cref="Core.DrugAllowancePolicy.BlocksAddictionUse"/>, which never throws and treats a missing
        /// entry as "allowed" — the value <c>DrugPolicy.InitializeIfNeeded</c> would have created; and (2) the
        /// verdict is then narrowed by <see cref="Core.DrugAllowancePolicy.MayRouteToDrug"/>, which refuses a
        /// no-entry def OUTRIGHT, because permitting is not the same as being able to finish (see the comment on
        /// that call below). For every def that HAS an entry — the case that matters — this is byte-identical to
        /// vanilla's clause.</para>
        /// </summary>
        /// <param name="pawn">The seeker, whose drug policy and traits gate the choice.</param>
        /// <param name="addiction">The addiction hediff the drug must treat.</param>
        /// <param name="drug">The candidate inventory stack.</param>
        /// <returns>True when this stack is a legal drug for that addiction.</returns>
        private static bool IsDrugForAddiction(Pawn pawn, Hediff_Addiction addiction, Thing drug)
        {
            if (drug?.def == null || !drug.def.IsDrug)
                return false;
            CompDrug compDrug = drug.TryGetComp<CompDrug>();
            if (compDrug?.Props?.chemical == null)
                return false;
            if (compDrug.Props.chemical.addictionHediff != addiction.def)
                return false;
            DrugPolicy drugPolicy = pawn.drugs?.CurrentPolicy;
            DrugPolicyEntry entry = DrugPolicyLookup.EntryFor(drugPolicy, drug.def);
            // COST NOTE: arguments evaluate EAGERLY, where vanilla's `&&` chain short-circuited — so the trait
            // scan and the mental-state read now happen for every candidate stack instead of only for one the
            // policy disallows. Both are cheap field/list reads behind `?.`, and this loop already pays a
            // CanReserve and (per holder) a CanReach, so the added cost is noise against what surrounds it.
            bool policyBlocks = Core.DrugAllowancePolicy.BlocksAddictionUse(
                hasPolicy: drugPolicy != null,
                entryPresent: entry != null,
                entryAllowedForAddiction: entry != null && entry.allowedForAddiction,
                hasStory: pawn.story != null,
                // A don't-care when the pawn has no story: the hasStory clause refuses first, exactly as
                // vanilla's `pawn.story != null &&` did.
                drugDesireDegree: pawn.story?.traits?.DegreeOfTrait(TraitDefOf.DrugDesire) ?? 0,
                mentalStateIgnoresPolicy: pawn.InMentalState && (pawn.MentalStateDef?.ignoreDrugPolicy ?? false));

            // THE NO-ENTRY GATE (#232). "Does the policy refuse this drug?" and "can the rest of the game finish
            // this job?" are DIFFERENT questions, and answering only the first re-opens the bug in a worse shape.
            // policyBlocks correctly says "not refused" for a def with no entry — a def with no entry has no row
            // in the drug-policy dialog, so the player cannot have switched it off. But this leg does not merely
            // PERMIT a drug: it MOVES a dose into the seeker's own inventory, and vanilla re-validates it there on
            // the very next think — JobGiver_SatisfyChemicalNeed.FindDrugFor runs its private DrugValidator over
            // pawn.inventory.innerContainer, and that validator performs the unguarded `drugPolicy[drug.def]` this
            // method stopped performing. So routing to a no-entry def would walk the addict to the holder, hand it
            // a dose it can NEVER ingest (vanilla throws every think and the priority sorter skips the whole
            // drug-satisfaction node) and never shed (ShouldKeepDrugInInventory keeps it — the pawn IS addicted).
            // Standing down leaves the pawn exactly as it would be without HD, which is what an optional
            // enhancement at a think-node seam owes its caller.
            //
            // SCOPE, stated honestly: the strictly-necessary case is a NON-null policy with no entry, since
            // vanilla's validator short-circuits on `drugPolicy != null &&` and never reaches the indexer for a
            // pawn with no policy at all. The gate is deliberately the broader "no entry, full stop" — one
            // indivisible rule (HD only routes a pawn to a drug vanilla can evaluate FROM AN ENTRY), and a
            // colonist with no drug policy loses only this optional leg, never vanilla's own drug search.
            //
            // entryPresent stays expressed as `entry != null` rather than folded into the gate's constant, so the
            // call remains correct if that scope is ever narrowed to the strictly-necessary case.
            return Core.DrugAllowancePolicy.MayRouteToDrug(entryPresent: entry != null, policyBlocks: policyBlocks);
        }
    }
}
