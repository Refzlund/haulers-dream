using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HaulersDream.Core;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Only a successful explicit source reservation may retire another pawn's matching pickup.</summary>
    [HarmonyPatch(typeof(ReservationManager), nameof(ReservationManager.Reserve))]
    internal static class Patch_ReservationManager_SweepHandoff
    {
        private static readonly MethodInfo EndJob = AccessTools.DeclaredMethod(typeof(Pawn_JobTracker),
            nameof(Pawn_JobTracker.EndCurrentOrQueuedJob), new[] { typeof(Job), typeof(JobCondition), typeof(bool), typeof(bool) });
        private static readonly MethodInfo Handoff = AccessTools.DeclaredMethod(typeof(Patch_ReservationManager_SweepHandoff),
            nameof(EndOrHandoff));
        private static bool bound;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var original = new List<CodeInstruction>(instructions);
            bound = false;
            // This installed native method has one source-displacement call and two building-interaction
            // calls. Patch the complete shape, or leave every native cancellation (and our postfix) alone.
            var expected = new[] { typeof(Pawn), typeof(Job), typeof(LocalTargetInfo), typeof(int), typeof(int),
                typeof(ReservationLayerDef), typeof(bool), typeof(bool), typeof(bool) };
            if (__originalMethod.IsStatic || __originalMethod.DeclaringType != typeof(ReservationManager)
                || !__originalMethod.GetParameters().Select(p => p.ParameterType).SequenceEqual(expected)
                || EndJob == null || Handoff == null || original.Count(i => i.Calls(EndJob)) != 3
                || original.Any(i => i.Calls(EndJob) && i.blocks.Count != 0))
            {
                HDLog.Warn("ReservationManager.Reserve has an unrecognized cancellation shape; native reservation behavior remains in place.");
                return original;
            }
            var rewritten = new List<CodeInstruction>(original.Count + 15);
            foreach (var instruction in original)
            {
                if (!instruction.Calls(EndJob)) { rewritten.Add(instruction); continue; }
                // The original receiver + four arguments remain on the stack. Supply only Reserve's
                // context. Move incoming labels to the first inserted load, never past the context loads.
                var manager = new CodeInstruction(OpCodes.Ldarg_0);
                manager.labels.AddRange(instruction.labels);
                rewritten.Add(manager);
                rewritten.Add(new CodeInstruction(OpCodes.Ldarg_1)); // claimant
                rewritten.Add(new CodeInstruction(OpCodes.Ldarg_2)); // new job
                rewritten.Add(new CodeInstruction(OpCodes.Ldarg_3)); // source
                rewritten.Add(new CodeInstruction(OpCodes.Ldarg_S, (byte)6)); // layer
                rewritten.Add(new CodeInstruction(OpCodes.Call, Handoff));
            }
            bound = true;
            return rewritten;
        }

        private static void EndOrHandoff(Pawn_JobTracker tracker, Job oldJob, JobCondition condition,
            bool startNewJob, bool canReturnToPool, ReservationManager manager, Pawn claimant, Job newJob,
            LocalTargetInfo target, ReservationLayerDef layer)
        {
            if (condition == JobCondition.InterruptForced && ExplicitSourceOwned(manager, claimant, newJob, target, layer))
            {
                // Native Reserve has already installed the new reservation. Find the exact old owner
                // from its reservation, rather than guessing from a target or creating a cached driver.
                var reservations = manager.ReservationsReadOnly;
                for (int i = 0; i < reservations.Count; i++)
                {
                    var old = reservations[i];
                    if (old.Target == target && old.Job == oldJob && old.Claimant?.jobs == tracker
                        && TryRetire(manager, claimant, old.Claimant, oldJob, target, requireReservation: true))
                        return;
                }
            }
            tracker.EndCurrentOrQueuedJob(oldJob, condition, startNewJob, canReturnToPool);
        }

        static void Postfix(ReservationManager __instance, Pawn claimant, Job job, LocalTargetInfo target,
            ReservationLayerDef layer, bool __result)
        {
            if (!bound || !__result || !ExplicitSourceOwned(__instance, claimant, job, target, layer)) return;
            // Queued orders may have no native reservation yet. Retire their soft pickup claim now so
            // admission later cannot force-take the source back. Keep jobs and queue order intact.
            var pawns = new List<Pawn>(claimant.Map.mapPawns.SpawnedPawnsInFaction(claimant.Faction));
            pawns.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            foreach (var pawn in pawns)
            {
                if (pawn == claimant || pawn.jobs == null) continue;
                TryRetire(__instance, claimant, pawn, pawn.CurJob, target, requireReservation: false);
                var queue = pawn.jobs.jobQueue;
                if (queue == null) continue;
                for (int i = 0; i < queue.Count; i++)
                    TryRetire(__instance, claimant, pawn, queue[i].job, target, requireReservation: false);
            }
        }

        private static bool ExplicitSourceOwned(ReservationManager manager, Pawn claimant, Job job,
            LocalTargetInfo target, ReservationLayerDef layer)
        {
            var source = target.Thing;
            if (layer != null || claimant?.Faction == null || claimant.Map == null
                || manager != claimant.Map.reservationManager || job?.playerForced != true
                || source?.Spawned != true || source.Map != claimant.Map || source.def.category != ThingCategory.Item
                || job.targetA != target)
                return false;
            if (job.def == HaulersDreamDefOf.HaulersDream_BulkHaul)
            {
                if (!DriverMatches(claimant, job, typeof(JobDriver_BulkHaul))
                    || !SweepPickupPlan.IsAligned(job.targetQueueB, job.countQueue)
                    || job.targetQueueB[0] != target || job.countQueue[0] <= 0) return false;
            }
            else
            {
                var driver = job.def == JobDefOf.HaulToCell ? typeof(JobDriver_HaulToCell)
                    : job.def == JobDefOf.HaulToContainer ? typeof(JobDriver_HaulToContainer)
                    : job.def == JobDefOf.TakeInventory ? typeof(JobDriver_TakeInventory) : null;
                if (driver == null || !DriverMatches(claimant, job, driver)) return false;
            }
            // Native TakeInventory reserves job.count for up to ten pawns; the supported hauling
            // drivers use one. Keep this exception tied to the exact def/driver checked above.
            int expectedMaxPawns = job.def == JobDefOf.TakeInventory ? 10 : 1;
            foreach (var reservation in manager.ReservationsReadOnly)
                if (reservation.Claimant == claimant && reservation.Job == job && reservation.Target == target
                    && reservation.Layer == null && reservation.MaxPawns == expectedMaxPawns && reservation.StackCount != 0)
                    return true;
            return false;
        }

        private static bool TryRetire(ReservationManager manager, Pawn claimant, Pawn pawn, Job job,
            LocalTargetInfo target, bool requireReservation)
        {
            if (pawn == null || pawn == claimant || pawn.Map != claimant.Map || pawn.Faction != claimant.Faction
                || job?.def != HaulersDreamDefOf.HaulersDream_BulkHaul
                || !DriverMatches(pawn, job, typeof(JobDriver_BulkHaul))
                || !OwnsJob(pawn, job) || !SweepPickupPlan.IsAligned(job.targetQueueB, job.countQueue)
                || !job.targetQueueB.Contains(target) || job.targetQueueA?.Count > 0)
                return false;

            // Release has no layer parameter and removes the first matching row. Unknown layered or
            // duplicate ownership must retain native cancellation, not release an unrelated reservation.
            int owned = 0;
            foreach (var reservation in manager.ReservationsReadOnly)
            {
                if (reservation.Claimant != pawn || reservation.Job != job || reservation.Target != target) continue;
                if (reservation.Layer != null || ++owned > 1) return false;
            }
            if (requireReservation && owned != 1) return false;
            if (!SweepPickupPlan.Retire(job.targetQueueB, job.countQueue, target, LocalTargetInfo.Invalid)) return false;
            if (job.targetA == target) job.targetA = LocalTargetInfo.Invalid;
            if (job.targetB == target) job.targetB = LocalTargetInfo.Invalid;
            if (job.targetC == target) job.targetC = LocalTargetInfo.Invalid;

            // Keep destination rows: their live evidence still includes actual held/tagged surplus.
            // Invalidate before Release's native notification can query the just-changed plan.
            RouteSelection.ClearClaimedCache();
            BulkHaul.InvalidatePlanCache();
            HaulersDreamGameComponent.InvalidateStorageClaimEvidence();
            if (owned == 1) manager.Release(target, pawn, job);
            // Never jump/run another pawn's next reservation inside native Reserve's enumeration.
            // The existing walk/pause toils consume this serialized tombstone on their own next tick.
            return true;
        }

        private static bool DriverMatches(Pawn pawn, Job job, Type expected)
        {
            if (job.def.driverClass != expected) return false;
            var cached = job.GetCachedDriverDirect;
            if (cached != null && (cached.GetType() != expected || cached.job != job || cached.pawn != pawn)) return false;
            var current = pawn.CurJob == job ? pawn.jobs.curDriver : null;
            return current == null || (current.GetType() == expected && current.job == job && current.pawn == pawn);
        }

        private static bool OwnsJob(Pawn pawn, Job job)
        {
            if (pawn.CurJob == job) return true;
            var queue = pawn.jobs?.jobQueue;
            if (queue != null)
                for (int i = 0; i < queue.Count; i++)
                    if (queue[i].job == job) return true;
            return false;
        }
    }
}
