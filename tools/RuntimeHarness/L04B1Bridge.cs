using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    internal sealed class L04B1Bridge
    {
        internal const string CounterContract = "legacy-anchor-tuple-v1;threshold6;gap180;backoff2500;exact-fields-required";
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly FieldInfo sync, anchors, backoff, warned, fails, cacheTick, cache, claims;
        private readonly MethodInfo backedOff;
        private readonly Dictionary<Job, int> tokens = new Dictionary<Job, int>();
        internal readonly Type Bulk, Churn;
        internal readonly MethodInfo TryBuild, Build, Note;
        internal readonly List<string> Bindings = new List<string>();
        internal L04B1Bridge()
        {
            Bulk = TypeNamed("HaulersDream.BulkHaul"); Churn = TypeNamed("HaulersDream.HaulChurnGuard");
            if (Bulk.Assembly != Churn.Assembly) throw new InvalidOperationException("Production bindings differ in assembly.");
            sync = Field(Churn, "sync", typeof(object));
            anchors = Field(Churn, "bulkAnchors", typeof(Dictionary<int, ValueTuple<int, int, int>>));
            backoff = Field(Churn, "backoffUntil", typeof(Dictionary<int, int>));
            warned = Field(Churn, "loopWarned", typeof(HashSet<int>));
            fails = Field(Churn, "thingFails", typeof(Dictionary<int, ValueTuple<int, int>>));
            cacheTick = Field(Bulk, "cacheTick", typeof(int));
            cache = AccessTools.Field(Bulk, "planCache");
            if (cache == null || !cache.IsStatic || cache.GetCustomAttributes(typeof(ThreadStaticAttribute), false).Length != 1
                || cacheTick.GetCustomAttributes(typeof(ThreadStaticAttribute), false).Length != 1
                || !cache.FieldType.IsGenericType || cache.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>)
                || cache.FieldType.GetGenericArguments()[0] != typeof(long)) throw new MissingFieldException("Exact thread-static plan cache binding.");
            var cached = cache.FieldType.GetGenericArguments()[1];
            foreach (var pair in new[] { Tuple.Create("job", typeof(Job)), Tuple.Create("loadID", typeof(int)), Tuple.Create("jobState", typeof(int)) })
                if (cached.GetField(pair.Item1)?.FieldType != pair.Item2) throw new MissingFieldException("CachedPlan." + pair.Item1);
            TryBuild = Method(Bulk, "TryBuildBulkJob", typeof(Job), typeof(Pawn), typeof(Thing), typeof(Job), typeof(bool), typeof(bool));
            Build = Method(Bulk, "BuildBulkJob", typeof(Job), typeof(Pawn), typeof(Thing), typeof(Job), typeof(bool), typeof(bool));
            Note = Method(Churn, "NoteBulkAnchor", typeof(void), typeof(Thing));
            backedOff = Method(Churn, "IsBackedOff", typeof(bool), typeof(Thing));
            claims = AccessTools.Field(TypeNamed("HaulersDream.HaulersDreamGameComponent"), "storageClaims");
            if (claims == null || !claims.IsStatic || !typeof(IEnumerable).IsAssignableFrom(claims.FieldType)) throw new MissingFieldException("storageClaims");
            var policy = TypeNamed("HaulersDream.Core.HaulChurnPolicy");
            foreach (var pair in new[] { Tuple.Create("MaxNetZeroReanchorsPerThing", 6), Tuple.Create("ReanchorGapTicks", 180), Tuple.Create("NetZeroBackoffTicks", 2500) })
            {
                var f = AccessTools.Field(policy, pair.Item1);
                if (f == null || f.FieldType != typeof(int) || (int)f.GetValue(null) != pair.Item2) throw new MissingMemberException("Unreviewed churn constants: " + pair.Item1);
            }
            foreach (var a in new[] { Bulk.Assembly, policy.Assembly, typeof(Map).Assembly, GetType().Assembly })
                Bindings.Add(a.FullName + ";path=" + a.Location + ";mvid=" + a.ManifestModule.ModuleVersionId);
            var native = AccessTools.Method(typeof(WorkGiver_HaulGeneral), "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            var patches = Harmony.GetPatchInfo(native);
            foreach (var patchType in new[] { "HaulersDream.Patch_WorkGiver_HaulGeneral_BulkHaul", "HaulersDream.Patch_WorkGiver_HaulGeneral_ChurnBackoff" })
            {
                var method = AccessTools.Method(TypeNamed(patchType), "Postfix");
                if (patches == null || !patches.Postfixes.Any(p => p.owner == "giwaffed.HaulersDream" && p.PatchMethod == method && method.Module.Assembly == Bulk.Assembly))
                    throw new MissingMethodException("Actual automatic production postfix absent: " + patchType);
                Bindings.Add("installed=" + patchType + ".Postfix;token=" + method.MetadataToken + ";mvid=" + method.Module.ModuleVersionId);
            }
        }
        internal static Type TypeNamed(string name)
        {
            var found = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).Where(t => t != null).ToList();
            if (found.Count != 1) throw new TypeLoadException(name + "; count=" + found.Count);
            return found[0];
        }
        private FieldInfo Field(Type t, string name, Type type)
        {
            var f = t.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (f == null || f.FieldType != type) throw new MissingFieldException(t.FullName, name);
            Bindings.Add(t.FullName + "." + name + ";token=" + f.MetadataToken); return f;
        }
        private MethodInfo Method(Type t, string name, Type returns, params Type[] args)
        {
            var m = t.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly, null, args, null);
            if (m == null || m.ReturnType != returns) throw new MissingMethodException(t.FullName, name);
            Bindings.Add(t.FullName + "." + name + ";token=" + m.MetadataToken + ";mvid=" + m.Module.ModuleVersionId); return m;
        }
        private void Main() { if (Thread.CurrentThread.ManagedThreadId != thread || !UnityData.IsInMainThread) throw new InvalidOperationException("B1 observer attempted worker read."); }
        internal int Token(Job job)
        {
            Main(); if (job == null) return 0;
            if (!tokens.TryGetValue(job, out var n)) tokens.Add(job, n = tokens.Count + 1);
            return n;
        }
        internal B1Job Job(Job job, JobDriver driver = null)
        {
            Main(); if (job == null) return null;
            return new B1Job { token = Token(job), loadId = job.loadID, def = job.def?.defName,
                startTick = job.startTick, expiry = job.expiryInterval, forced = job.playerForced,
                targetA = job.targetA.Thing?.thingIDNumber ?? -1, driver = driver?.GetType().FullName,
                workgiver = job.workGiverDef?.defName, workgiverClass = job.workGiverDef?.giverClass?.FullName,
                queueIds = job.targetQueueB?.Select(t => t.Thing?.thingIDNumber ?? -1).ToList() ?? new List<int>(),
                counts = job.countQueue?.ToList() ?? new List<int>() };
        }
        internal B1Counter Counter(Thing subject)
        {
            Main(); var row = new B1Counter { contract = CounterContract, subjectId = subject.thingIDNumber, tick = Find.TickManager.TicksGame };
            lock (sync.GetValue(null))
            {
                var a = (Dictionary<int, ValueTuple<int, int, int>>)anchors.GetValue(null);
                row.anchorPresent = a.TryGetValue(row.subjectId, out var value);
                if (row.anchorPresent) { row.anchorTick = value.Item1; row.count = value.Item2; row.stackCount = value.Item3; }
                row.backoffPresent = ((Dictionary<int, int>)backoff.GetValue(null)).TryGetValue(row.subjectId, out var until);
                if (row.backoffPresent) row.until = until;
                row.warned = ((HashSet<int>)warned.GetValue(null)).Contains(row.subjectId);
                row.failPresent = ((Dictionary<int, ValueTuple<int, int>>)fails.GetValue(null)).TryGetValue(row.subjectId, out var failed);
                if (row.failPresent) { row.failTick = failed.Item1; row.failCount = failed.Item2; }
                row.backedOff = (bool)backedOff.Invoke(null, new object[] { subject });
            }
            if (row.backedOff != (row.until.HasValue && row.tick < row.until.Value)) throw new InvalidOperationException("Counter stamp/IsBackedOff disagree.");
            return row;
        }
        internal B1Cache Cache(Pawn pawn, Thing subject)
        {
            Main(); var d = (IDictionary)cache.GetValue(null);
            long key = ((long)pawn.thingIDNumber << 32) | (uint)subject.thingIDNumber;
            var row = new B1Cache { tick = Find.TickManager.TicksGame, generation = (int)cacheTick.GetValue(null), key = key,
                pawnId = pawn.thingIDNumber, sourceId = subject.thingIDNumber, dictionaryPresent = d != null, entryPresent = d != null && d.Contains(key) };
            if (row.entryPresent)
            {
                var value = d[key]; var t = value.GetType();
                row.pinnedLoadId = (int)t.GetField("loadID").GetValue(value); row.jobState = (int)t.GetField("jobState").GetValue(value);
                row.actualJob = Job((Job)t.GetField("job").GetValue(value));
            }
            return row;
        }
        internal List<B1Claim> Claims(IEnumerable<Pawn> cohort, ISlotGroup high)
        {
            Main(); var ids = new HashSet<Pawn>(cohort); var answer = new List<B1Claim>();
            foreach (var row in (IEnumerable)claims.GetValue(null))
            {
                var t = row.GetType(); var p = (Pawn)t.GetField("Pawn").GetValue(row); if (!ids.Contains(p)) continue;
                var g = t.GetField("Group").GetValue(row);
                answer.Add(new B1Claim { pawnId = p.thingIDNumber, units = (int)t.GetField("Units").GetValue(row),
                    def = ((ThingDef)t.GetField("Def").GetValue(row))?.defName,
                    group = ReferenceEquals(g, high) ? "fixture-high" : "other-group" });
            }
            return answer;
        }
        internal void AssertReturnable(Map map, Job job)
        {
            Main(); if (job == null || job.def?.defName != "HaulersDream_BulkHaul" || job.playerForced) throw new InvalidOperationException("Not an automatic bulk candidate.");
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                if (ReferenceEquals(pawn.CurJob, job) || pawn.jobs?.jobQueue?.Contains(job) == true) throw new InvalidOperationException("Candidate is current or queued.");
            if (map.reservationManager.ReservationsReadOnly.Any(r => ReferenceEquals(r.Job, job))) throw new InvalidOperationException("Candidate owns a reservation.");
            if (PhysicalReservations(map).Any(r => ReferenceEquals(r.job, job))) throw new InvalidOperationException("Candidate owns a physical interaction reservation.");
            // Pool contents/IDs are never edited; the caller must observe real clear
            // and subsequent actual MakeJob reuse before claiming Q4 exercised.
            if (SimplePool<Job>.FreeItemsCount >= 1000) throw new InvalidOperationException("Native Job pool is full; recycling control inconclusive.");
        }
        internal static List<PhysicalInteractionReservationManager.PhysicalInteractionReservation> PhysicalReservations(Map map)
        {
            var f = typeof(PhysicalInteractionReservationManager).GetField("reservations", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null || f.FieldType != typeof(List<PhysicalInteractionReservationManager.PhysicalInteractionReservation>))
                throw new MissingFieldException("Exact physical reservation list binding.");
            return (List<PhysicalInteractionReservationManager.PhysicalInteractionReservation>)f.GetValue(map.physicalInteractionReservationManager);
        }
    }
}
