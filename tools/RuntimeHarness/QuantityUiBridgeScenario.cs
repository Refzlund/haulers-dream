using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // Unconnected native queue delivery preflight. No product dialog/row or visual acceptance.
    internal sealed class QuantityUiBridgeScenario : IDisposable
    {
        private const string ObserverPrefix = "HaulersDream.RuntimeHarness.QuantityUiBridge";
        private static QuantityUiBridgeScenario active;
        private readonly string observerId = ObserverPrefix + "." + Guid.NewGuid().ToString("N");
        private readonly Harmony observer;
        private readonly MethodInfo queue, rootOnGui;
        private readonly ProbeWindow window;
        private readonly WindowStack windows;
        private readonly TickManager ticks;
        private readonly string capturePath;
        private readonly TimeSpeed previousSpeed;
        private readonly List<UnityEngine.Event> retainedEvents = new List<UnityEngine.Event>();
        private int stage, startFrame, repaintCount, layoutCount, clickCount, successFrame, nextInvocation;
        private int stableCaptureFrames, lastCaptureFrame = -1;
        private bool requestedCapture, complete, disposed, downAcknowledged;
        private string failure, previousCaptureHash;
        private Geometry observedGeometry, gestureGeometry;
        private PlannedInput pending;
        private RootInvocation currentRoot;

        internal QuantityUiBridgeScenario(string capturePath)
        {
            if (active != null) throw new InvalidOperationException("One UI bridge fixture at a time.");
            // The caller must additionally confine this fresh path to its validated run evidence directory.
            if (string.IsNullOrWhiteSpace(capturePath) || !Path.IsPathRooted(capturePath))
                throw new ArgumentException("A fresh absolute run-owned capture path is required.", nameof(capturePath));
            this.capturePath = Path.GetFullPath(capturePath);
            if (File.Exists(this.capturePath) || Directory.Exists(this.capturePath) || !Directory.Exists(Path.GetDirectoryName(this.capturePath)))
                throw new InvalidOperationException("Preserve existing evidence and prepare the capture directory first.");
            queue = typeof(UnityEngine.Event).GetMethod("QueueEvent", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(UnityEngine.Event) }, null);
            rootOnGui = typeof(Root).GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (queue == null || queue.ReturnType != typeof(void) || !queue.IsStatic
                || (queue.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) == 0
                || rootOnGui == null || rootOnGui.ReturnType != typeof(void) || rootOnGui.IsStatic || rootOnGui.DeclaringType != typeof(Root))
                throw new MissingMethodException("Exact native UI delivery bindings unavailable.");
            windows = Find.WindowStack; ticks = Find.TickManager;
            if (windows == null || ticks == null) throw new InvalidOperationException("Initialized native window stack and ticks are required.");
            if (windows.IsOpen<ProbeWindow>()) throw new InvalidOperationException("Preserve an already registered probe window; cleanup must be resolved first.");
            previousSpeed = ticks.CurTimeSpeed;
            observer = new Harmony(observerId); startFrame = Time.frameCount; window = new ProbeWindow(this);
            try
            {
                observer.Patch(rootOnGui,
                    prefix: new HarmonyMethod(typeof(QuantityUiBridgeScenario), nameof(RootBefore)),
                    postfix: new HarmonyMethod(typeof(QuantityUiBridgeScenario), nameof(RootAfter)),
                    finalizer: new HarmonyMethod(typeof(QuantityUiBridgeScenario), nameof(RootFinally)));
                var patches = Harmony.GetPatchInfo(rootOnGui);
                if (patches == null || !patches.Prefixes.Any(p => p.owner == observerId)
                    || !patches.Postfixes.Any(p => p.owner == observerId) || !patches.Finalizers.Any(p => p.owner == observerId))
                    throw new InvalidOperationException("All three observer scopes must be installed.");
                active = this; windows.Add(window); ticks.CurTimeSpeed = TimeSpeed.Paused;
                HarnessSession.Event("quantity-ui-bridge-start", "queue=" + queue.Module.ModuleVersionId + ":" + queue.MetadataToken
                    + "; root=" + rootOnGui.Module.ModuleVersionId + ":" + rootOnGui.MetadataToken
                    + "; observer=" + observerId + "; screen=" + Screen.width + "x" + Screen.height
                    + "; scale=" + Prefs.UIScale + "; capture=" + this.capturePath
                    + "; scope=test-window-input-delivery-only; tupleCorrelationIsNotNativeEventIdentity=true");
            }
            catch
            {
                try { Dispose(); }
                catch (Exception cleanup) { RecordFault("Construction cleanup: " + cleanup); }
                throw;
            }
        }

        internal string Update()
        {
            try { return UpdateCore(); }
            catch (Exception error) { RecordFault("UI bridge update: " + error); return FinishFailure(); }
        }
        private string UpdateCore()
        {
            if (failure != null) return FinishFailure();
            if (disposed) { RecordFault("UI bridge updated after disposal."); return FinishFailure(); }
            if (complete) return "passed";
            if (Time.frameCount - startFrame > 600) return Fail("Native UI bridge exceeded 600 update frames.");
            if (!windows.IsOpen(window)) return Fail("Exact owned test window closed before completion.");
            if (gestureGeometry != null && !gestureGeometry.SameEnvironment(window))
                return Fail("Screen, UI scale or owned window changed during the frozen gesture/capture interval.");
            if (stage == 0 && repaintCount >= 3 && layoutCount >= 1 && observedGeometry != null)
            {
                if (clickCount != 0) return Fail("An unplanned button activation preceded the gesture.");
                gestureGeometry = observedGeometry;
                requestedCapture = true;
                ScreenCapture.CaptureScreenshot(capturePath);
                HarnessSession.Event("quantity-ui-capture-requested", "frame=" + Time.frameCount + "; path=" + capturePath
                    + "; screen=" + gestureGeometry.screenWidth + "x" + gestureGeometry.screenHeight + "; decodeReview=pending; visualReview=pending");
                Queue(EventType.MouseDown, 1);
            }
            else if (stage == 1 && pending.acknowledged)
            {
                if (clickCount != 0) return Fail("The native button activated before intended MouseUp.");
                downAcknowledged = true; Queue(EventType.MouseUp, 2);
            }
            else if (stage == 2 && pending.acknowledged)
            {
                if (clickCount != 1 || pending.clickInvocation == 0)
                    return Fail("Intended MouseUp did not activate the native button exactly once inside its matching Root invocation.");
                successFrame = Time.frameCount; stage = 3;
                // Keep ordinal2 frozen for any repeated native dispatch. Repeated delivery may occur;
                // another actual activation is always invalid. No new native input is queued.
            }
            if ((stage == 1 || stage == 2) && Time.frameCount - pending.sentFrame > 120)
                return Fail("Queued input did not complete the same Root/window invocation within 120 frames.");
            if (stage == 3)
            {
                if (clickCount != 1) return Fail("Unexpected duplicate native button activation.");
                if (CaptureAvailable()) { complete = true; return "passed"; }
                if (Time.frameCount - successFrame > 120) return Fail("Stable screenshot availability remains unproven.");
            }
            return null;
        }

        private void Queue(EventType type, int ordinal)
        {
            if (currentRoot != null || gestureGeometry == null || ordinal != stage + 1)
                throw new InvalidOperationException("Input must be queued from Update with one ordered outstanding specification.");
            pending = new PlannedInput(ordinal, type, gestureGeometry.center, Time.frameCount);
            stage = ordinal; // Set before crossing the native queue boundary, even if delivery is immediate.
            var e = new UnityEngine.Event { type = pending.type, button = pending.button, mousePosition = pending.point,
                delta = pending.delta, clickCount = pending.clicks, displayIndex = pending.display,
                modifiers = pending.modifiers, keyCode = pending.key, character = pending.character };
            retainedEvents.Add(e); Trace("queue", e, null, null);
            queue.Invoke(null, new object[] { e });
        }

        // Observer callbacks return void and write only their own __state. Never replace native inputs,
        // exceptions/results, Event.current, GUI state, focus or window ordering.
        private static void RootBefore(out RootInvocation __state)
        {
            __state = null; var test = active;
            if (test == null || test.disposed) return;
            try
            {
                var scope = new RootInvocation { owner = test, previous = test.currentRoot, id = ++test.nextInvocation, input = test.pending };
                __state = scope; test.currentRoot = scope;
                scope.frame = Time.frameCount;
                var e = UnityEngine.Event.current;
                if (e == null) { test.RecordFault("Root.OnGUI entered without a native current event."); return; }
                scope.matches = scope.input != null && scope.input.RelatedDispatch(e, e.mousePosition);
                test.Trace("root-before", e, scope, null);
                if (ActionInput(e) && !scope.matches) test.RecordFault("Unexpected native action input at Root.OnGUI; see its complete tuple.");
            }
            catch (Exception error) { test.RecordFault("Root prefix observation: " + error); }
        }
        private static void RootAfter(RootInvocation __state)
        {
            if (__state == null) return;
            __state.returned = true;
            try { __state.owner.Trace("root-after", UnityEngine.Event.current, __state, null); }
            catch (Exception error) { __state.owner.RecordFault("Root postfix observation: " + error); }
        }
        private static void RootFinally(RootInvocation __state, Exception __exception)
        {
            if (__state == null) return;
            var test = __state.owner;
            try
            {
                if (!ReferenceEquals(test.currentRoot, __state)) test.RecordFault("Root observer nesting lost its exact scope.");
                if (__exception != null) test.RecordFault("Native Root.OnGUI exception: " + __exception);
                if (!__state.returned) test.RecordFault("Root.OnGUI did not reach its normal observer return.");
                if (!test.disposed && test.failure == null && __state.matches && __state.windowReceived
                    && ReferenceEquals(test.pending, __state.input))
                {
                    __state.input.acknowledged = true; __state.input.deliveryCount++;
                    test.Trace("same-root-delivery-acknowledged", UnityEngine.Event.current, __state, null);
                }
            }
            catch (Exception error) { test.RecordFault("Root finalizer observation: " + error); }
            finally { test.currentRoot = test.disposed ? null : __state.previous; }
        }

        private static bool ActionInput(UnityEngine.Event e)
        {
            return ActionType(e.type) || ActionType(e.rawType);
        }
        private static bool ActionType(EventType type)
        {
            switch (type)
            {
                case EventType.MouseDown: case EventType.MouseUp: case EventType.MouseDrag:
                case EventType.KeyDown: case EventType.KeyUp: case EventType.ScrollWheel: case EventType.ContextClick:
                case EventType.DragUpdated: case EventType.DragPerform: case EventType.DragExited:
                case EventType.ValidateCommand: case EventType.ExecuteCommand: return true;
                default: return false; // Layout/Repaint and passive pointer enter/leave/move remain native.
            }
        }
        private void Draw(Rect inRect)
        {
            try { DrawCore(inRect); }
            catch (Exception error) { RecordFault("Owned native window draw: " + error); throw; }
        }
        private void DrawCore(Rect inRect)
        {
            var e = UnityEngine.Event.current;
            if (e == null) throw new InvalidOperationException("Owned window has no native current event.");
            var scope = currentRoot;
            if (scope == null) RecordFault("Owned window arrived outside an observed Root.OnGUI scope.");
            if (e.type == EventType.Layout) layoutCount++;
            if (e.type == EventType.Repaint) repaintCount++;
            var button = new Rect(30f, 65f, inRect.width - 60f, 48f);
            var geometry = new Geometry(window, button);
            if (e.type == EventType.Repaint)
            {
                observedGeometry = geometry; // This never rewrites either frozen pending input or gesture geometry.
                if (repaintCount == 1) Trace("first-repaint", e, scope, button);
            }
            if (gestureGeometry != null && !gestureGeometry.Same(geometry))
                RecordFault("Observed control geometry changed after the gesture coordinates were frozen.");
            var screenPoint = GUIUtility.GUIToScreenPoint(e.mousePosition);
            bool related = scope != null && scope.matches && ReferenceEquals(scope.input, pending)
                && pending.RelatedDispatch(e, screenPoint);
            bool candidate = related && e.type == pending.type && button.Contains(e.mousePosition) && GUI.enabled
                && gestureGeometry != null && gestureGeometry.Same(geometry);
            if (ActionInput(e))
            {
                Trace("window-before-button", e, scope, button);
                if (!related) RecordFault("Unexpected or unmatched action input in the exact owned window.");
                else if (e.type != EventType.Used && !candidate) RecordFault("Matching action did not reach the unchanged enabled target button.");
                // A native consumed/repeated dispatch is traced without being acknowledged as live input.
            }
            if (candidate) scope.windowReceived = true;
            Widgets.Label(new Rect(20f, 12f, inRect.width - 40f, 40f), "Disposable native input delivery probe");
            bool activated = Widgets.ButtonText(button, "Input probe"); // Real native GUI.Button result only.
            if (activated)
            {
                clickCount++;
                Trace("real-button-click", e, scope, button);
                if (!candidate || pending.ordinal != 2 || pending.type != EventType.MouseUp || !downAcknowledged
                    || clickCount != 1 || (stage != 2 && stage != 3))
                    RecordFault("Unplanned, early or duplicate real button activation.");
                else pending.clickInvocation = scope.id;
            }
            if (candidate || ActionInput(e)) Trace("window-after-button", e, scope, button);
        }

        private void Trace(string phase, UnityEngine.Event e, RootInvocation scope, Rect? rect)
        {
            if (e == null) throw new InvalidOperationException("Missing native event while recording " + phase);
            var spec = scope == null ? pending : scope.input;
            Vector2? screen = rect.HasValue ? GUIUtility.GUIToScreenPoint(e.mousePosition) : (Vector2?)null;
            Vector2? cornerA = rect.HasValue ? GUIUtility.GUIToScreenPoint(new Vector2(rect.Value.xMin, rect.Value.yMin)) : (Vector2?)null;
            Vector2? cornerB = rect.HasValue ? GUIUtility.GUIToScreenPoint(new Vector2(rect.Value.xMax, rect.Value.yMax)) : (Vector2?)null;
            HarnessSession.Event("quantity-ui-route", Json.Stringify(new QuantityUiBridgeEvent {
                phase = phase, type = e.type.ToString(), rawType = e.rawType.ToString(), frame = Time.frameCount,
                ordinal = spec?.ordinal ?? 0, sentFrame = spec?.sentFrame ?? -1, rootInvocation = scope?.id ?? 0, rootFrame = scope?.frame ?? -1, parentInvocation = scope?.previous?.id ?? 0,
                matchingRoot = scope?.matches ?? false, targetWindowId = window.ID, stage = stage, activations = clickCount,
                x = e.mousePosition.x, y = e.mousePosition.y, mappedScreenX = screen?.x, mappedScreenY = screen?.y,
                plannedX = spec?.point.x, plannedY = spec?.point.y, deltaX = e.delta.x, deltaY = e.delta.y,
                buttonScreenMinX = cornerA?.x, buttonScreenMinY = cornerA?.y, buttonScreenMaxX = cornerB?.x, buttonScreenMaxY = cornerB?.y,
                button = e.button, clickCount = e.clickCount, displayIndex = e.displayIndex, modifiers = (int)e.modifiers,
                key = e.keyCode.ToString(), character = e.character, keyboard = scope == null ? (int?)null : GUIUtility.keyboardControl,
                hot = scope == null ? (int?)null : GUIUtility.hotControl, rect = rect?.ToString(),
                enabled = scope == null ? (bool?)null : GUI.enabled, matrix = scope == null ? null : GUI.matrix.ToString(),
                windowRect = window.windowRect.ToString(), screenWidth = Screen.width, screenHeight = Screen.height,
                uiWidth = UI.screenWidth, uiHeight = UI.screenHeight, uiScale = Prefs.UIScale }));
        }
        private void RecordFault(string reason)
        {
            if (failure == null) failure = reason;
            try { HarnessSession.Event("quantity-ui-observer-fault", reason); }
            catch (Exception error) { failure += "\nFault evidence write: " + error; }
        }
        private string Fail(string reason) { RecordFault(reason); return FinishFailure(); }
        private string FinishFailure()
        {
            if (!complete)
            {
                complete = true;
                try { HarnessSession.Event("quantity-ui-bridge-result", "inconclusive=" + failure + "; productUiAcceptance=false; visualReview=pending"); }
                catch (Exception error) { failure += "\nResult evidence write: " + error; }
            }
            return "inconclusive";
        }

        private bool CaptureAvailable()
        {
            if (!requestedCapture || Time.frameCount == lastCaptureFrame || !File.Exists(capturePath)) return false;
            lastCaptureFrame = Time.frameCount;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(capturePath); }
            catch (IOException) { stableCaptureFrames = 0; return false; } // Native capture can still hold/write the file.
            if (bytes.Length < 33) { stableCaptureFrames = 0; return false; }
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (!signature.SequenceEqual(bytes.Take(8)) || ReadUInt32(bytes, 8) != 13
                || bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
                throw new InvalidDataException("Screenshot does not have the expected PNG/IHDR header.");
            uint width = ReadUInt32(bytes, 16), height = ReadUInt32(bytes, 20);
            if (width != gestureGeometry.screenWidth || height != gestureGeometry.screenHeight)
                throw new InvalidDataException("PNG header dimensions differ from the frozen capture screen dimensions.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            stableCaptureFrames = hash == previousCaptureHash ? stableCaptureFrames + 1 : 1; previousCaptureHash = hash;
            if (stableCaptureFrames < 3) return false;
            HarnessSession.Event("quantity-ui-bridge-result", "clicked=1; layout=" + layoutCount + "; repaint=" + repaintCount
                + "; frame=" + Time.frameCount + "; downAcknowledged=" + downAcknowledged
                + "; upDeliveryCount=" + pending.deliveryCount + "; clickRootInvocation=" + pending.clickInvocation
                + "; pngBytes=" + bytes.Length + "; pngHeaderWidth=" + width + "; pngHeaderHeight=" + height + "; pngSha256=" + hash
                + "; captureAvailability=stable-png-header; decodeReview=pending; visualReview=pending; productUiAcceptance=false");
            return true; // Delivery preflight plus capture availability, never decoded/visible/nonblank image acceptance.
        }
        private static uint ReadUInt32(byte[] bytes, int offset) => (uint)bytes[offset] << 24 | (uint)bytes[offset + 1] << 16
            | (uint)bytes[offset + 2] << 8 | bytes[offset + 3];

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(active, this)) active = null;
            currentRoot = null;
            var errors = new List<string>();
            void Attempt(string name, Action action) { try { action(); } catch (Exception error) { errors.Add(name + ": " + error); } }
            Attempt("unpatch", () => observer.Unpatch(rootOnGui, HarmonyPatchType.All, observerId));
            Attempt("close exact owned window", () => { if (windows.IsOpen(window)) window.Close(false); });
            Attempt("restore time speed", () => ticks.CurTimeSpeed = previousSpeed);
            Attempt("verify observer removal", () => { if (Harmony.GetPatchInfo(rootOnGui)?.Owners.Contains(observerId) ?? false) throw new InvalidOperationException("Owned observer remains installed."); });
            Attempt("verify owned window removal", () => { if (windows.IsOpen(window)) throw new InvalidOperationException("Owned test window remains registered."); });
            Attempt("verify time speed", () => { if (ticks.CurTimeSpeed != previousSpeed) throw new InvalidOperationException("Time speed was not restored."); });
            retainedEvents.Clear(); pending = null;
            if (errors.Count != 0) RecordFault("UI bridge cleanup: " + string.Join("\n", errors));
            HarnessSession.Check("quantity-ui-observer-removed", errors.Count == 0,
                "observer=" + observerId + "; exact owned window/time restoration attempted; " + string.Join("\n", errors));
            if (errors.Count != 0) throw new InvalidOperationException("UI bridge cleanup incomplete: " + string.Join("\n", errors));
        }

        private sealed class PlannedInput
        {
            internal readonly int ordinal, sentFrame, button = 0, clicks = 1, display = 0;
            internal readonly EventType type;
            internal readonly Vector2 point, delta = Vector2.zero;
            internal readonly EventModifiers modifiers = EventModifiers.None;
            internal readonly KeyCode key = KeyCode.None;
            internal readonly char character = '\0';
            internal bool acknowledged;
            internal int deliveryCount, clickInvocation;
            internal PlannedInput(int ordinal, EventType type, Vector2 point, int frame)
            { this.ordinal = ordinal; this.type = type; this.point = point; sentFrame = frame; }
            // Native Used repeats can be related but never acknowledge live window input or activate a button.
            internal bool RelatedDispatch(UnityEngine.Event e, Vector2 screenPoint) => (e.type == type || e.type == EventType.Used)
                && e.rawType == type && e.button == button && e.clickCount == clicks && e.displayIndex == display
                && e.modifiers == modifiers && e.keyCode == key && e.character == character
                && (e.delta - delta).sqrMagnitude < 0.0001f && (screenPoint - point).sqrMagnitude < 1f;
        }
        private sealed class RootInvocation
        {
            internal QuantityUiBridgeScenario owner;
            internal RootInvocation previous;
            internal PlannedInput input;
            internal int id, frame;
            internal bool matches, returned, windowReceived;
        }
        private sealed class Geometry
        {
            internal readonly Rect windowRect, button;
            internal readonly Vector2 center, cornerA, cornerB;
            internal readonly int screenWidth, screenHeight, uiWidth, uiHeight;
            internal readonly float scale;
            internal Geometry(Window window, Rect button)
            {
                windowRect = window.windowRect; this.button = button;
                center = GUIUtility.GUIToScreenPoint(button.center);
                cornerA = GUIUtility.GUIToScreenPoint(new Vector2(button.xMin, button.yMin));
                cornerB = GUIUtility.GUIToScreenPoint(new Vector2(button.xMax, button.yMax));
                screenWidth = Screen.width; screenHeight = Screen.height; uiWidth = UI.screenWidth; uiHeight = UI.screenHeight; scale = Prefs.UIScale;
            }
            internal bool SameEnvironment(Window window) => window.windowRect == windowRect && Screen.width == screenWidth
                && Screen.height == screenHeight && UI.screenWidth == uiWidth && UI.screenHeight == uiHeight && Prefs.UIScale == scale;
            internal bool Same(Geometry other) => other.windowRect == windowRect && other.button == button && other.center == center
                && other.cornerA == cornerA && other.cornerB == cornerB && other.screenWidth == screenWidth && other.screenHeight == screenHeight
                && other.uiWidth == uiWidth && other.uiHeight == uiHeight && other.scale == scale;
        }
        private sealed class ProbeWindow : Window
        {
            private readonly QuantityUiBridgeScenario test;
            public override Vector2 InitialSize => new Vector2(420f, 240f);
            internal ProbeWindow(QuantityUiBridgeScenario test)
            {
                this.test = test; forcePause = true; absorbInputAroundWindow = true;
                closeOnAccept = false; closeOnCancel = false; closeOnClickedOutside = false;
                doCloseX = false; doCloseButton = false; draggable = false;
            }
            public override void DoWindowContents(Rect inRect) => test.Draw(inRect);
        }
    }
    [DataContract] internal sealed class QuantityUiBridgeEvent
    {
        [DataMember] public string phase, type, rawType, key, rect, matrix, windowRect;
        [DataMember] public int frame, ordinal, sentFrame, rootInvocation, rootFrame, parentInvocation, targetWindowId, stage, activations;
        [DataMember] public int button, clickCount, displayIndex, modifiers, character, screenWidth, screenHeight, uiWidth, uiHeight;
        [DataMember] public int? keyboard, hot;
        [DataMember] public float x, y, deltaX, deltaY, uiScale;
        [DataMember] public float? mappedScreenX, mappedScreenY, plannedX, plannedY, buttonScreenMinX, buttonScreenMinY, buttonScreenMaxX, buttonScreenMaxY;
        [DataMember] public bool matchingRoot;
        [DataMember] public bool? enabled;
    }
}
