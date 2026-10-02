using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    [DataContract] internal sealed class Bg03FreshCargo
    {
        [DataMember] public string origin { get; set; } = "synthetic fixture setup; no observed prior haul";
        [DataMember] public int tick { get; set; }
        [DataMember] public int milkId { get; set; }
        [DataMember] public int previousYieldTick { get; set; }
        [DataMember] public int notifiedYieldTick { get; set; }
        [DataMember] public string notificationBinding { get; set; }
    }
    [DataContract] internal sealed class Bg03UnloadGate
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public string boundary { get; set; }
        [DataMember] public int? workCall { get; set; }
        [DataMember] public bool? emergency { get; set; }
        [DataMember] public bool? returnedBoolean { get; set; }
        [DataMember] public int? candidateId { get; set; }
        [DataMember] public bool? workResultValid { get; set; }
        [DataMember] public Bg03JobState workResultJob { get; set; }
        [DataMember] public int lastYieldTick { get; set; }
        [DataMember] public int graceTicks { get; set; }
        [DataMember] public bool beforeEating { get; set; }
        [DataMember] public bool beforeSleep { get; set; }
        [DataMember] public bool beforeLeisure { get; set; }
        [DataMember] public bool rawUnloadEverything { get; set; }
        [DataMember] public Bg03JobState currentJob { get; set; }
        [DataMember] public string currentDriver { get; set; }
        [DataMember] public List<Bg03JobState> queue { get; set; }
        [DataMember] public int queueCount { get; set; }
        [DataMember] public bool queueOverflow { get; set; }
        [DataMember] public float? foodRawUnits { get; set; }
        [DataMember] public float? restRawUnits { get; set; }
        [DataMember] public float? joyRawUnits { get; set; }
        [DataMember] public string[] fullTimetable { get; set; }
        [DataMember] public Bg03ThingState initialMilk { get; set; }
        [DataMember] public bool initialMilkTagged { get; set; }
    }
    internal sealed partial class Bg03Scenario
    {
        private object unloadSettings;
        private FieldInfo yieldTickField, graceTicksField, beforeEatingField, beforeSleepField, beforeLeisureField, rawUnloadField, rawNeedField;

        private static FieldInfo RequireGateField(Type type, string name, Type valueType, bool isPublic)
        {
            var field = type.GetField(name, BindingFlags.Instance | (isPublic ? BindingFlags.Public : BindingFlags.NonPublic));
            Require("bg03p1-passive-field-" + name, field != null && field.FieldType == valueType && !field.IsStatic,
                type.FullName + "." + name + "; expected=" + valueType.FullName + "; read backing field only");
            return field;
        }
        private void InitializeFreshCargo(object settings)
        {
            unloadSettings = settings;
            yieldTickField = RequireGateField(tagComp.GetType(), "lastYieldTick", typeof(int), true);
            graceTicksField = RequireGateField(settings.GetType(), "unloadGraceTicks", typeof(int), true);
            beforeEatingField = RequireGateField(settings.GetType(), "unloadBeforeEating", typeof(bool), true);
            beforeSleepField = RequireGateField(settings.GetType(), "unloadBeforeSleep", typeof(bool), true);
            beforeLeisureField = RequireGateField(settings.GetType(), "unloadBeforeLeisure", typeof(bool), true);
            rawUnloadField = RequireGateField(typeof(Pawn_InventoryTracker), "unloadEverything", typeof(bool), false);
            rawNeedField = typeof(Need).GetField("curLevelInt", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Require("bg03p1-raw-need-binding", rawNeedField != null && rawNeedField.DeclaringType == typeof(Need)
                && rawNeedField.FieldType == typeof(float) && !rawNeedField.IsStatic, "RimWorld.Need.curLevelInt; raw units, no category/percentage/stat getter.");
            var notify = tagComp.GetType().GetMethod("NotifyYieldPicked", BindingFlags.Instance | BindingFlags.Public,
                null, Type.EmptyTypes, null);
            Require("bg03p1-fresh-pickup-binding", notify != null && notify.ReturnType == typeof(void)
                && notify.DeclaringType == tagComp.GetType(), "Actual public NotifyYieldPicked(), called once during synthetic initial setup.");
            var record = new Bg03FreshCargo { tick = Find.TickManager.TicksGame, milkId = initialMilk.thingIDNumber,
                previousYieldTick = (int)yieldTickField.GetValue(tagComp), notificationBinding = notify.DeclaringType.FullName + "." + notify.Name
                + "; token=" + notify.MetadataToken + "; mvid=" + notify.Module.ModuleVersionId };
            notify.Invoke(tagComp, null); // The only fixture clock write; normal later gameplay is never restamped.
            record.notifiedYieldTick = (int)yieldTickField.GetValue(tagComp);
            result.freshCargo = record;
            HarnessSession.Event("bg03-synthetic-fresh-cargo", Json.Stringify(record));
            Require("bg03p1-fresh-pickup-clock", record.notifiedYieldTick == record.tick,
                "Actual notification stamps the setup tick; previous=" + record.previousYieldTick + "; now=" + record.notifiedYieldTick);
        }
        internal void CaptureUnloadGate(string boundary, int? workCall = null, bool? emergency = null,
            bool? returnedBoolean = null, ThinkResult? workResult = null, int? candidateId = null)
        {
            if (!active) return;
            var record = new Bg03UnloadGate { sequence = ++sequence, tick = Find.TickManager.TicksGame, boundary = boundary,
                workCall = workCall, emergency = emergency, returnedBoolean = returnedBoolean, candidateId = candidateId,
                workResultValid = workResult?.IsValid, workResultJob = workResult.HasValue ? JobState(workResult.Value.Job) : null,
                lastYieldTick = (int)yieldTickField.GetValue(tagComp), graceTicks = (int)graceTicksField.GetValue(unloadSettings),
                beforeEating = (bool)beforeEatingField.GetValue(unloadSettings), beforeSleep = (bool)beforeSleepField.GetValue(unloadSettings),
                beforeLeisure = (bool)beforeLeisureField.GetValue(unloadSettings), rawUnloadEverything = (bool)rawUnloadField.GetValue(cook.inventory),
                currentJob = JobState(cook.CurJob), currentDriver = cook.jobs.curDriver?.GetType().FullName,
                queueCount = cook.jobs.jobQueue.Count, queueOverflow = cook.jobs.jobQueue.Count > 32,
                queue = cook.jobs.jobQueue.Take(32).Select(x => JobState(x.job)).ToList(),
                foodRawUnits = RawNeed(cook.needs.food), restRawUnits = RawNeed(cook.needs.rest), joyRawUnits = RawNeed(cook.needs.joy),
                fullTimetable = cook.timetable.times.Select(x => x?.defName).ToArray(),
                initialMilk = ThingState(initialMilk), initialMilkTagged = IsTagged(initialMilk) };
            // Do not call UnloadEverything, IsEnteringDowntime, a job giver or food category/percentage getters here.
            result.unloadGates.Add(record);
            HarnessSession.Event("bg03-unload-gate", Json.Stringify(record));
            if (record.queueOverflow) ObserverFault(new InvalidOperationException("BG03 passive queue snapshot exceeded its 32-job bound."));
        }
        private float? RawNeed(Need need)
        {
            if (need == null) return null;
            float value = (float)rawNeedField.GetValue(need);
            if (float.IsNaN(value) || float.IsInfinity(value))
            { ObserverFault(new InvalidOperationException("BG03 raw need level is nonfinite: " + need.GetType().FullName)); return null; }
            return value;
        }
        private bool InitialWorkGateObserved()
        {
            int beforeSelection = result.candidates.FirstOrDefault(x => x.native != null)?.nativeSequence ?? int.MaxValue;
            // Coverage is independent of what the gate returned or which job HD
            // selected. A captured true result/unload is behavior, not a lost hook.
            return result.unloadGates.Any(g => g.boundary == "IsEnteringDowntime-actual-return"
                && g.workCall.HasValue && g.returnedBoolean.HasValue && g.sequence < beforeSelection
                && result.unloadGates.Any(b => b.workCall == g.workCall && b.boundary == "work-result-before-hd-unload"
                    && b.sequence < g.sequence && b.workResultValid == false && b.workResultJob == null)
                && result.unloadGates.Any(a => a.workCall == g.workCall && a.boundary == "work-result-after-hd-unload"
                    && a.sequence > g.sequence && a.sequence < beforeSelection && a.workResultValid.HasValue));
        }
        private bool FreshCargoSelectionWitness()
        {
            var candidate = result.candidates.FirstOrDefault(x => x.native != null);
            if (candidate == null || !InitialMilkSelected(candidate.native) || result.freshCargo == null) return false;
            var snapshots = result.unloadGates.Where(x => x.boundary == "native-candidate-before-route" && x.candidateId == candidate.id).ToList();
            if (snapshots.Count != 1) return false;
            var snapshot = snapshots[0]; long delta = (long)snapshot.tick - snapshot.lastYieldTick;
            bool observedProtectedMiss = result.unloadGates.Any(g => g.boundary == "IsEnteringDowntime-actual-return"
                && g.workCall.HasValue && g.returnedBoolean == false && g.sequence < snapshot.sequence
                && g.lastYieldTick == result.freshCargo.notifiedYieldTick && (long)g.tick - g.lastYieldTick >= 0
                && (long)g.tick - g.lastYieldTick < g.graceTicks
                && result.unloadGates.Any(b => b.workCall == g.workCall && b.boundary == "work-result-before-hd-unload"
                    && b.sequence < g.sequence && b.workResultValid == false && b.workResultJob == null)
                && result.unloadGates.Any(a => a.workCall == g.workCall && a.boundary == "work-result-after-hd-unload"
                    && a.sequence > g.sequence && a.sequence < snapshot.sequence && a.workResultValid == false && a.workResultJob == null));
            return observedProtectedMiss && result.freshCargo.notifiedYieldTick == result.freshCargo.tick && result.freshCargo.tick == startedTick
                && snapshot.lastYieldTick == result.freshCargo.notifiedYieldTick && delta >= 0 && delta < snapshot.graceTicks
                && !snapshot.rawUnloadEverything && !snapshot.queueOverflow && snapshot.queue.All(x => x?.def != "HaulersDream_UnloadInventory")
                && snapshot.initialMilk.id == result.freshCargo.milkId && snapshot.initialMilk.count == 12
                && snapshot.initialMilk.custody == "inventory" && snapshot.initialMilkTagged
                && snapshot.fullTimetable.Length == 24 && snapshot.fullTimetable.All(x => x == "Work");
        }
    }
}
