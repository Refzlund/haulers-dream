using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Mech shed-cargo-before-charge. When a player mechanoid (e.g. an Agrihand that scooped its own harvest)
    /// is about to go CHARGE while still carrying HD-tagged cargo, make it DELIVER that cargo to storage first,
    /// then charge — so perishables don't rot and carry-capacity isn't occupied during the (long, never-self-
    /// completing) <c>JobDefOf.MechCharge</c> job.
    ///
    /// Hook: a postfix on <see cref="JobGiver_GetEnergy_Charger"/>.<c>TryGiveJob</c>. That giver returns a
    /// MechCharge job only when the mech's energy has dropped below its control-group recharge threshold
    /// (<c>ShouldAutoRecharge</c>) — but a mech at its recharge threshold (~30% of maxMechEnergy) still has
    /// HOURS of runtime (active drain is only 10/day), so a minutes-long delivery trip is safe. We only fall
    /// back to a feet-drop when (a) there is no reachable storage destination for the cargo, or (b) the mech is
    /// already critically low (≤ the <c>ShutdownUntil=15</c> recovery floor), where a trip would be unwise.
    ///
    /// Self-shutdown / dormancy are NOT hooked here — those are already swept by the periodic anti-softlock
    /// auto-drop in <see cref="HaulersDreamGameComponent"/> (which also remains the backstop for anything this
    /// proactive hook misses). Both share the <c>enableSoftlockDrop</c> toggle, so the whole "don't let cargo
    /// get stuck on a charging / dormant mech" behavior is governed by one setting and is byte-inert when off.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_GetEnergy_Charger), "TryGiveJob")]
    public static class Patch_MechShedCargoBeforeCharge
    {
        // ShutdownUntil (RimWorld.Need_MechEnergy) — a mech recovers from self-shutdown at CurLevel >= 15; below
        // that it is in the forced-shutdown danger zone, so we drop rather than send it on a delivery trip.
        private const float CriticalEnergyFloor = 15f;

        static void Postfix(ref Job __result, Pawn pawn, JobGiver_GetEnergy_Charger __instance)
        {
            if (__result == null) // the giver didn't actually decide to charge this cycle
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableSoftlockDrop) // shares the softlock toggle; byte-inert when off
                return;
            // A player-FORCED charge (mechanitor "recharge now") must charge immediately — don't detour it.
            if (__instance != null && __instance.forced)
                return;
            if (pawn == null || pawn.Faction != Faction.OfPlayerSilentFail || !pawn.RaceProps.IsMechanoid)
                return;

            var comp = pawn.TryGetComp<CompHauledToInventory>();
            var owner = pawn.inventory?.innerContainer;
            if (comp == null || owner == null)
                return;
            if (!HasLiveTaggedCargo(comp, owner))
                return; // nothing scooped aboard — let it charge unchanged

            // Critically low → a delivery trip is unwise; just drop at its feet so the cargo doesn't rot/occupy
            // during the charge, and let the mech charge now.
            var energy = pawn.needs?.energy;
            bool energyOk = energy != null && energy.CurLevel > CriticalEnergyFloor;

            // Deliver to storage first when there's somewhere to deliver to AND energy is fine: queue a forced
            // unload (the tested PawnUnloadChecker path — it validates reservations before queuing) and suppress
            // the charge THIS cycle. The mech's think tree runs the queued unload first (its ThinkNode_QueuedJob
            // sits above this charge giver), then the charger fires again next cycle with the cargo gone.
            if (energyOk && HasDeliverableSurplus(pawn, comp, owner))
            {
                PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true);
                if (PawnUnloadChecker.HasQueuedUnload(pawn))
                {
                    __result = null; // defer the charge one cycle so the delivery runs first
                    return;
                }
            }

            // Fallback (no reachable destination, couldn't queue, or critically low): drop at feet so the cargo
            // is freed (stops decaying in inventory, frees carry capacity) and a hauler reclaims it; charge now.
            HaulersDreamGameComponent.DropTaggedCargo(pawn);
        }

        private static bool HasLiveTaggedCargo(CompHauledToInventory comp, Verse.ThingOwner<Thing> owner)
        {
            foreach (var t in comp.PeekHashSet())
                if (t != null && !t.Destroyed && owner.Contains(t))
                    return true;
            return false;
        }

        // A tagged stack still in inventory that has surplus above the mech's keep-stock AND a reachable storage
        // destination — i.e. the unload trip would actually deliver something rather than drop it desperately.
        // Uses the SAME surplus/destination math as the unload driver + the cannot-unload alert.
        private static bool HasDeliverableSurplus(Pawn pawn, CompHauledToInventory comp, Verse.ThingOwner<Thing> owner)
        {
            foreach (var t in comp.PeekHashSet())
                if (t != null && !t.Destroyed && owner.Contains(t)
                    && InventorySurplus.SurplusOf(pawn, t) > 0
                    && InventorySurplus.HasUnloadDestination(pawn, t))
                    return true;
            return false;
        }
    }
}
