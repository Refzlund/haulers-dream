using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Make the transporter/shuttle loading dialog aware of HD couriers' IN-FLIGHT cargo — the transporter twin of
    /// <see cref="Patch_EnterPortalUtility_ThingsBeingHauledTo"/> (and the HD counterpart of BLFT's
    /// <c>TransporterUtility_AllSendables_Patch</c>). Vanilla's <c>TransporterUtility.ThingsBeingHauledTo</c> only
    /// sees things physically in a hauler's <c>carryTracker</c> under <c>JobDriver_HaulToTransporter</c>, but HD's
    /// couriers carry their swept load in INVENTORY (tagged) under <c>HaulersDream_LoadTransportersInBulk</c>. The
    /// dialog computes "still needs hauling" from <c>AllSendableItems</c> / <c>AllSendablePawns</c> (the available
    /// pool) minus the manifest's already-counted; without these postfixes the in-flight tagged cargo is absent from
    /// that pool, so the player + vanilla think those goods still need hauling (and a fresh pawn could be dispatched
    /// for cargo already en route).
    ///
    /// Both postfixes append, for each spawned pawn running HD's transporter-load driver targeting THIS dialog's
    /// group, the tagged stacks still held in inventory — split items (<c>AllSendableItems</c>) vs pawns
    /// (<c>AllSendablePawns</c>) into the correct returned list, deduped via a HashSet so a thing vanilla already
    /// yielded isn't double-counted. The shuttle gate matches vanilla exactly (<c>shuttle == null ||
    /// IsRequired(t) || IsAllowed(t)</c>) so an injected thing appears iff vanilla would have shown it from the pool.
    ///
    /// READ-ONLY: no ledger/tag mutation (<see cref="CompHauledToInventory.PeekHashSet"/>, never <c>GetHashSet</c>).
    /// Byte-inert when <c>enableBulkLoadTransporters</c> is off (Prepare gates the patch out).
    /// </summary>
    [HarmonyPatch(typeof(TransporterUtility))]
    public static class Patch_TransporterUtility_AllSendables
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        [HarmonyPatch(nameof(TransporterUtility.AllSendableItems))]
        [HarmonyPostfix]
        static IEnumerable<Thing> AllSendableItems_Postfix(IEnumerable<Thing> __result, List<CompTransporter> transporters, Map map)
        {
            var seen = new HashSet<Thing>();
            foreach (var t in __result)
            {
                if (t != null)
                    seen.Add(t);
                yield return t;
            }
            // Items only — skip pawns (those flow through AllSendablePawns).
            foreach (var carried in EnumerateInFlightTagged(transporters, map, seen, wantPawns: false))
                yield return carried;
        }

        [HarmonyPatch(nameof(TransporterUtility.AllSendablePawns))]
        [HarmonyPostfix]
        static IEnumerable<Pawn> AllSendablePawns_Postfix(IEnumerable<Pawn> __result, List<CompTransporter> transporters, Map map)
        {
            var seen = new HashSet<Thing>();
            foreach (var p in __result)
            {
                if (p != null)
                    seen.Add(p);
                yield return p;
            }
            // Pawns only (a tagged carried colonist/animal/corpse-as-pawn would never be a Pawn here — corpses are
            // Things — but a Vehicle-Framework-style pawn cargo or a captured/animal pawn manifest entry can be).
            foreach (var carried in EnumerateInFlightTagged(transporters, map, seen, wantPawns: true))
                yield return (Pawn)carried;
        }

        /// <summary>
        /// Yield the in-flight HD-tagged cargo for the dialog's transporter group, filtered to items (<paramref
        /// name="wantPawns"/> = false) or pawns (= true), deduped against <paramref name="seen"/>, and gated by the
        /// shuttle filter exactly as vanilla gates the available pool. Mirrors the portal twin's resolution:
        /// scan spawned pawns running <c>HaulersDream_LoadTransportersInBulk</c> whose target transporter shares this
        /// group's <c>groupID</c>, then read each one's tagged inventory via <see cref="CompHauledToInventory.PeekHashSet"/>.
        /// </summary>
        private static IEnumerable<Thing> EnumerateInFlightTagged(List<CompTransporter> transporters, Map map, HashSet<Thing> seen, bool wantPawns)
        {
            if (map?.mapPawns == null || transporters == null || transporters.Count == 0)
                yield break;

            // The dialog's group is identified by its shared groupID (same way BLFT reads transporters[0].groupID).
            var primary = transporters[0];
            if (primary == null)
                yield break;
            int groupID = primary.groupID;
            if (groupID < 0)
                yield break;

            // CompShuttle is per-group; vanilla reads it off transporters[0]. Honor the same gate so injected cargo
            // appears iff vanilla would have shown it from the available pool (IsRequired covers the spec's shuttle
            // audit; IsAllowed matches vanilla's own optional cargo).
            var shuttle = primary.parent?.TryGetComp<CompShuttle>();

            var spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                var p = spawned[i];
                if (p?.CurJob == null || p.CurJobDef != HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk)
                    continue;
                // TargetIndex.A on the bulk-load job is the primary transporter (the deposit dest). Confirm it
                // belongs to THIS dialog's group via its shared groupID.
                var jobComp = p.CurJob.GetTarget(TargetIndex.A).Thing?.TryGetComp<CompTransporter>();
                if (jobComp == null || jobComp.groupID != groupID)
                    continue;

                var hcomp = p.GetComp<CompHauledToInventory>();
                var inner = p.inventory?.innerContainer;
                if (hcomp == null || inner == null)
                    continue;

                // PeekHashSet (read-only): UI/feedback touchpoint, NOT game logic — GetHashSet would mutate/self-heal.
                foreach (var carried in hcomp.PeekHashSet())
                {
                    if (carried == null || carried.Destroyed || carried.def == null || !inner.Contains(carried))
                        continue;
                    if ((carried is Pawn) != wantPawns)
                        continue;
                    if (!seen.Add(carried))
                        continue;
                    if (shuttle != null && !shuttle.IsRequired(carried) && !shuttle.IsAllowed(carried))
                        continue;
                    yield return carried;
                }
            }
        }
    }
}
