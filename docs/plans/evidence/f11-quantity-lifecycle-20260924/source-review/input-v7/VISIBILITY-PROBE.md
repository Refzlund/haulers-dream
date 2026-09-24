# F11 v7: rendering prerequisite before input

Review only; no worker/source/host/controller change or native launch in this diagnosis. Preserve v7 run `fd1e1462ec5344b7b6ebcaa37bcf3551` as failed (39/41, U0).

## What the capture proves

The early native Screen/Prefs assertion passes at 1024x768, windowed, scale 1. Native Italian metadata and all three HD translations pass. `render/open-gear` then times out before any geometry or input request. The worker finishes with zero requests, while disposal diagnostics show zero Root/play event entries. No native exception appears in the 1,855-line log; process/worker/controller join normally, Default remains the input desktop, and Verify reports no protected change.

The counters only count entries with a non-null `Event.current`, because `GuiBefore` returns before recording null events. Therefore the exact observation is **no event-bearing observed UI entry and no gear repaint geometry**, not proof of every possible Unity callback invocation. There is no evidence of a lost posted click in this run: no click was requested or sent.

The controller still starts the owned native process with `-WindowStyle Hidden`. V6 allowed native preferences to choose fullscreen 2560x1440 and did render/receive the actual click. V7 correctly retains 1024x768 windowed preferences, and now no rendering is observed. Hidden-window rendering is a well-supported next hypothesis, not an established retrospective fact: the current worker enumerates its Unity HWND only after an input request, so neither this run nor its startup receipt recorded visibility, iconic state or placement.

## Primary API evidence

- Microsoft defines `SW_SHOWNOACTIVATE` (4) as displaying a window at its recent size/position without activating it. Unlike normal Show/Restore modes, it does not request activation. First-call startup information can affect ShowWindow behavior, so an attempted operation must be followed by measured state. [ShowWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow).
- `ShowWindowAsync` posts a show-state operation to the target window thread and returns whether the operation was started, not whether rendering completed. It uses the same documented show-state values. [ShowWindowAsync](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindowasync).
- `IsWindowVisible` measures WS_VISIBLE on the window and its parent chain; it can return true for an obscured window. It is not proof of actual OnGUI or pixels. [IsWindowVisible](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindowvisible).
- Unity's `Application.runInBackground` determines continued background execution; its documentation does not guarantee IMGUI rendering for a hidden Win32 window. Here actual Update/ticks progressed already. [Unity 2022.3 runInBackground](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-runInBackground.html).

These official pages were read using the agent-reach generic Jina web route. No focus API, input-desktop switch, physical input or injection was tested.

## Recommended single bounded worker probe

Use the existing v7 host and frozen product. Before input, find exactly one UnityWndClass HWND for the already bound native PID on the existing inactive HaulersDreamTests desktop. Recheck process executable, HWND ownership and owning thread desktop. Record HWND, thread, class, IsWindowVisible, IsIconic, GetWindowPlacement and client rectangle, together with current input-desktop name. This supplies the missing evidence directly.

Only if the observed target is hidden or iconic, issue **one** `ShowWindowAsync(hwnd, 4)` to that exact owned private HWND. Record acceptance separately; within a finite bound, require the same window to be visible, noniconic and the expected client size while the input desktop remains unchanged and different from the private desktop. Never use Show/Restore/SetFocus/SetForegroundWindow/WM_ACTIVATE, SwitchDesktop, SendInput or any fallback to another desktop/window. Do not change the controller's hidden start policy globally.

The existing v7 native observers then remain the rendering and input authority. A before/after visibility receipt plus actual play-UI/repaint geometry establishes whether this step resolved the prerequisite. Only the ordinary subsequent request/receipt/native-event/native-control chain may advance the UI test. If visibility was already normal, or the one no-activate operation fails to produce rendering, keep the failure and inspect the new evidence; do not try other activation modes or repeatedly relaunch the full fixture.

This requires a bounded worker adaptation and updated worker pin only; no product or host rebuild is justified by the current evidence. Root owns source review and any native probe/execution.
