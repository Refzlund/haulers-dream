# Feedback sources and PR completion claims

Draft for the single PR and Part2 handoff, 24 September 2026. This maps the existing scoped catalogue and accepted resolution records; it does not refresh Steam/GitHub or change ledger status. The user has paused the larger goal and asked for all current work in one PR, with incomplete work carried forward. Including an implementation in that PR does not make its report resolved.

Scope remains the 9 August2026 release cutoff (16:13:10 Copenhagen), newer Steam posts, open GitHub issues/PRs, and qualifying later replies. The frozen collection has78 source entries and46 groups. At drafting, the register has23 resolved groups and23 open; the active F12/F13/F40 captures may produce subsequent bounded updates. Root must reconcile their final results and the actual PR source before copying completion wording. See [ledger](feedback-since-last-update.md), [register](scoped-feedback-register.json), [Part2 handoff](part-2-handoff.md) and [final integration](final-integration-checks.md).

## Issue-closing language

The safe report-specific issue set is **#259, #263, #266, #269 and #271**. Each issue's mapped requirement is resolved and has an accepted source/runtime disposition. Use closing keywords only if the PR includes the exact corresponding fixes and guidance; final assembled-source verification still applies.

```text
Closes #259
Closes #263
Closes #266
Closes #269
Closes #271
```

- #259 is Lensrub's delivery-only construction report (F33), not a claim that all construction feedback is solved.
- #263 is ErikRedbeard's sapient animal/mech persistence report (F25), including its reduced-mod-list reply.
- #266 is Lensrub's cooperative nearby-haul handoff report (F38), with actual network verification still named separately.
- #269 is HaPpY's Storage Refill Hysteresis report (F20), cross-posted as C005/C006; it does not close other storage loops/overdelivery.
- #271 is むなかた's Periodic Bills menu report (F45); PB's independently reproduced upstream copy/paste reset is not presented as fixed.

**Do not close #261:** the report-update support question is resolved (F19), but its apples/smokeleaf gameplay defect is F16 and remains open. Do not close #258 because F02 own-stock consumption is resolved: #258 requests the wider Common Sense gathering behavior, F01. The other unresolved issues are #255, #256, #257, #260, #262, #264, #265, #268 and #270. Their inclusion/relevance is not a closing disposition.

**#148 and #267 are contributor PRs, not issues to close with these keywords.** Credit and reference their adopted contributions as described below. Do not claim they were merged or that their original author testing is this campaign's acceptance.

## Resolved report groups

Steam author names below come from the frozen comments. GitHub reporter names come from the report body where the register's account-author field is empty. Dates and exact source links are retained in the linked register; references below point directly to the individual comment or issue.

