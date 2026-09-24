# Observed v9 cause and the single late show decision

The failed v9 native run `036de394b7d14ecebb137b3457868797` remains 39/41, U0. Its worker samples bind the same owned PID7768/FILETIME134347288891195145/HWND18221370/Unity thread19300/private desktop throughout:

- `window-samples/0001.json`, 13:08:10.972750Z: visible, noniconic 1024x768 after the original early show.
- `0002.json`, 13:08:11.244949Z: hidden again, noniconic 1024x768.
- `0004.json`, 13:09:10.986048Z: still hidden at actual event68 `render/open-gear` (event UTC13:09:10.8902584Z).

Default remains the input desktop. No input request was made. The captured window becomes hidden after the startup show and remains hidden at the rendering prerequisite; the precise internal caller of that hide was not instrumented. The BitBlt flag did not resolve this condition. No graphics-engine or swap-chain theory is needed to justify moving the one existing show decision later.

V10 preserves V9 controller, flag, product and strict v7 host. It makes early window discovery/state capture passive, then waits for the real native `f11-ui-step` with detail `render/open-gear` in the existing event stream. Only after that record, with zero input posted, does it consume its single conditional `ShowWindowAsync(4)` decision. Exact process/window/private-desktop checks and the finite visible/noniconic1024x768 wait remain. If already visible it makes no show call. An observed earlier terminal result produces a no-show receipt and exits normally. A refused decision cannot be retried.

The visibility receipt includes the actual phase sequence/UTC/detail and before/after window/desktop observations. Passive early and subsequent state-change receipts remain. All native event, real-control, physical quantity and screenshot oracles remain unchanged; visibility is not rendering acceptance. There is no second show, focus/activation/desktop-switch operation, display-setting API, direct UI callback or fallback.
