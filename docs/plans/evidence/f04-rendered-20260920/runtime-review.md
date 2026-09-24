# F04 actual rendering acceptance — 20 September 2026

**Decision: accept this run and recommend closing F04's reported feature-description clipping.** The current product source review, complete native results/events, whole-log review and all 14 actual image inspections support that conclusion. Physical button/wheel smoke on the final assembled build remains separate; this is not closure of the combined PR or unrelated feedback.

Run `3e41898ed99b4f1ebd0a0b10f7d1748c` used HD `32D57A64…`, Core `D623F164…` and fixture `57A82D19…`, built from the approved `SettingsRendered.cs` `D8F114F4…`. The retained [process outcome](native-outcome.json) records owned PID 14748 joined with exit 0, no timeout and no error. [Result](native/result.json) contains 105 unique passing assertions, zero failures and zero captured Unity errors. All 213 [events](native/events.jsonl) are contiguous and use the same run ID. The scenario stays paused at tick 6.

## Complete card and help evidence

All 55 emitted card name/blurb pairs were independently compared with the current English/Italian XML; none differed. Three active-catalogue traversals each contain the expected 18 non-Vehicles cards. Every ordinal 0–17 occurs wholly within at least one captured page in each view:

| View | Actual language/font/scale | Complete-card page ordinals |
| --- | --- | --- |
| 0 | English, Tiny, 1× | 0–5; 6–12; 11–17 |
| 1 | English, Tiny, 1.75× | 0–5; 6–12; 11–17 |
| 2 | Italian, actual Small fallback, 1.75× | 0–4; 5–11; 12–16; 13–17 |

Actual screen size was 2560×1440; logical UI was 2560×1440 at 1× and 1463×823 at 1.75×. The latter is the largest tested native-supported scale at this screen size: 2× would fall below the native 768-pixel logical-height minimum. All views use the ordinary 900×700 dialog, 414.56-pixel card width, 328.56-pixel text width and 590-pixel content viewport. Scale changes the screen space occupied by the dialog, not its local card width. Event 128 records the initial post-scale origin before recentering; it is not the screenshot position. The source waits and checks bounds after recentering, and the visual review confirms the captured window fits.

The old fixed boxes have 21 measured overflow observations: English Automatic unloading in both English views, plus the 18 Italian hub cards and Italian Vehicles helper. These are observations, not 21 distinct features. English Automatic unloading needs a 33-pixel blurb box; Italian needs 54 pixels. The new cards allocate those full heights (71/92 pixels overall), exceeding the old 20-pixel blurb/54-pixel card constraint. The disabled supplemental Vehicles card renders its real Italian text with Small fallback; it establishes text layout only, not Vehicle Framework activation or compatibility.

The actual search contains category 7, ordinal 1, “Permetti ai meccanoidi di raccogliere e trasportare”; the Italian visual review confirms the full setting label and checkbox are visible. Event 195 selects its help identity exactly once. Its complete 1,184-character body exactly matches `HaulersDream.AllowMechanoidsDesc`. Subsequent normal window frames retain it. Help begins at zero, has total height 828 inside a 566-pixel viewport, and retains the native bottom position 262 across further frames without moving the main content.

Fourteen distinct capture requests, completed-image events and retained PNG filenames agree. The [English/Vehicles visual review](english-visual-review.md) directly inspected and decoded seven images; the [Italian visual review](italian-visual-review.md) directly inspected the other seven. Both report no card-description truncation or text/toggle overlap. Adjacent pages expose rows cut by ordinary viewport boundaries. The final Italian help sentence is visibly complete through “modo di un colono.” with bottom margin. This resolves the distinction between successful geometry assertions and actual visible text.

## Cleanup and logs

The run asserts settings values unchanged before restoration. The approved cleanup then restores the snapshot and checks equality, restores language/scale/Tiny preference/view state, removes its owned windows, restores time speed and checks pre-existing non-immediate windows. Any restoration exception changes the terminal status to failed. Actual terminal event 208 reports passed after that cleanup, with no restoration failures. This is source-backed executed cleanup evidence, not a claim that a separate before/after snapshot of every UI cache was emitted. Verify reports `protectedChanges: []`.

The full 1,860-line [Player.log](native/Player.log) was scanned, including startup and shutdown, and all non-profiling content and diagnostic matches were reviewed. The two Verify candidates at lines 105–106 are Mono fallback requests before the harness startup line 108; their initiators are not established by this log. They are retained, not declared universally harmless. No subsequent settings draw, translation, shader or runtime exception accompanies them. Header.png (1499×221) and LangIcon.png (128×77) produce mipmap notices at lines 1532/1552; these concern texture loading, while the accepted images directly establish readable descriptions. Shutdown contains allocator statistics, not an observed rendering failure. The complete HD debug log contains initialization/tagger messages only.

All result assertions agree with the event assertions except the expected final error-counter detail suffix: host finalization adds “threaded capture through terminal-result boundary” while retaining zero errors and the passing state. [Verify](verify.json) remains honestly `not-verified`: it deliberately requires this manual review and log-candidate assessment. This report supplies the separate semantic decision; no original result, event, Verify file or screenshot was changed, and no rerun was needed.

Evidence SHA-256:

```text
result.json          AEE179C46DAADE7B75047A2A004102A0D6B72379CF54F77D05CAC4404E70ABE2
events.jsonl         4B1AB8706AC6B4578086AC01E6C557567F94AA8719EA72C5B01F527B28E0B8F7
Player.log           09A03A9B993D01CC47AF8332D959524CAD5FFE2CAAF4C0471F7455637D937DC8
native-outcome.json  E7F9D96CA61EA206C87C8DF7FE9B0E3062F67F9128280DE7606B38F9005574FD
verify.json          CEDC7F8F35251E6FBBFBBF877564E15C2804378D8182C2A0FF65204545A1A840
English review       BD85B4179FDA045A51582B33DC786E3159D8C5B2E0872887F84CC2B29E7C9AE0
Italian review       34F14F36BB12E5A8EC6222A27136C679E2CC41DAD3211B7F1DD5465BDE0FBA4C
```
