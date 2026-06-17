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
    }
}