| Group | Exact received source | Finding and included response | Accepted evidence and claim boundary |
|---|---|---|---|
| F02 | vorshlumpf, [C424][C424],7 Sep18:32, own-inventory crafting subject | Published HD already consumed eligible tracked carried ingredients. Clarify tracked/personal stock, Keep and Common Sense; add the requesting worker's native forbidden check after a real forbidden-stock loop was found. | Published/candidate48/48 each, six ordinary controls, both batch forms and corrected permission58/58. `096edbc`; [resolution](evidence/f02-resolution.md). Do not say a general published own-stock failure was reproduced. |
| F03 | vorshlumpf, [C423][C423],7 Sep04:32, gathering/H&H subject | Verify construction and harvest gathering; fix H&H's attribution of unrelated nearby output and removal of kept quantities. | Native100-wood construction, base harvest and H&H49/49 with11 fresh stored/10 kept/7 unrelated. `7f68e78`; [resolution](evidence/f03-resolution.md). Historical save cause and H&H's separate CE path remain explicit limits. |
| F04 | vorshlumpf, [C423][C423], clipped descriptions subject | Render complete feature descriptions and retained scrollable help. |105 assertions,14 screenshots, English/Italian, two scales, native font fallback and long-help final line. `97ff78c`; [review](evidence/f04-rendered-20260920/runtime-review.md). Final button/wheel smoke stays separate. |
| F05 | vorshlumpf, [C424][C424], robot inventory hauling subject | Attach the missing robot inventory tracker once and respect actual native Hauling roles. | Automatic hauling, role exclusions, published station upgrade and full restart50/50. `75a2771`; [resolution](evidence/f05-f06-resolution.md). No blanket robot-mod certification. |
| F06 | vorshlumpf, [C424][C424], robot command subject | Provide no-Biotech targeting and Biotech menu access with fresh role/setting admission; preserve queue/interruption intent. |61/61 automatic/command/recovery,52/52 Biotech and station restart with307 stable ticks. `75a2771`; [resolution](evidence/f05-f06-resolution.md). Keep is a quantity, not an original-stack identity promise. |
| F07 | vorshlumpf, [C424][C424], RIMMSqol/drafted subject | Expose command-only WorkGiver preferences, opt-in drafted use and scoped gathered-cargo delivery. Preserve positively identified queued orders through real colony saves. | Actual editor/commands, interruption, active-delivery restart and queued restart57/57. `ee6f840`, settings-save prerequisite `bf54eae`; [resolution](evidence/f07-resolution.md). |
| F08 | vorshlumpf, [C424][C424], Custom Alerts subject | Document the existing selector: current action → ALL OPTIONS → gathering items into inventory. Available-only filtering hid inactive jobs. |568 scheduled observations across productive producer/restart; saved identity and one registration preserved. `de9f649`; [resolution](evidence/f08-resolution.md). Support answer, not a new scheduling hook. |
| F10 | vorshlumpf, [C425][C425],7 Sep18:44, activity-name observation | Change the report wording to “gathering items into inventory,” retaining the stable job identity. | Actual method/JobDef bindings and all16 languages reviewed. `cacbf95`; [verification](f10-activity-wording-verification.json). The comment observed the wording; it did not explicitly demand a rename. |
| F11 | vorshlumpf, [C425][C425],7 Sep18:44, specific drop quantity subject | Add the Gear-tab amount dialog and preserve the remainder, Keep and provider state. Recover exact ownerless native split descendants when callbacks throw; preserve the original exception and any physical output. | UI63/63, original restart45/45, provider82/82 and final fault135/135 with387 conserved through302 stable ticks. Root accepted tested-source equality. [Resolution](evidence/f11-resolution.md). One declared diagnostic, zero unexpected; no identical-host comparison or MP claim. |
| F14 | vorshlumpf, [C426][C426],8 Sep01:41 | Preserve the positive report that a robot can gather enough to finish construction. | Actual BuilderIII gathered100 beyond its75 hand limit and completed the table with native skills/roles unchanged; shared scene56/56. [Resolution](evidence/f14-resolution.md). A regression witness, not a new defect repair. |
| F19 | HaPpY, [GH261][GH261], reporting-edit subject only | Explain corrective replies to the existing report and linked issue. Original-text editing is not implemented. | Inspected client/backend and bounded reporting access observations. `7f91579`; [support disposition](evidence/f19-f44-resolution.md). F16 still prevents closing #261. |
| F20 | HaPpY, [GH269][GH269] and [C005][C005]/[C006][C006],29 Aug | Respect SRH's native refill gate when choosing new destinations and when revisiting cached results; already-assigned delivery remains allowed. | Matched baseline policy failures; candidate59/59 with linked stock, manual latch, reopening and disabled controls. `cde74d3`; [resolution](evidence/f20-resolution.md). Upper threshold is not a hard incoming cap. |
| F24 | TripleZer0 [C004][C004], Maya Fey [C003][C003],30 Aug; Triel [C022][C022],16 Aug | Stop treating an empty emergency work search or native one-tick wait as the end of productive work. | Native harvest, grow-zone tree/stump clearing/sowing, deconstruction and default grace59/59. `137a905`; [resolution](evidence/f24-resolution.md). A separate91-cloth stranding observation remains open. |
| F25 | ErikRedbeard [GH263][GH263], [reply][GH263-C5360067014], [C023][C023]/[C025][C025],14 Aug | Add HD's exact component to the provider's supported generated-definition whitelist, preserving sapient pawn state across loads. | Actual conversion, legacy upgrade83/83, provider-absent42/42, exact original fresh restart65/65. `70bd490`; [resolution](evidence/f25-resolution.md). Missing historical save fields cannot be recovered; exact historical provider bytes were unavailable. |
| F33 | Lensrub, [GH259][GH259] | Distinguish delivery authorization from building; HaulOnly does not sustain forced native Construction priority. | Both causal baseline failures; four candidate scenes69/69 with explicit build and ordinary assigned-work controls. `14ce252`; [resolution](evidence/f33-resolution.md). |
| F34 | Richard Ramirez, [C027][C027],13 Aug21:22 | Restrict requested blight cutting to eligible infected plants and refresh the live route preview as infection changes. |18 selection/job scenarios, real preview and five productive cuts with healthy crops retained. `59292db`; [plan/evidence](planned-blight-cutting.md). Actual MP/final input checks stay explicit. |
| F35 | Arthur GC, [C007][C007],27 Aug09:36 | Observe actual Allow Tool/Keyz finish-off execution and append one eligible corpse haul behind prior work. | Both native baselines omit hauling; candidates60/60 and57/57 with disabled/forbidden/cancel controls. `604a764`; [resolution](evidence/f35-resolution.md). Reporter did not name their exact provider. |
| F36 | talias [C029][C029] and maintainer I'm A Giraffe [C028][C028],12 Aug | Explain complementarity with Build From Storage; exclude native install blueprints from three raw-material route queries. | Baseline reproduces invalid queries; candidate52/52 installs the exact packed building and constructs another with exact wood accounting. `c785e66`; [resolution](evidence/f36-resolution.md). |
| F38 | Lensrub, [GH266][GH266] | Scope forced source handoff to the selected pickup, preserving the original remaining sweep, queued work, cargo and capacity claims. | Candidate59/59, saved continuation45/45, capacity54/54 and two same-save consumers51/51 +64/64. `9549cad`; [resolution](evidence/f38-resolution.md). Bounded local replay is not actual two-client MP acceptance. |
| F41 | flixzf's [PR148][GH148] | Adopt orphan null-stack bill repair; retain both link directions, ingredient/work identity and unrelated current/queued jobs; handle both serialization orders. | Native failed baseline, corrected recovery59/59 and clean restart43/43 with no repeat repairs. [Resolution](evidence/f41-resolution.md). Actual WorkbenchConnect serializer/colony-freeze attribution is not verified. |
| F43 | nullpat, [C021][C021],16 Aug23:59 | Remove the query counter that inferred completed hauling and created a sixth-probe HasJob/JobOn disagreement. Explain both warning emitters without recommending speculative mod removal. | Native baseline produces both warnings before cloth moves; candidate54/54 delivers10 and remains stable301ticks. `825435b`; [resolution](evidence/f43-resolution.md). DropText is unavailable; physical loops remain F15/F17. |
| F44 | Discussion reply [T01-R01][T01-R01],17 Aug16:11; author not retained in the scoped capture | Provide the in-game reporter and public GitHub alternatives when Steam discussion creation is unavailable. | Verified menu/source/access guidance. `7f91579`; [support disposition](evidence/f19-f44-resolution.md). No Steam-account repair; the same reply's gameplay exception is open F42. |
| F45 | むなかた, [GH271][GH271] | Preserve the actual repeat-menu creator's options/actions and add HD batching; fail back to the original menu if the contract cannot bind. | Both load orders, eight native iterations,52 shared-provider cases, four restarts and failure boundaries. `cb61d20`; [resolution](evidence/f45-resolution.md). PB-alone copy/reset limitation remains disclosed. |

