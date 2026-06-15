using HaulersDream.Core;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Routes vanilla <see cref="WorkGiver_UnloadCarriers"/> through HD's pack-animal BULK UNLOAD: instead of the
    /// vanilla one-stack-in-hands-per-walk job (<c>JobDefOf.UnloadInventory</c>), a flagged carrier is emptied
    /// into the hauler's backpack in ONE visit (see <see cref="JobDriver_UnloadCarrierInBulk"/>). Two prefixes
    /// (split into sibling patch classes to match HD's one-method-per-class convention), both keyed through the
    /// shared <see cref="BulkUnloadGate"/>:
    ///   • <see cref="Patch_WorkGiver_UnloadCarriers_HasJob"/> — overrides "is there a job?" with the bulk gate.
    ///   • <see cref="Patch_WorkGiver_UnloadCarriers_JobOn"/> — builds the bulk job instead of the single-stack one.
    ///
    /// FAIL-OPEN: with the feature off, or for a target the bulk path does not handle — a <see cref="CompMechCarrier"/>
    /// (mech gestator unloading) or any non-<see cref="Pawn"/> — both prefixes return true and vanilla runs
    /// unchanged. When the bulk gate is not met, the prefixes also defer to vanilla (so a single remaining stack /
    /// a full-handed hauler still unloads the vanilla way) — they re-check the SAME gate, so the answers can't diverge.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_UnloadCarriers), nameof(WorkGiver_UnloadCarriers.HasJobOnThing))]
    public static class Patch_WorkGiver_UnloadCarriers_HasJob
    {
        static bool Prefix(Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (!BulkUnloadGate.ShouldHandle(pawn, t))
                return true; // feature off / mech / non-pawn -> vanilla
            // Only OVERRIDE the answer when the bulk path can actually run. When the bulk gate is not met (hands
            // occupied, no backpack room, another hauler already on it, etc.), defer to vanilla so its OWN
            // single-stack unload still empties the carrier — never suppress unloading entirely.
            if (!BulkUnloadGate.CanDoBulkUnload(pawn, (Pawn)t, forced))
                return true; // fall through to vanilla
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_UnloadCarriers), nameof(WorkGiver_UnloadCarriers.JobOnThing))]
    public static class Patch_WorkGiver_UnloadCarriers_JobOn
    {
        static bool Prefix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (!BulkUnloadGate.ShouldHandle(pawn, t))
                return true; // feature off / mech / non-pawn -> vanilla single-stack unload
            if (!BulkUnloadGate.CanDoBulkUnload(pawn, (Pawn)t, forced))
                return true; // gate not met (hands occupied, no backpack room) -> vanilla single-stack unload
            __result = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadCarrierInBulk, t);
            return false;
        }
    }

    /// <summary>Shared gate logic for the two <see cref="WorkGiver_UnloadCarriers"/> prefixes.</summary>
    internal static class BulkUnloadGate
    {
        /// <summary>Is this a target the BULK path owns? Feature on, a real <see cref="Pawn"/> carrier, and NOT a
        /// <see cref="CompMechCarrier"/> (mech gestator unloads stay vanilla — HD has no PUAH AllowMechanoids path,
        /// so the ref mod's mech branch is intentionally dropped).</summary>
        internal static bool ShouldHandle(Pawn pawn, Thing t)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableBulkUnloadCarriers)
                return false;
            if (pawn == null || !(t is Pawn carrier))
                return false;
            if (carrier.GetComp<CompMechCarrier>() != null)
                return false;
            return true;
        }

        /// <summary>
        /// The bulk gate (in addition to vanilla's own base gate): vanilla would give the job, the carrier is not
        /// itself mid load/haul, the hauler's HANDS are empty, the hauler has enough backpack room, the carrier's
        /// inventory is non-empty, and no OTHER pawn is already unloading this carrier.
        /// </summary>
        internal static bool CanDoBulkUnload(Pawn pawn, Pawn carrier, bool forced)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || carrier?.inventory == null || pawn?.carryTracker == null)
                return false;

            // 1. Inherit vanilla's own eligibility first (UnloadEverything set, faction, forbidden/burning,
            //    reservable, etc.) — the same predicate WorkGiver_UnloadCarriers delegates to.
            if (!UnloadCarriersJobGiverUtility.HasJobOnThing(pawn, carrier, forced))
                return false;

            // 2. The carrier must not itself be busy loading/hauling (would fight the unload). Key off HD + vanilla
            //    job defs (NOT PUAH — HD has none). A carrier in caravan formation / entering a transporter / being
            //    actively loaded shouldn't be bulk-emptied out from under that activity.
            if (CarrierIsMidLoadOrHaul(carrier))
                return false;

            // 3. The hauler's hands must be empty — the visit ENDS by putting the overflow stack into the carry
            //    tracker, so a pre-occupied carry tracker would block that and strand the visit.
            if (pawn.carryTracker.innerContainer != null && pawn.carryTracker.innerContainer.Count > 0)
                return false;

            // 4. Enough free backpack room to be worth a bulk visit (else it overflows to hands immediately).
            if (!BulkUnloadCarrierPolicy.HasEnoughBackpackRoom(
                    MassUtility.EncumbrancePercent(pawn), s.minFreeSpaceToUnloadCarrierPct))
                return false;

            // 5. The carrier actually has something to unload.
            if (carrier.inventory.innerContainer == null || carrier.inventory.innerContainer.Count == 0)
                return false;

            // 6. No OTHER spawned pawn is already unloading this carrier (vanilla or HD) — avoids two haulers
            //    racing one carrier. (The vanilla reservation in step 1's CanReserve handles the exclusive case;
            //    this also catches a non-exclusive HD unload already in flight.)
            if (AnotherPawnUnloading(pawn, carrier))
                return false;

            return true;
        }

        /// <summary>True if the carrier is itself running a load/haul/caravan-form job that the bulk unload would
        /// conflict with. Keyed off HD's own load def + vanilla loading/caravan defs.</summary>
        private static bool CarrierIsMidLoadOrHaul(Pawn carrier)
        {
            var def = carrier.CurJobDef;
            if (def == null)
                return false;
            return def == HaulersDreamDefOf.HaulersDream_LoadPackAnimal
                   || def == HaulersDreamDefOf.HaulersDream_UnloadCarrierInBulk
                   || def == JobDefOf.GiveToPackAnimal
                   || def == JobDefOf.PrepareCaravan_GatherItems
                   || def == JobDefOf.PrepareCaravan_GatherAnimals
                   || def == JobDefOf.PrepareCaravan_GatherDownedPawns
                   || def == JobDefOf.EnterTransporter
                   || def == JobDefOf.CarryDownedPawnToExit;
        }

        /// <summary>True if a spawned pawn OTHER than <paramref name="pawn"/> is currently unloading
        /// <paramref name="carrier"/> (vanilla <c>UnloadInventory</c> or HD bulk unload, targeting it).</summary>
        private static bool AnotherPawnUnloading(Pawn pawn, Pawn carrier)
        {
            var map = carrier.Map;
            if (map?.mapPawns == null)
                return false;
            var spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                var other = spawned[i];
                if (other == null || other == pawn || other == carrier)
                    continue;
                var cur = other.CurJob;
                if (cur == null)
                    continue;
                if ((cur.def == JobDefOf.UnloadInventory || cur.def == HaulersDreamDefOf.HaulersDream_UnloadCarrierInBulk)
                    && cur.targetA.Thing == carrier)
                    return true;
            }
            return false;
        }
    }
}
