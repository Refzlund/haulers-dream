using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Read-only physical cargo and reserved current-work intent, retaining each actual Thing.</summary>
    internal static class StorageParcelEvidence
    {
        internal struct Entry
        {
            public Thing Subject;
            public Job OwnerJob;
            public int Units;
            public ISlotGroup KnownGroup;
            public IntVec3? Destination;
            public bool Held;
        }

        internal static void Collect(Pawn pawn, List<Entry> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (pawn?.Map == null) return;

            // F13's live shelf lease already excludes the entire cell. Its exact parcel must not
            // also become incoming units; unrelated tagged inventory on this pawn remains visible.
            if (StorageCommitments.ExplicitShelfUnits(pawn, out Thing exclusiveSubject, out _) <= 0)
                exclusiveSubject = null;
            AddTaggedInventory(pawn, into, exclusiveSubject);

            Job job = pawn.CurJob;
            JobDriver driver = pawn.jobs?.curDriver;
            if (job != null && driver != null && ReferenceEquals(driver.job, job)
                && ReferenceEquals(driver.pawn, pawn) && !ExplicitHaulCommand.IsJob(job))
            {
                if (job.def == JobDefOf.HaulToCell && driver.GetType() == typeof(JobDriver_HaulToCell)
                    && job.haulMode == HaulMode.ToCellStorage)
                    AddNativeHaul(pawn, job, into);
                else if (job.def == HaulersDreamDefOf.HaulersDream_BulkHaul && driver is JobDriver_BulkHaul bulk)
                    AddPendingBulk(pawn, job, bulk.StoragePendingStartIndex, into);
                else if (job.def == HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk
                    && driver is JobDriver_UnloadTransporterInBulk transporter)
                    AddHands(pawn, job, pawn.carryTracker?.CarriedThing, transporter.StorageBoundHandCount, into);
                else if (driver is JobDriver_UnloadHauledInventory unload)
                {
                    if (NearbyHaulDelivery.Applies(job))
                    {
                        Thing held = unload.StorageBoundNearbyHands;
                        AddHands(pawn, job, held, held?.stackCount ?? 0, into);
                    }
                    else if (job.def == HaulersDreamDefOf.HaulersDream_UnloadInventory)
                    {
                        // Unsupported destinations retain native cell exclusivity; that physical
                        // reservation already removes capacity and must not gain numeric debt too.
                        bool exclusive = job.targetB.IsValid && !job.targetB.HasThing
                            && pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, job);
                        if (!exclusive) AddHands(pawn, job, unload.StorageBoundOrdinaryHands,
                            unload.StorageBoundOrdinaryHandCount, into);
                    }
                }
            }
            into.Sort((left, right) => left.Subject.thingIDNumber.CompareTo(right.Subject.thingIDNumber));
        }

        private static void AddTaggedInventory(Pawn pawn, List<Entry> into, Thing exclusiveSubject)
        {
            var inner = pawn.inventory?.innerContainer;
            var comp = pawn.GetComp<CompHauledToInventory>();
            var tagged = comp?.PeekHashSet();
            if (inner == null || tagged == null || tagged.Count == 0) return;

            var ordered = new List<Thing>();
            var remainingByDef = new Dictionary<ThingDef, int>();
            for (int i = 0; i < inner.Count; i++)
            {
                Thing thing = inner[i];
                if (!Usable(thing) || !Owns(inner, thing)) continue;
                remainingByDef.TryGetValue(thing.def, out int previous);
                remainingByDef[thing.def] = checked(previous + thing.stackCount);
                if (tagged.Contains(thing) && !ReferenceEquals(thing, exclusiveSubject)) ordered.Add(thing);
            }
            ordered.Sort((left, right) => left.thingIDNumber.CompareTo(right.thingIDNumber));
            foreach (Thing thing in ordered)
            {
                // SurplusOf describes the next withdrawal. Reduce only this local inventory snapshot
                // after each parcel, so two stacks do not both spend the same keep-count surplus.
                int units = InventorySurplus.SurplusOf(pawn, thing, comp, remainingByDef);
                if (UsesSidearmPairBudget(comp, thing))
                {
                    int available = YieldRouter.InventoryCountOfPair(inner, thing.def, thing.Stuff)
                        - SimpleSidearmsCompat.InventoryKeepCount(pawn, thing.def, thing.Stuff);
                    foreach (Entry prior in into)
                        if (prior.OwnerJob == null && prior.Held && prior.Subject.def == thing.def
                            && prior.Subject.Stuff == thing.Stuff) available -= prior.Units;
                    units = Math.Min(units, Math.Max(0, available));
                }
                units = Math.Min(thing.stackCount, units);
                if (units <= 0) continue;
                into.Add(new Entry { Subject = thing, Units = units, Held = true });
                remainingByDef[thing.def] -= units;
            }
        }

        private static bool UsesSidearmPairBudget(CompHauledToInventory comp, Thing thing)
        {
            if (comp.KeptCountOf(thing.def) > 0) return false;
            var settings = HaulersDreamMod.Settings;
            // The explicit-rule branch in SurplusOf bypasses the sidearm branch, including KeepAll.
            if (settings != null && settings.TryGetItemRule(thing.def, out _)) return false;
            return SimpleSidearmsCompat.IsActive && SimpleSidearmsCompat.MemoryApiOk
                && (thing.def.IsRangedWeapon || thing.def.IsMeleeWeapon);
        }

        private static void AddNativeHaul(Pawn pawn, Job job, List<Entry> into)
        {
            Thing source = job.targetA.Thing;
            Thing held = pawn.carryTracker?.CarriedThing;
            bool receipt = StorageCommitments.NativeParcels(pawn, job, out Thing boundHands,
                out int heldUnits, out Thing boundSource, out int pendingUnits);
            if (receipt)
            {
                AddHands(pawn, job, boundHands, heldUnits, into);
                // A settled pickup has no pending source, even while native has not rebound A.
                if (!ReferenceEquals(source, boundSource) || pendingUnits <= 0) return;
            }
            else if (Usable(held) && (ReferenceEquals(source, held) || ReservedUnits(pawn, job, held) > 0))
                // Original saves have no in-memory receipt yet. Load restoration records these
                // actual native hands before admitting any new pending source.
                AddHands(pawn, job, held, held.stackCount, into);

            if (!Usable(source) || ReferenceEquals(source, held) || job.count <= 0
                || !source.SpawnedOrAnyParentSpawned || source.MapHeld != pawn.Map) return;
            if (source.Spawned && job.targetB.IsValid && !job.targetB.HasThing
                && source.Position == job.targetB.Cell) return;
            int units = Math.Min(job.count, ReservedUnits(pawn, job, source));
            if (receipt) units = Math.Min(units, pendingUnits);
            if (units > 0) Add(into, source, job, units, pawn.Map, DestinationOf(job), false);
        }

        private static void AddPendingBulk(Pawn pawn, Job job, int start, List<Entry> into)
        {
            var queue = job.targetQueueB;
            var counts = job.countQueue;
            if (!SweepPickupPlan.IsAligned(queue, counts) || start < 0 || start >= queue.Count) return;
            for (int i = start; i < queue.Count; i++)
            {
                Thing source = queue[i].Thing;
                if (!Usable(source) || !source.Spawned || source.Map != pawn.Map || counts[i] <= 0) continue;
                int reserved = ReservedUnits(pawn, job, source);
                if (reserved <= 0) continue;
                // Repeated queue references describe portions of one physical source. Emit one
                // parcel bounded by that source/reservation, never repeated whole-stack debt.
                int units = Math.Min(counts[i], reserved);
                int previous = IndexOf(into, source);
                if (previous >= 0 && !into[previous].Held && ReferenceEquals(into[previous].OwnerJob, job))
                {
                    Entry entry = into[previous];
                    entry.Units = (int)Math.Min(reserved, (long)entry.Units + units);
                    into[previous] = entry;
                }
                else Add(into, source, job, units, pawn.Map, null, false);
            }
        }

        internal static int ReservedUnits(Pawn pawn, Job job, Thing source)
        {
            int units = 0;
            foreach (var reservation in pawn.Map.reservationManager.ReservationsReadOnly)
            {
                if (!ReferenceEquals(reservation.Claimant, pawn) || !ReferenceEquals(reservation.Job, job)
                    || reservation.Target.Thing != source || reservation.Layer != null) continue;
                // Native -1 reserves the complete stack; finite reservations cannot license more.
                int bound = reservation.StackCount == -1 ? source.stackCount : reservation.StackCount;
                units = Math.Max(units, Math.Min(source.stackCount, bound));
            }
            return units;
        }

        private static void AddHands(Pawn pawn, Job job, Thing held, int units, List<Entry> into)
        {
            if (!Usable(held) || !ReferenceEquals(pawn.carryTracker?.CarriedThing, held)
                || !Owns(pawn.carryTracker?.innerContainer, held)) return;
            Add(into, held, job, Math.Min(units, held.stackCount), pawn.Map, DestinationOf(job), true);
        }

        private static void Add(List<Entry> into, Thing subject, Job job, int units, Map map,
            IntVec3? destination, bool held)
        {
            if (units <= 0 || IndexOf(into, subject) >= 0) return;
            into.Add(new Entry { Subject = subject, OwnerJob = job, Units = units, Held = held,
                Destination = destination, KnownGroup = destination.HasValue
                    ? BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(destination.Value)) : null });
        }

        private static IntVec3? DestinationOf(Job job) => job.targetB.IsValid && !job.targetB.HasThing
            ? job.targetB.Cell : (IntVec3?)null;

        private static int IndexOf(List<Entry> entries, Thing subject)
        {
            for (int i = 0; i < entries.Count; i++)
                if (ReferenceEquals(entries[i].Subject, subject)) return i;
            return -1;
        }

        private static bool Usable(Thing thing) => thing?.def != null && !thing.Destroyed && thing.stackCount > 0;
        private static bool Owns(ThingOwner owner, Thing thing) => owner != null && thing != null
            && !thing.Spawned && ReferenceEquals(thing.holdingOwner, owner) && owner.Contains(thing);
    }
}
