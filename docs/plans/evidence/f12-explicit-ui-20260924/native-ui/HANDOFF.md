# F12 human UI native handoff

Ready for independent source review. No Prepare or native run occurred. The frozen UIv2 product remains HD `64D2B8A20A1F42D079EC643F0EA14414B62B6BAAA63CBC07E996D4F94B0B7602` / Core `2633530022F02B73D09A11254E088E6DE52D6F1B05A30C2E0233790B642BA4AA`; no product or backend test inputs changed.

This adapts the accepted F11 private-window transport and read-only native control observation. It adds ordinary right-button messages and a Shift-down/mouse-click/Shift-up sequence. QueueOrder must be observed at the actual option invocation and retained in the actual amount dialog and authoritative command; a posted-message receipt alone cannot pass it. No activation, desktop switching, SendInput, event substitution or direct dialog callbacks are used.

Read `CONTRACT.md`, the four `src/F12*.cs` files, and `source.diff`. The native source folder grounds map clicks, FloatMenuOption.DoGUI/Chosen, Command.GizmoOnGUIInt and Targeter.ProcessInputEvents. MainThread GUI observations record actual control rectangles, native events and resulting state. One passive pointer move for the edge-clamped prompt accepts observed pointer coordinates on the prompt repaint, separately labeled; command interactions require actionable control events.

The closed `F12-UI` case covers:

- Actual right-click **Haul to…**, quantity7 from15, target click to a farther bare point despite nearer storage; original source8/destination7, original job success and unchanged personal Wood5/all unrelated ground IDs/counts/positions.
- Cancel amount/source/destination stages; blank,0,16 and2147483648 invalid text; stale selection, quantity, location, identity, forbidden status and actor removal from the map before confirmation; another real split after the destination targeter opened. No order/job/queue/reservation/cargo allocation for rejected or canceled selections.
- Native stockpile/shelf/wall/incompatible resident/foreign reservation/unreachable cell rejection, followed by the valid destination using that same local selection.
- A genuinely nonstackable, initially forbidden steel knife bypasses amount selection and preserves its exact physical identity at the destination.
- Actual QueueOrder-captured7 and visible-checkbox9 at identical targets, distinct native queue/order IDs, then actual progress-window cancellation. Plain submissions must become the actual current job.
- Two real orders made forbidden while walking at normal native speed, producing actual Blocked0/7. Italian progress and reason, actual Resume of the first original order with productive7 delivery, and actual Cancel of the second. Completed/canceled rows disable revival. No order state or delivered count is assigned by the fixture.
- Italian and English1024×768 quantity/target/progress screenshots, actual target prompt bounds and real progress overflow/scrollbar geometry.

The scene is an open-air controlled yard with natural pre-actor native temperature-cache warmup. No roof span, temperature-cache write or health change is introduced. Temporary stale-control objects are explicitly removed between independent controls; completed delivery witnesses are retained. Human backend/save matrices are not repeated. Supported robot source-targeter, exact shelf F13, provider and network profiles remain separate.

Pins and validation:

- Host `5C2E83AA97F6EED93DFB21778A0D1305376FF40A5CCAAB25CB99BC0E17129532`, MVID `1a8c3231-edee-4511-b1db-4d0934c64858`.
- Selection `97D8FCC0589A863A18FBE12467B086E419F0379B240A7EF8F7A8D282381CE212`.
- Diff `3ED74FB803E3C6180212DA45557CB7CE0CB58FAAB9F1A7BE7E887C8FE37202D3`.
- Final build3.05s,0warnings/errors;373 input/source/build/preservation checks. `controller-parser-audit.json` validates every copied controller script plus launch/build.
- Exact final source snapshot: `C:/Users/Arthur/AppData/Local/Packages/OpenAI.Codex_2p2nqsd0c76g0/LocalCache/Local/HaulersDreamQA/inputs/f12-ui-native-20260924/source-v2`.

Root preparation uses `controller/scripts/runtime-test.ps1`, `-CaseId F12-UI -ExpectedBehavior satisfied -HdSource Built`, and the exact `candidate.root`/`harness.path` in selection.json, with `TEMP/TMP=C:/HDQA/runtime-temp`. Launch the existing `launch.ps1` only through the owned private desktop pipeline. It starts the selected input worker on that desktop and retains native/input-worker process receipts. Keep the worker/controller/native outputs together for actual independent acceptance; the generic Verify status never substitutes for semantic review.
