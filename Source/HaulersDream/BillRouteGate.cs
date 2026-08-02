using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Single source of truth for "may HD reroute this bill-giver's ingredient gather into a pawn's INVENTORY?"
    /// Shared by the two inventory-gather conversions — <see cref="Patch_WorkGiver_DoBill_InventoryRoute"/>
    /// (→ <c>HaulersDream_BillPrepGather</c>) and <see cref="Patch_WorkGiver_DoBill_BatchRoute"/>
    /// (→ <c>HaulersDream_BatchCraft</c>) — so the two type guards can never drift apart.
    ///
    /// Routable = a <see cref="Building_WorkTable"/> (never a Pawn bill giver / surgery / other special giver)
    /// that is NOT a <see cref="Building_WorkTableAutonomous"/> (the mech gestator family + any modded autonomous
    /// bench).
    ///
    /// WHY autonomous worktables are excluded: an autonomous worktable DEPOSITS its ingredients into the
    /// building's OWN container — vanilla <c>JobDriver_DoBill.CollectIngredientsToils</c> runs with
    /// <c>placeInBillGiver = (BillGiver is Building_WorkTableAutonomous)</c>, ending each ingredient with
    /// <c>Toils_Haul.DepositHauledThingInContainer</c>, which transfers ONLY <c>carryTracker.CarriedThing</c>
    /// (it never reads the pawn's inventory). HD's inventory-gather relay instead loads each ingredient INTO
    /// inventory and ends at the bench with no deposit toil, relying on a fragile next-scan re-handoff to pull
    /// the tagged stock back out of inventory. On an autonomous bench that re-handoff can leave the ingredient
    /// stranded in inventory — the pawn walks to the gestator, never deposits, and HD's auto-unload carries it
    /// back to a stockpile (reported bug; aggravated by mods that act at toil transitions, e.g. Grab Your Tool!).
    /// Letting autonomous worktables keep vanilla's native carry-in-hands-then-deposit-into-container flow is the
    /// same "container destinations keep their dedicated vanilla flow" convention HD already applies to
    /// <c>HaulToContainer</c> (subcore scanner / construction frames / refuel).
    ///
    /// <para>PER-BENCH VETO (issue #230): on top of the type test, a bench may carry
    /// <see cref="CompBenchGather"/> switched OFF, which hands THAT bench back to vanilla's one-stack-per-trip
    /// ingredient flow. It covers all THREE of HD's <c>WorkGiver_DoBill.JobOnThing</c> postfixes by construction,
    /// because they are this predicate's only call sites: the one-sweep gather
    /// (<see cref="Patch_WorkGiver_DoBill_InventoryRoute"/>), the batch route
    /// (<see cref="Patch_WorkGiver_DoBill_BatchRoute"/>) and the closer-storage relocation
    /// (<see cref="Patch_WorkGiver_DoBill_Routing"/>). All three consult it BEFORE they replace <c>__result</c>,
    /// while it is still vanilla's own DoBill, so a vetoed bench simply returns early and the player gets byte-exact
    /// vanilla behaviour. The relocation postfix belongs here as much as the two gathers do: it likewise ends the
    /// pawn's job away from the bench and defers the bill to the next work scan.</para>
    ///
    /// <para>SCOPE — this decides the CONVERSION and nothing else; no running job re-reads it. Flipping a bench off
    /// mid-gather therefore lets that in-flight gather finish, which is correct and strands nothing: the swept stock
    /// is tagged, so whatever the craft does not consume is reclaimed by the unload backstop. From the next work
    /// scan on, that bench routes as vanilla.</para>
    ///
    /// <para>PRECEDENCE — the two directions are deliberately ASYMMETRIC. Per-bench OFF wins unconditionally: it is
    /// a veto inside <see cref="MayRouteToInventory"/>, so it kills both routes at that bench no matter what the
    /// global settings say. Per-bench ON is NOT an override — it only means "not vetoed here", and a global OFF
    /// still applies wherever the global is read. A bench with no comp at all reads as allowed.</para>
    ///
    /// <para>The asymmetry is forced by the globals themselves:
    /// <see cref="Patch_WorkGiver_DoBill_InventoryRoute"/> opens with a three-way AND
    /// (<c>inventoryCraftDeliver &amp;&amp; shareForCrafting &amp;&amp; markForUnload</c>), while the BATCH route
    /// never reads <c>inventoryCraftDeliver</c> at all. That is exactly why the gizmo and its settings checkbox must
    /// stay visible even when <c>inventoryCraftDeliver</c> is off — otherwise the only per-bench control over BATCH
    /// gathering would vanish in the one configuration where batching is the sole thing still gathering.</para>
    /// </summary>
    public static class BillRouteGate
    {
        /// <summary>
        /// The pure TYPE test: is this bill giver a bench whose ingredient gather HD may reroute at all? True for a
        /// <see cref="Building_WorkTable"/> that is not a <see cref="Building_WorkTableAutonomous"/> (see the class
        /// remarks for why autonomous benches are excluded).
        ///
        /// <para>Split out from <see cref="MayRouteToInventory"/> so the per-bench gizmo can ask "does the gather
        /// even apply to this building?" WITHOUT reading the switch it is drawing — reading the full predicate there
        /// would hide the button the moment a player switched a bench off.</para>
        /// </summary>
        /// <param name="billGiver">The bill giver from the DoBill job's target A; null reads as not routable.</param>
        /// <returns>True when the gather COULD apply to this building type, ignoring every player choice.</returns>
        public static bool IsRoutableBenchType(Thing billGiver) =>
            billGiver is Building_WorkTable && !(billGiver is Building_WorkTableAutonomous);

        /// <summary>
        /// May HD reroute this bill giver's ingredient gather into a pawn's inventory? The routable building type
        /// AND the bench's own per-bench switch (issue #230). Both gather conversions call this and nothing else,
        /// so the veto can never reach one route and miss the other.
        /// </summary>
        /// <param name="billGiver">The bill giver from the DoBill job's target A.</param>
        /// <returns>False when the type is not routable, or when this bench's switch is turned off.</returns>
        public static bool MayRouteToInventory(Thing billGiver) =>
            IsRoutableBenchType(billGiver) && CompBenchGather.Allows(billGiver);

        /// <summary>
        /// Is BATCH crafting suppressed at this bill's bench by the per-bench switch (issue #230)? The batch route
        /// runs through <see cref="MayRouteToInventory"/>, so a vetoed bench already crafts one at a time — this is
        /// the UI's read of the same fact, so the batch dropdown options, the button prefix and the ×N row marker
        /// can hide themselves rather than advertise a mode that will not run. Mirrors
        /// <c>CommonSenseCompat.BatchSuppressedByCommonSense</c>, which those three sites already consult.
        /// </summary>
        /// <param name="bill">The bill whose row/dropdown is being drawn; null or unattached reads as not suppressed.</param>
        /// <returns>True only when the bill sits on a bench whose switch is off.</returns>
        public static bool BatchSuppressedByBench(Bill bill) =>
            bill?.billStack?.billGiver is Thing bench && !CompBenchGather.Allows(bench);

        /// <summary>
        /// May HD apply its "share carried ingredients for crafting" machinery — the ingredient-share INJECTION
        /// (<see cref="Patch_WorkGiver_DoBill_TryFindBestBillIngredientsInSet"/> → <see cref="InventoryShare.AddSharableStacksForBill"/>),
        /// the meet-in-the-middle carrier nudge (<see cref="Patch_WorkGiver_DoBill_JobOnThing"/>), and the
        /// gather-into-inventory conversions (BillPrepGather / BatchCraft) — to a bill worked by
        /// <paramref name="worker"/>? FALSE for a MECHANOID worker.
        ///
        /// WHY mechs are excluded: HD's share-for-crafting is a COLONIST scoop feature — it lets a pawn craft from
        /// stock it (or another colonist) carries in inventory. A mechanoid does not participate in HD's
        /// scoop/haul economy the way colonists do, IGNORES forbidden / allowed-area when sourcing ingredients
        /// (<c>ForbidUtility.CaresAboutForbidden</c> is false for a colony mech), and is bounded by its work
        /// range. Injecting a candidate (possibly in another pawn's inventory, possibly across the map) into a
        /// mech's <c>DoBill</c> ingredient search — or rerouting a mech's gather through inventory — can yield a
        /// <c>DoBill</c> the mech then cannot complete and re-issues every tick (the reported "started 10 jobs in
        /// one tick" stonecutter loop). The injection was previously gated ONLY on <c>shareForCrafting</c>, so it
        /// ran for a mech even when mech hauling (<c>allowMechanoids</c>) was OFF — inconsistent with the
        /// conversion gates, which already respect mech eligibility. This single predicate closes that gap so the
        /// whole feature is consistently mech-excluded. Byte-identical for non-mechs: HD simply leaves a mech's
        /// bill on vanilla's native flow (which is the only thing that touches it today anyway).
        /// </summary>
        public static bool WorkerMayShareCraft(Pawn worker) =>
            worker?.RaceProps != null && !worker.RaceProps.IsMechanoid;

        /// <summary>
        /// #63 (Bulk Stonecutting compat): does any ingredient vanilla chose for this DoBill <paramref name="job"/>
        /// have <c>stackLimit == 1</c> (stone chunks)? Such ingredients are carried ONE-PER-TRIP and vanilla
        /// places each onto the bench's single ingredient/interaction cell. HD's gather-into-inventory relay then
        /// loops forever on them: the chunk gathered into inventory is pulled back out by vanilla's DoBill and
        /// dropped on that lone cell, which makes the next bill scan prefer a <c>HaulStuffOffBillGiverJob</c> over
        /// crafting; HD re-converts to a <c>BillPrepGather</c> and the pawn runs bench&lt;-&gt;storage endlessly
        /// (reported with "Bulk Stonecutting (Forked)", whose 10x recipes always plan ~10 single-chunk stacks).
        /// So HD must NOT route such a bill — leave it to vanilla's native one-stack-per-trip gather, which crafts
        /// correctly. Read from vanilla's OWN chosen ingredient queue, so it covers any recipe whose actual chosen
        /// ingredients are unstackable, not just chunks. Returns false (route normally) when the queue is empty —
        /// an inventory-sourced / unfinished-thing resume has no floor queue and never loops.
        /// </summary>
        public static bool ChosenIngredientsUnstackable(Job job)
        {
            var queue = job?.targetQueueB;
            if (queue == null)
                return false;
            for (int i = 0; i < queue.Count; i++)
            {
                var def = queue[i].Thing?.def;
                if (def != null && def.stackLimit == 1)
                    return true;
            }
            return false;
        }
    }
}
