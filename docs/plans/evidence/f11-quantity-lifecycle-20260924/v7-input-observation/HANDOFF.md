# F11 v7 — ready for root source review and fresh native capture

The v6 native click reached the actual gear control, but its Root observer supplied no matching event acknowledgement. The private preferences also allowed RimWorld to replace the requested 1024x768 window with 2560x1440. See `../source-review/input-v7/DIAGNOSIS.md` for the exact request, receipt, events 67-69, and installed native source. The original run remains failed; all 18 retained capture files and every v6 selected fixture/controller/worker source have preserved hashes or copies under `before/`. The old host remains untouched.

`source.diff` changes exactly two fixture sources and four fields in this fixture's private Prefs.xml generator:

- Keep `Root.OnGUI` as a diagnostic observer; add the concrete managed `RimWorld.UIRoot_Play.UIRootOnGUI` prefix before the native gear/window dispatch. Capture an immutable native event tuple including both `type` and `rawType`. Count entries by boundary/type/rawType and emit each distinct tuple's first sample; emit total counts at each acknowledgement and disposal. This will distinguish an unentered callback from used/nonmatching events without assuming Unity's internal dispatch behavior.
- Only the managed play-UI entry can acknowledge. Matching still requires actual **type** MouseUp at the requested coordinates/button, or KeyDown with the requested key/character. Used/raw-only events are recorded but cannot acknowledge. The exact owned-window request/receipt, private desktop unchanged, native control/state postcondition, and finite timeout requirements remain intact. No direct callback, Event mutation, focus operation, or input transport change.
- Generate native `screenWidth=1024`, `screenHeight=768`, `fullscreen=False`, `uiScale=1` in the private Prefs file. A new assertion verifies both live Screen state and native Prefs state before any input. The final 1024x768 translated dialog screenshot assertion remains.

The candidate product, input worker, launcher, genuine pickup/tag/Keep/drop/save/load/unload lifecycle, every invalid/cancel/stale control, and save/physical oracles are unchanged. This is fixture readiness, not F11 runtime acceptance.

## Exact current inputs

- Selection: `../selection.json`, SHA256 `EEF3D32D168C4FBBB8B04EAF106AECA5AFD861732813E35870D49321A77D7280`.
- Host: `C:\Users\Arthur\AppData\Local\HaulersDreamQA\inputs\f11-quantity-lifecycle-20260924-v1\host-build-v7\Assemblies\HaulersDream.RuntimeHarness.dll`, SHA256 `B842099B666BA77FD45FC2D4C2CB170EF42146C380A5F85800367606DFD91A16`, MVID `990d6c08-baea-42ed-81ab-68cd25f6d171`.
- Controller: `../controller/scripts/runtime-test.ps1`, SHA256 `4AD0ECC4B17975D9A45D45514B4201CF5A009F24CE34326A47BFC9FF2BB50D2E`.
- Frozen product remains the exact v5 runtime-only tree selected previously; HD SHA256 `15D6ECE69478A31F8EB2B16F27FA2015C69A00D079918105BB21F483B1370292`, Core `C09E2132E2B5CA6B1C45D3082AE44B4EF87B062B4FA297A9E5FBD831CB45FE19`.

Build: 7.09 seconds, zero warnings/errors. Fresh PS5 metadata reader passes; controller parser reports zero errors. `audit.json` verifies 627 unchanged selected pins, all 85 actual reference source/copy pairs, all 18 failed capture files, complete v6 source preservation, and exact v7 compiled/source snapshot. Controller comparison proves removal of the four added XML fields reproduces the entire old controller exactly. No Prepare/native execution by the author.

Root should review the diff and run a fresh producer with these exact inputs. The initial request becomes a bounded proof of the revised observer; its native control/action and matched event must agree before later UI/lifecycle steps can proceed. Existing failed or unlaunched runs retain their original identities and are not reused with this new host/controller.
