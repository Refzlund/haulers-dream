using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The quest-pawn faction-transition seam (issue #123). Vanilla hands the player temporary pawns from
    /// other factions through ONE chokepoint, <c>Pawn.SetFaction(Faction.OfPlayer)</c> (fired by
    /// QuestPart_PawnsArrive / QuestPart_DropPods / QuestPart_JoinPlayer / QuestPart_GiveNearPawn /
    /// QuestPart_GiveToCaravan), and takes them back through the same one (LeaveQuestPartUtility.MakePawnLeave,
    /// QuestPart_LeavePlayer, QuestPart_RefugeeInteractions.LeavePlayer / AssaultColony, the arrest revert in
    /// JobDriver_TakeToBed, and QuestPart_ExtraFaction.Notify_PawnKilled for dead pawns). Decompile-verified:
    /// vanilla touches NO inventory at either end (SetFaction has no inventory code; MakePawnLeave only drops
    /// the in-hands carried thing and redistributes caravan inventory), so a departing guest keeps every meal,
    /// medicine and hauled stack it pocketed while under player control. This seam snapshots the inventory at
    /// gain and drops the excess at loss, see <see cref="QuestPawnReversion"/> for the rules.
    ///
    /// Timing fact this relies on: the main revert (MakePawnLeave) flips the faction while the pawn is still
    /// SPAWNED, before the exit-map lord forms, so the drop lands right where the guest stands in the colony.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class Patch_Pawn_SetFaction
    {
        // __state carries the pre-change faction from prefix to postfix PER INVOCATION (Harmony binds it by
        // name across the pair), so the postfix sees both sides of the transition even through nested
        // SetFaction calls.
        static void Prefix(Pawn __instance, out Faction __state) => __state = __instance.Faction;

        static void Postfix(Pawn __instance, Faction newFaction, Faction __state)
            => QuestPawnReversion.OnFactionChanged(__instance, __state, newFaction);
    }

    /// <summary>
    /// Permanent recruitment discards the arrival snapshot. A lodger accepting its join offer runs
    /// QuestPart_JoinPlayer -&gt; RecruitUtility.Recruit while the pawn is ALREADY player faction, so no
    /// SetFaction fires and the transition seam above never sees it. Without this postfix the new colonist's
    /// snapshot would linger (until the save-time prune) and any far-future faction departure, for example a
    /// mod gifting the colonist away, would dump a bogus years-old diff at its feet.
    /// </summary>
    [HarmonyPatch(typeof(RecruitUtility), nameof(RecruitUtility.Recruit))]
    public static class Patch_RecruitUtility_Recruit
    {
        static void Postfix(Pawn pawn, Faction faction)
        {
            if (faction != null && faction == Faction.OfPlayerSilentFail)
                QuestPawnReversion.NotifyRecruited(pawn);
        }
    }
}
