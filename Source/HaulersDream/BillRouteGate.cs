using RimWorld;
using Verse;

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
    /// </summary>
    public static class BillRouteGate
    {
        public static bool MayRouteToInventory(Thing billGiver) =>
            billGiver is Building_WorkTable && !(billGiver is Building_WorkTableAutonomous);

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
    }
}
