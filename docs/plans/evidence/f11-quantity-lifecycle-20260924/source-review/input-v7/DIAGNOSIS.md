# F11 v6: native click arrived; observation and viewport require correction

This is a source/evidence review, not a new native run. The original v6 result remains failed (38/40, U0). No product, active fixture, controller, selection or input-worker code was changed for this review.

## Exact observed sequence

Run `c6d7f948ce984e02a1fa2c16eaba018d` retains all 76 events and its original request/receipt under `native/c6d7f948ce984e02a1fa2c16eaba018d/raw`.

- Event 67 requests gear-drop at native pixel (398,1040), within the observed (386,1028)-(410,1052) control, at actual 2560x1440 / UI scale 1. The request binds native PID 13944 and sequence 1.
- `f11-input/receipts/0001.json` binds that exact request to owned HWND 30607626 on `HaulersDreamTests-cf62dc55eced44108852d18bc673ebaf`. Ordinary WM_MOUSEMOVE/WM_LBUTTONDOWN/WM_LBUTTONUP were posted. The input desktop remains `Default` before and after.
- Event 68 records the actual native gear `Widgets.ButtonImage` result **true** at frame 24411; event 69 records `HaulersDream.Patch_InventoryDropAmount.NativeAction` for the exact `Silver24223`, both on sequence 1.
- There are no `f11-native-input` events. After seven seconds the fixture reports that input failed to reach IMGUI, although events 68-69 prove that this click did reach the native control.

The worker's `messages-posted-only` result is correctly limited; the stronger delivery proof here comes from the actual native control/action observers. There is no justification for WM_ACTIVATE, SetFocus, physical input, desktop switching, DLL injection, or a default-desktop fallback.

## Observation boundary

The current observer patches **a prefix**, not a postfix, of `Verse.Root.OnGUI`. It emits only when `Event.current.type` is MouseDown/MouseUp/KeyDown/KeyUp, so the absence of emitted events does not prove whether that callback was entered with other event types or bypassed. The existing record contains no root invocation counters or raw event type to settle that distinction.

Installed game source (`Root.native.txt`, Assembly-CSharp SHA256 `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`) shows `Root.OnGUI` calling `uiRoot.UIRootOnGUI()` before map, tab and window controls. `Root_Play` has no OnGUI override. `RimWorld.UIRoot_Play.native.txt` shows the concrete managed override containing the gear/window dispatch. Thus there is no source basis for claiming a Root_Play override or a post-control root postfix consumed the event.

Recommended minimal observer correction: observe the exact managed `RimWorld.UIRoot_Play.UIRootOnGUI` entry before those controls, recording both immutable `type` and `rawType`, coordinates/key/character/button/frame, and the pending request identity. Add bounded entry counts/type samples so a later failure distinguishes a missing callback from a nonmatching native event. Keep the exact request, owned inactive-window receipt, and actual control/state postcondition requirements. Do not acknowledge from posting alone, synthesize an event, or call the product callback directly. A fresh run must prove the changed boundary; the current data cannot identify Unity's callback dispatch internals conclusively.

## Independently proven viewport cause

The generated private `Prefs.xml` currently omits `screenWidth`, `screenHeight`, `fullscreen` and `uiScale`; the CLI asks for 1024x768 windowed. Native `Verse.PrefsData` initializes the two dimensions to zero. Its `Apply()` calls `ResolutionUtility.SetNativeResolutionRaw()` when either is zero. Native `RimWorld.ResolutionUtility.SetNativeResolutionRaw()` chooses the largest advertised resolution and calls `SetResolutionRaw(..., !BorderlessFullscreen)`; `SetResolutionRaw` calls Unity's `Screen.SetResolution`. `Root.Update()` explicitly applies preferences after startup.

This explains why the actual request and owned HWND client area both report 2560x1440. The later screenshot oracle requiring 1024x768 would independently fail even after acknowledgement is repaired.

Recommended minimal configuration correction: include native `<screenWidth>1024</screenWidth>`, `<screenHeight>768</screenHeight>`, `<fullscreen>False</fullscreen>`, and `<uiScale>1</uiScale>` in this controller's newly generated **private** Prefs.xml. Check actual Screen dimensions, UI scale and windowed state before the first input request, and retain the final render oracle. Leave the user's preferences untouched and preserve the CLI flags and existing input-window size guard.

## Source capture notes

`Root.native.txt`, `Prefs.native.txt`, `RimWorld.UIRoot_Play.native.txt` and `RimWorld.ResolutionUtility.native.txt` were decompiled read-only from the actual installed game. Existing `f07-rimmsqol-20260920/restart-observation-review/Verse.PrefsData.cs.txt` supplies the complete native preferences implementation. Initial queries for the two latter types under namespace `Verse` did not find them; the exact definitions are under `RimWorld`. The empty initial output files are retained and are not source evidence. The installed ILSpy version notice is retained in the output; no tool update was performed.

Proceed with these bounded fixture changes only after root review; root remains the sole owner of Prepare/native execution. Do not relabel the original failure or alter the proven PostMessage transport.
