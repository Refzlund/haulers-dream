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
    internal sealed class B1HealthyObservers : IDisposable
    {
        private const string Owner = "HaulersDream.RuntimeHarness.B1Healthy.observers";
        private static B1HealthyObservers current;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly B1HealthyScenario scenario;
        private readonly Pawn actor;
        private readonly List<MethodBase> targets = new List<MethodBase>();
        [ThreadStatic] private static Stack<B1HealthyQuery> freeQueries;
        [ThreadStatic] private static int driverDepth;
        [ThreadStatic] private static int carryDepth;
        [ThreadStatic] private static int dropDepth;
        private readonly PropertyInfo insideScan;

        internal B1HealthyObservers(B1HealthyScenario scenario, Pawn actor)
        {
            if (current != null) throw new InvalidOperationException("Storage delivery observers already installed.");
            this.scenario = scenario; this.actor = actor;
            current = this;
            freeQueries = new Stack<B1HealthyQuery>(); driverDepth = carryDepth = dropDepth = 0;
            try
            {
                var adapter = B1HealthyScenario.TypeNamed("HaulersDream.StorageCommitments");
                insideScan = AccessTools.Property(adapter, "InsideSpaceScan");
                if (insideScan == null) throw new MissingMemberException("StorageCommitments.InsideSpaceScan");
                Patch(AccessTools.Method(typeof(WorkGiver_HaulGeneral), "JobOnThing"), null, nameof(CandidateAfter));
                Patch(AccessTools.Method(adapter, "FreeUnitsFor", new[] { typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef), typeof(Thing), typeof(bool).MakeByRefType() }),
                    nameof(FreeBefore), nameof(FreeAfter), nameof(FreeFinally));
                Patch(AccessTools.Method(adapter, "IsDelivering", new[] { typeof(Pawn), typeof(Thing) }), null, nameof(DeliveringAfter));
                Patch(AccessTools.Method(adapter, "UnitsMoving", new[] { typeof(Pawn), typeof(ThingDef) }), null, nameof(LiveUnitsAfter));
                Patch(AccessTools.Method(typeof(StoreUtility), "IsGoodStoreCell"), null, nameof(GateAfter));
                Patch(AccessTools.Method(B1HealthyScenario.TypeNamed("HaulersDream.JobDriver_BulkHaul"), "DepositSwept"), nameof(PickupBefore), nameof(PickupAfter));
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
                HarnessSession.Check("fixture-l04-b1-healthy-observers", targets.Count >= 16,
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
                return new HarmonyMethod(typeof(B1HealthyObservers), name) { priority = Priority.Last, after = new[] { "giwaffed.HaulersDream" } };
            }
            targets.Add(target);
            harmony.Patch(target, Hook(prefix), Hook(postfix), null, Hook(finalizer));
            var installed = Harmony.GetPatchInfo(target);
            foreach (var name in new[] { prefix, postfix, finalizer }.Where(n => n != null))
            {
                var method = AccessTools.Method(typeof(B1HealthyObservers), name);
                if (installed == null || !installed.Prefixes.Concat(installed.Postfixes).Concat(installed.Finalizers)
                    .Any(p => p.owner == Owner && p.PatchMethod == method)) throw new InvalidOperationException("Exact healthy observer absent: " + name);
            }
            HarnessSession.Event("l04-b1-healthy-observer-bind", target.DeclaringType.FullName + "." + target.Name
                + "; token=" + target.MetadataToken + "; assembly=" + target.Module.Assembly.FullName + "; MVID=" + target.Module.ModuleVersionId);
        }
        private static void Safe(Action<B1HealthyObservers> action)
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

        private sealed class FreeToken { internal B1HealthyQuery query; internal bool pushed; }
        private static void FreeBefore(Pawn __0, ISlotGroup __1, Thing __3, out FreeToken __state)
        {
            FreeToken token = null;
            Safe(o =>
            {
                var query = o.scenario.BeginFree(__0, __1, __3);
                if (query == null) return;
                token = new FreeToken { query = query, pushed = true };
                (freeQueries ?? (freeQueries = new Stack<B1HealthyQuery>())).Push(query);
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

        private static void PickupBefore(JobDriver __instance, Thing __0, out B1HealthyScenario.PickupToken __state)
        {
            B1HealthyScenario.PickupToken token = null;
            Safe(o => token = o.scenario.BeginPickup(__instance, __0)); __state = token;
        }
        private static void PickupAfter(bool __result, B1HealthyScenario.PickupToken __state) => Safe(o => o.scenario.EndPickup(__state, __result));
        private static void CleanupBefore(Pawn_JobTracker __instance, JobCondition condition, out B1HealthyScenario.EndToken __state)
        {
            B1HealthyScenario.EndToken token = null;
            Safe(o => { if (ReferenceEquals(__instance, o.actor.jobs)) token = o.scenario.BeginCleanup(condition); }); __state = token;
        }
        private static void CleanupAfter(B1HealthyScenario.EndToken __state) => Safe(o => o.scenario.EndCleanup(__state));
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

        private sealed class CarryToken { internal bool outer; internal B1HealthyState before; }
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
        private sealed class DropToken { internal bool outer; internal B1HealthyScenario.DropToken before; }
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
        private sealed class SplitToken { internal B1HealthyThing original; internal int jobId, requested; }
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
            if (__state != null) HarnessSession.Event("l04-b1-healthy-split", "job=" + __state.jobId + "; requested=" + __state.requested
                + "; originalBefore=" + Json.Stringify(__state.original) + "; originalAfter=" + Json.Stringify(o.scenario.Describe(__instance))
                + "; result=" + Json.Stringify(o.scenario.Describe(__result)));
        });
        private sealed class AbsorbToken { internal B1HealthyThing target, source; internal int jobId; }
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
            if (__state != null) HarnessSession.Event("l04-b1-healthy-absorb", "job=" + __state.jobId + "; returned=" + __result
                + "; targetBefore=" + Json.Stringify(__state.target) + "; sourceBefore=" + Json.Stringify(__state.source)
                + "; targetAfter=" + Json.Stringify(o.scenario.Describe(__instance)) + "; sourceAfter=" + Json.Stringify(o.scenario.Describe(__0)));
        });
        public void Dispose()
        {
            Exception failure = null;
            try { foreach (var target in targets) { try { harmony.Unpatch(target, HarmonyPatchType.All, Owner); } catch (Exception error) { failure = error; } } }
            finally
            {
                targets.Clear();
                if (ReferenceEquals(current, this)) current = null;
                freeQueries?.Clear(); driverDepth = carryDepth = dropDepth = 0;
            }
            if (failure != null) throw new InvalidOperationException("Healthy observer cleanup failed; all targets attempted.", failure);
        }
    }
}
