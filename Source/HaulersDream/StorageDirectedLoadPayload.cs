using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public abstract partial class JobDriver_LoadInBulkBase
    {
        // In-flight identity only, not a cargo ledger or new saved truth. Rebound by the driver's
        // existing actual start/adapter reconstruction; never created by a storage query.
        [NonSerialized] private StorageDirectedLoadPayload storagePayload;
        private void CaptureStoragePayloadBinding()
            => storagePayload = StorageDirectedLoadPayload.Bind(this, pawn, job, adapter as LoadTransportersAdapter);
        internal StorageDirectedLoadPayload StoragePayload => storagePayload;
        internal bool StoragePayloadCurrent(StorageDirectedLoadPayload payload, LoadTransportersAdapter expected)
            => ReferenceEquals(storagePayload, payload) && ReferenceEquals(adapter, expected);
    }

    internal sealed class StorageDirectedLoadPayload
    {
        private readonly JobDriver_LoadInBulkBase driver;
        private readonly Pawn pawn;
        private readonly Job job;
        private readonly int jobId, taskKey;
        private readonly Map map;
        private readonly Thing target;
        private readonly LoadTransportersAdapter adapter;
        private readonly HaulersDreamGameComponent component;
        private readonly LoadLedgerEntry task;
        private readonly IReadOnlyList<CompTransporter> group;
        private readonly ProjectionListGuard<CompTransporter> groupGuard;

        private StorageDirectedLoadPayload(JobDriver_LoadInBulkBase driver, Pawn pawn, Job job,
            LoadTransportersAdapter adapter, HaulersDreamGameComponent component, LoadLedgerEntry task)
        {
            this.driver = driver; this.pawn = pawn; this.job = job; jobId = job.loadID;
            this.adapter = adapter; this.component = component; this.task = task;
            target = adapter.Primary.parent; map = pawn.Map; taskKey = adapter.Primary.groupID;
            group = adapter.Group;
            groupGuard = new ProjectionListGuard<CompTransporter>(group as List<CompTransporter>);
        }

        internal static StorageDirectedLoadPayload Bind(JobDriver_LoadInBulkBase driver, Pawn pawn,
            Job job, LoadTransportersAdapter adapter)
        {
            if (!(driver is JobDriver_LoadTransportersInBulk) || adapter?.Primary?.parent == null
                || adapter.Primary.groupID < 0
                || !(adapter.Group is List<CompTransporter>)
                || pawn?.Map == null || job == null || job.def != HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk
                || !ReferenceEquals(job.targetA.Thing, adapter.Primary.parent)) return null;
            var component = HaulersDreamGameComponent.Instance;
            return component != null && component.TryReadTransportLoadClaim(adapter.Primary.groupID, pawn.Map, out var task)
                ? new StorageDirectedLoadPayload(driver, pawn, job, adapter, component, task) : null;
        }

        private bool Current()
        {
            if (!UnityData.IsInMainThread || !driver.StoragePayloadCurrent(this, adapter)
                || taskKey < 0
                || !ReferenceEquals(HaulersDreamGameComponent.Instance, component)
                || !pawn.Spawned || !ReferenceEquals(pawn.Map, map) || !ReferenceEquals(pawn.CurJob, job)
                || job.loadID != jobId || job.def != HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk
                || !ReferenceEquals(pawn.jobs.curDriver, driver) || !ReferenceEquals(driver.job, job)
                || !ReferenceEquals(driver.pawn, pawn)
                || target.Destroyed || !target.Spawned || !ReferenceEquals(target.Map, map)
                || !ReferenceEquals(adapter.Primary.parent, target) || adapter.Primary.groupID != taskKey
                || !ReferenceEquals(adapter.Group, group) || !groupGuard.Matches(group as List<CompTransporter>)
                || !component.TryReadTransportLoadClaim(taskKey, map, out var live) || !ReferenceEquals(live, task)
                || HaulersDreamMod.Settings?.masterEnabled != true || !HaulersDreamMod.Settings.enableBulkLoadTransporters)
                return false;
            bool ownsCurrentTarget = false;
            foreach (var member in adapter.Group)
            {
                StorageProgressWork.Charge(StorageWorkKind.Custody);
                if (member?.parent == null || member.parent.Destroyed || !member.parent.Spawned
                    || !ReferenceEquals(member.Map, map) || member.groupID != taskKey
                    || component.BulkUnloadAllFlagged(member.parent.thingIDNumber)) return false;
                if (ReferenceEquals(member.parent, job.targetA.Thing)) ownsCurrentTarget = true;
            }
            // The real redirect may choose another member of this SAME captured task/group.
            return ownsCurrentTarget;
        }

        private sealed class Manifest
        {
            internal CompTransporter Member;
            internal List<TransferableOneWay> List;
            internal ProjectionListGuard<TransferableOneWay> ListGuard;
            internal TransferableOneWay Entry;
            internal int Units;
            internal Thing Exemplar;
            internal StorageSubjectAttributeGuard ExemplarGuard;
            internal ProjectionListGuard<Thing> ThingsGuard;
        }

        private sealed class ClaimSnapshot
        {
            internal Pawn Owner;
            internal Dictionary<ThingDef, int> Live, Copy;
        }

        // Every exclusion is local to this collector result. Tags, load claims, manifests,
        // inventory and saved cargo remain untouched, including the ordinary failure salvage.
        internal static void Exclude(Pawn pawn, List<StorageParcelEvidence.Entry> entries)
        {
            var payload = (pawn.jobs?.curDriver as JobDriver_LoadInBulkBase)?.StoragePayload;
            if (payload == null || entries.Count == 0 || !payload.Current()) return;
            try { payload.Exclude(entries); }
            catch (StorageWorkExhausted) { throw; }
            // Optional native/compatibility attribution failed. The original collector result
            // remains authoritative; all list edits occur only after the complete guarded pass.
            catch (Exception) { }
        }

        private void Exclude(List<StorageParcelEvidence.Entry> entries)
        {
            if (task.pawnClaims == null || !task.pawnClaims.TryGetValue(pawn, out var claim)
                || claim == null || task.totalNeeded == null) return;
            var currentTarget = job.targetA.Thing;
            var claimMap = task.pawnClaims;
            var allClaims = new List<ClaimSnapshot>();
            var otherClaims = new Dictionary<ThingDef, long>();
            foreach (var owner in claimMap)
            {
                StorageProgressWork.Charge(StorageWorkKind.Custody);
                if (owner.Value == null) return;
                StorageProgressWork.Charge(StorageWorkKind.Custody, owner.Value.Count);
                var copy = new Dictionary<ThingDef, int>(owner.Value);
                allClaims.Add(new ClaimSnapshot { Owner = owner.Key, Live = owner.Value, Copy = copy });
                if (ReferenceEquals(owner.Key, pawn)) continue;
                foreach (var pair in copy)
                {
                    StorageProgressWork.Charge(StorageWorkKind.Custody);
                    if (pair.Value <= 0) continue;
                    otherClaims.TryGetValue(pair.Key, out long previous);
                    otherClaims[pair.Key] = checked(previous + pair.Value);
                }
            }
            StorageProgressWork.Charge(StorageWorkKind.Custody, task.totalNeeded.Count);
            var neededGuard = new Dictionary<ThingDef, int>(task.totalNeeded);
            var manifests = new List<Manifest>();
            foreach (var member in adapter.Group)
            {
                StorageProgressWork.Charge(StorageWorkKind.Custody);
                var list = member.leftToLoad;
                if (list == null) continue;
                StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                var listGuard = new ProjectionListGuard<TransferableOneWay>(list);
                foreach (var entry in list)
                {
                    StorageProgressWork.Charge(StorageWorkKind.Custody);
                    if (entry == null || !entry.HasAnyThing || entry.CountToTransfer <= 0) continue;
                    // A duplicated transferable reference must not gain another independent budget.
                    StorageProgressWork.Charge(StorageWorkKind.Custody, manifests.Count);
                    if (manifests.Exists(m => ReferenceEquals(m.Entry, entry))) return;
                    var exemplar = entry.AnyThing;
                    if (exemplar?.def?.category != ThingCategory.Item) continue;
                    StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                    manifests.Add(new Manifest { Member = member, List = list, ListGuard = listGuard,
                        Entry = entry, Units = entry.CountToTransfer, Exemplar = exemplar,
                        // An original split/merge exemplar may no longer be physical cargo. Its
                        // raw variant attributes still describe the native manifest comparison.
                        ExemplarGuard = new StorageSubjectAttributeGuard(exemplar),
                        ThingsGuard = new ProjectionListGuard<Thing>(entry.things) });
                }
            }
            StorageProgressWork.Charge(StorageWorkKind.Custody, claim.Count);
            var budget = new DirectedLoadCargoBudget<ThingDef, TransferableOneWay>(claim, neededGuard, otherClaims);
            StorageProgressWork.Charge(StorageWorkKind.Custody, checked(entries.Count * 2));
            var excluded = new int[entries.Count];
            var subjects = new ProjectionThingGuard[entries.Count];
            var inner = pawn.inventory?.innerContainer;
            var cargo = pawn.GetComp<CompHauledToInventory>();
            var tags = cargo?.PeekHashSet();
            if (inner == null || tags == null) return;
            var kept = cargo.PeekKeptCounts();
            StorageProgressWork.Charge(StorageWorkKind.Custody, kept.Count);
            var keptGuard = new Dictionary<ThingDef, int>(kept);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; var subject = entry.Subject;
                StorageProgressWork.Charge(StorageWorkKind.Custody);
                if (!entry.Held || entry.OwnerJob != null || subject == null || subject.Spawned
                    || !ReferenceEquals(subject.holdingOwner, inner) || !tags.Contains(subject)) continue;
                subjects[i] = new ProjectionThingGuard(subject);
                foreach (var member in adapter.Group)
                {
                    StorageProgressWork.Charge(StorageWorkKind.Custody);
                    Manifest matched = null;
                    foreach (var manifest in manifests)
                    {
                        StorageProgressWork.Charge(StorageWorkKind.Custody);
                        if (!ReferenceEquals(manifest.Member, member)) continue;
                        foreach (var declared in manifest.Entry.things)
                        {
                            StorageProgressWork.Charge(StorageWorkKind.Custody);
                            if (ReferenceEquals(declared, subject)) { matched = manifest; break; }
                        }
                        if (matched != null) break;
                    }
                    if (matched == null)
                        foreach (var manifest in manifests)
                        {
                            StorageProgressWork.Charge(StorageWorkKind.Custody);
                            if (!ReferenceEquals(manifest.Member, member)) continue;
                            StorageProgressWork.Charge(StorageWorkKind.Predicate);
                            // Native read-only comparison covers split/merge descendants. No def-only
                            // fallback, planner, reservation or load-claim acquisition runs here.
                            if (ReferenceEquals(subject.def, manifest.Exemplar.def)
                                && StorageProgressWork.Provider(() => TransferableUtility.TransferAsOne(
                                    subject, manifest.Exemplar, TransferAsOneMode.PodsOrCaravanPacking)))
                            { matched = manifest; break; }
                        }
                    if (matched == null) continue;
                    StorageProgressWork.Charge(StorageWorkKind.Custody, checked(claim.Count + manifests.Count));
                    excluded[i] += budget.Take(subject.def, matched.Entry, matched.Units, entry.Units - excluded[i]);
                    if (excluded[i] == entry.Units) break;
                }
            }
            // A compatibility callback may have changed a previous parcel/claim/manifest. Publish
            // no partial exclusions from that stale pass; the original storage evidence stays intact.
            if (!Current() || !ReferenceEquals(job.targetA.Thing, currentTarget) || !ReferenceEquals(pawn.inventory?.innerContainer, inner)
                || !ReferenceEquals(pawn.GetComp<CompHauledToInventory>(), cargo) || !ReferenceEquals(cargo.PeekHashSet(), tags)
                || !ReferenceEquals(cargo.PeekKeptCounts(), kept) || !Same(kept, keptGuard)
                || !ClaimsCurrent(claimMap, allClaims) || !Same(task.totalNeeded, neededGuard)) return;
            foreach (var manifest in manifests)
            {
                StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                if (!ReferenceEquals(manifest.Member.leftToLoad, manifest.List) || !manifest.ListGuard.Matches(manifest.List)
                    || manifest.Entry.CountToTransfer != manifest.Units || !manifest.ThingsGuard.Matches(manifest.Entry.things)
                    || !ReferenceEquals(manifest.Entry.AnyThing, manifest.Exemplar) || !manifest.ExemplarGuard.Matches()) return;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                StorageProgressWork.Charge(StorageWorkKind.Custody);
                if (subjects[i] != null && (!subjects[i].Matches() || !tags.Contains(entries[i].Subject)
                    || !ReferenceEquals(entries[i].Subject.holdingOwner, inner))) return;
            }
            // Finish with raw guards after the last native property access above.
            if (!Current() || !ReferenceEquals(job.targetA.Thing, currentTarget)
                || !ClaimsCurrent(claimMap, allClaims) || !Same(task.totalNeeded, neededGuard) || !Same(kept, keptGuard)) return;
            foreach (var manifest in manifests)
            {
                StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                if (!ReferenceEquals(manifest.Member.leftToLoad, manifest.List) || !manifest.ListGuard.Matches(manifest.List)
                    || manifest.Entry.CountToTransfer != manifest.Units || !manifest.ThingsGuard.Matches(manifest.Entry.things)
                    || !ReferenceEquals(manifest.Entry.AnyThing, manifest.Exemplar) || !manifest.ExemplarGuard.Matches()) return;
            }
            foreach (var subject in subjects)
            { StorageProgressWork.Charge(StorageWorkKind.Custody); if (subject != null && !subject.AttributesMatch()) return; }
            // Reserve all publication work first: exhaustion must not leave a partially edited list.
            StorageProgressWork.Charge(StorageWorkKind.Custody, checked(entries.Count * 2));
            for (int i = 0; i < entries.Count; i++)
            { var entry = entries[i]; entry.Units -= excluded[i]; entries[i] = entry; }
            entries.RemoveAll(entry => entry.Units <= 0);
        }

        private bool ClaimsCurrent(Dictionary<Pawn, Dictionary<ThingDef, int>> captured,
            List<ClaimSnapshot> snapshots)
        {
            if (!ReferenceEquals(task.pawnClaims, captured) || captured.Count != snapshots.Count) return false;
            foreach (var snapshot in snapshots)
            {
                StorageProgressWork.Charge(StorageWorkKind.Custody);
                if (!captured.TryGetValue(snapshot.Owner, out var live) || !ReferenceEquals(live, snapshot.Live)
                    || !Same(live, snapshot.Copy)) return false;
            }
            return true;
        }

        private static bool Same(Dictionary<ThingDef, int> live, Dictionary<ThingDef, int> captured)
        {
            if (live == null || live.Count != captured.Count) return false;
            foreach (var pair in captured)
            {
                StorageProgressWork.Charge(StorageWorkKind.Custody);
                if (!live.TryGetValue(pair.Key, out int value) || value != pair.Value) return false;
            }
            return true;
        }
    }
}
