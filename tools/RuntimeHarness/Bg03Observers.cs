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
    // All callbacks return void. Only Harmony's private __state is written.
    // No ref __result, native out arguments, __args array, iterator enumeration,
    // job/toil edits, argument substitution, or exception replacement occurs.
    internal sealed class Bg03Observers : IDisposable
    {
        private const string Owner = "HaulersDream.RuntimeHarness.BG03P1.observers";
        private const string HdOwner = "giwaffed.HaulersDream";
        private static Bg03Observers current;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Bg03Scenario scenario;
        private readonly Pawn cook;
        private readonly List<MethodBase> targets = new List<MethodBase>();
        private int driverDepth, holderDepth, startDepth, splitDepth, mergeDepth;
        private int workCallSequence;
        private readonly List<WorkFrame> workFrames = new List<WorkFrame>();

        internal Bg03Observers(Bg03Scenario scenario, Pawn cook)
        {
            if (current != null) throw new InvalidOperationException("BG03 observer already active.");
            this.scenario = scenario; this.cook = cook; current = this;
            try
            {
                var workgiver = AccessTools.Method(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing), new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                Patch(workgiver, nameof(CandidateBefore), nameof(NativeCandidateAfter), postfixBefore: new[] { HdOwner }, priority: Priority.First);
                Patch(workgiver, null, nameof(RoutedCandidateAfter), postfixAfter: new[] { HdOwner }, priority: Priority.Last);
                var routed = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "HaulersDream")
                    .GetType("HaulersDream.Patch_WorkGiver_DoBill_InventoryRoute", true).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                var sorted = PatchProcessor.GetSortedPatchMethods(workgiver, Harmony.GetPatchInfo(workgiver).Postfixes.ToArray());
                int before = sorted.IndexOf(AccessTools.Method(typeof(Bg03Observers), nameof(NativeCandidateAfter)));
                int product = sorted.IndexOf(routed);
                int after = sorted.IndexOf(AccessTools.Method(typeof(Bg03Observers), nameof(RoutedCandidateAfter)));
                if (before < 0 || product <= before || after <= product) throw new InvalidOperationException("Actual sorted candidate observers do not bracket the HD route.");
                HarnessSession.Event("bg03-candidate-postfix-order", string.Join(" | ", sorted.Select(m => m.DeclaringType.FullName + "." + m.Name)));

                var work = AccessTools.Method(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage), new[] { typeof(Pawn), typeof(JobIssueParams) });
                var hdAssembly = routed.Module.Assembly;
                var unloadPatch = hdAssembly.GetType("HaulersDream.Patch_JobGiver_Work_OpportunisticUnload", true);
                RequireExactPatch(work, "postfix", HdOwner, unloadPatch, "Postfix");
                Patch(work, nameof(WorkBefore), nameof(WorkResultBeforeUnload), nameof(WorkFinally), postfixBefore: new[] { HdOwner }, priority: Priority.First);
                Patch(work, null, nameof(WorkResultAfterUnload), postfixAfter: new[] { HdOwner }, priority: Priority.Last);
                var workSorted = PatchProcessor.GetSortedPatchMethods(work, Harmony.GetPatchInfo(work).Postfixes.ToArray());
                int workBefore = workSorted.IndexOf(AccessTools.Method(typeof(Bg03Observers), nameof(WorkResultBeforeUnload)));
                int workProduct = workSorted.IndexOf(unloadPatch.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static));
                int workAfter = workSorted.IndexOf(AccessTools.Method(typeof(Bg03Observers), nameof(WorkResultAfterUnload)));
                if (workBefore < 0 || workProduct <= workBefore || workAfter <= workProduct)
                    throw new InvalidOperationException("Actual work-result observers do not bracket HD opportunistic unloading.");
                HarnessSession.Event("bg03-work-postfix-order", string.Join(" | ", workSorted.Select(m => m.DeclaringType.FullName + "." + m.Name)));
                var unloadType = hdAssembly.GetType("HaulersDream.OpportunisticUnload", true);
                var settingsType = hdAssembly.GetType("HaulersDream.HaulersDreamSettings", true);
                foreach (var name in new[] { "IsEnteringDowntime", "IsInDowntimeJob" })
                {
                    var method = unloadType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static, null,
                        new[] { typeof(Pawn), settingsType }, null);
                    if (method == null || method.ReturnType != typeof(bool)) throw new MissingMethodException("Actual HD downtime gate binding: " + name);
                    Patch(method, null, nameof(DowntimeReturned));
                }

                var start = typeof(Pawn_JobTracker).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Single(m => m.Name == "StartJob" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Job));
                Patch(start, nameof(StartBefore), null, nameof(StartFinally));
                Patch(AccessTools.Method(typeof(JobDriver), nameof(JobDriver.Notify_Starting)), null, nameof(ActualStartingAfter));
                foreach (var name in new[] { "DriverTick", "DriverTickInterval", "TryActuallyStartNextToil" })
                    Patch(AccessTools.Method(typeof(JobDriver), name), nameof(DriverBefore), null, nameof(DriverFinally));
                Patch(AccessTools.Method(typeof(Pawn_JobTracker), "CleanupCurrentJob"), nameof(CleanupBefore), nameof(CleanupAfter));
                Patch(AccessTools.Method(typeof(Filth), "ThinFilth"), nameof(FilthBefore), nameof(FilthAfter));
                Patch(AccessTools.Method(typeof(Pawn_RecordsTracker), "Increment", new[] { typeof(RecordDef) }), nameof(RecordBefore), nameof(RecordAfter));
                Patch(AccessTools.Method(typeof(GenRecipe), "PostProcessProduct"), null, nameof(ProductAfter));
                Patch(AccessTools.Method(typeof(RecipeWorker), nameof(RecipeWorker.ConsumeIngredient), new[] { typeof(Thing), typeof(RecipeDef), typeof(Map) }),
                    nameof(ConsumptionBefore), nameof(ConsumptionAfter));
                foreach (var method in typeof(ThingOwner).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.DeclaringType == typeof(ThingOwner)))
                {
                    if (method.Name == "TryTransferToContainer") Patch(method, nameof(TransferBefore), null, nameof(HolderFinally));
                    else if (method.Name == "TryDrop") Patch(method, nameof(HolderBefore), null, nameof(HolderFinally));
                }
                foreach (var method in typeof(ThingOwner<Thing>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.DeclaringType == typeof(ThingOwner<Thing>) && m.Name == "TryAdd"))
                    Patch(method, nameof(HolderBefore), null, nameof(HolderFinally));
                foreach (var method in typeof(Pawn_CarryTracker).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "TryStartCarry"))
                    Patch(method, nameof(CarryBefore), null, nameof(HolderFinally));
                foreach (var type in new[] { typeof(Thing), typeof(ThingWithComps) })
                {
                    Patch(type.GetMethod("SplitOff", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(int) }, null),
                        nameof(SplitBefore), nameof(SplitAfter), nameof(SplitFinally));
                    Patch(type.GetMethod("TryAbsorbStack", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(Thing), typeof(bool) }, null),
                        nameof(MergeBefore), nameof(MergeAfter), nameof(MergeFinally));
                }
                HarnessSession.Check("fixture-bg03p1-observers-installed", targets.Count >= 20,
                    "Read-only targets=" + targets.Count + "; native out parameters are never injected into observers; owner=" + Owner);
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodBase target, string prefix, string postfix, string finalizer = null,
            string[] postfixBefore = null, string[] postfixAfter = null, int priority = Priority.Normal)
        {
            if (target == null) throw new MissingMethodException("BG03 observation target missing.");
            HarmonyMethod Hook(string name)
            {
                if (name == null) return null;
                var method = AccessTools.Method(typeof(Bg03Observers), name);
                if (method.ReturnType != typeof(void) || method.GetParameters().Any(p => p.Name == "__args"
                    || (p.ParameterType.IsByRef && (p.Name != "__state" || !p.IsOut))))
                    throw new InvalidOperationException("Observer violates the read-only parameter contract: " + method);
                return new HarmonyMethod(method) { priority = priority };
            }
            var post = Hook(postfix);
            if (post != null) { post.before = postfixBefore; post.after = postfixAfter; }
            harmony.Patch(target, Hook(prefix), post, null, Hook(finalizer));
            if (!targets.Contains(target)) targets.Add(target);
            if (Harmony.GetPatchInfo(target)?.Owners.Contains(Owner) != true) throw new InvalidOperationException("Observer patch not installed.");
            HarnessSession.Event("bg03-observer-binding", target.DeclaringType.FullName + "." + target.Name + "; token=" + target.MetadataToken
                + "; mvid=" + target.Module.ModuleVersionId + "; originalOutParameters="
                + string.Join(",", target.GetParameters().Where(p => p.IsOut).Select(p => p.Name + ":" + p.ParameterType.FullName))
                + "; hooks=" + prefix + "/" + postfix + "/" + finalizer + "; writes=__state only");
        }
        internal static void RequireExactPatch(MethodBase target, string kind, string owner, Type patchType, string methodName)
        {
            if (target == null || patchType == null) throw new MissingMethodException("BG03 required product patch missing.");
            var info = Harmony.GetPatchInfo(target);
            var patches = kind == "prefix" ? info?.Prefixes : info?.Postfixes;
            var exact = patches?.Where(p => p.owner == owner && p.PatchMethod.DeclaringType == patchType
                && p.PatchMethod.Name == methodName && p.PatchMethod.Module.Assembly == patchType.Assembly).ToList();
            bool okay = exact?.Count == 1;
            string detail = target + "; " + kind + "; owner=" + owner + "; method=" + patchType.FullName + "." + methodName
                + "; assembly=" + patchType.Assembly.FullName + "; path=" + patchType.Assembly.Location + "; mvid=" + patchType.Module.ModuleVersionId;
            HarnessSession.Check("fixture-bg03p1-required-" + kind + "-" + patchType.Name, okay, detail);
            if (!okay) throw new InvalidOperationException(detail);
        }
        private static void Safe(Action<Bg03Observers> action)
        {
            var observer = current; if (observer == null || !observer.scenario.Active) return;
            try { action(observer); } catch (Exception error) { observer.scenario.ObserverFault(error); }
        }
        private bool Settled => driverDepth == 0 && holderDepth == 0 && startDepth == 0 && splitDepth == 0 && mergeDepth == 0;
        private void ObserveIfSettled(string reason) { if (Settled) scenario.ObserveSettled(reason); }
        private static void CandidateBefore(WorkGiver_DoBill __instance, Pawn pawn, Thing thing, bool forced, out Bg03Scenario.CandidateFrame __state)
        { Bg03Scenario.CandidateFrame state = null; Safe(o => state = o.scenario.BeginCandidate(__instance, pawn, thing, forced)); __state = state; }
        private static void NativeCandidateAfter(Job __result, Bg03Scenario.CandidateFrame __state) => Safe(o => o.scenario.NativeCandidate(__state, __result));
        private static void RoutedCandidateAfter(Job __result, Bg03Scenario.CandidateFrame __state) => Safe(o => o.scenario.RoutedCandidate(__state, __result));
        private sealed class WorkFrame { internal int id; internal bool emergency, afterObserved; }
        private static void WorkBefore(JobGiver_Work __instance, Pawn pawn, out WorkFrame __state)
        {
            WorkFrame frame = null;
            Safe(o =>
            {
                if (pawn != o.cook) return;
                frame = new WorkFrame { id = ++o.workCallSequence, emergency = __instance.emergency };
                o.workFrames.Add(frame);
                o.scenario.CaptureUnloadGate("work-call-entry", frame.id, frame.emergency);
            });
            __state = frame;
        }
        private static void WorkResultBeforeUnload(ThinkResult __result, WorkFrame __state) => Safe(o =>
        { if (__state != null) o.scenario.CaptureUnloadGate("work-result-before-hd-unload", __state.id, __state.emergency, workResult: __result); });
        private static void WorkResultAfterUnload(ThinkResult __result, WorkFrame __state) => Safe(o =>
        {
            if (__state == null) return;
            __state.afterObserved = true;
            o.scenario.CaptureUnloadGate("work-result-after-hd-unload", __state.id, __state.emergency, workResult: __result);
        });
        private static void WorkFinally(WorkFrame __state, Exception __exception) => Safe(o =>
        {
            if (__state == null) return;
            if (o.workFrames.LastOrDefault() != __state || !__state.afterObserved)
                o.scenario.ObserverFault(new InvalidOperationException("Incomplete or improperly nested actual work-result observation."));
            o.workFrames.Remove(__state);
            if (__exception != null) o.scenario.ObserverFault(__exception);
        });
        private static void DowntimeReturned(Pawn pawn, bool __result, MethodBase __originalMethod) => Safe(o =>
        {
            if (pawn != o.cook) return;
            var frame = o.workFrames.LastOrDefault();
            o.scenario.CaptureUnloadGate(__originalMethod.Name + "-actual-return", frame?.id, frame?.emergency, __result);
        });
        private static void StartBefore(Pawn_JobTracker __instance, Job newJob, JobCondition lastJobEndCondition, out bool __state)
        {
            bool tracked = false;
            Safe(o => { if (ReferenceEquals(__instance, o.cook.jobs)) { tracked = true; o.startDepth++; o.scenario.CaptureStart(newJob, lastJobEndCondition); } });
            __state = tracked;
        }
        private static void StartFinally(bool __state, Exception __exception) => Safe(o =>
        { if (__state) { o.startDepth--; if (__exception != null) o.scenario.ObserverFault(__exception); o.ObserveIfSettled("startjob-boundary"); } });
        private static void ActualStartingAfter(JobDriver __instance) => Safe(o => { if (__instance.pawn == o.cook) o.scenario.ActualStarting(__instance); });
        private static void DriverBefore(JobDriver __instance, out bool __state)
        {
            bool tracked = false;
            Safe(o => { if (__instance.pawn == o.cook) { o.ObserveIfSettled("before-driver-boundary"); tracked = true; o.driverDepth++; o.scenario.ObserveActualJob(); } });
            __state = tracked;
        }
        private static void DriverFinally(bool __state, Exception __exception) => Safe(o =>
        { if (__state) { o.driverDepth--; if (__exception != null) o.scenario.ObserverFault(__exception); o.ObserveIfSettled("after-driver-boundary"); } });
        private sealed class HolderFrame { internal bool tracked, outer; internal Bg03Scenario.HeldFrame snapshot; }
        private void BeginHolder(HolderFrame frame, string reason)
        {
            frame.tracked = true; frame.outer = holderDepth++ == 0;
            if (frame.outer) frame.snapshot = scenario.BeginHeld(reason);
        }
        private void EndHolder(HolderFrame frame, Exception error)
        {
            if (frame == null || !frame.tracked) return;
            holderDepth--;
            if (error != null) scenario.ObserverFault(error);
            if (frame.outer && frame.snapshot != null) scenario.EndHeld(frame.snapshot);
            ObserveIfSettled("holder-boundary");
        }
        private bool CookHolder(ThingOwner owner) => ReferenceEquals(owner, cook.inventory.innerContainer) || ReferenceEquals(owner, cook.carryTracker.GetDirectlyHeldThings());
        private static void HolderBefore(ThingOwner __instance, MethodBase __originalMethod, out HolderFrame __state)
        {
            var state = new HolderFrame(); Safe(o => { if (o.CookHolder(__instance)) o.BeginHolder(state, __originalMethod.Name); }); __state = state;
        }
        private static void TransferBefore(ThingOwner __instance, ThingOwner otherContainer, MethodBase __originalMethod, out HolderFrame __state)
        {
            var state = new HolderFrame(); Safe(o => { if (o.CookHolder(__instance) || o.CookHolder(otherContainer)) o.BeginHolder(state, __originalMethod.Name); }); __state = state;
        }
        private static void CarryBefore(Pawn_CarryTracker __instance, MethodBase __originalMethod, out HolderFrame __state)
        {
            var state = new HolderFrame(); Safe(o => { if (ReferenceEquals(__instance, o.cook.carryTracker)) o.BeginHolder(state, __originalMethod.Name); }); __state = state;
        }
        private static void HolderFinally(HolderFrame __state, Exception __exception) => Safe(o => o.EndHolder(__state, __exception));
        private sealed class SplitFrame
        { internal bool tracked, outer; internal Thing source, result; internal int requested; internal Bg03ThingState before; internal string method; }
        private static void SplitBefore(Thing __instance, int count, MethodBase __originalMethod, out SplitFrame __state)
        {
            var state = new SplitFrame();
            Safe(o => { if (o.scenario.Relevant(__instance)) { state.tracked = true; state.outer = o.splitDepth++ == 0;
                state.source = __instance; state.requested = count; state.before = o.scenario.ThingState(__instance); state.method = __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name; } });
            __state = state;
        }
        private static void SplitAfter(Thing __result, SplitFrame __state) { if (__state != null) __state.result = __result; }
        private static void SplitFinally(SplitFrame __state, Exception __exception) => Safe(o =>
        {
            if (__state == null || !__state.tracked) return;
            o.splitDepth--;
            if (__state.outer) o.scenario.SplitObserved(__state.before, __state.source, __state.result, __state.requested, __state.method, __exception);
            // SplitOff can leave a detached Thing before TryAdd. Its finalizer is
            // diagnostic ancestry, never a settled physical conservation boundary.
        });
        private sealed class MergeFrame
        { internal bool tracked, outer; internal Thing target, source; internal Bg03ThingState targetBefore, sourceBefore; internal bool? result; internal string method; internal HolderFrame held; }
        private static void MergeBefore(Thing __instance, Thing other, MethodBase __originalMethod, out MergeFrame __state)
        {
            var state = new MergeFrame();
            Safe(o =>
            {
                if (!o.scenario.Relevant(__instance) && !o.scenario.Relevant(other)) return;
                state.tracked = true; state.outer = o.mergeDepth++ == 0; state.target = __instance; state.source = other;
                state.targetBefore = o.scenario.ThingState(__instance); state.sourceBefore = o.scenario.ThingState(other);
                state.method = __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name;
                if (state.outer && (ReferenceEquals(__instance.ParentHolder, o.cook.inventory) || ReferenceEquals(other?.ParentHolder, o.cook.inventory)
                    || ReferenceEquals(__instance.ParentHolder, o.cook.carryTracker) || ReferenceEquals(other?.ParentHolder, o.cook.carryTracker)))
                { state.held = new HolderFrame(); o.BeginHolder(state.held, "TryAbsorbStack"); }
            });
            __state = state;
        }
        private static void MergeAfter(bool __result, MergeFrame __state) { if (__state != null) __state.result = __result; }
        private static void MergeFinally(MergeFrame __state, Exception __exception) => Safe(o =>
        {
            if (__state == null || !__state.tracked) return;
            o.mergeDepth--;
            if (__state.outer)
            {
                o.scenario.MergeObserved(__state.targetBefore, __state.sourceBefore, __state.target, __state.source, __state.result, __state.method, __exception);
                o.EndHolder(__state.held, __exception);
            }
        });
        private static void FilthBefore(Filth __instance, out Bg03Scenario.CleanFrame __state)
        { Bg03Scenario.CleanFrame state = null; Safe(o => state = o.scenario.CaptureFilth(__instance, "filth-removal")); __state = state; }
        private static void FilthAfter(Bg03Scenario.CleanFrame __state) => Safe(o => o.scenario.FilthThinned(__state));
        private static void RecordBefore(Pawn_RecordsTracker __instance, RecordDef def, out Bg03Scenario.CleanFrame __state)
        { Bg03Scenario.CleanFrame state = null; Safe(o => { if (ReferenceEquals(__instance, o.cook.records) && def == RecordDefOf.MessesCleaned) state = o.scenario.CaptureCleaningIncrement(); }); __state = state; }
        private static void RecordAfter(Bg03Scenario.CleanFrame __state) => Safe(o => o.scenario.CleaningIncremented(__state));
        private static void CleanupBefore(Pawn_JobTracker __instance, JobCondition condition, out Bg03Scenario.EndFrame __state)
        { Bg03Scenario.EndFrame state = null; Safe(o => { if (ReferenceEquals(__instance, o.cook.jobs)) state = o.scenario.CaptureCleanup(condition); }); __state = state; }
        private static void CleanupAfter(Bg03Scenario.EndFrame __state) => Safe(o => { o.scenario.CleanupCompleted(__state); o.ObserveIfSettled("cleanup-boundary"); });
        private static void ProductAfter(Thing __result, RecipeDef recipeDef, Pawn worker) => Safe(o => { if (worker == o.cook) o.scenario.ProductCreated(__result, recipeDef); });
        private static void ConsumptionBefore(RecipeWorker __instance, Thing __0, RecipeDef __1, Map __2, out Bg03Scenario.ConsumeFrame __state)
        { Bg03Scenario.ConsumeFrame state = null; Safe(o => state = o.scenario.CaptureConsumption(__instance, __0, __1, __2)); __state = state; }
        private static void ConsumptionAfter(Bg03Scenario.ConsumeFrame __state) => Safe(o => o.scenario.Consumed(__state));
        public void Dispose()
        {
            foreach (var target in targets) harmony.Unpatch(target, HarmonyPatchType.All, Owner);
            targets.Clear(); if (ReferenceEquals(current, this)) current = null;
        }
    }
}
