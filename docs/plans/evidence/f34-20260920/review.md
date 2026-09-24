# F34 baseline and candidate native boundary review

**Decision, 20 September 2026:** the candidate passes the eighteen paused selection, confirmation and native-job boundary scenes. The baseline independently reproduces unwanted healthy-plant designation and queued cutting. This is accepted evidence of the correction at those boundaries; **F34 is not yet fully resolved**, because actual rendered interaction, productive cutting and live preview behavior remain untested here.

The review read both complete results and event streams, all candidate snapshot transitions, and the actual companion source at `C:/Users/Arthur/AppData/Local/Temp/haulersdream-f34-mixed-boundary-root-build-20260920/src/F34MixedBoundary.cs`. It did not execute the controller, fixture, reader or game. No new framework or product change was introduced.

## Actual runs

| | Baseline | Candidate |
|---|---|---|
| Run | `36355997a71b450da8d446fbf6a4807d` | `ca489507357449adb1a038975dec4c96` |
| Product | HD `42766C0D`, Core `30F1CA32` | HD `32D57A64`, Core `D623F164` |
| Native outcome | PID16972 joined, exit0 | PID19012 joined, exit0 |
| Result | Failed: 303 assertions, 31 failures | Passed: 377 assertions, zero failures |
| Captured Unity errors | 0 | 0 |
| Events / snapshots | 423 / 89 | 529 / 116 |
| Scene coverage | 13 complete, one partial, four never started | All 18 complete |

Both use the accepted host `A0A5474E` and companion `F47BDA34` (MVID `bb2c1678-7d55-466d-b65e-0fe58a81e4d8`). The matched-baseline selection/compiled review is recorded in [the F34 plan](../../planned-blight-cutting.md); this behavioral review does not repeat its source-copy census.

See [baseline result](baseline/result.json), [baseline events](baseline/events.jsonl), [candidate result](candidate/result.json), [candidate events](candidate/events.jsonl) and both [baseline](baseline/native-outcome.json)/[candidate](candidate/native-outcome.json) process outcomes. The four retained result/event files were independently hash-compared with their unchanged original TEMP files and match. [retained-files.json](retained-files.json) identifies the remaining preserved manifests, profiles and logs.

## Demonstrated baseline defect

Baseline event74 shows native **Cut all blight** adding only infected A/B/E/H/I. Healthy rice **C40949** is still unmarked. HD Radius confirmation at event83 newly adds C's CutPlant designation and queues **CutPlantDesignated job18** for C. Its eight-job route also includes healthy D/F, whose independent pre-existing designations remain intact. Chained and Zone include healthy targets; Vein's A/B/C/I preview uses healthy C to connect infected I. Secondary selection additionally marks previously unmarked healthy corn G.

Scene 14 first caches an unroutable, unmarked anchor, invokes native CutAllBlight and resolves again in the same paused tick. The old `WorkKindResolver` caches null by tick/pawn/Thing without checking changed plant designations; the companion's required cutting-kind check therefore throws predictably. This is an old-resolver behavior failure and a fail-fast coverage limit, not an invalidation of the preceding reproduction. `same-tick-ordinary-to-blight`, `same-tick-blight-to-ordinary`, `direct-stale-plan` and `append-stale-stops` never start. The roster lists the partial scene with those four under `unexecuted`; it must not be described as five never-started scenes.

## Candidate scene evidence

Names A/B/E/H/I initially identify infected crops; C/G are healthy and unmarked; healthy D/F have independent cut orders. Sequences below refer to candidate events.

