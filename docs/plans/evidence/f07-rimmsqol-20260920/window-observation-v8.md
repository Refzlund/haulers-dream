# F07 window boundary and native menu capture, v8

The failed v7 run `86689445eb724417b5e0a4034c2ef1f6` remains unchanged. Its complete events show editor application/reset, native ordinary-hauling preservation, drafted permission, pure query, stale-action rejection and native default-option consumers passing. The retained stale before/after snapshots now prove unchanged gameplay fields and changed RNG alongside one actual `RejectInput` message; this does not retroactively establish the unrecorded v6 difference. v7 stopped at the final persistent-window comparison before productive commands. Its image was visually inspected: the selected-pawn Inspect UI and rejection message are visible, but the menu is absent. The old comparison did not emit its two lists, so its precise mismatch and the menu's actual closure cause cannot be reconstructed.

Native API evidence:

- `RimWorld.Selector.Select` changes selection. `SelectorOnGUI`, lines 167–169 in the read-only decompile, later opens `MainButtonDefOf.Inspect` when something is selected and no main tab is open. `MainTabsRoot.ToggleTab` adds that actual tab window to `WindowStack`. Capturing the comparison baseline before arena creation and selection was therefore outside the intended editor/menu ownership boundary.
- `Verse.FloatMenu` exposes `vanishIfMouseDistant`, default `true`. Its `UpdateBaseColor` can fade and close the menu when the native event pointer is distant. The initial position derives from the native pointer, which this inactive-desktop fixture does not move. This is a credible cause for the absent image, not a recorded v7 cause.
- `FloatMenu.DoWindowContents` and `WindowStack.WindowStackOnGUI` perform actual native rendering. The latter also maintains ephemeral immediate windows each Repaint. Persistent windows remain compared across frames; every unrelated window, including immediate windows, remains compared across each synchronous owned add/remove.

The Selector/MainTabsRoot decompiles are retained in `C:/Users/Arthur/AppData/Local/Temp/hd-f07-20260920/native-api/`; the previously retained FloatMenu/WindowStack bodies are in `C:/Users/Arthur/AppData/Local/Temp/haulersdream-f45-window-stack-diagnostic-author-20260920/native/`. These were read-only assembly inspections, not target execution.

The source-only correction is in `window-observation-v8.diff`, against the unchanged `src/RimmsCommand.v7.cs.txt` (`07802B3066257BDEDDEDEB9C1F45679F546FBB97653415E2AB730479C3B0D7A5`). It captures the GUI baseline at the first actual post-Selector Repaint, requires the fixture pawn's native Inspect tab, and emits pre-scene, ready and comparison window lists with object identities, native IDs, types, order and useful state. No unknown window is closed or whitelisted. Synchronous opening now also checks all unrelated identities, as closing already did.

Only the owned screenshot menu uses the public native `vanishIfMouseDistant=false` display policy. Its native calculated dimensions are centered within the actual UI; no window size, scale, pointer or input is fabricated. A small observer subclass calls the unchanged base `DoWindowContents` and records actual completed Repaints. Capture requires that same-frame observation, an open window, one enabled actual HD option and an on-screen rectangle. It remains open until the next frame confirms screenshot retention. This demonstrates native menu content under a disclosed stable display policy; it does not claim pointer-driven opening, clicking or native distance-dismissal coverage. The real discovered options, original delegates, editor lifecycle, pure-query check, productive assertions and transit/restart stages are unchanged. Future PNG review must still confirm visible content.

One fresh build completed with zero warnings/errors in 3.02 seconds. No Prepare or native execution was performed.

| Selected artifact | SHA-256 / identity |
| --- | --- |
| Current `src/RimmsCommand.cs` | `B4A0E2762CE063983303C34F83B49CB7B5B7F3F044F9D8EFE848ECA13DC353D0` |
| Unchanged `host/Bootstrap.cs` | `2533E3F6CA3056467421218D0EA43E6601188E20F1CC9FB734689A93E3F8FFBB` |
| Unchanged host project | `1B439113BC049253FD83A8CD2C94791B7C3B17B0C0E5D7317D9279EC6AA45947` |
| Actual build-v8 DLL | `A393FC78B684B59B26C94A1118ED59A003F1B99CF2FD73DF5BA14F3F38F14A70` |
| Actual build-v8 PDB | `5E0826C57DB4033247937D51D01D2A968BDEBFD67065014F6497E1AAABA57C7D` |
| Actual MVID | `93a2cbce-739b-4710-a234-ff1192161654` |

Outputs and complete build log are in `C:/Users/Arthur/AppData/Local/Temp/hd-f07-20260920/build-v8/`. The existing reflection-only metadata reader confirmed the actual DLL identity without target invocation or file mutation. The selected product/native/Harmony/RIMMS references remain those of v7, including the recovered complete RIMMS package. `build.ps1` changes only its output suffix from v7 to v8. Root can select `build-v8/Assemblies` with the existing controller; no controller, product or frozen prior run inputs were edited.
