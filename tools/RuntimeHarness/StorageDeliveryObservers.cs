using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    // Observation only: void prefixes/postfixes/finalizers. No original is skipped;
    // no arguments/results/jobs/claims are modified and exceptions are not replaced.
    internal sealed class StorageDeliveryObservers : IDisposable
    {
        private const string Owner = "HaulersDream.RuntimeHarness.StorageDelivery.observers";
        private static StorageDeliveryObservers current;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly StorageDeliveryScenario scenario;
        private readonly Pawn actor;
        private readonly List<MethodBase> targets = new List<MethodBase>();
        [ThreadStatic] private static Stack<StorageDeliveryQuery> freeQueries;
        [ThreadStatic] private static int driverDepth;
        [ThreadStatic] private static int carryDepth;
        [ThreadStatic] private static int dropDepth;
        private readonly PropertyInfo insideScan;

        internal StorageDeliveryObservers(StorageDeliveryScenario scenario, Pawn actor)
        {
            if (current != null) throw new InvalidOperationException("Storage delivery observers already installed.");
            this.scenario = scenario; this.actor = actor;
            current = this;
            freeQueries = new Stack<StorageDeliveryQuery>(); driverDepth = carryDepth = dropDepth = 0;
            try
            {
                var adapter = StorageDeliveryScenario.TypeNamed("HaulersDream.StorageCommitments");
                insideScan = AccessTools.Property(adapter, "InsideSpaceScan");
                if (insideScan == null) throw new MissingMemberException("StorageCommitments.InsideSpaceScan");
                Patch(AccessTools.Method(typeof(WorkGiver_HaulGeneral), "JobOnThing"), null, nameof(CandidateAfter));
                Patch(AccessTools.Method(adapter, "FreeUnitsFor", new[] { typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef), typeof(Thing), typeof(bool).MakeByRefType() }),
                    nameof(FreeBefore), nameof(FreeAfter), nameof(FreeFinally));
                Patch(AccessTools.Method(adapter, "IsDelivering", new[] { typeof(Pawn), typeof(Thing) }), null, nameof(DeliveringAfter));
                Patch(AccessTools.Method(adapter, "UnitsMoving", new[] { typeof(Pawn), typeof(ThingDef) }), null, nameof(LiveUnitsAfter));
                Patch(AccessTools.Method(typeof(StoreUtility), "IsGoodStoreCell"), null, nameof(GateAfter));
                Patch(AccessTools.Method(StorageDeliveryScenario.TypeNamed("HaulersDream.JobDriver_BulkHaul"), "DepositSwept"), nameof(PickupBefore), nameof(PickupAfter));
                Patch(AccessTools.Method(typeof(Pawn_JobTracker), "CleanupCurrentJob"), nameof(CleanupBefore), nameof(CleanupAfter));
                foreach (var name in new[] { "DriverTick", "DriverTickInterval", "TryActuallyStartNextToil" })
                    Patch(AccessTools.Method(typeof(JobDriver), name), nameof(DriverBefore), null, nameof(DriverFinally));
                foreach (var method in typeof(Pawn_CarryTracker).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "TryStartCarry"))
                    Patch(method, nameof(CarryBefore), null, nameof(CarryFinally));
                foreach (var method in typeof(Pawn_CarryTracker).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "TryDropCarriedThing"))
                    Patch(method, nameof(DropBefore), nameof(DropAfter), nameof(DropFinally));
                Patch(AccessTools.Method(typeof(Thing), "SplitOff", new[] { typeof(int) }), nameof(SplitBefore), nameof(SplitAfter));
                foreach (var type in new[] { typeof(Thing), typeof(ThingWithComps) })
                    Patch(type.GetMethod("TryAbsorbStack", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), nameof(AbsorbBefore), nameof(AbsorbAfter));
                HarnessSession.Check("fixture-storage-delivery-observers", targets.Count >= 16,
                    "Read-only targets=" + targets.Count + "; owner=" + Owner + "; actual method identities recorded.");
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodBase target, string prefix, string postfix, string finalizer = null)
        {
            if (target == null) throw new MissingMethodException("A storage delivery observation target is missing.");
            HarmonyMethod Hook(string name)
            {
                if (name == null) return null;
                return new HarmonyMethod(typeof(StorageDeliveryObservers), name) { priority = Priority.Last, after = new[] { "giwaffed.HaulersDream" } };
            }
            harmony.Patch(target, Hook(prefix), Hook(postfix), null, Hook(finalizer));
            targets.Add(target);
            if (Harmony.GetPatchInfo(target)?.Owners.Contains(Owner) != true) throw new InvalidOperationException("Observer not installed: " + target);
            HarnessSession.Event("storage-delivery-observer-bind", target.DeclaringType.FullName + "." + target.Name
                + "; token=" + target.MetadataToken + "; assembly=" + target.Module.Assembly.FullName + "; MVID=" + target.Module.ModuleVersionId);
        }
        private static void Safe(Action<StorageDeliveryObservers> action)
        {
            var o = current;
            if (o == null || !o.scenario.IsActive) return;
            try { action(o); }
            catch (Exception error) { o.scenario.ObserverFault(error); }
        }
        private static void CandidateAfter(Pawn __0, Thing __1, bool __2, Job __result) => Safe(o =>
        {
            if (o.scenario.IsActor(__0)) o.scenario.Candidate(__result, __1, __2);
        });

        private sealed class FreeToken { internal StorageDeliveryQuery query; internal bool pushed; }
        private static void FreeBefore(Pawn __0, ISlotGroup __1, Thing __3, out FreeToken __state)
        {
            FreeToken token = null;
            Safe(o =>
            {
                var query = o.scenario.BeginFree(__0, __1, __3);
                if (query == null) return;
                token = new FreeToken { query = query, pushed = true };
                (freeQueries ?? (freeQueries = new Stack<StorageDeliveryQuery>())).Push(query);
            });
            __state = token;
        }
        private static void FreeAfter(bool __4, int __result, FreeToken __state) => Safe(o =>
        {
            if (__state != null) o.scenario.EndFree(__state.query, __result, __4);
        });
        private static void FreeFinally(FreeToken __state, Exception __exception) => Safe(o =>
        {
            if (__state?.pushed == true)
            {
                if (freeQueries == null || freeQueries.Count == 0 || !ReferenceEquals(freeQueries.Peek(), __state.query))
                    throw new InvalidOperationException("Unbalanced free-space observation nesting.");
                freeQueries.Pop();
            }
            if (__exception != null) o.scenario.ObserverFault(new InvalidOperationException("Observed FreeUnitsFor exception.", __exception));
        });
        private static void DeliveringAfter(Pawn __0, Thing __1, bool __result) => Safe(o =>
        {
            if (o.scenario.IsActor(__0) && freeQueries?.Count > 0 && freeQueries.Peek().subject.id == __1?.thingIDNumber)
                freeQueries.Peek().delivering = __result ? 1 : 0;
        });
        private static void LiveUnitsAfter(Pawn __0, ThingDef __1, int __result) => Safe(o =>
        {
            if (o.scenario.IsActor(__0) && __1 == ThingDefOf.Steel && freeQueries?.Count > 0)
                freeQueries.Peek().productionLiveUnits = __result;
        });
        private static void GateAfter(IntVec3 __0, Thing __2, Pawn __3, bool __result) => Safe(o =>
        {
            if (o.scenario.IsActor(__3) && !(bool)o.insideScan.GetValue(null, null)) o.scenario.CellGate(__0, __2, __result);
        });

        private static void PickupBefore(JobDriver __instance, Thing __0, out StorageDeliveryScenario.PickupToken __state)
        {
            StorageDeliveryScenario.PickupToken token = null;
            Safe(o => token = o.scenario.BeginPickup(__instance, __0)); __state = token;
        }
        private static void PickupAfter(bool __result, StorageDeliveryScenario.PickupToken __state) => Safe(o => o.scenario.EndPickup(__state, __result));
        private static void CleanupBefore(Pawn_JobTracker __instance, JobCondition condition, out StorageDeliveryScenario.EndToken __state)
        {
            StorageDeliveryScenario.EndToken token = null;
            Safe(o => { if (ReferenceEquals(__instance, o.actor.jobs)) token = o.scenario.BeginCleanup(condition); }); __state = token;
        }
        private static void CleanupAfter(StorageDeliveryScenario.EndToken __state) => Safe(o => o.scenario.EndCleanup(__state));
        private static void DriverBefore(JobDriver __instance, out bool __state)
        {
            bool entered = false;
            Safe(o =>
            {
                if (__instance.pawn != o.actor || !ReferenceEquals(__instance, o.actor.jobs.curDriver) || !ReferenceEquals(__instance.job, o.actor.CurJob)) return;
                entered = true; driverDepth++; o.scenario.ObserveActualJob();
            });
            __state = entered;
        }
        private static void DriverFinally(bool __state, Exception __exception) => Safe(o =>
        {
            if (!__state) return;
            driverDepth--;
            if (__exception != null) o.scenario.ObserverFault(new InvalidOperationException("Observed driver exception.", __exception));
            if (driverDepth == 0) o.scenario.ObserveSettled("driver-boundary");
        });

        private sealed class CarryToken { internal bool outer; internal StorageDeliveryState before; }
        private static void CarryBefore(Pawn_CarryTracker __instance, out CarryToken __state)
        {
            CarryToken token = null;
            Safe(o =>
            {
                if (!ReferenceEquals(__instance, o.actor.carryTracker)) return;
                token = new CarryToken { outer = carryDepth++ == 0 };
                if (token.outer) token.before = o.scenario.BeginCarry();
            });
            __state = token;
        }
        private static void CarryFinally(CarryToken __state, Exception __exception) => Safe(o =>
        {
            if (__state == null) return;
            carryDepth--;
            if (__exception != null) o.scenario.ObserverFault(new InvalidOperationException("Observed TryStartCarry exception.", __exception));
            if (__state.outer) o.scenario.EndCarry(__state.before);
        });
        private sealed class DropToken { internal bool outer; internal StorageDeliveryScenario.DropToken before; }
        private static void DropBefore(Pawn_CarryTracker __instance, out DropToken __state)
        {
            DropToken token = null;
            Safe(o =>
            {
                if (!ReferenceEquals(__instance, o.actor.carryTracker)) return;
                token = new DropToken { outer = dropDepth++ == 0 };
                if (token.outer) token.before = o.scenario.BeginDrop();
            });
            __state = token;
        }
        // Read the native out value by value. Harmony's __args restoration can write
        // a stale pre-call array back into ref/out arguments even in a void postfix.
        private static void DropAfter(Thing resultingThing, bool __result, DropToken __state) => Safe(o =>
        {
            if (__state?.outer == true) o.scenario.EndDrop(__state.before, __result, resultingThing);
        });
        private static void DropFinally(DropToken __state, Exception __exception) => Safe(o =>
        {
            if (__state == null) return;
            dropDepth--;
            if (__exception != null) o.scenario.ObserverFault(new InvalidOperationException("Observed TryDropCarriedThing exception.", __exception));
        });

        // Identity evidence only. These can occur inside a transfer; their intermediate
        // counts are never used as conservation boundaries or summed as pickup/deposit totals.
        private sealed class SplitToken { internal StorageDeliveryThing original; internal int jobId, requested; }
        private static void SplitBefore(Thing __instance, int __0, out SplitToken __state)
        {
            SplitToken token = null;
            Safe(o =>
            {
                if ((driverDepth > 0 || carryDepth > 0 || dropDepth > 0) && o.scenario.IsSteel(__instance) && o.actor.CurJob != null)
                    token = new SplitToken { original = o.scenario.Describe(__instance), requested = __0, jobId = o.actor.CurJob.loadID };
            });
            __state = token;
        }
        private static void SplitAfter(Thing __instance, Thing __result, SplitToken __state) => Safe(o =>
        {
            if (__state != null) HarnessSession.Event("storage-delivery-split", "job=" + __state.jobId + "; requested=" + __state.requested
                + "; originalBefore=" + Json.Stringify(__state.original) + "; originalAfter=" + Json.Stringify(o.scenario.Describe(__instance))
                + "; result=" + Json.Stringify(o.scenario.Describe(__result)));
        });
        private sealed class AbsorbToken { internal StorageDeliveryThing target, source; internal int jobId; }
        private static void AbsorbBefore(Thing __instance, Thing __0, out AbsorbToken __state)
        {
            AbsorbToken token = null;
            Safe(o =>
            {
                if ((driverDepth > 0 || carryDepth > 0 || dropDepth > 0) && o.scenario.IsSteel(__instance) && o.scenario.IsSteel(__0) && o.actor.CurJob != null)
                    token = new AbsorbToken { target = o.scenario.Describe(__instance), source = o.scenario.Describe(__0), jobId = o.actor.CurJob.loadID };
            });
            __state = token;
        }
        private static void AbsorbAfter(Thing __instance, Thing __0, bool __result, AbsorbToken __state) => Safe(o =>
        {
            if (__state != null) HarnessSession.Event("storage-delivery-absorb", "job=" + __state.jobId + "; returned=" + __result
                + "; targetBefore=" + Json.Stringify(__state.target) + "; sourceBefore=" + Json.Stringify(__state.source)
                + "; targetAfter=" + Json.Stringify(o.scenario.Describe(__instance)) + "; sourceAfter=" + Json.Stringify(o.scenario.Describe(__0)));
        });
        public void Dispose()
        {
            foreach (var target in targets) harmony.Unpatch(target, HarmonyPatchType.All, Owner);
            targets.Clear();
            if (ReferenceEquals(current, this)) current = null;
            freeQueries?.Clear(); driverDepth = carryDepth = dropDepth = 0;
        }
    }
}
