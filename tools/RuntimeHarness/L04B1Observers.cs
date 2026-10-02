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
    // No ref results, argument arrays or skipping prefixes. State is test-owned.
    internal sealed class L04B1Observers : IDisposable
    {
        private const string Owner = "HaulersDream.RuntimeHarness.L04B1.readonly";
        private static L04B1Observers current;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly List<MethodBase> targets = new List<MethodBase>();
        private readonly L04B1Scenario scene;
        [ThreadStatic] private static Stack<Token> calls;
        [ThreadStatic] private static Stack<int> notes;
        private int call;
        internal L04B1Observers(L04B1Scenario scenario)
        {
            if (current != null) throw new InvalidOperationException("B1 observers already active.");
            scene = scenario; current = this; calls = new Stack<Token>(); notes = new Stack<int>();
            try
            {
                Patch(AccessTools.Method(typeof(WorkGiver_Scanner), "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }), nameof(HasBefore), nameof(HasAfter), nameof(CallFinally));
                Patch(AccessTools.Method(typeof(WorkGiver_HaulGeneral), "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }), nameof(CandidateBefore), nameof(CandidateAfter), nameof(CallFinally));
                Patch(scene.Api.TryBuild, nameof(TryBefore), nameof(TryAfter), nameof(CallFinally));
                Patch(scene.Api.Build, nameof(BuildBefore), nameof(BuildAfter), nameof(CallFinally));
                Patch(scene.Api.Note, nameof(NoteBefore), nameof(NoteAfter), nameof(NoteFinally));
                Patch(AccessTools.Method(L04B1Bridge.TypeNamed("HaulersDream.HDLog"), "Warn", new[] { typeof(string) }), nameof(WarnBefore), null);
                Patch(AccessTools.Method(typeof(Pawn_JobTracker), "StartJob"), nameof(StartBefore), nameof(StartAfter), nameof(StartFinally));
                Patch(AccessTools.Method(typeof(JobGiver_Work), "TryIssueJobPackage", new[] { typeof(Pawn), typeof(JobIssueParams) }), nameof(WorkBefore), nameof(WorkAfter), nameof(CallFinally));
                Patch(AccessTools.Method(typeof(JobMaker), "MakeJob", Type.EmptyTypes), null, nameof(MadeAfter));
                Patch(AccessTools.Method(typeof(JobMaker), "ReturnToPool", new[] { typeof(Job) }), nameof(PoolBefore), nameof(PoolAfter));
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodBase target, string pre, string post, string fin = null)
        {
            if (target == null) throw new MissingMethodException("B1 observation target missing.");
            HarmonyMethod Hook(string name) => name == null ? null : new HarmonyMethod(typeof(L04B1Observers), name)
                { priority = Priority.Last, after = new[] { "giwaffed.HaulersDream" } };
            targets.Add(target);
            harmony.Patch(target, Hook(pre), Hook(post), null, Hook(fin));
            var installed = Harmony.GetPatchInfo(target);
            foreach (var name in new[] { pre, post, fin }.Where(n => n != null))
            {
                var method = AccessTools.Method(typeof(L04B1Observers), name);
                if (installed == null || !installed.Prefixes.Concat(installed.Postfixes).Concat(installed.Finalizers)
                    .Any(p => p.owner == Owner && p.PatchMethod == method)) throw new InvalidOperationException("Exact B1 observer hook not installed: " + name);
            }
            scene.Emit(new B1Event { kind = "observer-bind", method = target.DeclaringType.FullName + "." + target.Name,
                detail = target.Module.Assembly.FullName + ";mvid=" + target.Module.ModuleVersionId + ";token=" + target.MetadataToken });
        }
        private static void Safe(Action<L04B1Observers> body)
        {
            var o = current; if (o == null) return;
            try { body(o); } catch (Exception e) { o.scene.ObserverFault(e); }
        }
        internal sealed class Token
        {
            internal int id, parent; internal Pawn pawn; internal Thing source; internal Job input;
            internal string kind; internal bool forced, forceSweep;
        }
        private Token Begin(string kind, Pawn pawn, Thing source, bool forced = false, bool sweep = false, Job input = null)
        {
            if (!scene.IsActor(pawn)) return null;
            var token = new Token { id = ++call, parent = calls.Count == 0 ? 0 : calls.Peek().id, pawn = pawn, source = source,
                kind = kind, forced = forced, forceSweep = sweep, input = input };
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
            if (calls.Count == 0 || !ReferenceEquals(calls.Pop(), __state)) throw new InvalidOperationException("B1 call nesting mismatch.");
            if (__exception != null) o.scene.ObserverFault(__exception);
        });
        private static void NoteBefore(Thing __0, out int __state)
        {
            int token = 0; Safe(o => { if (!o.scene.IsSource(__0)) return; token = ++o.call; notes.Push(__0.thingIDNumber);
                o.scene.Emit(new B1Event { kind = "note-enter", sourceId = __0.thingIDNumber, callId = token,
                    parentCallId = calls.Count == 0 ? 0 : calls.Peek().id, counter = o.scene.Api.Counter(__0) }); }); __state = token;
        }
        private static void NoteAfter(Thing __0, int __state) => Safe(o => { if (__state != 0) o.scene.Emit(new B1Event
            { kind = "note-return", sourceId = __0.thingIDNumber, callId = __state, counter = o.scene.Api.Counter(__0) }); });
        private static void NoteFinally(int __state, Exception __exception) => Safe(o => { if (__state != 0) notes.Pop(); if (__exception != null) o.scene.ObserverFault(__exception); });
        private static void WarnBefore(string __0) => Safe(o => o.scene.Warning(__0,
            notes == null || notes.Count == 0 ? -1 : notes.Peek(),
            calls?.FirstOrDefault(t => t.source != null), calls == null || calls.Count == 0 ? 0 : calls.Peek().id));
        private static void StartBefore(Pawn_JobTracker __instance, Job newJob, out Token __state)
        {
            Token t = null; Safe(o => { var p = o.scene.ActorFor(__instance); if (p == null) return;
                t = new Token { id = ++o.call, pawn = p, input = newJob, kind = "start" };
                o.scene.StartEvent(t, false); }); __state = t;
        }
        private static void StartAfter(Token __state) => Safe(o => { if (__state != null) o.scene.StartEvent(__state, true); });
        private static void StartFinally(Token __state, Exception __exception) => Safe(o => { if (__state != null && __exception != null) o.scene.ObserverFault(__exception); });
        private static void MadeAfter(Job __result) => Safe(o => { if (calls.Count > 0 || o.scene.Borrowing) o.scene.Emit(new B1Event
            { kind = "make-job", parentCallId = calls.Count > 0 ? calls.Peek().id : 0, job = o.scene.Api.Job(__result) }); });
        private static void PoolBefore(Job __0, out B1Job __state)
        { B1Job t = null; Safe(o => { if (o.scene.Recycling) { t = o.scene.Api.Job(__0); o.scene.Emit(new B1Event { kind = "pool-enter", job = t }); } }); __state = t; }
        private static void PoolAfter(Job __0, B1Job __state) => Safe(o => { if (__state != null) o.scene.Emit(new B1Event
            { kind = "pool-return", inputJob = __state, job = o.scene.Api.Job(__0) }); });
        public void Dispose()
        {
            Exception failure = null;
            try { foreach (var t in targets) { try { harmony.Unpatch(t, HarmonyPatchType.All, Owner); } catch (Exception error) { failure = error; } } }
            finally { targets.Clear(); if (ReferenceEquals(current, this)) current = null; calls?.Clear(); notes?.Clear(); }
            if (failure != null) throw new InvalidOperationException("B1 observer cleanup failed; all target removals were attempted.", failure);
        }
    }
}
