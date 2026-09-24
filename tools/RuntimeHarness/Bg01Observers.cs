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
    // Test-only observation. No patch skips an original, changes an argument/result,
    // adds a toil, edits a job, or consumes an IEnumerable of recipe products.
    internal sealed class Bg01Observers : IDisposable
    {
        private static Bg01Observers current;
        private readonly string owner;
        private readonly Harmony harmony;
        private readonly Bg01Scenario scenario;
        private readonly Pawn cook;
        private int driverDepth, holderDepth;
        private readonly List<MethodBase> targets = new List<MethodBase>();

        internal Bg01Observers(Bg01Scenario scenario, Pawn cook)
        {
            if (current != null) throw new InvalidOperationException("An ordinary-meal fixture observer is already active.");
            this.scenario = scenario;
            this.cook = cook;
            owner = "HaulersDream.RuntimeHarness." + scenario.CaseId + ".observers";
            harmony = new Harmony(owner);
            current = this;
            try
            {
                Patch(AccessTools.Method(typeof(Filth), "ThinFilth"), nameof(FilthBefore), nameof(FilthAfter));
                Patch(AccessTools.Method(typeof(Pawn_RecordsTracker), "Increment", new[] { typeof(RecordDef) }), nameof(RecordBefore), nameof(RecordAfter));
                Patch(AccessTools.Method(typeof(Pawn_JobTracker), "CleanupCurrentJob"), nameof(CleanupBefore), nameof(CleanupAfter));
                Patch(AccessTools.Method(typeof(GenRecipe), "PostProcessProduct"), null, nameof(ProductAfter));
                if (scenario.ObserveIngredientConsumption)
                    Patch(AccessTools.Method(typeof(RecipeWorker), nameof(RecipeWorker.ConsumeIngredient),
                        new[] { typeof(Thing), typeof(RecipeDef), typeof(Map) }), nameof(ConsumptionBefore), nameof(ConsumptionAfter));
                foreach (var name in new[] { "DriverTick", "DriverTickInterval", "TryActuallyStartNextToil" })
                    Patch(AccessTools.Method(typeof(JobDriver), name), nameof(DriverBefore), null, nameof(DriverFinally));
                // Include the enclosing transfer/drop methods so inventory-to-hands
                // transfers aren't mistaken for a new pickup after Remove + TryAdd.
                foreach (var method in typeof(ThingOwner).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.DeclaringType == typeof(ThingOwner) && (m.Name == "TryTransferToContainer" || m.Name == "TryDrop")))
                    Patch(method, nameof(HolderBefore), null, nameof(HolderFinally));
                foreach (var method in typeof(ThingOwner<Thing>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.DeclaringType == typeof(ThingOwner<Thing>) && m.Name == "TryAdd"))
                    Patch(method, nameof(HolderBefore), null, nameof(HolderFinally));
                // Vanilla TryStartCarry splits inventory stock BEFORE TryAdd, so
                // the enclosing carry call is the necessary net-transfer boundary.
                foreach (var method in typeof(Pawn_CarryTracker).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "TryStartCarry"))
                    Patch(method, nameof(CarryBefore), null, nameof(HolderFinally));
                HarnessSession.Check("fixture-" + scenario.CaseId.ToLowerInvariant() + "-observers-installed",
                    targets.Count >= (scenario.ObserveIngredientConsumption ? 11 : 10),
                    "Read-only observer targets=" + targets.Count + "; owner=" + owner);
            }
            catch { Dispose(); throw; }
        }

        private void Patch(MethodBase target, string prefix, string postfix, string finalizer = null)
        {
            if (target == null) throw new MissingMethodException(scenario.CaseId + " observation target missing.");
            HarmonyMethod Hook(string name) => name == null ? null : new HarmonyMethod(typeof(Bg01Observers), name);
            harmony.Patch(target, Hook(prefix), Hook(postfix), null, Hook(finalizer));
            targets.Add(target);
            if (Harmony.GetPatchInfo(target)?.Owners.Contains(owner) != true)
                throw new InvalidOperationException(scenario.CaseId + " observer was not installed on " + target);
        }

        internal static void RequireExactPatch(MethodBase target, string kind, string owner, Type patchType, string methodName)
        {
            if (target == null || patchType == null) throw new MissingMethodException("Required BG01 product patch is missing.");
            var info = Harmony.GetPatchInfo(target);
            var patches = kind == "prefix" ? info?.Prefixes : info?.Postfixes;
            var actual = patches?.Where(p => p.owner == owner && p.PatchMethod.DeclaringType == patchType
                && p.PatchMethod.Name == methodName && p.PatchMethod.Module.Assembly == patchType.Assembly).ToList();
            bool passed = actual != null && actual.Count == 1;
            string identity = "target=" + target.DeclaringType.FullName + "." + target.Name + "; kind=" + kind
                + "; owner=" + owner + "; expectedMethod=" + patchType.FullName + "." + methodName
                + "; assembly=" + patchType.Assembly.FullName + "; path=" + patchType.Assembly.Location
                + "; moduleMvid=" + patchType.Module.ModuleVersionId + "; matches=" + (actual?.Count ?? 0);
            string id = kind == "prefix" ? "fixture-cs-native-driver-prefix" : "fixture-hd-dobill-route-postfix";
            HarnessSession.Check(id, passed, identity);
            if (!passed) throw new InvalidOperationException("Required patch identity not installed: " + identity);
        }

        private static void Safe(Action<Bg01Observers> action)
        {
            var observer = current;
            if (observer == null) return;
            try { action(observer); }
            catch (Exception error) { observer.scenario.ObserverFault(error); }
        }

        private static void FilthBefore(Filth __instance, out Bg01Scenario.CleanEvent __state)
        {
            Bg01Scenario.CleanEvent state = null;
            Safe(o => state = o.scenario.CaptureFilth(__instance));
            __state = state;
        }
        private static void FilthAfter(Filth __instance, Bg01Scenario.CleanEvent __state) =>
            Safe(o => o.scenario.FilthThinned(__instance, __state));
        private static void RecordBefore(Pawn_RecordsTracker __instance, RecordDef def, out Bg01Scenario.CleanEvent __state)
        {
            Bg01Scenario.CleanEvent state = null;
            Safe(o => { if (ReferenceEquals(__instance, o.cook.records) && def == RecordDefOf.MessesCleaned)
                state = o.scenario.CaptureCleaningIncrement(); });
            __state = state;
        }
        private static void RecordAfter(Pawn_RecordsTracker __instance, RecordDef def, Bg01Scenario.CleanEvent __state) =>
            Safe(o => { if (__state != null) o.scenario.CleaningIncremented(__state); });

        private static void CleanupBefore(Pawn_JobTracker __instance, JobCondition condition, out Bg01Scenario.EndEvent __state)
        {
            Bg01Scenario.EndEvent state = null;
            Safe(o => { if (ReferenceEquals(__instance, o.cook.jobs)) state = o.scenario.CaptureCleanup(condition); });
            __state = state;
        }
        private static void CleanupAfter(Pawn_JobTracker __instance, Bg01Scenario.EndEvent __state) =>
            Safe(o => { if (__state != null) { o.scenario.CleanupCompleted(__state); if (o.driverDepth == 0) o.scenario.ObserveSettled("cleanup-boundary"); } });

        private static void ProductAfter(Thing __result, RecipeDef recipeDef, Pawn worker) =>
            Safe(o => { if (worker == o.cook) o.scenario.ProductCreated(__result, recipeDef); });

        private static void ConsumptionBefore(RecipeWorker __instance, Thing __0, RecipeDef __1, Map __2,
            out Bg01Scenario.IngredientConsumeEvent __state)
        {
            Bg01Scenario.IngredientConsumeEvent state = null;
            Safe(o => state = o.scenario.CaptureIngredientConsumption(__instance, __0, __1, __2));
            __state = state;
        }
        private static void ConsumptionAfter(Bg01Scenario.IngredientConsumeEvent __state) =>
            Safe(o => { if (__state != null) o.scenario.IngredientConsumed(__state); });

        private static void DriverBefore(JobDriver __instance, out bool __state)
        {
            bool tracked = false;
            Safe(o => { if (__instance.pawn == o.cook) { tracked = true; o.driverDepth++; o.scenario.ObserveActualJob(); } });
            __state = tracked;
        }
        // A void finalizer observes normal/exceptional exits without suppressing or
        // replacing exceptions. It also balances nested instant-toil recursion.
        private static void DriverFinally(bool __state, Exception __exception) => Safe(o =>
        {
            if (!__state) return;
            o.driverDepth--;
            if (__exception != null) o.scenario.ObserverFault(new InvalidOperationException("Observed driver exception", __exception));
            if (o.driverDepth == 0) o.scenario.ObserveSettled("driver-boundary");
        });

        private sealed class HolderState
        {
            internal bool outer;
            internal int rice, potato, floorRice, floorPotato;
        }
        private static void HolderBefore(ThingOwner __instance, object[] __args, out HolderState __state)
        {
            HolderState state = null;
            Safe(o =>
            {
                bool relevant = o.IsCookHolder(__instance) || __args.OfType<ThingOwner>().Any(o.IsCookHolder);
                if (!relevant) return;
                state = o.BeginHolderOperation();
            });
            __state = state;
        }
        private HolderState BeginHolderOperation()
        {
            var state = new HolderState { outer = holderDepth++ == 0 };
            if (state.outer) scenario.HeldCounts(out state.rice, out state.potato, out state.floorRice, out state.floorPotato);
            return state;
        }
        private static void CarryBefore(Pawn_CarryTracker __instance, out HolderState __state)
        {
            HolderState state = null;
            Safe(o => { if (ReferenceEquals(__instance, o.cook.carryTracker)) state = o.BeginHolderOperation(); });
            __state = state;
        }
        private static void HolderFinally(HolderState __state, Exception __exception) => Safe(o =>
        {
            if (__state == null) return;
            o.holderDepth--;
            if (__exception != null) o.scenario.ObserverFault(new InvalidOperationException("Observed item-transfer exception", __exception));
            if (__state.outer)
                o.scenario.HeldOperationCompleted(__state.rice, __state.potato, __state.floorRice, __state.floorPotato);
        });
        private bool IsCookHolder(ThingOwner holder) => ReferenceEquals(holder, cook.inventory.innerContainer)
            || ReferenceEquals(holder, cook.carryTracker.GetDirectlyHeldThings());

        public void Dispose()
        {
            foreach (var target in targets) harmony.Unpatch(target, HarmonyPatchType.All, owner);
            targets.Clear();
            if (ReferenceEquals(current, this)) current = null;
        }
    }
}
