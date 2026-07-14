using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// MEDICINE ON WHEELS (the away-only, opt-in sibling of Meals On Wheels) — when a doctor's map has NO reachable
    /// medicine, let them tend using medicine carried in a Vehicle Framework vehicle's cargo, on a pack animal, or by
    /// another player-faction colonist. For a nomad camp with no stockpiled medicine, the wagons you brought become a
    /// usable medicine store. Gated on <see cref="HaulersDreamSettings.medicineFromVehiclesAway"/> (default OFF) and
    /// NON-HOME maps only, so a base's curated vehicle loadout is never raided.
    ///
    /// A postfix on <see cref="HealthAIUtility.FindBestMedicine"/> (<see cref="Priority.Low"/>, only when vanilla
    /// returned null). Vanilla already scans the healer's own inventory, map storage, and — as a last resort —
    /// SpawnedColonyAnimals; this ADDS other colonists' inventory and VF vehicle cargo (the mobile-store case a nomad
    /// needs, which vanilla never checks). The chosen medicine is LEFT IN the holder's inventory:
    /// <c>WorkGiver_Tend.JobOnThing</c> passes it as target B with its <c>SpawnedParentOrMe</c> holder as target C, and
    /// <c>JobDriver_TendPatient.CollectMedicineToils</c> already routes an inventory-carried medicine out via
    /// <c>Toils_Haul.CheckItemCarriedByOtherPawn</c> + <c>TakeFromOtherInventory</c> — the exact medicine analog of
    /// food's <c>TakeFromOtherInventory</c> (decompile-verified, RW 1.6). So NO custom job is needed; the returned
    /// Thing MUST stay in the holder's inventory (never dropped/respawned) or that holder detection breaks.
    ///
    /// Selection mirrors vanilla's own validator so a carried source is filtered identically to a stockpiled one: the
    /// patient's <c>medCare</c> policy must allow the medicine, it must be un-forbidden and reservable, highest
    /// <c>MedicalPotency</c> wins (nearest as tiebreak). Vanilla's two early-outs are honored (no-meds care, or nothing
    /// to heal) so HD never sources medicine vanilla itself would have refused. The <see cref="HDGuard.SeamDegraded"/>
    /// wrapper keeps a scan throw from taking the tend job down (mirrors <see cref="Patch_TryFindBestFoodSourceFor"/>).
    /// </summary>
    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.FindBestMedicine))]
    public static class Patch_FindBestMedicine
    {
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(ref Thing __result, Pawn healer, Pawn patient, bool onlyUseInventory)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.medicineFromVehiclesAway)
                return;                              // single opt-in gate, default OFF
            if (__result != null || onlyUseInventory)
                return;                              // vanilla found medicine, or caller wants ONLY the healer's own kit
            if (healer == null || patient == null || !healer.IsColonist)
                return;                              // match vanilla's colonist-healer scope
            // ONLY a SPAWNED patient (a colonist in a bed / on the ground). A held entity on an Anomaly holding
            // platform is an UNSPAWNED HeldPawn, and its tend path (WorkGiver_TendEntity / the holding-platform tend
            // float menu) builds a TendEntity job with NO targetC holder slot — so a holder-carried medicine we
            // returned there could not be fetched, and JobDriver_TendPatient.CollectMedicineToils (reused by
            // TendEntity) would fail the job Incompletable on the stale null holder and churn (re-issued every scan).
            // Every consumer that CAN deliver holder-held medicine (WorkGiver_Tend, the in-progress FindMoreMedicine
            // toil) tends a SPAWNED patient, so gating on Spawned admits exactly them and never the two broken ones.
            if (!patient.Spawned)
                return;
            var map = healer.Map;
            if (map == null || map.IsPlayerHome)
                return;                              // AWAY-only: at home the loadout is off-limits, base storage handles it
            // Honor vanilla FindBestMedicine's own early-outs so HD never sources medicine vanilla would refuse:
            // a no-meds / doctor-care-only patient, or a patient with nothing left to heal.
            if (patient.playerSettings != null && (int)patient.playerSettings.medCare <= 1)
                return;
            if (Medicine.GetMedicineCountToFullyHeal(patient) <= 0)
                return;

            var vanillaResult = __result; // null here (guarded above); restored if the scan throws
            try
            {
                var found = FindCarriedMedicine(healer, patient, map);
                if (found != null)
                    __result = found;
            }
            catch (Exception ex)
            {
                __result = vanillaResult;
                HDGuard.SeamDegraded(ex, "HealthAIUtility.FindBestMedicine (HD medicine-on-wheels)", healer,
                    "kept vanilla's result (no carried medicine found by HD), so tending still works.");
            }
        }

        /// <summary>The best carried medicine a doctor may fetch: highest <c>MedicalPotency</c> (vanilla's own
        /// priority), nearest holder as tiebreak, from an eligible holder's inventory (other colonists, pack animals,
        /// and — since this runs only away + opt-in — a VF vehicle's cargo). Left IN the holder's inventory so vanilla's
        /// tend toils deliver it. A holder RIDING inside a vehicle is skipped (its inventory is unreachable).</summary>
        private static Thing FindCarriedMedicine(Pawn healer, Pawn patient, Map map)
        {
            var care = patient.playerSettings != null ? patient.playerSettings.medCare : MedicalCareCategory.NoMeds;

            Thing best = null;
            float bestPotency = -1f;
            int bestDist = int.MaxValue;

            var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                var holder = pawns[i];
                // Live, distinct, un-busy holder (excludes self/dead/downed/drafted/mental/mid-HD-batch). A VF vehicle
                // passes this (it is an away+opt-in mobile store here); only a holder RIDING inside a vehicle is skipped
                // — its inventory can't be reached. IsEligibleCarrier is vehicle-safe (the food postfix relies on the
                // same call before its own vehicle skip).
                if (!InventoryShare.IsEligibleCarrier(holder, healer))
                    continue;
                if (VehicleFrameworkCompat.InVehicle(holder))
                    continue;
                var inv = holder.inventory?.innerContainer;
                if (inv == null || inv.Count == 0)
                    continue;
                if (holder.Position.IsForbidden(healer))
                    continue;

                Thing candidate = BestMedicineIn(inv, healer, care);
                if (candidate == null)
                    continue;
                // Reach last (a pathfind) — only for a holder that actually has usable, reservable medicine.
                if (!healer.CanReach(holder, PathEndMode.Touch, Danger.Some))
                    continue;

                float potency = candidate.def.GetStatValueAbstract(StatDefOf.MedicalPotency);
                int dist = IntVec3Utility.ManhattanDistanceFlat(healer.Position, holder.Position);
                if (best == null || potency > bestPotency || (potency == bestPotency && dist < bestDist))
                {
                    best = candidate;
                    bestPotency = potency;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>The highest-<c>MedicalPotency</c> medicine in one inventory that the patient's <paramref name="care"/>
        /// policy allows, the <paramref name="healer"/> can reserve, and isn't forbidden — vanilla FindBestMedicine's own
        /// validator (<c>medCare.AllowsMedicine</c> + <c>CanReserve(m,10,1)</c> + <c>!IsForbidden</c>), replicated for an
        /// inventory scan.</summary>
        private static Thing BestMedicineIn(ThingOwner inv, Pawn healer, MedicalCareCategory care)
        {
            Thing best = null;
            float bestPotency = -1f;
            for (int i = 0; i < inv.Count; i++)
            {
                var t = inv[i];
                if (t == null || !t.def.IsMedicine)
                    continue;
                if (!care.AllowsMedicine(t.def))
                    continue;
                if (t.IsForbidden(healer))
                    continue;
                if (!healer.CanReserve(t, 10, 1))
                    continue;
                float p = t.def.GetStatValueAbstract(StatDefOf.MedicalPotency);
                if (best == null || p > bestPotency)
                {
                    best = t;
                    bestPotency = p;
                }
            }
            return best;
        }
    }
}
