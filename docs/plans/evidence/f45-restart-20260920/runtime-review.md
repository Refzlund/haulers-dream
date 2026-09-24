# F45 actual checkpoint restart review

## Simple meal, partial cycle

**Accept actual fresh-process continuation** from `F45-PB-0-partial` in run `94f2757e99a4411e96d02482c4103450`. Owned PID 22940 joined with exit 0, no deadline/error. Independently read the complete result and 66 contiguous events: all 51 assertions pass, with zero captured Unity errors. The copied native save and checkpoint record still equal their originals from corrected producer `0e63f30388344a16ad54559991bdca34`.

Native LoadedGame occurs at saved tick 578; first paused admission is tick 579, exactly the native PauseOnLoad tick. Before fixture advancement or eligibility queries, actual map 0, actor Human37260, stove FueledStove37263 and Bill_CookMealSimple_0 retain PB mode, nonbatch state and all four fields **amount 2 / interval 1 / produced 1 / completion −1**, with 40 corn and one meal. No field or item restoration is performed.

The actual unforced WorkGiver dispatches HD gather job 39, which hands off to native DoBill 45. The native driver records 366 recipe-work ticks and one completion at tick 1,202. Exact settled totals become 30 corn/two meals; PB becomes produced 2/completion 1,202. A further 121 ticks to 1,325 retain those totals and reject extra ordinary/forced work. Cleanup removes the owned observer and restores paused speed, leaving the loaded scene in the disposable process. This proves this partial-cycle continuation, not an uninterrupted scheduling day or physical input.

The complete 1,909-line log diagnostic scan includes actual `Loading game from file Autostart` and contains no missing-deep-save, unresolved-reference or corresponding load/recipe exception. Two early Mono fallback requests, Header.png mipmap notice and profiler/allocator output remain. Debug independently records gather39→DoBill45 with both ingredient rows valid.

For this first run, Verify returned its object directly rather than writing a JSON file. Root reports its actual exited-0 output: no protected changes; only the explicit independent-restart-review notice and the two Mono fallback candidates. Its missing-PID lookup agrees with the independently read owned joined outcome. This paragraph distinguishes that **root-observed Verify result** from this reviewer's independently read raw evidence; no verification was rerun to manufacture a file.

Evidence: `%TEMP%/haulersdream-runtime-tests/94f2757e99a4411e96d02482c4103450/`; owned outcome: `native-outcome-F45-PB-0-partial.json` in this folder. The other three checkpoints and overall F45 completion remain separately reviewed.

## Simple meal, saved cooldown

**Accept cooldown continuation** in `aa427b30a43c4cdc9cc09f3e1d888df9`, from `F45-PB-0-cooldown`. Owned PID 31244 joined with exit 0, no deadline/error. All 54 assertions and 78 contiguous events were read; result passes with no captured Unity errors. Copied save/record remain identical to the actual producer inputs. Native LoadedGame tick 1,204 becomes paused admission 1,205; map/actor/bench/bill identities remain 0/37260/37263/Bill_CookMealSimple_0. Loaded PB fields are **2/1/2/1,202**, mode remains PB and batching is off, with 30 corn/two meals.

Ordinary and forced work remain unavailable over 121 actual cooldown ticks, with no counter/payment/product changes. The explicitly labeled time jump tests tick 61,201, then the exact retained deadline 61,202, where actual PB eligibility resets produced to zero. Two gather jobs (57, 69) hand off to native DoBill (61, 74), each recording 366 work ticks. Completions at 61,808 and 62,441 yield exact totals 20/three then 10/four corn/meals. Final PB count is two with completion 62,441; another 120 settled ticks retain totals and exclude extra work. Observer cleanup/paused-speed restoration complete normally.

