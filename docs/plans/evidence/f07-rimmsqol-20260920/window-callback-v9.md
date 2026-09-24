# F07 native window callback capture, v9

The failed v8 producer `39d4b5d92bae496196f77041782b4003` remains unchanged. Its editor lifecycle, permission query and stale-action rejection assertions passed, but it stopped before any productive command at the Root postfix's requirement that the owned menu had already rendered in that same frame. The record does not contain `LastRepaint`, callback entry/exit event types or closure state at that failure, so it does not prove which conjunct failed. No product failure or successful menu rendering is inferred from that run.

Inspection of the actual native assembly rejects the proposed derived-root ordering explanation: `Root_Play` does not override `OnGUI`. `Root.OnGUI` calls `UIRoot_Play.UIRootOnGUI`, which calls `WindowStack.WindowStackOnGUI`, before the current Root postfix. `WindowStack` calls `Window.WindowOnGUI`; that method submits `GUI.Window` with `InnerWindowOnGUI` as its callback. In the actual callback, native `Window` invokes `DoWindowContents`, ends its content group, then calls `LateWindowOnGUI`. Native `FloatMenu.DoWindowContents` only consumes MouseDown; its normal Repaint path does not call `Event.Use`. The exact Unity callback scheduling in the failed process was not recorded and is not claimed established by managed source inspection.

The source correction requests the menu screenshot from the owned menu's actual `LateWindowOnGUI` callback. It first requires that its unchanged base `FloatMenu.DoWindowContents` entered and returned as Repaint in the same frame and that the menu remains open. The same native option count, enabled state and on-screen native rectangle assertions remain. No direct render call is made. The screenshot request still completes at frame end, while the menu remains open until the next Root observation sees a nonempty image file. That retention check binds the image request to the exact captured Repaint frame; it no longer assumes another native content callback has already run during that later Root pass.

New events record actual frame, current/raw event, open state, content callback entry/exit types, content count, last completed Repaint and capture frame. The first two content callbacks, an initial Root wait (if any), the native late callback and final retention boundary are retained. These observations distinguish absent content, altered event type, delayed callback and closed window on the next actual run. A missing native Repaint still fails through the existing finite scenario bound; a closed menu fails immediately. This is a correction of observation ownership, not a relaxed rendering requirement.

All editor, query purity, stale-action gameplay, window ownership, productive command, queued permission, interruption, conservation, keep inventory and save/restart logic is unchanged. The public `vanishIfMouseDistant=false` screenshot policy remains disclosed. No OS input, native launch, Prepare, product change or ledger edit was performed.

Preserved prior source: `src/RimmsCommand.v8.cs.txt`, SHA-256 `B4A0E2762CE063983303C34F83B49CB7B5B7F3F044F9D8EFE848ECA13DC353D0`. Exact source diff: `window-callback-v9.diff`. New native decompiles are in `C:/Users/Arthur/AppData/Local/Temp/hd-f07-20260920/native-api/`: `Verse.Root.cs`, `Verse.UIRoot.cs`, `RimWorld.UIRoot_Play.cs` and `Verse.Window.cs`. They came from the installed Assembly-CSharp image; the existing Root_Play, FloatMenu and WindowStack decompiles were also read.

One fresh build completed with zero warnings/errors in 14.39 seconds. `build.ps1` selects the recovered complete Product and RIMMS packages under `LOCALAPPDATA/HaulersDreamQA/inputs/f07-20260920`, and writes a fresh `build-v9` output. The reference DLL contents are unchanged. Complete build, selected-source, product-hash and successful reflection-only metadata receipts are retained in `C:/Users/Arthur/AppData/Local/Temp/hd-f07-20260920/build-v9/`.

| Selected artifact | SHA-256 / identity |
| --- | --- |
| Current `src/RimmsCommand.cs` | `BFC129957CE0B1CF900590F54DF5BA2613FADAD8449810B5C530D6656B652A29` |
| Unchanged `host/Bootstrap.cs` | `2533E3F6CA3056467421218D0EA43E6601188E20F1CC9FB734689A93E3F8FFBB` |
| Unchanged host project | `1B439113BC049253FD83A8CD2C94791B7C3B17B0C0E5D7317D9279EC6AA45947` |
| Actual build-v9 DLL | `58B8715AED999EA238D1EA8099A7BD76543F2A14628898AEE35AD14948E053C8` |
| Actual build-v9 PDB | `B9990A42F0B4C697E72904AA5AB0B08618147BB42008AEF4D6CAD686069A7103` |
| Actual MVID | `3a8512d1-755a-4a11-b2a3-3fd13f833be5` |

Root can select `build-v9/Assemblies` in the existing controller after independent source review. Actual PNG review, productive command stages and a bound restart remain required. This build does not close F07.
