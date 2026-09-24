# F11 v8: bounded private-window visibility probe

Ready for root's fresh Prepare/native execution. This is a worker-only change; no product, host, controller, UI oracle or lifecycle change. No native process or Windows GUI API was invoked by this author. The exact final worker diff is [source.diff](source.diff); [audit.json](audit.json) binds all selected inputs.

The preserved v7 run `fd1e1462ec5344b7b6ebcaa37bcf3551` passed the early actual 1024×768/windowed/Italian checks, then timed out waiting for gear repaint geometry before issuing input. It recorded no event-bearing Root/play-UI entry. It did not record HWND visibility/iconic state, so hidden-window rendering remains a hypothesis, not an established diagnosis. See [primary-source diagnosis](../source-review/input-v7/VISIBILITY-PROBE.md).

Before the first input request, v8 finds the unique Unity window belonging to the selected native PID on the worker's inactive private desktop. It binds that PID to the exact controller-recorded 100 ns process creation time, selected manifest and private executable. Every window observation rechecks PID/start/executable, HWND/class and owner thread's desktop. If and only if the window is hidden or iconic, it makes one `ShowWindowAsync(SW_SHOWNOACTIVATE=4)` request. It then requires the same owned window to be visible, non-iconic and 1024×768 within five seconds. Input-desktop identity must remain unchanged and different from the private desktop before and after the request and throughout observation. There is no activation, focus, input-desktop switch or alternate input route.

`evidence/f11-input/window-visibility.json` records both no-change and changed paths, exact process/window identity, before/after state, requested operation, asynchronous acceptance, input desktop, completion status and any error. A successful visibility receipt proves window state only. Actual native IMGUI event/control acknowledgements and all existing dialog/physical/save conditions remain mandatory. The original posted-message action/request/receipt tail is byte-identical to v7.

Current selection SHA-256: `D84FC32E220B3E328479BE1F709FA55591AAD80E40A1B3ACB57A2023F57B0550`.

- Worker: `2408DAE1F162FD4FA461B0B971A55D3AF245957E4F8E87DDDDF0AB52127AA431`.
- Unchanged v7 host: `B842099B666BA77FD45FC2D4C2CB170EF42146C380A5F85800367606DFD91A16`, MVID `990d6c08-baea-42ed-81ab-68cd25f6d171`.
- Unchanged controller: `4AD0ECC4B17975D9A45D45514B4201CF5A009F24CE34326A47BFC9FF2BB50D2E`.
- Unchanged runtime product: HD `15D6ECE69478A31F8EB2B16F27FA2015C69A00D079918105BB21F483B1370292`; Core `C09E2132E2B5CA6B1C45D3082AE44B4EF87B062B4FA297A9E5FBD831CB45FE19`.

The audit verifies all 737 unchanged selected pins, 85 actual reference pairs and 16 preserved v7 capture files. Python syntax and pure timestamp parsing passed without invoking `main`; exact native .NET FILETIME conversion independently matched `134347261083707173`, three malformed timestamps were rejected, and native WINDOWPLACEMENT size is 44 bytes. No host rebuild was needed. The full selected source is frozen at `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f11-quantity-lifecycle-20260924-v1/fixture-source-v8`. All prior selected sources, pins and failed capture remain under `before/` or their original native evidence path.

Root has read and accepted the complete worker diff and audit for this bounded fresh test. Runtime acceptance remains pending; this probe does not complete F11.