Full 1,909-line log scanning finds actual Autostart loading and no unresolved-reference/deep-save/load or recipe exception. The same two Mono fallback requests, Header mipmap notice and profiler/allocator output remain. The **serialized actual Verify** was independently read this time: no protected changes, only its explicit manual review and those two log candidates. Durable raw result/events/log/debug, owned outcome and Verify are in `native/aa427b30a43c4cdc9cc09f3e1d888df9/`. This accepts the saved simple-meal deadline and subsequent productive cycle; both bulk checkpoints remain separate.

## Bulk meal, partial cycle

**Accept bulk partial-cycle continuation** in `409a3c120d704fd39fd3d753a89ec44c`. Owned PID 18388 joined with exit 0, no deadline/error. All 51 assertions pass; the complete 66-event stream is contiguous and captures no Unity errors. Original and copied save/record agree. LoadedGame tick 64,179 becomes paused admission 64,180, retaining map 0, actor 37260, bench 37263 and Bill_CookMealSimpleBulk_1; PB fields **2/1/1/−1**, PB/nonbatch state, 160 corn and four meals are intact before fixture work.

Actual gather109 hands off to native DoBill114, whose 1,461 recipe-work ticks complete once at 65,900. Exact payment is 40 corn and production four meals, leaving 120 corn/eight meals and PB produced 2/completion 65,900. The next 120 settled ticks reject extra work and preserve totals. Cleanup completes normally. This confirms PB counts one bulk recipe iteration despite four products.

The full 1,909-line log scan has no save-reference/load/recipe exception; it retains the same Mono/mipmap/profiler notices. Debug confirms both 20-corn rows and the native handoff, plus one existing stale-tag-pruning diagnostic. Independently read serialized Verify reports no protected changes and only manual review/two Mono candidates. Durable evidence is in `native/409a3c120d704fd39fd3d753a89ec44c/`. The final bulk cooldown checkpoint remains separate.

## Bulk meal, saved cooldown

**Accept bulk cooldown continuation** in `1076abf5a8634e56bc7ecf36cbd0b688`. Owned PID 12916 joined with exit 0, no deadline/error. All 54 assertions pass; all 77 raw events are contiguous, with zero captured Unity errors. Copied save/record equal the original inputs. Native LoadedGame tick 65,898 becomes paused admission 65,899; actual map/actor/bench remain 0/37260/37263 with Bill_CookMealSimpleBulk_1. PB fields **2/1/2/65,897**, PB/nonbatch state and 120 corn/eight meals survive loading.

The saved deadline remains unavailable to ordinary/forced work for 121 observed ticks, preserving all goods and PB count. The labeled jump tests 125,896 and the exact deadline 125,897; the real PB query resets count only at that boundary. Gather140/169 hand off to native DoBill146/174. Each native iteration records 1,461 work ticks, completing at 127,643 and 129,374. Actual totals progress 120/8→80/12→40/16 corn/meals, with two new PB iterations. Another 120 settled ticks exclude extra work; observer/speed cleanup completes normally.

The full 1,909-line log scan contains actual Autostart loading and no unresolved saved-reference/load/recipe exception. Existing Mono, Header mipmap and profiler notices remain; debug records the two valid gather handoffs and one stale-tag-pruning notice. Independently read serialized Verify has no protected changes and only its explicit manual review/two Mono candidates. Durable evidence is in `native/1076abf5a8634e56bc7ecf36cbd0b688/`.

**All four planned checkpoint restart checks are accepted:** both partial-cycle and cooldown saves for simple and four-meal recipes load in separate native processes, preserve the four PB fields and HD nonbatch state, and continue with exact productive accounting. Across the four runs, all 210 assertions pass; six actual native recipe completions follow six HD gather handoffs, without captured Unity errors or missing-save-reference warnings. The original invalid producer saves remain rejected. This closes this specific checkpoint persistence/continuation evidence; it does not claim clone repair, physical interaction, multiplayer or whole-F45 completion. Cooldown boundaries used the explicitly recorded time jumps, not uninterrupted day simulations.