## Contributor credit and adoption

**flixzf — PR #148**, frozen head `6430509acf2f759ca1849efd86062459ab45cf01`: credit the proposed two-sided unfinished-item/orphan-bill recovery. The integrated implementation adds the reviewed current/queued recipe safeguards and reciprocal saving-order guard. Its accepted native campaign verifies generic null-stack corruption, not the actual WorkbenchConnect serializer. Suggested wording: “Adapts flixzf's orphaned-bill recovery contribution from #148, preserving unfinished work and ingredients across repair and restart.” Reference the PR without claiming it was merged separately.

**nullpat — PR #267**, frozen head `4e2b4b34101512080864d19323dc0b18da250ae9`: credit the transporter bulk-unload feature and design. Adapted work corrects first-scan overflow, stale admission/load ownership, per-transfer custody, hands fallback, consistent prioritized visits and saved recovery. The contribution is partially verified but F40 is still open at this snapshot. Suggested wording: “Adapts nullpat's transporter bulk-unloading contribution from #267; remaining provider/presentation and network acceptance is listed in Part2.” Keep attribution in the feature changeset as well as the PR body. Do not describe the original proposal as already merged, unchanged, or fully accepted.

## Included work still requiring completion

| Group/source | Verified or implemented progress | Keep open / Part2 boundary |
|---|---|---|
| F01 — [GH258][GH258] | Ordinary ingredient-gather candidates and several productive Common Sense paths. | Named recipe/CS ownership/capacity/competing-work/interrupt/save controls remain. F02 consumption resolution does not finish gathering. |
| F09 — vorshlumpf [C424][C424] | Own-inventory fuel jobs and bounded direct-command recovery with exact payment and harmless duplicate. | Rendered recovery controls, automatic scheduling, remaining recovery/save/restart/provider and MP checks. |
| F12 — vorshlumpf [C425][C425] | Saved point-haul backend/lifecycle/fault custody accepted; corrected robot UI56/56 and actual opaque prompt rendering accepted. Human direct deliveries/stale controls pass but full UI does not. | Checkbox companion94/96 and held-Shift93/95 remain failed; actual queue/progress/Resume/Cancel UI and combined/network acceptance remain. [Root accepted fix/limits](evidence/f12-explicit-ui-20260924/ROOT-PROMPT-REVIEW.md). |
| F13 — vorshlumpf [C425][C425] | Correct native shelf passability and truncated linked-group capacity;32 focused tests and native causal candidate54/54 accepted. Basic54/56 proves alternate/filter/move/partial-cancel milestones. | Fix the fixture's invalid two-current-order admission assumption, then finish remaining basic/incoming/concurrency/original-save, providers and shelf UI. No checkpoint exists. [Accepted correction](evidence/f13-selected-shelf-20260924/ROOT-LATE-CAPACITY-REVIEW.md), [next step](evidence/f13-selected-shelf-20260924/native-fixture/HANDOFF.md). |
| F40 — nullpat [PR267][GH267] | Core212/212; saved replanning59/59, feature-off52/52; CE producer53/53 and fresh consumer71/71 with exact cargo/corpse outcomes accepted. | Actual shuttle/VF lifecycle and rendered controls remain; assembled/network checks explicit. Credit contribution without full-feature acceptance. |

