using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Only units actually deposited by this explicit sweep, retained across save/load.</summary>
    internal sealed class NearbyHaulCargo : IExposable
    {
        private int version = 1;
        private List<Thing> things = new List<Thing>();
        private List<int> counts = new List<int>();

        public NearbyHaulCargo() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version", 0);
            Scribe_Collections.Look(ref things, "things", LookMode.Reference);
            Scribe_Collections.Look(ref counts, "counts", LookMode.Value);
        }

        internal void Record(Thing thing, int moved)
        {
            if (version != 1 || things == null || counts == null || things.Count != counts.Count
                || thing == null || moved <= 0)
                return;
            int index = things.IndexOf(thing);
            if (index < 0)
            {
                things.Add(thing);
                counts.Add(moved);
            }
            else
                counts[index] = checked(counts[index] + moved);
        }

        internal void QueueSuccessfulDraftedDelivery(Pawn pawn, Job completed)
        {
            if (!pawn.Drafted || !ReferenceEquals(pawn.CurJob, completed)
                || !NearbyHaulCommand.CanContinue(pawn, completed)
                || version != 1 || things == null || counts == null || things.Count != counts.Count)
                return;
            var targets = new List<LocalTargetInfo>();
            var amounts = new List<int>();
            var seen = new HashSet<Thing>();
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                // A lost/destroyed reference cannot be inferred from a same-def sibling. Retain the
                // ordinary tracked stock for later recovery, without extending drafted authority.
                if (thing == null || !seen.Add(thing) || counts[i] <= 0)
                    return;
                if (thing.Destroyed || pawn.inventory.innerContainer.Contains(thing) != true
                    || pawn.GetComp<CompHauledToInventory>().PeekHashSet().Contains(thing) != true)
                    continue;
                int count = Math.Min(counts[i], InventorySurplus.SurplusOf(pawn, thing));
                if (count <= 0)
                    continue;
                targets.Add(thing);
                amounts.Add(count);
            }
            if (targets.Count == 0)
                return;
            var delivery = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_NearbyDelivery);
            delivery.playerForced = true;
            delivery.haulMode = HaulMode.ToCellStorage;
            delivery.workGiverDef = NearbyHaulCommand.Definition;
            delivery.globalTarget = completed.globalTarget;
            // The unload driver's native container continuation owns targetQueueB. Its unused A
            // queue holds this inventory manifest, independently from the active targetA transfer.
            delivery.targetQueueA = targets;
            delivery.countQueue = amounts;
            // The native queued-job path validates again when this order eventually starts.
            // No existing order is removed or promoted; no unrelated inventory is adopted here.
            pawn.jobs.jobQueue.EnqueueLast(delivery, JobTag.Misc);
        }
    }

    internal static class NearbyHaulDelivery
    {
        // The dedicated def selects the safe route even if saved command metadata is malformed.
        // Invalid metadata must never fall through to the ordinary unrestricted unload driver.
        internal static bool Applies(Job job) => job != null
            && job.def == HaulersDreamDefOf.HaulersDream_NearbyDelivery;

        internal static bool ValidManifest(Job job)
        {
            if (!Applies(job) || !NearbyHaulCommand.IsIdentifiedOrder(job)
                || job.haulMode != HaulMode.ToCellStorage || job.targetQueueA == null || job.countQueue == null
                || job.targetQueueA.Count == 0 || job.targetQueueA.Count != job.countQueue.Count)
                return false;
            var seen = new HashSet<Thing>();
            for (int i = 0; i < job.targetQueueA.Count; i++)
                if (job.countQueue[i] < 0 || (job.countQueue[i] > 0 && job.targetQueueA[i].Thing == null)
                    || (job.targetQueueA[i].Thing != null && !seen.Add(job.targetQueueA[i].Thing)))
                    return false;
            return true;
        }

        internal static HashSet<Thing> Candidates(Pawn pawn, Job job)
        {
            var result = new HashSet<Thing>();
            if (!ValidManifest(job))
                return result;
            var tracked = pawn.GetComp<CompHauledToInventory>()?.PeekHashSet();
            var inner = pawn.inventory?.innerContainer;
            for (int i = 0; i < job.targetQueueA.Count; i++)
            {
                var thing = job.targetQueueA[i].Thing;
                if (thing != null && !thing.Destroyed && job.countQueue[i] > 0 && inner?.Contains(thing) == true
                    && tracked?.Contains(thing) == true)
                    result.Add(thing);
            }
            return result;
        }

        internal static int Remaining(Job job, Thing thing)
        {
            if (!ValidManifest(job) || thing == null)
                return 0;
            for (int i = 0; i < job.targetQueueA.Count; i++)
                if (ReferenceEquals(job.targetQueueA[i].Thing, thing))
                    return job.countQueue[i];
            return 0;
        }

        internal static void Debit(Job job, Thing source, int moved)
        {
            if (!ValidManifest(job) || moved <= 0 || moved > Remaining(job, source))
                throw new InvalidOperationException("Invalid nearby delivery withdrawal debit.");
            for (int i = 0; i < job.targetQueueA.Count; i++)
                if (ReferenceEquals(job.targetQueueA[i].Thing, source))
                {
                    job.countQueue[i] = checked(job.countQueue[i] - moved);
                    return;
                }
        }

        // Called only by the exact in-flight receipt after its known piece is verified in inventory.
        internal static void CreditReturn(Job job, Thing piece, int count)
        {
            if (!ValidManifest(job) || piece == null || count <= 0)
                throw new InvalidOperationException("Invalid nearby delivery return credit.");
            for (int i = 0; i < job.targetQueueA.Count; i++)
                if (ReferenceEquals(job.targetQueueA[i].Thing, piece))
                {
                    job.countQueue[i] = checked(job.countQueue[i] + count);
                    return;
                }
            job.targetQueueA.Add(piece);
            job.countQueue.Add(count);
        }

        internal static int UnitsBoundFor(Pawn pawn, Job job, ThingDef def)
        {
            int units = 0;
            foreach (var thing in Candidates(pawn, job))
                if (thing.def == def)
                    units = checked(units + Math.Min(Remaining(job, thing), InventorySurplus.SurplusOf(pawn, thing)));
            return units;
        }
    }
}