| Scene(s) | Observed result |
|---|---|
| `native-radius-replace` | At83, exactly five native cut jobs target A/B/I/E/H. C37508 and G37515 remain unmarked. D/F retain their old designations but gain no route jobs. Replace removes the Wait sentinel. |
| `native-chained-select` | At107, preview is exactly A/B/I/E/H. |
| `native-zone-select` | At130, preview is exactly A/B/I/E; outside-zone H is excluded. |
| `native-vein-select` | At153, preview is A/B only; healthy C no longer connects I. |
| `manual-blight-picks`, `blight-secondary-picks` | Healthy picker inputs are rejected; infected E remains accepted. At181/210, confirmations queue only the five infected plants; C/G gain no designations. |
| `ordinary-rice-from-infected-corn`, `ordinary-corn-from-infected-rice` | At237/262, ordinary clearing still deliberately marks and queues healthy secondary crops G/C. The new filter has not prohibited ordinary clearing. |
| `native-radius-append` | At285, the existing Wait remains alongside five infected-target cutting jobs. |
| `captured-anchor-cured` | At313, rejected captured intent preserves the same Wait and designation set. Blight deletion here is explicit fault injection, not a claimed natural cure. |
| `captured-target-cured` | At338, jobs target A/I/E/H; newly healthy B is excluded while its prior cut designation remains. |
| `captured-target-replaced` | At364, the healthy replacement receives no designation/job. The stale preview still contains old B37693, but confirmation independently queues only A/I/E/H. |
| `new-infection-before-confirm` | At391, newly infected C gains its designation and cutting job despite being absent from the earlier preview; G stays unmarked. |
| `same-tick-cut-order` | Native marking now invalidates the cached null result. At414, a valid five-infected-target preview opens; the next unchanged lookup reuses its descriptor. |
| `same-tick-ordinary-to-blight` | At438→441, infecting D changes intent from ordinary to blight-only and excludes healthy C/F from the new preview. |
| `same-tick-blight-to-ordinary` | At461→465, explicit blight-removal injection permits newly opened ordinary intent. At468 it deliberately marks/queues healthy C. |
| `direct-stale-plan`, `append-stale-stops` | Supplied A/C/D/F lists produce only A's cutting job at494/519; no healthy designation is added. |

All 18 native-mark delta checks and all 29 independent-order preservation checks pass. The candidate has 19 successful owned-retirement assertions: one for each scene, plus the final empty cleanup. The actual captured tick manager's speed is restored to Normal. Cleanup destroys the fixture-owned actor, plants and blight; it is not a claim that prepared ground alterations were reversed or that a whole-world cleanup census occurred.

The candidate event sequence is contiguous. Its first 376 assertion details match the result exactly; the final zero-error assertion gains the host's documented terminal-capture suffix when the result is finalized (`Bootstrap.cs:789`). Both still report zero errors. This review accepts the behavioral evidence; zero captured errors is not a blanket claim about startup/shutdown logs or a substitute for the separate controller verification.

## Remaining work, kept specific

Every snapshot is at tick 6. Jobs are real native jobs, but no scene demonstrates a plant being productively cut. Dialog preview/confirmation and picker bodies are invoked directly; they do not prove normal UI delivery, cancellation or the live 0.5-second preview refresh. Explicit Blight destruction is fault injection. Replacement/new-infection snapshots prove confirmation revalidation, not natural spread timing or another pawn's completed work.

Next, use one ordinary UI-to-completion mixed-crop example: display the blight plan, confirm, let infected cutting finish, and verify unrequested healthy C/G remain unmarked and alive. Keep D/F's independent orders distinct. In the same focused work, check a paused changing preview and unchanged-preview pathfinding behavior. Preserve relevant queued-work/save and changed Multiplayer-command integration checks; do not multiply every mode across every integration or rerun this passing boundary suite without a relevant change. Other original F34 obligations remain tracked in the focused plan and are not silently closed by this review.

Raw SHA256 identities:

- Baseline result: `6EF261147AAD09D0903BA98D2A4B9F92429673B2E9ADB29A473FD38C952A34DE`
- Baseline events: `5B72EEE740F56959A7D90FCA0F006791F664F85C34DF8E5C529A68D58AD5A34F`
- Candidate result: `7E285E7E76C2154DD3B6F2904A0F0DEE33B4F7B58C6C90BBA351E98DC3FA6961`
- Candidate events: `63207EE9057B224AFA4981A3A53E39363012A4085F4D234D0BB3165E5BCC48D0`
