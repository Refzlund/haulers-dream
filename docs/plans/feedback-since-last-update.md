# Feedback since the last update — status and remaining work

Status reviewed **24 September 2026** (Copenhagen time). Steam and GitHub feedback was last collected **19 September, through 22:57 CEST / 20:57 UTC**. The source list is a snapshot of that collection, not a fresh Steam/GitHub refresh today. Implementation statuses include the latest local investigation and test results; partial checks do not count as completed feedback items.

**This is the authoritative working scope. The broad goal is paused; unfinished work is handed off in [Part 2](part-2-handoff.md).** The cutoff is the Workshop release of v1.24.0: **9 August 2026, 16:13:10 Copenhagen time / 14:13:10 UTC**. Include Steam comments and individual discussion posts at or after that timestamp, every currently open GitHub issue and PR regardless of age, and closed issues/PRs with comments after the cutoff. A topic receiving a new reply does not turn its older, unrelated opening report into new feedback. [Workshop change notes](https://steamcommunity.com/sharedfiles/filedetails/changelog/3742459652).

The previous 569-source catalogue included older Steam reports as active obligations. **That was broader than the scope you have now confirmed.** Its original records are preserved as history. Older reports are recurrence evidence or relevant regression checks, not additional standalone work. Necessary shared fixes and regression checks must link back to an included item below. The older plan language saying every historical source is mandatory is superseded by this scope.

The refreshed scope contains **39 Workshop comments, three discussion posts, 16 open issues, two open PRs and their 17 conversation replies**, plus **one post-release acknowledgement on merged PR #251**: **78 source entries**, not 78 independent bugs. There are no qualifying closed issue threads and no submitted reviews on the two open PRs; the repository-wide post-cutoff inline-review-comment query also returned none. The source index below accounts for every included entry.

**Reading this file:** the 46 rows below group related feedback, including requests, compatibility questions and positive regression reports. Each row states the work already done and what remains. **Implemented** means a development candidate exists; **passed** applies only to the named checks; **resolved** means the reported requirement has sufficient reviewed evidence. Final assembled-build checks are tracked separately and can reopen an item if they find a regression. Older feedback is excluded as standalone work even when an old plan still calls it active.

**At a glance:** 46 feedback groups; **23 resolved reports (F02, F03, F04, F05, F06, F07, F08, F10, F11, F14, F19, F20, F24, F25, F33, F34, F35, F36, F38, F41, F43, F44, F45)**, **5 with implementation candidates or partial gameplay verification** (F01, F09, F12, F13 and F40), and **18 requiring investigation, implementation or a verified support disposition**. **23 groups remain open.** All four supplied Vorshlumpf comments are included in F02–F14. Final PR integration remains unfinished.

| Progress | Feedback groups |
|---|---:|
| Report-specific resolution verified | 23 |
| Implementation candidate or partial gameplay verification; work remains | 5 |
| Investigation, design, unresolved support, or regression verification | 18 |
| **Total tracked** | **46** |

**Completion accounting corrected on 20 September:** individual report resolution no longer waits for every other change in the final PR. This does not turn partial gameplay checks into complete fixes. F10 is a text-only correction verified from its actual source and language bindings; its former Custom Alerts/drafted-command requirements belong to F08/F07. F04 now has accepted actual rendering and long-help evidence; F34 has accepted native selection, live-preview and productive-cutting evidence. Its targeted Multiplayer and final button checks remain visible in [final integration](final-integration-checks.md). [Independent scope review](feedback-completion-scope-review.md).

The 78 individual sources, including praise, corrections and unavailable content, are accounted for in the source index. The 46 groups are not all defects. Each table row separates completed progress from the work still needed; passing a test of the verification tools does not mean the player's problem has been fixed.

## What has actually been completed

- **Drop a chosen amount:** F11 now resolved, including the final native exception-custody repair and provider stability. [Resolution](evidence/f11-resolution.md).

- **False cloth and workgiver warnings:** F43 resolved in `825435b`. The native failure is reproduced and the corrected candidate completes a real delivery without either warning. [Resolution](evidence/f43-resolution.md).

- **Cooperative nearby hauling:** F38 resolved in `9549cad`. Forced source handoff preserves the original remaining sweep, queued orders and carried cargo; saved continuation, constrained capacity and both fresh command replays are independently accepted. [Resolution](evidence/f38-resolution.md).

- **Own-inventory crafting:** F02 resolved in `096edbc`. Support boundaries and ordinary/batch consumption are independently verified; a demonstrated forbidden-stock loop is fixed. Guidance and all 16 tooltip translations are aligned. [Resolution](evidence/f02-resolution.md).

- **Sapient animals and mechs:** F25 resolved in `70bd490`. Actual conversion, old-save upgrade, partial cargo, Keep/opt-out, reload and fresh restart are independently accepted, with a provider-absent control. [Resolution](evidence/f25-resolution.md).

- **Orphaned unfinished items:** F41 resolved after independently accepted recovery59/59 and fresh restart43/43. Original work/ingredients survive, both save orders are safe, and repair does not repeat. [Resolution](evidence/f41-resolution.md). Exact-source commit and final integration remain pending.

- **Work before storage:** F24 resolved in `137a905`; native harvesting, grow-zone clearing/sowing, deconstruction and default unloading timing pass59/59 reviewed checks. [Resolution](evidence/f24-resolution.md). A different stranded-cargo observation remains in [open diagnostic leads](evidence/open-diagnostic-leads.md).

- **RIMMSqol drafted nearby hauling:** resolved and committed as `ee6f840`. Actual editor/command behavior, scoped deliveries, interruptions and active/queued save-restart paths are independently accepted. [Resolution](evidence/f07-resolution.md).

- **Build From Storage:** resolved and committed as `c785e66`. The real paired test verifies packed-building installation followed by ordinary construction, conservation and unavailable-source refusal. [Resolution](evidence/f36-resolution.md).

- **Refill thresholds:** resolved and committed as `cde74d3`. Native paired tests prove that paused storage is skipped for new HD destinations while in-flight deliveries, reopening and linked-group rules remain correct. [Resolution](evidence/f20-resolution.md).

- **Finish-off hauling:** resolved and committed as `604a764`. Both supported providers reproduce the original omission on the baseline; candidates pass native queue/delivery and relevant negative controls. Guidance and 64 translated values are accepted. [Resolution](evidence/f35-resolution.md).

These are completed or historically recorded milestones in development candidates; they have not been released. F10 has fresh current-source evidence. Some older settings/quantity-drop test artifacts referenced by the plans are currently unavailable; their recorded outcomes are preserved, but must be recovered before claiming a fresh audit. See the [evidence-availability review](feedback-completion-scope-review.md#historical-evidence-availability).

- **Custom Alerts activity:** resolved as supported behavior with verified discovery instructions, committed as `de9f649`. Actual editor selection, queued/active/interrupted/completed matching, conserved materials and one fresh saved-alert restart passed independent review. [Resolution](evidence/f08-resolution.md).
- **Ordinary ingredient gathering:** single-meal and bulk-meal scenarios passed with Common Sense cleaning enabled and its gathering disabled. A separate scenario used 12 carried Milk plus floor ingredients to produce four meals. The wider recipe/settings/save-load coverage remains open. [Evidence](ordinary-bill-gather.md), [partial-inventory scenario](bg03-fixture.md).
- **Robot hauling and commands:** F05/F06 resolved and committed as `75a2771`, with actual role/command controls, published-save upgrade and full station restart accepted. Builder III construction remains separately resolved as F14. [Robot resolution](evidence/f05-f06-resolution.md), [construction resolution](evidence/f14-resolution.md).
- **Resource gathering and H&H coexistence:** resolved in the verified scope, with the compatibility fix committed as `7f68e78`. Construction/base harvest evidence is accepted; the H&H candidate stores fresh output while retaining kept stock and unrelated nearby stock. The historical save cause and separate CE path remain explicit limits. [Resolution](evidence/f03-resolution.md).
- **Drop a chosen quantity:** the command passed 19 controlled scenarios; one actual Gear-window operation dropped seven items and retained eight. Remaining compatibility and lifecycle checks are open. [Evidence](partial-inventory-drop-runtime.md).
- **Settings descriptions:** resolved and committed as `97ff78c`. The actual settings window passed 105 assertions and visual review of 14 screenshots: complete English/Italian feature cards, two supported scales, native Small-font fallback, search and the final line of retained long help. Final button/wheel smoke remains separate. [Evidence](evidence/f04-rendered-20260920/runtime-review.md).
- **Delivery-only construction orders:** resolved and committed as `14ce252`. The baseline reproduced both an unassigned hauler being forced to build and a HaulOnly route sustaining native Construction priority. The corrected candidate passed all four native scenes and 69 assertions: delivery-only stays delivery-only, explicit build orders still build, and ordinary nonforced Construction remains available. The failed baseline and environmental interruption are retained. [Resolution](evidence/f33-resolution.md).
- **Blight cutting:** the old behavior was reproduced in game. All 18 corrected selection/job scenarios and a native cutting run passed independent review. The real paused preview refreshed as infection changed and reused unchanged route legs. Five infected plants were productively cut; unrequested healthy crops survived. The single-player report is resolved; targeted Multiplayer replay and final button input remain integration checks. [Evidence](planned-blight-cutting.md).
- **Activity wording and nearby-haul command:** F10 wording and F07 RIMMSqol command behavior are resolved with reviewed translations and native command/lifecycle evidence. Robot-specific lifecycle is now accepted under F05/F06; final integration remains separate. [Command resolution](evidence/f07-resolution.md).
- **Own-inventory refuelling:** ordinary scenarios and one direct-command recovery scenario have passed independent evidence review. In recovery, an uncertain state was closed once, a stale duplicate had no additional effect, and the successor job consumed 20 fuel items while retaining 80. Rendered recovery controls, automatic scheduling, persistence and wider compatibility remain unfinished. [Evidence](own-inventory-refuelling.md).
- **Periodic Bills compatibility:** resolved and committed as `cb61d20`. The published omission was reproduced; both corrected load orders, native menus, eight real crafting iterations and 52 shared-provider cases passed independent review. Four actual checkpoint restarts preserved state and resumed production. Eleven composition/action boundary cases and fresh binding-refusal fallback also passed. PB-only copying reproduced the same settings reset as HD+PB, establishing an upstream limitation. [Resolution and evidence](evidence/f45-resolution.md).
- **Shared hauling prerequisites:** an own-cargo recognition correction and a first-delivery scenario passed. Storage component checks and investigation of a false loop warning also produced useful evidence. These do not establish that the reported loops or concurrent over-delivery are fixed. [Evidence](runtime-run-log.md).

**F02, F03, F04, F05, F06, F07, F08, F10, F11, F14, F19, F20, F24, F25, F33, F34, F35, F36, F38, F41, F43, F44 and F45 are resolved; 24 groups remain open.** F19 and F44 are support answers; original-report editing remains unsupported. The combined PR still requires integration validation. Partial implementation and testing remain visible without being labelled complete. “Open” below means no accepted fix for that specific report is recorded, not that no related code has been touched. Source evidence and tests must still match the eventual integrated build.

## Feedback with implementation or investigation progress

| ID | Feedback and sources | Current status | What remains |
|---|---|---|---|
| F01 | Common Sense: collect all ingredients before crafting. [GH258][GH258]. | **Implemented; some gameplay cases passed.** See completed cooking milestones. | Remaining recipes, settings combinations, competing jobs, interruptions and save/restart; verify the reporter's scenario. |
| F02 | Craft using the pawn's own inventory. [C424][C424], subject S05. | **Resolved and committed as `096edbc`: verified own-stock guidance and forbidden-stock fix.** Published/candidate comparison, six ordinary controls and both batch forms are independently accepted. The corrected permission scenario passes 58/58, consumes allowed floor ingredients and preserves forbidden carried Milk40. Guidance and one tooltip in all 16 languages now describe tracked stock, Keep and Common Sense accurately. [Resolution](evidence/f02-resolution.md). | None for the documented single-player ordinary/explicit-batch scope. The reporter's historical save/cause is unknown. Named F01 gathering/compatibility controls and final assembled-build checks remain explicitly open. |
| F03 | “Gather resources into inventory” fails; Harvest and Haul was re-added. [C423][C423], S01. | **Resolved in the verified scope; committed as `7f68e78`.** Native construction/base harvest work, and the H&H compatibility correction passes 49/49 assertions: store the 11 fresh cloth, retain the 10 kept units and leave unrelated 7 untouched. [Resolution](evidence/f03-resolution.md). | None for the verified construction/harvest and ordinary H&H coexistence behavior. Historical save-specific cause is unknown; H&H's separate CE unload path is explicitly outside this protection. F01/F02 crafting and F24 work interruption remain separate. |
| F04 | Feature descriptions are cut off. [C423][C423], S02. | **Resolved: complete settings descriptions and retained scrollable help.** Native English/Italian rendering at two supported UI scales, Small-font fallback, all 18 active cards, search and longest-help final line passed independent review. [Runtime review](evidence/f04-rendered-20260920/runtime-review.md). | None for the reported clipping. Physical card-button and help-wheel input remain final assembled-build smoke checks. [Integration checks](final-integration-checks.md). |
| F05 | Misc. Robots++ do not haul into inventory. [C424][C424], S01. | **Resolved and committed as `75a2771`.** Native automatic hauling, role controls, no-Biotech targeting, Biotech menus and direct/queued/interrupted commands pass independent review. Published-save upgrade and full station restart preserve the same robot, cargo and Keep state; final restart50/50 with307 stable ticks. [Resolution](evidence/f05-f06-resolution.md). | None for these reported single-player robot hauling/command requirements. Final assembled-build, ordinary input and supported Multiplayer checks remain separately tracked. |
| F06 | Robots cannot be given the nearby-haul command. [C424][C424], S02. | **Resolved and committed as `75a2771`.** Native automatic hauling, role controls, no-Biotech targeting, Biotech menus and direct/queued/interrupted commands pass independent review. Published-save upgrade and full station restart preserve the same robot, cargo and Keep state; final restart50/50 with307 stable ticks. [Resolution](evidence/f05-f06-resolution.md). | None for these reported single-player robot hauling/command requirements. Final assembled-build, ordinary input and supported Multiplayer checks remain separately tracked. |
| F07 | RIMMSqol discovery and making the command usable while drafted. [C424][C424], S03. | **Resolved and committed as `ee6f840`.** Actual RIMMSqol editor, drafted/undrafted commands, replacement/interruption, active-delivery restart and both queued-order save/restart paths are independently accepted. The last fresh restart passes57/57 with exact cargo preservation. [Resolution](evidence/f07-resolution.md). | None for the reported single-player discovery/drafted-command behavior. Final assembled interactions, ordinary input and Multiplayer replay remain explicit integration checks. |
| F08 | Select the hauling activity in Custom Alerts - Continued. [C424][C424], S04. | **Resolved: supported activity with verified setup guidance.** Use current action → ALL OPTIONS → gathering items into inventory. Actual discovery, 568 scheduled observations across productive producer/restart runs, saved configuration and one live registration passed independent review. Guidance committed as `de9f649`. [Resolution](evidence/f08-resolution.md). | None for this support request. One compact final-build selector check beside F07/F10 remains in [final integration](final-integration-checks.md). |
| F09 | Refuel buildings from the pawn's own inventory. [C424][C424], S06. | **Implemented; ordinary scenarios and bounded direct-command recovery verified.** Independent review confirms one recovery closure, harmless duplicate handling, exact successor payment and retained keep quantities. The reporting correction passed checks and complete evidence reads in both PowerShell versions; the original intentional-error report remains preserved. | Verify actual recovery menus/confirmation, natural scheduling, save/reload and full restart, multiplayer, other recovery branches and supported consumer/mod integrations. |
| F10 | Picking up an item/stack displays “Haul everything nearby.” [C425][C425], S01. This is the reporter's activity-name observation, not an explicit request to rename it. | **Resolved: activity wording corrected.** Root and independent QA verified the actual report method, JobDef binding and matching text in all 16 languages. English now says “gathering items into inventory.” [Verification](f10-activity-wording-verification.json), [scope review](feedback-completion-scope-review.md). Commit `cacbf95`. | None for the wording correction. One activity-label smoke check remains part of final assembled-build validation. Custom Alerts and drafted commands are resolved separately as F08/F07. |
| F11 | Drop a specific quantity rather than a whole stack. [C425][C425], S02. | **Resolved: quantity UI, saved remainder and provider/fault recovery verified.** Actual UI63/63, original restart45/45, CE/Sidearms82/82 and final five-fault recovery135/135 are independently accepted. All387 items and original job/Keep/tag/provider state remain stable302ticks; the one declared fault diagnostic is retained. Tested recovery sources match the PR workspace. [Resolution](evidence/f11-resolution.md). | None for the reported single-player quantity-drop requirement. Actual two-client Multiplayer and final assembled-product interactions remain explicit integration work. |
| F12 | Explicit hauling from one place to another. [C425][C425], S03. | **Point-haul backend and prompt correction verified; human UI remains partial.** Saved order/lifecycle/fault witnesses remain accepted. Final robot UI56/56 with12 actual inputs and exact7/15 delivery passes; root inspected readable opaque robot/Italian prompts. Human direct deliveries and stale controls pass, but held-Shift and checkbox queue input are not verified; the checkbox companion is94/96, not a full pass. [Prompt review and limits](evidence/f12-explicit-ui-20260924/ROOT-PROMPT-REVIEW.md). | Finish actual checkbox queue and remaining progress/Resume/Cancel UI, with read-only native polled-input/drag-result evidence; verify held-Shift separately. Preserve failed captures instead of assigning UI state. Final combined-product and two-client Multiplayer checks remain. |
| F13 | Explicitly deliver to a selected shelf. [C425][C425], S04. | **Native shelf passability and truncated-capacity fixes verified.** The same-host control fails the intended boundary45/48; corrected native candidate54/54 verifies selected225 capacity, real competing claim deduction to150, exact7/15 delivery and300 stable ticks. Basic lifecycle progresses54/56 through alternate cell, filter/moved identity and partial3/cancel4, then stops on a fixture assumption that a second current-destination command should be admitted. No save was produced. [Accepted correction](evidence/f13-selected-shelf-20260924/ROOT-LATE-CAPACITY-REVIEW.md), [Part2 stop](evidence/f13-selected-shelf-20260924/native-fixture/PART2-COMPETITION-STOP.md). | Correct the competition fixture to require unchanged refusal when another actual current job already targets the cell; separately stage genuine queued intents for activation-time races if needed. Finish remaining native incoming/concurrency and original producer-resume-resaved chain, then named storage providers and shelf UI. Final combined/Multiplayer remains. |
| F14 | Robot successfully gathered enough materials to construct a building. [C426][C426], S01. | **Resolved: positive robot-construction behavior preserved.** Actual Builder III collected 100 wood into inventory, delivered it and completed construction through its own work logic, with unchanged roles and exact consumption. [Resolution](evidence/f14-resolution.md). | None for this representative positive report. The unspecified robot model/hidden-job explanation is not inferred; F05/F06 robot hauling/commands are now separately resolved. |
| F15 | Repeated rox-wool/item merging; As Above So Below 2 suspected. [GH256][GH256]. | **Investigated; related correction and diagnostic milestones passed.** | Reproduce and fix the actual repeated job. Candidate scanning can inflate the warning without representing executed haul failures. |
| F16 | Erratic apples/smokeleaf stocking despite disabling “Top existing stacks.” [GH261][GH261], [C019][C019]. | **Open; related loop investigation underway.** | Trace this specific report and verify a correction. It is not established as the same cause as F15, F17 or hopper refilling. |
| F17 | Food repeatedly hauled/dropped into crates. [GH268][GH268]. | **Investigated; unresolved.** Stack gap workaround was retracted. | Actual executed-job reproduction and correction; do not close using the temporary workaround. |
| F18 | Several pawns overfill high-priority storage because incoming cargo is ignored. [GH270][GH270]. | **Design/component work in progress.** Baseline and projection checks completed. | Integrated allocation and real concurrent hauling tests. This report names v1.21, so it does not prove v1.24 regressed. |
| F19 | Cannot edit an earlier submitted report. [GH261][GH261], separate reporting subject. | **Support resolved: corrective replies to the same report are supported and documented.** Current client/backend and existing relay were independently reviewed. Original-text editing remains unsupported; no edit feature is claimed. Guide committed as `7f91579`. [Disposition](evidence/f19-f44-resolution.md). | None for the correction-workflow support request. The original submission stays unchanged; F16 gameplay remains open. |

## Other feedback and current dispositions

| ID | Feedback and sources | Current status | What remains |
|---|---|---|---|
| F20 | Repeated full-stack hopper refills; support Storage Refill Hysteresis. [GH269][GH269], [C005][C005], [C006][C006]. | **Resolved and committed as `cde74d3`.** Actual baseline reproduces paused-shelf delivery and automatic top-ups; the candidate passes59/59 assertions, respecting refill gates and preserving in-flight deliveries. [Resolution](evidence/f20-resolution.md). | None for the demonstrated SRH destination-selection gap. Historical232-mod/VNPERP runtime is not claimed; unrelated overdelivery reports remain separate. |
| F21 | Stack gap limits/item disappearance; KanbanStockpileContinue alternative. [C001][C001], [C031][C031], [C032][C032]. | **Open.** The claimed Stack gap success was corrected to OgreStack usage. | Test limit changes and item conservation, and identify the actual supported integrations. Common Sense conflict is recalled, not proven. |
| F22 | Skipdoor Pathing compatibility. [C001][C001]. | **Actual provider investigation complete; native verification pending.** Current Redux and Unlimited binaries have different integration behavior. [Investigation](evidence/f22-skipdoor-20260924/HANDOFF.md). | Verify actual portal pickup/delivery with Redux and resolve Unlimited destination rewriting/drafted-job replacement without claiming an unsafe adapter is compatible. |
| F23 | Automatic/ordinary prioritized bulk hauling intermittently disappears in an established save. [C002][C002], [C008][C008], [C010][C010], [C011][C011], [C012][C012]. | **Open.** Quicktest worked; removing Simple Sidearms was only a temporary workaround. | Trace eligibility and job selection in the failing save, then verify automatic, ordinary prioritized and explicit commands separately. |
| F24 | Harvesting, chopping and deconstruction stop after one/few tasks to haul; finish the growing area first. [C003][C003], [C004][C004], [C022][C022]. | **Resolved and committed as `137a905`.** Two scheduler corrections prevent premature storage trips. Independent native review accepts59/59 checks across harvesting, grow-zone trees/stumps and sowing, deconstruction and default timing, with exact output stored. [Resolution](evidence/f24-resolution.md). | None for the reported premature-trip behavior. A distinct older stranded-cargo test remains an explicitly unresolved diagnostic lead; final assembled work/cargo checks remain separate. |
| F25 | Big & Small sapient animals/mechs lose bulk hauling after save/load or restart. [GH263][GH263], [C023][C023], [C025][C025]. | **Resolved and committed as `70bd490`: sapient-pawn conversion and persistence.** Original lifter/monkey save upgrade, physical hauling, partial tagged cargo, Keep/opt-out, same-process load and fresh restart are independently accepted. Final restart65/65 preserves23 units per actor and finishes21 stored+2 kept, stable300 ticks; provider-absent42/42 passes. [Resolution](evidence/f25-resolution.md). | None for the demonstrated generated-component defect. Missing historical settings/tags cannot be recovered. Exact historical reporter binaries were unavailable; preserve the conditional XML in final assembled-build integration. |
| F26 | Intermittent missing GUI, startup/quicktest exceptions and crashes. [GH262][GH262], [GH264][GH264], [GH265][GH265], [C015][C015], [C017][C017]. | **Open.** Different UI outcomes retained; load-order workaround retracted. | Isolate first exceptions and real mod interactions across repeated starts. TD Enhancement, HugsLib, Allow Tool, Defensive Positions and the five-mod removal list are leads, not confirmed culprits. |
| F27 | Severe lag and blocked mech gestation, relieved after removing mood-bar/construction mods. [C014][C014]. | **Open; separate report.** | Reproduce gestation and performance symptoms; determine cause without inheriting the diagnosis of F26. |
| F28 | Matter Network falls back to single-item hauling. [C018][C018]. | **Open compatibility report.** | Verify and support the actual network pickup/delivery route. |
| F29 | Caravan/horse unloading inconsistently uses storage commitments. [GH260][GH260], [C020][C020]. | **Open; minimal reproduction supplied.** | Run the actual return/unload route with Harmony + HD; verify commitment lifecycle, partial delivery and interruption. |
| F30 | WVC Work Modes recharges mechs before they empty inventory. [C024][C024]. | **Open compatibility report.** | Coordinate unloading and work-mode/recharge transitions without preventing necessary charging. |
| F31 | Builders and material haulers should choose nearby construction tiles. [C009][C009]. | **Open request.** | Improve and verify routing for automatic construction, delivery and planned orders. |
| F32 | Materials next to construction are hauled away. [C026][C026]. | **Open.** Vanilla attribution is only a question. | Establish ownership/useful-material retention and prevent unwanted removal without trapping excess supplies. |
| F33 | “Deliver resources” proceeds to building with Build unassigned. [GH259][GH259]. | **Resolved: delivery-only orders no longer force construction.** Four native scenes and all 69 assertions passed independent review. Explicit Construction/HaulBuild still build; HaulOnly retires prior sustained Construction while permitting ordinary nonforced work. Commit `14ce252`. [Resolution](evidence/f33-resolution.md). | None for the reported defect or demonstrated HaulOnly continuation. One targeted final assembled-route/Multiplayer interaction check remains alongside F34. [Integration checks](final-integration-checks.md). |
| F34 | Blight-cut plan also selects healthy plants. [C027][C027]. | **Resolved: blight plans preserve healthy crops in single-player.** The prior build reproduced the defect; all 18 corrected selection/job scenarios and one real productive-cutting run passed independent review. Native preview polls updated five→four→five targets; all five infected plants were cut and unrequested healthy plants survived. [Runtime review](evidence/f34-live-20260920/runtime-review.md). | None for the reported single-player misdesignation. Targeted Multiplayer command replay and ordinary confirmation-button input remain explicit final combined-build checks; neither is claimed verified. [Integration checks](final-integration-checks.md). |
| F35 | Finishing wild animals does not trigger haul-after-slaughter. [C007][C007]. | **Resolved and committed as `604a764`.** Both provider baselines reproduce the missing haul; Allow Tool and Keyz candidates preserve queued work and deliver the same corpse. Disabled/forbidden/cancel controls and all 16 translated descriptions are accepted. [Resolution](evidence/f35-resolution.md). | None for the supported providers. The reporter's exact provider/historical cause is unknown; final integration remains separate. |
| F36 | Is Build From Storage replaced or compatible? [C029][C029], [C028][C028]. | **Resolved and committed as `c785e66`.** Build From Storage complements HD gathering. The matched baseline reproduces three invalid install-material queries; the corrected candidate passes 52/52, installing the exact packed building and constructing another with exact material consumption. [Resolution](evidence/f36-resolution.md). | None for the actual provider pairing and affected route paths. Final assembled route/Multiplayer checks remain separate. |
| F37 | Items stay reserved while a pawn sleeps or does unrelated work. [GH255][GH255]. | **Open.** Reload clears it; removal of HD stopped observed recurrence. | Track the real reservation owner/lifetime and verify interruption, sleep and reload. No original incident logs establish sole cause. |
| F38 | A second forced haul cancels the first pawn's whole nearby sweep. [GH266][GH266]. | **Resolved and committed as `9549cad`.** A second explicit haul hands off its selected source while preserving the first sweep and queued work. Native baseline/candidate, original-save continuation, constrained storage and two fresh replay consumers are independently accepted. [Resolution](evidence/f38-resolution.md). | None for the reported native/bulk source handoff. Actual two-client synchronization and final assembled-build interaction remain in final integration. |
| F39 | Bulk-carry corpses through map portals. [GH257][GH257]. | **Open feature request.** Report names v1.23. | Implement/confirm portal corpse admission, capacity and multiple-corpse trips. Ordinary corpse storage hauling is different. |
| F40 | Bulk-unload transporters: toggle, chained prioritized unloading, load/unload exclusion, corpse/CE handling; consistent loading-order behavior. [GH267][GH267]. | **Original-save replanning and CE cargo branches verified.** Persistence59/59, disabled-start52/52, CE producer53/53 and fresh consumer71/71 are independently accepted, all zero errors. Original5-inventory/7-hands and queued Wait survive restart; all12 are stored. Zero-CE-bulk massless cargo and four actual corpse policy branches finish in correct storage/native Graves with conserved identities and300-tick stability. [Persistence](evidence/f40-transporter-20260924/persistence/ROOT-CORRECTED-CONSUMERS-REVIEW.md), [CE continuation review](evidence/f40-transporter-20260924/ce-carrier-v6b/ROOT-CONSUMER-REVIEW.md). | Finish actual shuttle/VF lifecycle and rendered controls; final assembled/network checks remain. The earlier CE consumer setup failure remains recorded but its corrected successor is accepted. |
| F41 | Repair unfinished items bound to orphaned bills after load; WorkbenchConnect association. [GH148][GH148]. | **Resolved: orphan-bill recovery and clean restart independently accepted.** Corrected recovery passes59/59 and fresh-process restart43/43; original work/ingredients and unrelated jobs are preserved, both serialization orders are safe, and repairs do not repeat. [Resolution](evidence/f41-resolution.md). | None for the verified null-stack orphan repair. Actual WorkbenchConnect serializer/causation is not claimed. Exact-source commit reconciliation and final assembled load smoke remain integration work. |
| F42 | Better Workbench Management/product-counting exception interrupts a lifter haul. [T01-R01][T01-R01]. | **Open.** Patch names in the trace do not establish blame. | Reproduce actual bill/product state and fix or contain HD's provoking path. This is distinct from the older storage opening post. |
| F43 | Cloth haul-loop warning, CanGiveJob/JobOnX synchronization warning, and whether another mod should be removed. [C021][C021]. | **Resolved and committed as `825435b`.** Native baseline reproduces both warnings before any cloth moves; candidate54/54, zero errors, delivers all10 cloth and stays stable301ticks. Removed the faulty query counter; actual failure/retarget protections remain. Player guidance explains both messages. [Resolution](evidence/f43-resolution.md). | None for this warning/support report. The unavailable historical diagnostic prevents attribution of every possible original cause. Actual physical loops remain F15/F17; assembled-build regression remains final integration. |
| F44 | Cannot create a Steam discussion, so uses another person's topic. [T01-R01][T01-R01], separate support subject. | **Support resolved: accessible alternate reporting routes documented.** Use the in-game reporter or public GitHub tracker; endpoint and tracker access were checked without posting. Guide committed as `7f91579`. [Disposition](evidence/f19-f44-resolution.md). | None for alternate reporting support. The unspecified Steam restriction is not diagnosed or repaired; F42 gameplay remains open. |
| F45 | Periodic Bills' repeat mode is replaced by HD's batch option; changing load order did not help. [GH271][GH271]. | **Resolved: supported repeat modes remain available alongside HD batching.** Both load orders, actual production, four checkpoint restarts, all 52 shared-provider cases and the changed-code failure controls passed independent review. Commit `cb61d20`. [Resolution](evidence/f45-resolution.md). | None for the reported menu compatibility defect. PB's copy/paste settings reset also occurs without HD and remains an explicit upstream limitation. Presentation, multiplayer completion and final assembled-build checks remain in [final integration](final-integration-checks.md). |
| F46 | Is the mod performance-heavy; suitable for larger colonies? [T12-OP][T12-OP]. | **Newly catalogued; unanswered.** | Measure representative larger-colony workloads and provide an evidence-backed answer, including settings tradeoffs. |

## Context and feedback that creates no new fix

| Sources | Feedback | Disposition |
|---|---|---|
| [C013][C013], [C033][C033], [C422][C422] | Maintainer availability/update acknowledgements and request for diagnostic reports. | Context only; no separate feature or defect. |
| [C016][C016] | Concern about AI-assisted development and past save damage, without a new reproduction. | Preserve as quality/trust feedback; does not establish a new current save bug. |
| [C030][C030], [C427][C427] | Praise for consolidating hauling mods; C427 says the mod still seems to need polish without specifying a defect. | Context only; no invented implementation task. |
| [C423][C423], [C425][C425] | Praise for inventory construction, pickup delay, full vehicle loading, construction gathering, keep-in-inventory and settings UI. | Preserve these behaviors while addressing the specific requests above. Praise is not a new feature request. |
| [T01-R02][T01-R02] | Steam moderation placeholder. | **Content unavailable.** Date is in range; no defect or resolution can be inferred from the hidden body. |
| [GH251-C5231994333][GH251-C5231994333] | Post-release acknowledgement of Chinese translation consistency work; contributor planned an in-game check. | PR #251 was already merged before the release. No new correction is requested; planned checking is not a completed test. |
| [GH148-C4903415050][GH148-C4903415050], [GH267-C5407844472][GH267-C5407844472] | Changeset-bot notices on open PRs. | Metadata only; neither proves merge or release. |
| [GH261-C5331573201][GH261-C5331573201] | Dot-only follow-up. | No additional technical feedback. |

## Recurrence and exclusions

**Current recurrence leads:** GH258 follows the explicit v1.24 Common Sense fix claim. GH256/GH268 and the work-abandonment reports resemble earlier failures but still need cause-specific proof. C002 and GH268 retract apparent workarounds; GH265 retracts a load-order cure. These remain open.

**Confirmed recurring design omission:** F45 exposes the same menu-replacement approach that previously required separate fixes for Everybody Gets One, Compositable Loadouts and Ingredient Threshold. Those fixes did not preserve contributions from an additional mod. This is evidence of an underlying integration problem recurring across mods, not proof that Periodic Bills itself was previously fixed. [Cause and earlier fixes](periodic-bills-compatibility.md).

**Related intent/permission mistake, not proven exact recurrence:** F33 confused forced delivery with permission to construct, and its HaulOnly route retained native Construction priority. Earlier capability/assignment fixes are related, but do not establish a previous exact GH259 fix that regressed. Both demonstrated paths are now corrected and independently verified. [Diagnosis](evidence/f33-delivery-intent-review.md), [resolution](evidence/f33-resolution.md).

**Historical evidence only:** pre-release Steam reports such as T01-OP (7 August), T07-R06 and C390; the old RimIOT, hospital and portal fix chains; and closed PR #249, whose later metadata update does not supply a qualifying new comment. They may inform included work, but do not independently expand this backlog. The two Steam comments posted earlier on 9 August (C034/C035) precede the release and are also history. Active PR #148 is explicitly included by the open-PR rule.

Newly found C427, T12-OP, GH271 and GH251-C5231994333 receive stable IDs in this scoped catalogue. They were absent from the older 569-source register. The original register is preserved unchanged; do not use its full count or historical “active” labels as the remaining workload. Before continuing implementation, use this file to select work and carry its scope restriction into any detailed acceptance mapping. Do not delete prior implementations or evidence merely because an original report is now historical.

The source index below supplies dates and direct links. Follow-up replies are included with their parent report; cross-posts retain their identities without counting as independent defects.

The [scoped machine-readable register](scoped-feedback-register.json) retains these 78 sources, their dispositions, the 46 requirement rows, report-specific completion states and separate integration requirements. It preserves the original historical register separately. New implementation evidence must update the affected row and its focused plan; context entries do not create additional feature obligations.

## Included source index

All timestamps below are **2026, Copenhagen (CEST)**. F-numbers refer to the status tables above. GitHub dates are original posting dates; open PR #148 and its July replies qualify by current open status.

### Steam Workshop — 39 comments

| Source | Posted | Status / coverage |
|---|---|---|
| [C427][C427] | 15 Sep 13:47:23 | Context; no separate fix |
| [C426][C426] | 08 Sep 01:41:45 | F14 |
| [C425][C425] | 07 Sep 18:44:04 | F10–F13; praise retained |
| [C424][C424] | 07 Sep 18:32:23 | F02, F05–F09 |
| [C423][C423] | 07 Sep 04:32:54 | F03, F04; praise retained |
| [C422][C422] | 07 Sep 00:47:50 | Context; no separate fix |
| [C001][C001] | 05 Sep 21:56:49 | F21, F22 |
| [C002][C002] | 02 Sep 12:07:27 | F23 |
| [C003][C003] | 30 Aug 21:26:17 | F24 |
| [C004][C004] | 30 Aug 19:35:14 | F24 |
| [C005][C005] | 29 Aug 22:06:27 | F20 |
| [C006][C006] | 29 Aug 22:03:08 | F20 |
| [C007][C007] | 27 Aug 09:36:53 | F35 |
| [C008][C008] | 27 Aug 00:15:02 | F23 |
| [C009][C009] | 26 Aug 03:54:20 | F31 |
| [C010][C010] | 25 Aug 16:29:53 | F23 |
| [C011][C011] | 25 Aug 16:01:42 | F23 |
| [C012][C012] | 25 Aug 15:20:49 | F23 |
| [C013][C013] | 24 Aug 13:27:19 | Context; no separate fix |
| [C014][C014] | 23 Aug 23:31:13 | F27 |
| [C015][C015] | 22 Aug 10:09:31 | F26 |
| [C016][C016] | 22 Aug 04:58:16 | Context; no separate fix |
| [C017][C017] | 19 Aug 13:35:36 | F26 |
| [C018][C018] | 17 Aug 17:23:56 | F28 |
| [C019][C019] | 17 Aug 17:02:22 | F16 |
| [C020][C020] | 17 Aug 02:02:33 | F29 |
| [C021][C021] | 16 Aug 23:59:49 | F43 |
| [C022][C022] | 16 Aug 05:45:13 | F24 |
| [C023][C023] | 14 Aug 20:53:41 | F25 |
| [C024][C024] | 14 Aug 19:43:26 | F30 |
| [C025][C025] | 14 Aug 03:53:47 | F25 |
| [C026][C026] | 13 Aug 22:27:18 | F32 |
| [C027][C027] | 13 Aug 21:22:12 | F34 |
| [C028][C028] | 12 Aug 20:20:29 | F36 |
| [C029][C029] | 12 Aug 19:22:13 | F36 |
| [C030][C030] | 11 Aug 02:32:59 | Context; no separate fix |
| [C031][C031] | 09 Aug 17:17:11 | F21 |
| [C032][C032] | 09 Aug 16:47:05 | F21 |
| [C033][C033] | 09 Aug 16:37:50 | Context; no separate fix |

### Steam discussions — three posts

| Source | Posted | Status / coverage |
|---|---|---|
| [T12-OP][T12-OP] | 13 Sep 11:49:10 | F46 |
| [T01-R02][T01-R02] | 17 Aug 16:22:09 | Unavailable body; no inferred fix |
| [T01-R01][T01-R01] | 17 Aug 16:11:30 | F42, F44 |

### GitHub — 18 open items and 18 conversation replies

| Source | Posted | Status / coverage |
|---|---|---|
| [GH271][GH271] | 11 Sep 01:45:40 | F45 |
| [GH270][GH270] | 01 Sep 16:00:19 | F18 |
| [GH269][GH269] | 29 Aug 22:13:13 | F20 |
| [GH268-C5461920982][GH268-C5461920982] | 29 Aug 12:54:25 | F17 |
| [GH268-C5430010324][GH268-C5430010324] | 26 Aug 21:20:46 | F17 |
| [GH268][GH268] | 26 Aug 20:52:08 | F17 |
| [GH267-C5407936551][GH267-C5407936551] | 25 Aug 10:57:07 | F40; contributor testing planned |
| [GH267-C5407844472][GH267-C5407844472] | 25 Aug 10:50:04 | Bot metadata; unmerged PR |
| [GH267][GH267] | 25 Aug 10:50:00 | F40 |
| [GH266][GH266] | 22 Aug 17:30:53 | F38 |
| [GH265-C5379263383][GH265-C5379263383] | 22 Aug 10:21:25 | F26 |
| [GH262-C5379178652][GH262-C5379178652] | 22 Aug 10:07:27 | F26 |
| [GH265][GH265] | 22 Aug 08:33:20 | F26 |
| [GH264][GH264] | 22 Aug 06:44:38 | F26 |
| [GH262-C5377860369][GH262-C5377860369] | 22 Aug 06:26:10 | F26 |
| [GH262-C5369956723][GH262-C5369956723] | 21 Aug 14:45:07 | F26 |
| [GH263-C5360067014][GH263-C5360067014] | 20 Aug 20:29:39 | F25 |
| [GH263][GH263] | 20 Aug 20:04:42 | F25 |
| [GH262-C5341477114][GH262-C5341477114] | 19 Aug 13:26:45 | F26 |
| [GH262][GH262] | 19 Aug 13:23:12 | F26 |
| [GH261-C5331573201][GH261-C5331573201] | 18 Aug 19:11:29 | Dot-only reply; no new feedback |
| [GH261][GH261] | 17 Aug 17:24:21 | F16, F19 |
| [GH260][GH260] | 17 Aug 02:01:53 | F29 |
| [GH259][GH259] | 16 Aug 20:14:37 | F33 |
| [GH255-C5303554026][GH255-C5303554026] | 15 Aug 20:08:26 | F37 |
| [GH258][GH258] | 13 Aug 19:47:38 | F01 |
| [GH257][GH257] | 10 Aug 16:59:52 | F39 |
| [GH256-C5241237484][GH256-C5241237484] | 10 Aug 15:54:45 | F15 |
| [GH256][GH256] | 10 Aug 15:01:19 | F15 |
| [GH255-C5236650604][GH255-C5236650604] | 10 Aug 08:26:14 | F37 |
| [GH255-C5234743392][GH255-C5234743392] | 10 Aug 02:46:26 | F37 |
| [GH255][GH255] | 09 Aug 20:13:30 | F37 |
| [GH251-C5231994333][GH251-C5231994333] | 09 Aug 16:21:41 | Already merged; acknowledgement only |
| [GH148-C4904961695][GH148-C4904961695] | 07 Jul 16:33:43 | F41; review requested when ready |
| [GH148-C4903415050][GH148-C4903415050] | 07 Jul 13:45:58 | Bot metadata; unmerged PR |
| [GH148][GH148] | 07 Jul 13:45:55 | F41 |

The refresh read the newest 100 Workshop comments through well before the release boundary; all 39 qualifying comments were retained. The discussion index lists all 12 topics; only the two topics with post-cutoff activity have qualifying posts. All GitHub list/comment endpoints were paginated. Closed PR #251 is included for its later comment; closed PR #249 has no qualifying later comment. The original source IDs and dates were checked against the earlier register. Implementation statuses come from the linked focused plans and recorded results, not from assuming that an open/closed tracker state proves behavior.

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
[C013]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999473479
[C014]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999435100
[C015]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999285398
[C016]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813130434387941
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
[C030]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434705716618225
[C031]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320103811
[C032]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320101744
[C033]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320101201
[C422]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292699148
[C423]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292709966
[C424]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292755974
[C425]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292756958
[C426]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292789520
[C427]: https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c565919666444422349
[GH148]: https://github.com/Refzlund/haulers-dream/pull/148
[GH148-C4903415050]: https://github.com/Refzlund/haulers-dream/pull/148#issuecomment-4903415050
[GH148-C4904961695]: https://github.com/Refzlund/haulers-dream/pull/148#issuecomment-4904961695
[GH251-C5231994333]: https://github.com/Refzlund/haulers-dream/pull/251#issuecomment-5231994333
[GH255]: https://github.com/Refzlund/haulers-dream/issues/255
[GH255-C5234743392]: https://github.com/Refzlund/haulers-dream/issues/255#issuecomment-5234743392
[GH255-C5236650604]: https://github.com/Refzlund/haulers-dream/issues/255#issuecomment-5236650604
[GH255-C5303554026]: https://github.com/Refzlund/haulers-dream/issues/255#issuecomment-5303554026
[GH256]: https://github.com/Refzlund/haulers-dream/issues/256
[GH256-C5241237484]: https://github.com/Refzlund/haulers-dream/issues/256#issuecomment-5241237484
[GH257]: https://github.com/Refzlund/haulers-dream/issues/257
[GH258]: https://github.com/Refzlund/haulers-dream/issues/258
[GH259]: https://github.com/Refzlund/haulers-dream/issues/259
[GH260]: https://github.com/Refzlund/haulers-dream/issues/260
[GH261]: https://github.com/Refzlund/haulers-dream/issues/261
[GH261-C5331573201]: https://github.com/Refzlund/haulers-dream/issues/261#issuecomment-5331573201
[GH262]: https://github.com/Refzlund/haulers-dream/issues/262
[GH262-C5341477114]: https://github.com/Refzlund/haulers-dream/issues/262#issuecomment-5341477114
[GH262-C5369956723]: https://github.com/Refzlund/haulers-dream/issues/262#issuecomment-5369956723
[GH262-C5377860369]: https://github.com/Refzlund/haulers-dream/issues/262#issuecomment-5377860369
[GH262-C5379178652]: https://github.com/Refzlund/haulers-dream/issues/262#issuecomment-5379178652
[GH263]: https://github.com/Refzlund/haulers-dream/issues/263
[GH263-C5360067014]: https://github.com/Refzlund/haulers-dream/issues/263#issuecomment-5360067014
[GH264]: https://github.com/Refzlund/haulers-dream/issues/264
[GH265]: https://github.com/Refzlund/haulers-dream/issues/265
[GH265-C5379263383]: https://github.com/Refzlund/haulers-dream/issues/265#issuecomment-5379263383
[GH266]: https://github.com/Refzlund/haulers-dream/issues/266
[GH267]: https://github.com/Refzlund/haulers-dream/pull/267
[GH267-C5407844472]: https://github.com/Refzlund/haulers-dream/pull/267#issuecomment-5407844472
[GH267-C5407936551]: https://github.com/Refzlund/haulers-dream/pull/267#issuecomment-5407936551
[GH268]: https://github.com/Refzlund/haulers-dream/issues/268
[GH268-C5430010324]: https://github.com/Refzlund/haulers-dream/issues/268#issuecomment-5430010324
[GH268-C5461920982]: https://github.com/Refzlund/haulers-dream/issues/268#issuecomment-5461920982
[GH269]: https://github.com/Refzlund/haulers-dream/issues/269
[GH270]: https://github.com/Refzlund/haulers-dream/issues/270
[GH271]: https://github.com/Refzlund/haulers-dream/issues/271
[T01-R01]: https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691114229
[T01-R02]: https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691115322
[T12-OP]: https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564793766239611588/
