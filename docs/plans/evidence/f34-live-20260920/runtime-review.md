# F34 live runtime review — 20 September 2026

**Accepted. Recommend closing F34's reported single-player misdesignation defect.** The retained baseline demonstrates healthy plants being marked/queued; the corrected eighteen-scene run verifies the selection/confirmation boundaries; this live run additionally demonstrates real paused preview refresh and productive native cutting while unrequested healthy plants survive. Final combined-build UI smoke and targeted Multiplayer integration remain separate obligations, not missing evidence for the original single-player report.

This review inspected the complete [result](native/result.json), all 102 [events](native/events.jsonl), the full [debug log](native/HaulersDream-debug.log), and all 1,863 [Player.log](native/Player.log) lines through diagnostic scanning and inspection of the complete non-profiling content. It used the previously reviewed [source](review.md) and [baseline/candidate boundary evidence](../f34-20260920/review.md). No run, controller, product or raw evidence was changed.

## Actual execution

Run `6b1cbb6cd7a046c9923bb767305dc1d4`, case `F34-LIVE-CUT`, used HD `32D57A64`, Core `D623F164`, host `CF760B43` and the reviewed live source `9EE2876D`. [Native outcome](native-outcome.json) records PID 3180 joined with exit 0, no timeout/error. The result contains 52 passing assertions, zero failures and zero captured Unity errors. All 102 event sequences are contiguous and identify this run/case. The first 51 assertion details exactly match the result; the final zero-error assertion receives the previously reviewed host's terminal-capture suffix.

## Paused live preview

All preview observations remain at tick 6. The fixture observes the real window's poll field; it does not invoke refresh after Setup.

| Event | Actual evidence |
|---|---|
| 51 | Original five targets A10398/B10400/I10403/E10406/H10410; deadline21.93107→22.93306; legs object `-64861332` reused. |
| 52–55 | Explicit fault injection removes B's Blight10401, preserving the same B plant and its order. At the next poll, deadline22.93306→23.43311, exactly A/I/E/H remain; legs change to557304628. |
| 56–59 | Native reinfection creates owned Blight10412 on original B10400. Deadline23.43311→23.93325 restores the original five targets; legs change to1641051272. |

This proves unchanged-leg reuse and changed-membership refresh through normal window frames. It is not a natural-cure claim. The debug log independently records route selection5→4→5.

Root visually inspected the actual [preview screenshot](native/preview.png): the real “Plan route: Cutting” dialog is rendered with Radius20, AmountAll, Smart disabled, Straight-line and Cancel/Append/Replace controls. The camera does not frame the crop garden; target identity and outcome come from native events, not that image. Confirmation was programmatic through the product's existing confirmation body. No mouse/keyboard click is claimed.

## Productive cutting before cleanup

Event 63 contains the five exact forced native jobs. Each is subsequently observed current under `RimWorld.JobDriver_PlantCut_Designated`; its original plant is destroyed and its job leaves current/queue before cleanup:

| Plant | Native job | Observed start tick | Destruction/completion tick |
|---|---|---|---|
| A10398 | 17 | 6 | 73 |
| B10400 | 18 | 76 | 127 |
| I10403 | 19 | 130 | 202 |
| E10406 | 20 | 205 | 262 |
| H10410 | 21 | 265 | 421 |

The brief `Wait_MaintainPosture` jobs between cuts are recorded explicitly; they do not substitute for any of the five cutting observations. The evidence does not observe `EndCurrentJob`'s condition, and does not claim it does.

At event 95, **before cleanup**, all five original infected plants are destroyed/unspawned. Healthy **C10402/G10409** are alive, healthy and unmarked. Healthy **D10405/F10408** are alive and retain their independent cut orders; the reference-identity guard passes at event 94. Every intervening progress snapshot agrees. Productive completion takes 415 running ticks, within the 1,800-tick bound.

Owned retirement follows at 96; speed restoration to Normal follows at 97; terminal event 98 records five starts/five completions and completed cleanup. No cleanup-failure assertion exists. The reviewed source's priority and route-preference readback guards complete without failure; individual restored priority values are not separately exported, so this is not a claim of an additional independent state census. Cleanup cannot account for the five earlier plant destructions.

## Logs and the unchanged Verify result

[verify.json](verify.json) honestly remains `not-verified`. Its manual-review marker is satisfied by this review; its missing generic `scenario-observed` marker does not mean the specific live evidence is absent. The complete specific events above exist. No event retrofit or rerun is warranted merely to satisfy that generic branch. The report also records no protected-file changes. Its later PID lookup finding reflects an already-exited process; the owned join is separately retained.

The full log contains no stack exception or shader error. Lines107–108 are two Mono fallback-library requests before harness startup; their initiating code is not identified, and this review does not declare all such messages harmless. Here the intended assemblies load with matching identities and the complete scenario succeeds, so these messages do not undermine the F34 result. Line1534 is the existing non-power-of-two `Header.png` mipmap warning. Graphics refresh/vsync timing notices are also retained. ShaderTypeDef/ErrorCheck entries are profiling labels; shutdown “Failed Allocations” entries are allocator statistics, not captured gameplay exceptions. None demonstrates healthy-plant misdesignation or failed cutting in this run.

## Closure limits

Close the reported single-player planning defect using this live evidence together with the retained failing baseline and passing eighteen-scene run. Do not claim physical button input, broad third-party compatibility, or Multiplayer success. The change adds no saved job/schema, so a broad save/restart matrix is not required for F34. Keep a focused final-build check for transmission/replay of the new `blightedOnly` command argument and the ordinary assembled-build UI smoke check. These limits do not erase the completed report-specific fix.

SHA256 identities:

- Result: `1CB4BAD7E55C9AFFC27C3C26D5E8D5C29FCD2DEEDF5EDFD3836BFDB0C1C29310`
- Events: `289E2BDAAD22BFCA7A7C02E57F46D675DBB49423D3BECABA61A8A9942BA1C135`
- Player.log: `E03431306CE02C951142AA5B582CC4D4DEDC04C1FEB86E293D90C11CAEB13F25`
- Screenshot: `9C6DDC5B67A8580DECC1C15E2776535A8C4609BABCA719863258E8FE2B7EB5B2`
- Native outcome: `769D49BDAE4C6155B591AC6447D2F1C7DB88BBE462164B24D54F2AEB234F465E`
- Verify: `B49DD538404F736E26736CD4BE07974051DACD58C9C91002D67CA97C096860AF`
