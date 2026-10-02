using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // This is durable admitted work, not a saved capacity/world snapshot. Native job.count is
    // a remaining trip budget and cannot encode the amount admitted from its current source.
    internal sealed class NativeStorageIntent : IExposable
    {
        public NativeStorageIntent() { }

        internal Pawn Pawn;
        internal int JobId;
        internal Thing Subject;
        internal IntVec3 Destination;
        internal int Units;
        internal bool Held;

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref JobId, "jobId", -1);
            Scribe_References.Look(ref Subject, "subject");
            Scribe_Values.Look(ref Destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref Units, "units", 0);
            Scribe_Values.Look(ref Held, "held", false);
        }
    }

    public partial class HaulersDreamGameComponent
    {
        private int nativeStorageIntentVersion;
        private List<NativeStorageIntent> nativeStorageIntents;

        internal int NativeStorageIntentVersion => nativeStorageIntentVersion;
        internal IReadOnlyList<NativeStorageIntent> NativeStorageIntents => nativeStorageIntents;

        private void ExposeNativeStorageIntents()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                nativeStorageIntentVersion = 1;
                nativeStorageIntents = StorageCommitments.CaptureNativeStorageIntents();
            }
            Scribe_Values.Look(ref nativeStorageIntentVersion, "haulersDreamNativeStorageIntentVersion", 0);
            Scribe_Collections.Look(ref nativeStorageIntents, "haulersDreamNativeStorageIntents", LookMode.Deep);
        }
    }

    internal static partial class StorageCommitments
    {
        internal static List<NativeStorageIntent> CaptureNativeStorageIntents()
        {
            var captured = new List<NativeStorageIntent>();
            var entries = new List<StorageParcelEvidence.Entry>();
            foreach (var row in HaulersDreamGameComponent.storageClaims)
            {
                if (!(row.Pawn is Pawn pawn) || !(row.WorkOwner is NativeStorageOwner owner)
                    || !owner.Live(pawn) || row.Units <= 0 || !NativeStoragePickup.Applies(pawn, out Job job)) continue;
                int remaining = row.Units;
                StorageParcelEvidence.Collect(pawn, entries);
                foreach (var entry in ResourceParcelOrder(entries, native: true))
                {
                    if (remaining <= 0) break;
                    if (!ReferenceEquals(entry.OwnerJob, job) || entry.Subject?.def != row.Def
                        || entry.Units <= 0 || !ReferenceEquals(entry.KnownGroup, row.Group)
                        || !entry.Destination.HasValue || entry.Destination.Value != job.targetB.Cell
                        || !pawn.Map.reservationManager.ReservedBy(entry.Subject, pawn, job)) continue;
                    int units = Math.Min(remaining, entry.Units);
                    remaining -= units;
                    captured.Add(new NativeStorageIntent { Pawn = pawn, JobId = job.loadID,
                        Subject = entry.Subject, Destination = entry.Destination.Value,
                        Units = units, Held = entry.Held });
                }
            }
            captured.Sort((left, right) =>
            {
                int order = left.Pawn.Map.uniqueID.CompareTo(right.Pawn.Map.uniqueID);
                if (order == 0) order = left.Pawn.thingIDNumber.CompareTo(right.Pawn.thingIDNumber);
                if (order == 0) order = left.JobId.CompareTo(right.JobId);
                if (order == 0) order = right.Held.CompareTo(left.Held);
                if (order == 0) order = left.Subject.thingIDNumber.CompareTo(right.Subject.thingIDNumber);
                return order;
            });
            return captured;
        }

        private static int SavedNativePendingUnits(IReadOnlyList<NativeStorageIntent> intents,
            Pawn pawn, Job job, StorageParcelEvidence.Entry entry)
        {
            int units = 0;
            if (intents == null) return units;
            foreach (var saved in intents)
                if (saved != null && !saved.Held && ReferenceEquals(saved.Pawn, pawn)
                    && saved.JobId == job.loadID && ReferenceEquals(saved.Subject, entry.Subject)
                    && saved.Destination == job.targetB.Cell && saved.Units > 0)
                    // Repeated/corrupt records cannot multiply one actual source's responsibility.
                    units = Math.Max(units, Math.Min(saved.Units, entry.Units));
            return units;
        }
    }
}
