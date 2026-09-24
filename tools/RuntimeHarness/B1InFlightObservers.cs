using System;
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
    // A separate observer owner keeps the accepted L04-B1 coordinator untouched.
    // No argument arrays, ref results, skipped originals, or game-state writes.
    internal sealed class B1InFlightObservers : IDisposable
    {
        private const string Owner = "HaulersDream.RuntimeHarness.L04B1D2.readonly";
        private static B1InFlightObservers current;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly List<MethodBase> targets = new List<MethodBase>();
        private readonly B1InFlightScenario scene;
        private readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        private int unexpectedWorker;
        [ThreadStatic] private static Stack<Token> calls;
        [ThreadStatic] private static Stack<int> notes;
        private int nextCall;

        internal B1InFlightObservers(B1InFlightScenario scene)
        {
            if (current != null) throw new InvalidOperationException("D2 observers already active.");
            this.scene = scene; current = this;
            calls = new Stack<Token>(); notes = new Stack<int>();
            try
            {
                Patch(AccessTools.Method(typeof(WorkGiver_Scanner), "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }), nameof(HasBefore), nameof(HasAfter), nameof(CallFinally));
                Patch(AccessTools.Method(typeof(WorkGiver_HaulGeneral), "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }), nameof(CandidateBefore), nameof(CandidateAfter), nameof(CallFinally));
                Patch(scene.Api.TryBuild, nameof(TryBefore), nameof(TryAfter), nameof(CallFinally));
                Patch(scene.Api.Build, nameof(BuildBefore), nameof(BuildAfter), nameof(CallFinally));
                Patch(scene.Api.Note, nameof(NoteBefore), nameof(NoteAfter), nameof(NoteFinally));
                Patch(AccessTools.Method(L04B1Bridge.TypeNamed("HaulersDream.HDLog"), "Warn", new[] { typeof(string) }), nameof(WarnBefore), null);
                Patch(AccessTools.Method(typeof(JobGiver_Work), "TryIssueJobPackage", new[] { typeof(Pawn), typeof(JobIssueParams) }), nameof(WorkBefore), nameof(WorkAfter), nameof(CallFinally));
                Patch(AccessTools.Method(typeof(Pawn_JobTracker), "StartJob"), nameof(StartBefore), nameof(StartAfter), nameof(StartFinally));
                Patch(AccessTools.Method(typeof(JobMaker), "MakeJob", Type.EmptyTypes), null, nameof(MadeAfter));
                Patch(AccessTools.Method(typeof(ReservationManager), "CanReserve", new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(int), typeof(int), typeof(ReservationLayerDef), typeof(bool) }), null, nameof(ReserveAfter));
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodBase target, string prefix, string postfix, string finalizer = null)
        {
            if (target == null) throw new MissingMethodException("D2 observer target missing.");
            HarmonyMethod Hook(string name) => name == null ? null : new HarmonyMethod(typeof(B1InFlightObservers), name)
                { priority = Priority.Last, after = new[] { "giwaffed.HaulersDream" } };
            targets.Add(target); harmony.Patch(target, Hook(prefix), Hook(postfix), null, Hook(finalizer));
            var installed = Harmony.GetPatchInfo(target);
            foreach (var name in new[] { prefix, postfix, finalizer }.Where(n => n != null))
            {
                var method = AccessTools.Method(typeof(B1InFlightObservers), name);
                if (installed == null || !installed.Prefixes.Concat(installed.Postfixes).Concat(installed.Finalizers)
                    .Any(p => p.owner == Owner && p.PatchMethod == method)) throw new InvalidOperationException("Exact D2 observer absent: " + name);
            }
            scene.EmitCall(new B1Event { kind = "observer-bind", method = target.DeclaringType.FullName + "." + target.Name,
                detail = target.Module.Assembly.FullName + ";mvid=" + target.Module.ModuleVersionId + ";token=" + target.MetadataToken });
        }
        private static void Safe(Action<B1InFlightObservers> action)
        {
            var owner = current; if (owner == null) return;
            // This fixture supports only the actual main-thread simulation. Never
            // touch Verse state or thread-static stacks on a worker callback.
            int thread = Thread.CurrentThread.ManagedThreadId;
            if (thread != owner.mainThread) { Interlocked.CompareExchange(ref owner.unexpectedWorker, thread, 0); return; }
            try { action(owner); } catch (Exception error) { owner.scene.ObserverFault(error); }
        }
        internal void DrainThreadFault()
        {
            int worker = Interlocked.Exchange(ref unexpectedWorker, 0);
            if (worker != 0) scene.ObserverFault(new InvalidOperationException("D2 observer callback on unsupported worker " + worker + "; main=" + mainThread));
        }
        internal sealed class Token
        {
            internal int id, parent;
            internal string kind;
            internal Pawn pawn;
            internal Thing source;
            internal Job input;
            internal bool forced, sweep;
        }
        private Token Begin(string kind, Pawn pawn, Thing source, bool forced = false, bool sweep = false, Job input = null)
        {
            if (!scene.IsActor(pawn)) return null;
            var token = new Token { id = ++nextCall, parent = calls.Count == 0 ? 0 : calls.Peek().id,
                kind = kind, pawn = pawn, source = source, forced = forced, sweep = sweep, input = input };
            calls.Push(token); scene.ObservedCall(token, "enter", null, null); return token;
        }
        private static void HasBefore(WorkGiver_Scanner __instance, Pawn __0, Thing __1, bool __2, out Token __state)
        { Token t = null; Safe(o => { if (__instance is WorkGiver_HaulGeneral) t = o.Begin("has", __0, __1, __2); }); __state = t; }
        private static void HasAfter(bool __result, Token __state) => Safe(o => { if (__state != null) o.scene.ObservedCall(__state, "return", null, __result); });
        private static void CandidateBefore(Pawn __0, Thing __1, bool __2, out Token __state)
        { Token t = null; Safe(o => t = o.Begin("candidate", __0, __1, __2)); __state = t; }
        private static void CandidateAfter(Job __result, Token __state) => Safe(o => { if (__state != null) o.scene.ObservedCall(__state, "return", __result, __result != null); });
        private static void TryBefore(Pawn __0, Thing __1, Job __2, bool __3, bool __4, out Token __state)
        { Token t = null; Safe(o => t = o.Begin("try-build", __0, __1, __3, __4, __2)); __state = t; }
        private static void TryAfter(Job __result, Token __state) => Safe(o => { if (__state != null) o.scene.ObservedCall(__state, "return", __result, __result != null); });
        private static void BuildBefore(Pawn __0, Thing __1, Job __2, bool __3, bool __4, out Token __state)
        { Token t = null; Safe(o => t = o.Begin("build", __0, __1, __3, __4, __2)); __state = t; }
        private static void BuildAfter(Job __result, Token __state) => Safe(o => { if (__state != null) o.scene.ObservedCall(__state, "return", __result, __result != null); });
        private static void WorkBefore(Pawn __0, out Token __state)
        { Token t = null; Safe(o => t = o.Begin("native-work", __0, null)); __state = t; }
        private static void WorkAfter(ThinkResult __result, Token __state) => Safe(o => { if (__state != null) o.scene.ObservedCall(__state, "return", __result.Job, __result.Job != null); });
        private static void CallFinally(Token __state, Exception __exception) => Safe(o =>
        {
            if (__state == null) return;
            if (calls.Count == 0 || !ReferenceEquals(calls.Pop(), __state)) throw new InvalidOperationException("D2 native call nesting mismatch.");
            if (__exception != null) o.scene.ObserverFault(__exception);
        });
        private static void NoteBefore(Thing __0, out int __state)
        {
            int token = 0; Safe(o => { if (!o.scene.IsSource(__0)) return; token = ++o.nextCall; notes.Push(__0.thingIDNumber);
                o.scene.EmitCall(new B1Event { kind = "note-enter", callId = token, sourceId = __0.thingIDNumber,
                    parentCallId = calls.Count == 0 ? 0 : calls.Peek().id, counter = o.scene.Api.Counter(__0) }); }); __state = token;
        }
        private static void NoteAfter(Thing __0, int __state) => Safe(o => { if (__state != 0) o.scene.EmitCall(new B1Event
            { kind = "note-return", callId = __state, sourceId = __0.thingIDNumber, counter = o.scene.Api.Counter(__0) }); });
        private static void NoteFinally(int __state, Exception __exception) => Safe(o =>
        { if (__state != 0) notes.Pop(); if (__exception != null) o.scene.ObserverFault(__exception); });
        private static void WarnBefore(string __0) => Safe(o => o.scene.Warning(__0, notes.Count == 0 ? -1 : notes.Peek(),
            calls.FirstOrDefault(t => t.source != null), calls.Count == 0 ? 0 : calls.Peek().id));
        private static void StartBefore(Pawn_JobTracker __instance, Job newJob, out Token __state)
        {
            Token token = null; Safe(o => { var pawn = o.scene.ActorFor(__instance); if (pawn == null) return;
                token = new Token { id = ++o.nextCall, kind = "start", pawn = pawn, input = newJob }; o.scene.StartEvent(token, false); }); __state = token;
        }
        private static void StartAfter(Token __state) => Safe(o => { if (__state != null) o.scene.StartEvent(__state, true); });
        private static void StartFinally(Token __state, Exception __exception) => Safe(o => { if (__state != null && __exception != null) o.scene.ObserverFault(__exception); });
        private static void MadeAfter(Job __result) => Safe(o => { if (calls.Count > 0) o.scene.EmitCall(new B1Event
            { kind = "make-job", parentCallId = calls.Peek().id, job = o.scene.Api.Job(__result) }); });
        private static void ReserveAfter(Pawn __0, LocalTargetInfo __1, int __2, int __3, ReservationLayerDef __4, bool __5, bool __result) => Safe(o =>
        {
            if (o.scene.IsActor(__0) && __1.HasThing && o.scene.IsSource(__1.Thing))
                o.scene.ReservationGate(new B1InFlightReservationGate { actorId = __0.thingIDNumber, sourceId = __1.Thing.thingIDNumber,
                    parentCallId = calls.Count == 0 ? 0 : calls.Peek().id, maxPawns = __2, stackCount = __3,
                    layer = __4?.defName, ignoreOtherReservations = __5, returned = __result });
        });
        public void Dispose()
        {
            Exception failure = null;
            foreach (var target in targets) { try { harmony.Unpatch(target, HarmonyPatchType.All, Owner); } catch (Exception error) { failure = error; } }
            targets.Clear(); if (ReferenceEquals(current, this)) { current = null; calls?.Clear(); notes?.Clear(); }
            DrainThreadFault();
            if (failure != null) scene.ObserverFault(failure);
        }
    }
}