The other18 open groups must remain visible even if shared infrastructure relevant to them is included: F15 ([GH256][GH256], rox-wool loop); F16 ([GH261][GH261]/[C019][C019], stocking); F17 ([GH268][GH268], crate loop); F18 ([GH270][GH270], concurrent overdelivery); F21 ([C001][C001]/[C031][C031]/[C032][C032], gap/disappearance); F22 ([C001][C001], Skipdoor); F23 ([C002][C002]/[C008][C008]/[C010][C010]/[C011][C011]/[C012][C012], established-save hauling loss); F26 ([GH262][GH262]/[GH264][GH264]/[GH265][GH265]/[C015][C015]/[C017][C017], GUI/startup/crash); F27 ([C014][C014], lag/gestation); F28 ([C018][C018], Matter Network); F29 ([GH260][GH260]/[C020][C020], caravan unloading); F30 ([C024][C024], WVC recharge); F31 ([C009][C009], construction proximity); F32 ([C026][C026], construction materials hauled away); F37 ([GH255][GH255], reservation lifetime); F39 ([GH257][GH257], portal corpses); F42 ([T01-R01][T01-R01], product-counting exception); F46 ([T12-OP][T12-OP], larger-colony performance support).

## Recurrence wording for the PR

The strongest confirmed repeated design omission is F45: successive provider-specific menu repairs retained exclusive replacement, so another provider's option disappeared. F43 demonstrates that the attempted #214 query-based backstop itself creates false recurrence and workgiver warnings without movement. Its removal does not solve actual transport cycles. F24 identifies missed scheduler transitions; F07 implementation exposed distinct settings-save and colony-save cancellation paths. F25 and F38 establish concrete component-lifetime and forced-reservation boundaries without proving earlier fixes of those exact reports regressed. F11 and F12 independently exposed the same native callback boundary: a throw can follow a physical split or partial placement before the caller receives the result. F11 now has exact split-descendant recovery and fault evidence; this is an identified shared design weakness, not proof of a repeated historical player report. Preserve retracted workarounds in F16/F23/F26 and the open F01 post-v1.24 recurrence lead; do not state those unresolved causes are fixed.

Final PR wording should lead with the actual fixes/features delivered and link this map and Part2. Avoid “all feedback fixed,” “all mods compatible,” “clean logs,” or “all tests passed” across deliberately failing baselines and incomplete cases. Accepted report resolutions, implemented candidates, benign/provider warnings, declared fault diagnostics and remaining integration each retain their own evidence status.

## Exact frozen source links

[C001]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292592327
[C002]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940297913156104
[C003]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001633990
[C004]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001626310
[C005]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001556231
[C006]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001556021
[C007]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045885677
[C008]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045859360
[C009]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045781797
[C010]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889007307
[C011]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889005481
[C012]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889002872
[C014]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999435100
[C015]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999285398
[C017]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813130434155325
[C018]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043389321
[C019]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043387830
[C020]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043344698
[C021]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043335749
[C022]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043260054
[C023]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265884141
[C024]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265879048
[C025]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265832567
[C026]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265816104
[C027]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265812007
[C028]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265733261
[C029]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265729951
[C031]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320103811
[C032]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320101744
[C423]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292709966
[C424]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292755974
[C425]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292756958
[C426]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292789520
[GH148]: https://github.com/Refzlund/haulers-dream/pull/148
[GH255]: https://github.com/Refzlund/haulers-dream/issues/255
[GH256]: https://github.com/Refzlund/haulers-dream/issues/256
[GH257]: https://github.com/Refzlund/haulers-dream/issues/257
[GH258]: https://github.com/Refzlund/haulers-dream/issues/258
[GH259]: https://github.com/Refzlund/haulers-dream/issues/259
[GH260]: https://github.com/Refzlund/haulers-dream/issues/260
[GH261]: https://github.com/Refzlund/haulers-dream/issues/261
[GH262]: https://github.com/Refzlund/haulers-dream/issues/262
[GH263]: https://github.com/Refzlund/haulers-dream/issues/263
[GH263-C5360067014]: https://github.com/Refzlund/haulers-dream/issues/263#issuecomment-5360067014
[GH264]: https://github.com/Refzlund/haulers-dream/issues/264
[GH265]: https://github.com/Refzlund/haulers-dream/issues/265
[GH266]: https://github.com/Refzlund/haulers-dream/issues/266
[GH267]: https://github.com/Refzlund/haulers-dream/pull/267
[GH268]: https://github.com/Refzlund/haulers-dream/issues/268
[GH269]: https://github.com/Refzlund/haulers-dream/issues/269
[GH270]: https://github.com/Refzlund/haulers-dream/issues/270
[GH271]: https://github.com/Refzlund/haulers-dream/issues/271
[T01-R01]: https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691114229
[T12-OP]: https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564793766239611588/
