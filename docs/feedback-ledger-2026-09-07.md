# Hauler’s Dream — unified feedback and recurrence ledger

**19 September 2026 scope correction:** This is the preserved historical ledger. Use [feedback since the last update: current status](plans/feedback-since-last-update.md) for the working catalogue. Older Steam reports remain recurrence/context evidence, rather than standalone obligations. The new catalogue applies the 9 August release cutoff, retains active GitHub issues/PRs and qualifying later comments, and includes the latest newly received feedback. Historical source IDs below are preserved.

Initial snapshot: **7 September 2026**; append-only additions through **8 September 2026** are recorded below. GitHub/topic status retains its original collection cutoff. This single file combines the complete Steam collection with every currently open GitHub issue and pull request, then connects those reports to earlier fix claims, retractions and retests. It supersedes the earlier Steam-only report for GitHub status and recurrence assessment.

**Latest published Workshop update: v1.24.0, 9 August 2026 at 16:13:10 CEST (14:13:10 UTC; Unix 1786284790).** Steam’s item metadata and change-note timestamp agree. The corresponding GitHub release was published four seconds later, at 14:13:14 UTC; it is a separate publication timestamp. [Steam change notes](https://steamcommunity.com/sharedfiles/filedetails/changelog/3742459652) · [GitHub v1.24.0](https://github.com/Refzlund/haulers-dream/releases/tag/v1.24.0).

## Read this first

**Yes: the history contains repeated attempts that missed the mechanism actually producing the reported behavior.** The strongest documented examples are destination-capacity accounting, RimIOT loops, hospital haul-aside loops, shrinking portal loads, dropped crop cargo, and Common Sense integration. These are not inferred solely from similar complaint wording: later PRs explicitly identify why earlier fixes did not work, and several have intervening user reports on newer builds.

For the current backlog, **Common Sense gathering (#258) is the clearest report contradicting an explicit fix in the installed version**: it reports v1.24.0, whose release notes claim that exact gathering configuration works. Work abandonment after a single task is also a strong recurring symptom, but the recent Steam commenters do not state their installed versions. Current food/wool loops need deeper tracing because their symptoms resemble several already-distinct historical mechanisms. [#258](https://github.com/Refzlund/haulers-dream/issues/258) · [v1.24.0](https://github.com/Refzlund/haulers-dream/releases/tag/v1.24.0) · [C003](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001633990) [C004](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001626310).

**Do not count recent dates as proof of current regressions.** Open #270 is on v1.21.0; #257 is on v1.23.0; closed #247 was on v1.19.0. The storage topic’s actual over-delivery reproduction is after v1.22.0 but before v1.24.0. Its later BWM exception reply is a different issue. PRs #148 and #267 are still unmerged; closed PR #249 also never merged. [#270](https://github.com/Refzlund/haulers-dream/issues/270) · [#257](https://github.com/Refzlund/haulers-dream/issues/257) · [#247](https://github.com/Refzlund/haulers-dream/issues/247) · [T01-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/) [T01-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691114229).

## Coverage and how to use the ledger

| Collected source | Coverage |
|---|---|
| Steam Workshop comments | **426**: 421 original all-page entries, C422 goal-start refresh and C423–C426 targeted additions; 425 readable bodies |
| Steam discussion topics | **11** opening posts plus **98** replies; 108 readable bodies |
| Steam total | **535 entries**, **533 readable**; two moderation-hidden bodies retained as metadata |
| Open GitHub issues | **15**: #255–#266, #268–#270 |
| Open GitHub PRs | **2**: #148 (draft), #267 (not draft) |
| Conversation comments on those 17 GitHub items | **17**: 13 issue comments and 4 PR comments, including two changeset-bot notices and one dot-only reply |
| Open-PR review coverage | Both review lists and all paginated inline-review-comment endpoints inspected; **0 submitted reviews and 0 inline comments** returned |
| Unified source register | **569 primary entries**: 535 Steam entries + 17 open GitHub bodies + 17 GitHub conversation comments |
| Grouped ledger | **55 topic records**, including product feedback and context; this is not a count of 55 independent bugs |
| Historical evidence collected | 97 issues and 167 PRs (264 total), 240 conversation comments and 65 releases; relevant prior fixes and follow-ups examined for recurrence |

All 535 Steam references appear in the source register inside this file, with their author, exact Copenhagen timestamp, paraphrase and direct link. Every entry maps to at least one ledger record. A message touching two subjects maps to both; those mappings are not additional reports. GitHub records retain all conversation follow-ups and evidence links. Closed historical items are supporting evidence, not silently added to the open backlog.

Steam includes ten locked topics and one unlocked topic. A lock is not a resolution. **40 Steam entries postdate the Workshop update**, including developer replies and one hidden body; posting after the cutoff does not establish an installed version. C240 and T01-R02 display moderation placeholders, so their content is unavailable. Their bodies were not reconstructed.

“Recurrence” below means a report persisted or returned after a claim of resolution. It does **not** necessarily mean code was fixed successfully and later regressed; several examples were incomplete fixes all along. “Reported” preserves user evidence; “claimed fixed” preserves developer/release evidence; “confirmed retest” requires an explicit reporter result. Technical explanations from PRs are attributed to those investigations. This work did not reproduce bugs in RimWorld, independently validate every PR diagnosis, or inspect the contents of every linked external log/video/save. Those evidence links remain available for the next investigation.

## Where to investigate first

1. **Reproduce the exact v1.24.0 Common Sense failure** ([L01](#l01)). It has the cleanest old-fix/current-report match. Test configuration and actual driver selection together.
2. **Instrument the active haul/drop loops and work abandonment** ([L04](#l04), [L07](#l07), [L08](#l08)). Capture executed jobs and material progress before adding another generic backoff or assigning another mod as cause. Keep each reporter’s scenario separate until traces connect them.
3. **Use the reduced save/load and arrival-unload reproductions** ([L09](#l09), [L17](#l17)). They directly test the state changes and game entry points that earlier unit-only checks missed. Investigate the startup cluster and draft orphan-bill fix as separate failure paths ([L10](#l10), [L22](#l22)).
4. **Verify v1.24.0 storage behavior in-game, including limit mods** ([L02](#l02), [L03](#l03)). Historical repeated misses justify this even though #270 does not prove the latest rewrite failed.
5. **Keep previously solved hospital, RimIOT and portal scenarios as release fixtures** ([L05](#l05), [L06](#l06), [L19](#l19)). A short successful retest alone already proved insufficient for RimIOT.

The needed improvement is specific: **test the real job path and its inputs, not just the rule it is supposed to call**. PR #252 says prior storage logic never reached vanilla’s actual counting path and used a stale per-tick view. PR #244 says tests could observe the Core rule but not the arguments supplied by the runtime adapter. PR #175 says instrumentation of the actual job finally falsified the storage/failure assumptions. Both #241 and #252 explicitly report no in-game verification by their authors. Passing those checks therefore did not establish the reported game scenario was fixed. [Storage investigation](https://github.com/Refzlund/haulers-dream/pull/252) · [Portal investigation](https://github.com/Refzlund/haulers-dream/pull/244) · [Hospital investigation](https://github.com/Refzlund/haulers-dream/pull/175) · [v1.22 work](https://github.com/Refzlund/haulers-dream/pull/241).

For the next proposed fix in these families, keep one trace/repro that demonstrates the failure on the prior build, verify the proposed build against the same scenario, then repeat through relevant interruptions, save/reload and multiple-pawn execution. Record installed assembly version, settings, mod identities, source/destination, job definition and mode, result, and item counts. A bounded wait or a warning is not sufficient if the colony still makes no useful progress.

## Recurrence evidence and the assumptions that failed

### R01 — Common Sense gathering: current report after the exact release claim

- **June:** T07-R06 reports gathering/crafting trouble even with Common Sense gathering disabled; broad compatibility and optional batching work followed. This is background, not proof of the same precise current cause. [T07-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365458930) · [PR #39](https://github.com/Refzlund/haulers-dream/pull/39) · [PR #68](https://github.com/Refzlund/haulers-dream/pull/68).
- **August 2–3:** #243 is filed on v1.22.0. PR #244 corrects the planned-order gates and notices, but expressly defers narrowing the Common Sense cede condition. Lensrub then says disabling Common Sense resolves unwanted gathering, yet enabling HD’s controls still does not gather. [#243 follow-up](https://github.com/Refzlund/haulers-dream/issues/243#issuecomment-5170583751) · [PR #244](https://github.com/Refzlund/haulers-dream/pull/244).
- **August 9:** PR #252 separates “Common Sense owns the driver” from “Common Sense actually gathers”; v1.24.0 claims gathering works when Common Sense’s own gathering is off. [PR #252](https://github.com/Refzlund/haulers-dream/pull/252) · [v1.24.0](https://github.com/Refzlund/haulers-dream/releases/tag/v1.24.0).
- **August 13:** the same reporter files #258 on **v1.24.0**, describing HD’s global/per-bench switches on and Common Sense’s gathering off, still without gathering. [#258](https://github.com/Refzlund/haulers-dream/issues/258).

**Finding: strong current recurrence evidence at the symptom/configuration level.** The cause of #258 has not been reproduced here. Investigate the full cleaning/gathering configuration matrix, installed Common Sense fork and actual chosen driver rather than assuming the earlier predicate change covered every route.

### R02 — Storage capacity: repeated ineffective fixes, latest rewrite still needs gameplay evidence

- #114 prompted PR #116; #138/PR #139 revisited over-hauling. [#114](https://github.com/Refzlund/haulers-dream/issues/114) · [PR #116](https://github.com/Refzlund/haulers-dream/pull/116) · [PR #139](https://github.com/Refzlund/haulers-dream/pull/139).
- **August 2:** v1.22.0/PR #241 claims multiple haulers now account for cargo already in flight. **August 7:** Eversset reports the same small-deficit over-delivery after v1.22.0 and asks for actual in-game testing. [v1.22.0](https://github.com/Refzlund/haulers-dream/releases/tag/v1.22.0) · [T01-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/).
- **August 9:** PR #252 explicitly says the three previous fixes shipped without solving the reported family. Removing vanilla cell reservations also removed information used by vanilla’s count calculation; separately, a tick-level snapshot hid other pawns planning within the same tick. The fix consolidates accounting and adds adapters to the actual vanilla count/gate paths. The PR reports no in-game verification. [PR #252](https://github.com/Refzlund/haulers-dream/pull/252).
- **September 1:** #270 describes the same symptom but its embedded version is **v1.21.0**. #248 also used an older build (**v1.19.0**). Neither is a verified failure after the v1.24 rewrite. [#270](https://github.com/Refzlund/haulers-dream/issues/270) · [#248](https://github.com/Refzlund/haulers-dream/issues/248).

**Finding: proven historical incomplete fixes; current post-v1.24 recurrence not established.** The strongest deeper test is real same-tick concurrency across both HD and ordinary game haul paths. Numeric stockpile limits/hysteresis remain a separate admitted integration gap in PR #252 and current #269; fixing in-flight capacity does not automatically implement another mod’s refill policy.

### R03 — RimIOT: the symptom returned after a user said it was fixed

| Reported build / event | Evidence and outcome |
|---|---|
| v1.16.10, #177 | Network stacking loop; PR #179 gates network-managed pickup/storage behavior. |
| v1.17.0, #184 | Loop persists. PR #185 covers the terminal’s overflow ground area. Maintainer requests v1.17.1 retest; reporter says an hour passes without trouble. |
| v1.18.0, #192 | Same terminal loop reported again. PR #193 broadens terminal recognition and overflow radius. |
| v1.20.3, #214 | Loop reported again; reporter says rare occurrences had also gone unreported in prior updates. |
| v1.20.4, PR #216 | Investigation finds another mod can rewrite the pickup target after planning. Adds execution-time checks and handling of repeated no-progress successful jobs. No later RimIOT-specific failure is established in this collection. |

[#177](https://github.com/Refzlund/haulers-dream/issues/177) · [#184](https://github.com/Refzlund/haulers-dream/issues/184) · [successful retest](https://github.com/Refzlund/haulers-dream/issues/184#issuecomment-4940397748) · [#192](https://github.com/Refzlund/haulers-dream/issues/192) · [#214](https://github.com/Refzlund/haulers-dream/issues/214) · [PR #216](https://github.com/Refzlund/haulers-dream/pull/216) · [v1.20.4](https://github.com/Refzlund/haulers-dream/releases/tag/v1.20.4).

**Finding: strong documented recurrence, not just duplicate reporting.** PR #185 presented a two-cell overflow radius as exact; #193 later explains why overflow may land farther away. PR #216 then finds that plan-time cell checks cannot constrain a later target rewrite. Repeated spatial exclusions and failure-only guards each missed another execution condition. New generic loop warnings such as #256 do not establish that RimIOT is installed or culpable.

### R04 — Hospital/prison: many storage guards, but the looping job was not a storage haul

The same reporter retested with reduced saves across v1.16.1, v1.16.3, v1.16.4, v1.16.5 and v1.16.7 while developer replies announced successive fixes. #133/#145/#149 focused on failed destination/storage behavior, and #153 corrected a keep-stock unload loop. Those mechanisms can exist, but did not eliminate this reported hospital loop. [T03-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640421351) [T03-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640449430) [T03-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640489340) [T03-R09](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640496737) [T03-R11](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640532134) · [PR #133](https://github.com/Refzlund/haulers-dream/pull/133) · [PR #145](https://github.com/Refzlund/haulers-dream/pull/145) · [PR #149](https://github.com/Refzlund/haulers-dream/pull/149) · [PR #153](https://github.com/Refzlund/haulers-dream/pull/153).

PR #175 records the eventual observation: the pawn was repeatedly moving an item aside between competing work cells, using `ToCellNonStorage`, and every short job succeeded. Storage-mode and failed-job guards could never see it. Available storage also did not prevent the loop because higher-priority haul-aside jobs kept winning. The reporter then confirms the core problems fixed after **v1.16.12**. [PR #175](https://github.com/Refzlund/haulers-dream/pull/175) · [v1.16.12](https://github.com/Refzlund/haulers-dream/releases/tag/v1.16.12) · [T03-R19](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930312327) [T03-R20](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930317422).

**Finding: strongest historical example of repeatedly diagnosing the wrong path; eventual successful user retest.** Preserve it as a regression test, not a newly reopened issue. PR #147 was closed unmerged and replaced by #149; do not count it as another published fix.

### R05 — Portal/shuttle trips: correct arithmetic tested with the wrong inputs

PR #129 addressed one hauler claiming portal loot. #167 then reported shrinking/single-unit loads; #169 addressed transport coordination, but #241 later says the earlier leftover-cargo explanation cannot fit the reporter’s empty-pack single-hauler reproduction. Its fair-share divisor also included mechs that could not do the hauling work. [PR #129](https://github.com/Refzlund/haulers-dream/pull/129) · [#167](https://github.com/Refzlund/haulers-dream/issues/167) · [PR #169](https://github.com/Refzlund/haulers-dream/pull/169) · [PR #241](https://github.com/Refzlund/haulers-dream/pull/241).

After the v1.22 correction, C045 exposed the same single-unit shape when Smart overload had no effective ceiling. PR #244 states that prior tests used finite budgets and a divisor of one, and that the Core tests could not see incorrect runtime arguments. It shipped the additional fix in v1.23.0. [C045](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547636700) [C044](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547656455) · [PR #244](https://github.com/Refzlund/haulers-dream/pull/244) · [v1.23.0](https://github.com/Refzlund/haulers-dream/releases/tag/v1.23.0).

**Finding: documented repeat escape through an omitted settings/input case.** #247’s later date is misleading because it used v1.19.0; the maintainer closed it asking for an updated test. PR #252’s earlier prose saying it was left open is superseded by the actual closure/comment. #257’s corpse portal request, SRTS huge-stack exclusivity and PR #267’s unloading direction are separate coverage questions.

### R06 — Harvested cargo: established-save age and merge/split state escaped tests

PR #65 fixed #62’s immediate dropped yields by guarding vanilla raw-food cleanup; it expressly explains why an aged save can fail while quicktest works. #87 reports the problem again on v1.12.0, now with psychoid leaves. PR #89 identifies a guard reading an unrepaired cargo-tag list after stack splitting/merging, then applies the corrected ownership view to loading paths too. [PR #65](https://github.com/Refzlund/haulers-dream/pull/65) · [#87](https://github.com/Refzlund/haulers-dream/issues/87) · [PR #89](https://github.com/Refzlund/haulers-dream/pull/89).

**Finding: explicit historical recurrence caused by lifecycle state.** The general lesson is relevant to Richard’s established-save-only bulk failure and #263’s reload-dependent sapient eligibility, but those new reports are not proven instances of the old crop-drop bug. Reuse the test axes, not the diagnosis.

### R07 — Productive work interrupted after one yield

June reports describe miners, woodcutters and growers hauling home after minimal work; the developer calls it a regression and PR #73 changes the end-of-run unload grace logic. PR #89 separately adds a cooldown for full-capacity yield unloading. Later PR #241 finds construction neither stamps the same settle window nor protects carried building material, so finishing one wall looked like the end of a work run. [C193](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030069946) [C194](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030069473) [C195](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030065715) [C196](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030065479) [C052](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547597615) · [PR #73](https://github.com/Refzlund/haulers-dream/pull/73) · [PR #89](https://github.com/Refzlund/haulers-dream/pull/89) · [PR #241](https://github.com/Refzlund/haulers-dream/pull/241).

August 30 comments again describe doing one harvest/chop/deconstruction action and leaving to haul; a second user corroborates without Sidearms. C022 earlier asks for growing-area completion. **Finding: strong recurring symptom, but current build and specific unload trigger still need verification.** Treat work-run transitions and all unload entry points as the deeper target, not one recipe or Sidearms assumption. [C003](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001633990) [C004](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001626310) [C022](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043260054).

### R08 — Retraction of apparent workarounds

| Case | Apparent improvement | Subsequent evidence | Ledger consequence |
|---|---|---|---|
| Richard’s bulk activation | Removing Simple Sidearms seems to help, August 27 | September 2 says it returns | Open; one evolving report, not a confirmed Sidearms conflict. [C008](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045859360) [C002](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940297913156104) |
| #268 food crates | Re-enable bulk with Stack gap 25–75 appears helpful, August 26 | August 29 says the loop returns | Withdraw the workaround; only disabled bulk remains the reporter’s working avoidance. [first reply](https://github.com/Refzlund/haulers-dream/issues/268#issuecomment-5430010324) · [retraction](https://github.com/Refzlund/haulers-dream/issues/268#issuecomment-5461920982) |
| #265 quicktest crashes | Load Smarter Construction first | Same reporter still crashes about half the time | Do not publish a load-order cure. [#265](https://github.com/Refzlund/haulers-dream/issues/265) · [follow-up](https://github.com/Refzlund/haulers-dream/issues/265#issuecomment-5379263383) |
| #261 stocking | Disable Top existing stacks | Report says it did not resolve apples/smoke leaves | Preserve the failed setting experiment. [#261](https://github.com/Refzlund/haulers-dream/issues/261) |
| Manual pickup | A restart/update seems to fix it | Later narrowed to drugs still immediately unloading | Preserve both the improvement and the remaining case. [C177](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030151134) [C175](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030151413) [C174](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030166311) |

These are recurrence of observed symptoms after an apparent workaround, not evidence that an official release had fixed the problem. C108’s retracted deconstruction complaint, C136’s Nice Bill Tab explanation and C031’s OgreStack correction are the reverse: they weaken the corresponding initial claim and must also remain visible.

### R09 — Save removal and pending fixes: resolution claims that cannot be used

The developer’s early save-safety announcement (C319) corresponds to PR #17. PR #23 explicitly reverts that serialization approach and removes its pending release announcement; the broader work was parked in #22. Thus a later removal/UI report is not proof that the same fix regressed: the proposed protection was withdrawn. PUAH migration work in #45 is narrower. [C319](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006697316) [C324](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006694048) [C326](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006693170) [C382](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006659399) · [PR #17](https://github.com/Refzlund/haulers-dream/pull/17) · [revert #23](https://github.com/Refzlund/haulers-dream/pull/23) · [parked #22](https://github.com/Refzlund/haulers-dream/pull/22) · [PR #45](https://github.com/Refzlund/haulers-dream/pull/45).

Draft #148 remains unmerged and awaits an actual failing-save retest. It cannot close the general startup/UI cluster or the BWM counting report based on a similar mention of bills. PR #249 also remains unmerged despite a title and body describing a CE fix; it was closed September 4. PR #267 is pending transporter unloading, not an already delivered resolution for #260 or #257. [#148](https://github.com/Refzlund/haulers-dream/pull/148) · [#249](https://github.com/Refzlund/haulers-dream/pull/249) · [#267](https://github.com/Refzlund/haulers-dream/pull/267).

### R10 — Diagnostics can hide the real owner; successful patch installation must be observed

PR #193 claimed a Common Sense ingredient-sort integration; PR #195 documents that the method lookup targeted the wrong class, so the advertised patch never bound. T03-R26 reports the warning; #195 corrects lookup across forks. This is a separate, explicit historical ineffective fix from current #258 gathering. [PR #193](https://github.com/Refzlund/haulers-dream/pull/193) · [PR #195](https://github.com/Refzlund/haulers-dream/pull/195) · [T03-R26](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930510227) [T03-R27](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930511397).

Separately, PR #252 withdraws prior blame assigned in #235 because its attribution used patch ownership while a finalizer could truncate the original exception frames. **Do not repeat that inference when triaging #262/#265, #255, #256 or T01-R01.** A candidate mod and a stack trace containing its patch are starting evidence; they are not the verified cause. [PR #252](https://github.com/Refzlund/haulers-dream/pull/252).

## Duplicate and relationship rules used

| Sources | Treatment |
|---|---|
| C005/C006 ↔ #269 | Same HaPpY report; count once as a hopper/hysteresis case. |
| C019 ↔ #261 | Same apples/smokeleaf report; relation to #269 not established as exact duplication. |
| C020 ↔ #260 | Same nullpat horse/caravan reproduction. |
| C023/C025 ↔ #263 | Same ErikRedbeard sapient-pawn campaign. |
| C017/C015 ↔ #262; #264/#265 | Related FireBeard campaign; retain distinct repro variants, not three independent witnesses. C014 is a separate user. |
| C012/C011/C010/C008/C002 | One evolving Richard report with a retracted workaround. |
| C242 ↔ T09-R08 | Flyrija cross-post. T09-R16 is independent RV corroboration. |
| T03 sequence ↔ #144/#152/#162 and their fix PRs | One repeatedly retested hospital campaign, not many independent hospital users. |
| #256/#268/#261/C021 ↔ historical RimIOT/storage/hospital loops | Similar observable family only; no shared cause assumed. |
| #255 ↔ #266 | Different reservation symptoms: stale ownership versus deliberate sweep preclaim/forced handoff. |
| T01-R01 ↔ T01-OP | Different issue in a reused topic: product-count exception versus capacity over-delivery. |

## Consolidated ledger records

Each record below gives the current interpretation and the next discriminating check. “High regression coverage” means a valuable historical test, not a claim of a currently open failure. GitHub source states and every original feedback entry follow later in this same file.

| Record | Priority / purpose | Current interpretation | Open GitHub |
|---|---|---|---|
| [L01 — Common Sense: ingredient gathering and switches](#l01) | High | Current report after an explicit fix claim | [#258](https://github.com/Refzlund/haulers-dream/issues/258) |
| [L02 — Several haulers commit the same remaining storage capacity](#l02) | High | Proven historical recurrence; latest-build recurrence unconfirmed | [#270](https://github.com/Refzlund/haulers-dream/issues/270) |
| [L03 — Hopper refills, hysteresis and numeric stockpile limits](#l03) | Medium | Current compatibility gap; separate from concurrent over-delivery | [#269](https://github.com/Refzlund/haulers-dream/issues/269) |
| [L04 — Food, wool and cloth haul/drop or merge loops](#l04) | High | Current v1.24 reports; common cause not established | [#256](https://github.com/Refzlund/haulers-dream/issues/256), [#261](https://github.com/Refzlund/haulers-dream/issues/261), [#268](https://github.com/Refzlund/haulers-dream/issues/268) |
| [L05 — RimIOT terminal hauling/unloading loop](#l05) | High regression coverage | Documented repeated failure, including after a successful retest |  |
| [L06 — Hospital/prison hemogen and organ pacing](#l06) | High regression coverage | Repeated historical misses; reporter eventually confirmed fixed |  |
| [L07 — Workers leave productive work to haul after one task](#l07) | High | Strong recurring symptom; latest installed versions missing |  |
| [L08 — Automatic and ordinary prioritized bulk activation disappears](#l08) | High | Current unresolved report; apparent workaround retracted |  |
| [L09 — Big &amp; Small sapient animals/mechs lose bulk hauling after reload](#l09) | High | Open v1.24 issue with reduced reproduction | [#263](https://github.com/Refzlund/haulers-dream/issues/263) |
| [L10 — Intermittent GUI/startup failure, idle work and gestation symptoms](#l10) | High | Open related report cluster; attribution unsettled | [#262](https://github.com/Refzlund/haulers-dream/issues/262), [#264](https://github.com/Refzlund/haulers-dream/issues/264), [#265](https://github.com/Refzlund/haulers-dream/issues/265) |
| [L11 — Better Workbench Management product-counting exception during hauling](#l11) | High | Post-update Steam report; no matching open issue |  |
| [L12 — Reservations survive while a pawn sleeps or does other work](#l12) | High | Open v1.24 issue; conflicting causal impressions | [#255](https://github.com/Refzlund/haulers-dream/issues/255) |
| [L13 — Haul-everything claims prevent manually splitting work](#l13) | Medium | Open v1.24 behavioral/UX report | [#266](https://github.com/Refzlund/haulers-dream/issues/266) |
| [L14 — Blight cut planning includes healthy plants](#l14) | High | Concrete post-update Steam report |  |
| [L15 — Construction routing, tiny top-ups and useful material retention](#l15) | Medium | Recurring efficiency family; distinct paths remain to isolate |  |
| [L16 — Deliver-resources order proceeds to building with Build unassigned](#l16) | Medium | Open v1.24 issue; related permissions history, not exact recurrence | [#259](https://github.com/Refzlund/haulers-dream/issues/259) |
| [L17 — Pack-animal arrival unloading inconsistently reaches storage accounting](#l17) | High | Open minimal reproduction; version unstated | [#260](https://github.com/Refzlund/haulers-dream/issues/260) |
| [L18 — Mechs recharge before unloading](#l18) | Medium | New named interaction after earlier general request |  |
| [L19 — Portal/shuttle loading shrinks to single units; oversized-stack monopolies](#l19) | High regression coverage | Proven historical recurrence; newer examples need version/path checks |  |
| [L20 — Bulk corpses through map portals](#l20) | Medium | Open feature request on v1.23 | [#257](https://github.com/Refzlund/haulers-dream/issues/257) |
| [L21 — Bulk unloading transporters](#l21) | Medium | Open PR #267; proposed, not released | [PR 267](https://github.com/Refzlund/haulers-dream/pull/267) |
| [L22 — Orphan unfinished bills can freeze work after loading](#l22) | High | Open draft PR #148; unshipped proposal | [PR 148](https://github.com/Refzlund/haulers-dream/pull/148) |
| [L23 — Removing HD or replacing PUAH leaves UI/load failures](#l23) | High investigation | Historical unresolved safety reports; old promise withdrawn |  |
| [L24 — Equipment, ammo and loadout pickup/unload loops](#l24) | High regression coverage | Repeated retention family; several fixes and an unmerged later proposal |  |
| [L25 — Harvested crops are dropped or lose cargo ownership](#l25) | High regression coverage | Explicit historical recurrence; no exact latest-build renewal |  |
| [L26 — Ingredient gather/unload loops and capacity-limited batches](#l26) | High regression coverage | Historical multi-cause crafting family |  |
| [L27 — Automatic batch mode, interruption, pause thresholds and visibility](#l27) | Medium | Requests and historical fixes; some user diagnosis corrected |  |
| [L28 — Mech gestators/core production return inputs or stall](#l28) | High | Historical concrete loop plus newer broad gestation complaint |  |
| [L29 — Third-party bench repeat modes and crafting integrations](#l29) | Medium | Historical compatibility requests with claimed fixes |  |
| [L30 — Matter Network and Storage Network bulk behavior](#l30) | Medium | New Matter Network report; older distinct Storage Network integration |  |
| [L31 — Keyz/Allow Tool urgent hauling does not use bulk inventory](#l31) | Medium | Repeated support questions and fixes; latest retest incomplete |  |
| [L32 — Medical priorities, rituals and animal-work supplies](#l32) | High regression coverage | Several distinct historical fixes |  |
| [L33 — Corpse hauling, stripping and wild-animal finishing](#l33) | Medium | Historical changes plus a distinct post-update gap |  |
| [L34 — Reachability, dangerous extra targets and no-storage fallback](#l34) | High regression coverage | Historical fixes; latest retest absent |  |
| [L35 — Guests lose possessions or become stuck loading vehicles](#l35) | High regression coverage | v1.24 fixes/hardening; one half attributed elsewhere by investigation |  |
| [L36 — Nonhuman hauling eligibility, capacity and specialist balance](#l36) | Medium | Mixed feature requests and historical fixes |  |
| [L37 — Performance, hitches and large-mod-list overhead](#l37) | Medium | Some fixes and positive reports; no comparative benchmark |  |
| [L38 — Diagnostics, issue-report flow and unreliable mod attribution](#l38) | High | Repeated diagnostic corrections; affects all triage |  |
| [L39 — Translations, hardcoded strings and Workshop metadata](#l39) | Low | Historical requests with implementation history |  |
| [L40 — Manual pickup, exact keep quantities and container access](#l40) | Medium | Requests largely implemented; specific gaps require retest |  |
| [L41 — Carried work results never unload or are unusable](#l41) | Medium | Early reports and later distinct retention fixes |  |
| [L42 — RV/interior unloading, furniture trips and nomadic vehicle cargo](#l42) | Medium | Historical fix claims and implemented feature requests |  |
| [L43 — Bulk refuelling fails for modded pots or ship engines](#l43) | Medium | Historical reports with two-stage correction |  |
| [L44 — Fishing-yield support](#l44) | Low | Implemented historically; no renewed failure found |  |
| [L45 — Replacement scope and named compatibility questions](#l45) | Low | Documentation/compatibility questions, not established defects |  |
| [L46 — Planning interaction, routes and repetitive clicks](#l46) | Low | Feature requests with implementation history |  |
| [L47 — Configuration, profiles and pawn gizmos](#l47) | Medium | Several implemented requests; historical recurrence in profile persistence |  |
| [L48 — Scope, balance and feature-growth requests](#l48) | Low | Product feedback |  |
| [L49 — Praise, trust, AI disclosure and maintenance expectations](#l49) | Context | Non-defect feedback retained |  |
| [L50 — Organizational, unrelated or unreadable entries](#l50) | Context | Coverage-only records |  |
| [L51 — Most-stocked/spoilage ingredient ordering](#l51) | Medium regression coverage | Confirmed historical ineffective fix; no new exact failure |  |
| [L52 — CE/strict carry budgets and heavy chunks](#l52) | Medium | Capacity limits plus historical override corrections |  |
| [L53 — Forced jobs and third-party interruption behavior](#l53) | Medium | Compatibility leads and historical queue fixes |  |
| [L54 — Pickup delays and harvest presentation](#l54) | Low | Feature confirmed; scope regression later corrected |  |
| [L55 — Multiplayer and threading compatibility](#l55) | Medium regression coverage | Support introduced; later deterministic-state fixes |  |

<a id="l01"></a>

### L01 — Common Sense: ingredient gathering and switches

**High. Current report after an explicit fix claim.**

Issue #258 reports v1.24.0 with HD gathering enabled and Common Sense collecting disabled, yet no complete gather. This matches the remaining symptom acknowledged on #243 and the exact v1.24.0 promise. The earlier June crafting-loop report is related compatibility history, not proof of an identical mechanism.

**Next discriminating check:** Reproduce #258 first. Cross Common Sense cleaning on/off with gathering on/off, HD global and per-bench controls, automatic bills and planned/batch orders; record which driver actually runs.

**Open GitHub:** [#258](https://github.com/Refzlund/haulers-dream/issues/258).

**Prior reports/fix evidence:** [PR 39](https://github.com/Refzlund/haulers-dream/pull/39), [PR 68](https://github.com/Refzlund/haulers-dream/pull/68), [PR 70](https://github.com/Refzlund/haulers-dream/pull/70), [#230](https://github.com/Refzlund/haulers-dream/issues/230), [#243](https://github.com/Refzlund/haulers-dream/issues/243), [PR 244](https://github.com/Refzlund/haulers-dream/pull/244), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C130](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702267458) [T07-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365458930).

<a id="l02"></a>

### L02 — Several haulers commit the same remaining storage capacity

**High. Proven historical recurrence; latest-build recurrence unconfirmed.**

Three earlier correction attempts are explicitly disavowed in PR #252. The unlocked Steam topic reproduces after v1.22.0. Open #270 describes the same over-delivery but reports v1.21.0; closed #248 reports v1.19.0. Neither establishes failure of the August 9 storage rewrite. The August 17 topic replies do not repeat its over-delivery test.

**Next discriminating check:** Run the five-haulers/three-free-units scenario on the installed v1.24.0 binary, including same-tick planning and vanilla/HD mixed jobs. Record commitments through pickup, job transitions, cancellation and deposit.

**Open GitHub:** [#270](https://github.com/Refzlund/haulers-dream/issues/270).

**Prior reports/fix evidence:** [#114](https://github.com/Refzlund/haulers-dream/issues/114), [PR 116](https://github.com/Refzlund/haulers-dream/pull/116), [#138](https://github.com/Refzlund/haulers-dream/issues/138), [PR 139](https://github.com/Refzlund/haulers-dream/pull/139), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241), [#248](https://github.com/Refzlund/haulers-dream/issues/248), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C037](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320044501) [C038](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320044020) [T01-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/).

<a id="l03"></a>

### L03 — Hopper refills, hysteresis and numeric stockpile limits

**Medium. Current compatibility gap; separate from concurrent over-delivery.**

#269 and HaPpY’s Steam comments are one report on v1.24.0: repeated top-ups defeat desired refill thresholds. PR #252 expressly leaves numeric-cap handling incomplete when HD bypasses other mods’ count clamps. Stack gap item-loss claims and a mistaken OgreStack endorsement must not be counted as confirmed HD deletion.

**Next discriminating check:** Test Storage Refill Hysteresis, Stack Gap and each named limit mod separately, on empty and partial cells. Check threshold/count semantics as well as whether a cell accepts any cargo.

**Open GitHub:** [#269](https://github.com/Refzlund/haulers-dream/issues/269).

**Prior reports/fix evidence:** [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C001](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292592327) [C005](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001556231) [C006](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001556021) [C031](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320103811) [C032](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320101744) [C036](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320047245) [C037](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320044501) [C038](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320044020) [C274](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006807557).

<a id="l04"></a>

### L04 — Food, wool and cloth haul/drop or merge loops

**High. Current v1.24 reports; common cause not established.**

#256 (wool, 1,163 mods), #268 (food crates, 338 mods) and #261 (apples/smoke leaves, 214 mods) remain open. Disabling bulk hauling helps #256/#268. #268’s Stack gap workaround was explicitly retracted. #261 says disabling stack top-up did not fix it. C021 adds a cloth recurrence warning. These are symptom-family links, not proven duplicates of RimIOT or the storage-capacity bug.

**Next discriminating check:** Capture an actual repeated job’s start/end, source/destination, item identity/count, job result and inventory changes. Determine whether warning counters reflect completed trips or repeated candidate job construction; the #256 trace reaches BuildBulkJob during HasJobOnThing scanning.

**Open GitHub:** [#256](https://github.com/Refzlund/haulers-dream/issues/256), [#261](https://github.com/Refzlund/haulers-dream/issues/261), [#268](https://github.com/Refzlund/haulers-dream/issues/268).

**Prior reports/fix evidence:** [PR 216](https://github.com/Refzlund/haulers-dream/pull/216), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C019](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043387830) [C021](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043335749).

<a id="l05"></a>

### L05 — RimIOT terminal hauling/unloading loop

**High regression coverage. Documented repeated failure, including after a successful retest.**

#177 → #184 → #192 → #214 spans v1.16.10, v1.17.0, v1.18.0 and v1.20.3. #184’s reporter initially confirmed an hour without trouble on v1.17.1. Later fixes changed terminal recognition, overflow radius, then execution-time retargeting. v1.20.4 contains PR #216; no newer RimIOT-specific confirmation of failure was found. Do not label #256’s warning as proof that RimIOT is responsible.

**Next discriminating check:** Retest full networks, terminal variants and targets rewritten after planning, over a sustained run. Assert actual progress even when each short job returns Succeeded.

**Prior reports/fix evidence:** [#177](https://github.com/Refzlund/haulers-dream/issues/177), [PR 179](https://github.com/Refzlund/haulers-dream/pull/179), [#184](https://github.com/Refzlund/haulers-dream/issues/184), [PR 185](https://github.com/Refzlund/haulers-dream/pull/185), [#192](https://github.com/Refzlund/haulers-dream/issues/192), [PR 193](https://github.com/Refzlund/haulers-dream/pull/193), [#214](https://github.com/Refzlund/haulers-dream/issues/214), [PR 216](https://github.com/Refzlund/haulers-dream/pull/216). Merged/closed distinctions are listed in the historical register below.

<a id="l06"></a>

### L06 — Hospital/prison hemogen and organ pacing

**High regression coverage. Repeated historical misses; reporter eventually confirmed fixed.**

Kostr’s minimal saves still failed after several releases through v1.16.7. PR #175 documents the decisive discovery: successful non-storage haul-aside jobs, not failing storage deliveries. T03-R20 confirms the core problems fixed on v1.16.12. Keep the hospital case as a regression fixture; there is no evidence here to reopen it as a current confirmed defect.

**Next discriminating check:** Preserve the cramped-room save. Test stackable hemogen and unstackable organs, available/full storage, competing work cells and long enough execution to expose repeated successful jobs.

**Prior reports/fix evidence:** [PR 133](https://github.com/Refzlund/haulers-dream/pull/133), [#144](https://github.com/Refzlund/haulers-dream/issues/144), [PR 145](https://github.com/Refzlund/haulers-dream/pull/145), [PR 147](https://github.com/Refzlund/haulers-dream/pull/147), [PR 149](https://github.com/Refzlund/haulers-dream/pull/149), [#152](https://github.com/Refzlund/haulers-dream/issues/152), [PR 153](https://github.com/Refzlund/haulers-dream/pull/153), [#162](https://github.com/Refzlund/haulers-dream/issues/162), [PR 175](https://github.com/Refzlund/haulers-dream/pull/175). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [T03-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/) [T03-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668602725147613) [T03-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640380927) [T03-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640421351) [T03-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640436109) [T03-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640449430) [T03-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640473661) [T03-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640489340) [T03-R08](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640490667) [T03-R09](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640496737) [T03-R10](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640518850) [T03-R11](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640532134) [T03-R12](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640547816) [T03-R13](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640550189) [T03-R14](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640568095) [T03-R15](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640570100) [T03-R16](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640583316) [T03-R17](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572669129317693223) [T03-R19](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930312327) [T03-R20](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930317422).

<a id="l07"></a>

### L07 — Workers leave productive work to haul after one task

**High. Strong recurring symptom; latest installed versions missing.**

C003/C004 corroborate the behavior after the latest release date; C003 explicitly has no Simple Sidearms. C022 asks to finish growing areas first. PR #73 previously addressed the same mining/harvest trip shape, PR #89 addressed full-capacity unloading, and PR #241 addressed per-wall construction trips. C108 retracted a separate deconstruction refusal and is not corroboration.

**Next discriminating check:** Test mining, growing-zone harvest, manual harvest, chopping and deconstruction independently at default and strict capacity. Trace the next-job choice, work-run timer and every unload trigger.

**Prior reports/fix evidence:** [PR 73](https://github.com/Refzlund/haulers-dream/pull/73), [#75](https://github.com/Refzlund/haulers-dream/issues/75), [#84](https://github.com/Refzlund/haulers-dream/issues/84), [PR 89](https://github.com/Refzlund/haulers-dream/pull/89), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C003](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001633990) [C004](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001626310) [C022](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043260054) [C108](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369459714) [C109](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369459476) [C193](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030069946) [C194](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030069473) [C195](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030065715) [C196](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030065479) [C203](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153367696) [C306](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006725895) [C364](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006672299) [C380](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006661093).

<a id="l08"></a>

### L08 — Automatic and ordinary prioritized bulk activation disappears

**High. Current unresolved report; apparent workaround retracted.**

Richard’s C012/C011/C010/C008/C002 are one evolving report: explicit bulk orders work, normal/automatic upgrades fail in the established save, quicktest works, and removing Sidearms only helped temporarily. PR #35’s older save-related phantom claims are a lead, not an established diagnosis.

**Next discriminating check:** Compare the same pawn/save before and after reload with logged eligibility, workgiver decisions, quarantine state and reservations; retain settings and binary version. Test ordinary and explicit bulk orders separately.

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C002](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940297913156104) [C008](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045859360) [C010](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889007307) [C011](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889005481) [C012](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889002872).

<a id="l09"></a>

### L09 — Big & Small sapient animals/mechs lose bulk hauling after reload

**High. Open v1.24 issue with reduced reproduction.**

#263 and C023/C025 are the same reporter. Newly converted pawns work, existing converted pawns fail after save/restart; a reduced mod set still reproduces. Earlier generic mech-capacity fixes do not prove this race-conversion persistence bug was fixed before.

**Next discriminating check:** Preserve converted pawn state through save/load and full application restart. Compare race, intelligence, work types, eligibility cache and attached HD components before/after conversion and loading.

**Open GitHub:** [#263](https://github.com/Refzlund/haulers-dream/issues/263).

**Prior reports/fix evidence:** [PR 45](https://github.com/Refzlund/haulers-dream/pull/45), [PR 53](https://github.com/Refzlund/haulers-dream/pull/53), [PR 198](https://github.com/Refzlund/haulers-dream/pull/198). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C023](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265884141) [C025](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265832567).

<a id="l10"></a>

### L10 — Intermittent GUI/startup failure, idle work and gestation symptoms

**High. Open related report cluster; attribution unsettled.**

#262/#264/#265 are FireBeard’s related testing campaign, not three independent witnesses. #264/#265 report v1.24.0. Changing Smarter Construction load order was later reported insufficient. Steam C014 supplies another mixed-mod observation. Removing several mods at once does not establish five separate incompatibilities.

**Next discriminating check:** Capture fresh-session first exceptions across repeated cold starts with the reduced mod list; distinguish UI drawing faults, initialization and work scans. Compare each proposed conflict one at a time and test the actual failing save.

**Open GitHub:** [#262](https://github.com/Refzlund/haulers-dream/issues/262), [#264](https://github.com/Refzlund/haulers-dream/issues/264), [#265](https://github.com/Refzlund/haulers-dream/issues/265).

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [PR 45](https://github.com/Refzlund/haulers-dream/pull/45), [PR 51](https://github.com/Refzlund/haulers-dream/pull/51), [PR 60](https://github.com/Refzlund/haulers-dream/pull/60), [#235](https://github.com/Refzlund/haulers-dream/issues/235), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C014](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999435100) [C015](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999285398) [C017](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813130434155325) [C063](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810592556219193) [C076](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587717813) [C077](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587706475) [C219](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436652347) [C223](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436630832) [C234](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436532476) [T09-R20](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877682591) [T09-R24](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793348713825908) [T09-R25](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793348713827029).

<a id="l11"></a>

### L11 — Better Workbench Management product-counting exception during hauling

**High. Post-update Steam report; no matching open issue.**

T01-R01 is a separate lifter/product-count exception under an older storage topic. It involves BWM and HD’s carried-product counting. PR #148 concerns an orphan unfinished-bill path but remains an unverified possible neighbor, not a demonstrated fix for this exception.

**Next discriminating check:** Obtain the first complete exception and relevant bill/bench state; check null/deleted product and bill objects at the actual failing counting seam before grouping with save freezes.

**Prior reports/fix evidence:** [PR 148](https://github.com/Refzlund/haulers-dream/pull/148). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [T01-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691114229).

<a id="l12"></a>

### L12 — Reservations survive while a pawn sleeps or does other work

**High. Open v1.24 issue; conflicting causal impressions.**

#255 reports kibble remaining reserved. The maintainer initially suspected other animal-work mods; the reporter later saw no recurrence after removing HD. Reload clears it. With 1,355 mods and no original incident log, the removal observation matters but does not isolate HD.

**Next discriminating check:** Trace who creates each reservation and which job/queue owns it after interruption, sleep and reload. Reproduce with the smallest animal-work setup before assigning blame.

**Open GitHub:** [#255](https://github.com/Refzlund/haulers-dream/issues/255).

**Prior reports/fix evidence:** [PR 65](https://github.com/Refzlund/haulers-dream/pull/65), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

<a id="l13"></a>

### L13 — Haul-everything claims prevent manually splitting work

**Medium. Open v1.24 behavioral/UX report.**

#266 says a sweep reserves every nearby item immediately; forcing another pawn on one reserved item cancels the first pawn’s whole sweep. This is distinct from #255’s stale claims and from destination capacity.

**Next discriminating check:** Specify and test reservation handoff when a second forced order steals one target: the first sweep should retain valid remaining work or explain its cancellation.

**Open GitHub:** [#266](https://github.com/Refzlund/haulers-dream/issues/266).

**Prior reports/fix evidence:** [PR 65](https://github.com/Refzlund/haulers-dream/pull/65), [PR 169](https://github.com/Refzlund/haulers-dream/pull/169). Merged/closed distinctions are listed in the historical register below.

<a id="l14"></a>

### L14 — Blight cut planning includes healthy plants

**High. Concrete post-update Steam report.**

C027 reports healthy plants being designated when planning cuts for blight. No earlier exact fix or matching open GitHub issue was found in the reviewed evidence.

**Next discriminating check:** Reproduce on a mixed healthy/blighted patch and inspect the generated designations before executing them. Test clicked target, expansion filter and later plant-state changes.

**Steam evidence:** [C027](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265812007).

<a id="l15"></a>

### L15 — Construction routing, tiny top-ups and useful material retention

**Medium. Recurring efficiency family; distinct paths remain to isolate.**

Early planned wall top-ups and zigzag delivery received PR #18/#30/#35; carried-material use received #130; per-wall unloading received #241. C009/C026 are later requests/reports about nearest work and hauling useful supplies away. These are not all the same cause. T09-R11 retracts a toggle workaround and separately identifies a work-priority conflict.

**Next discriminating check:** Use one wall-line scenario to compare automatic construction, forced delivery and planned routes; include a helper delivering materials while the builder works. Record route and material ownership rather than just trip totals.

**Prior reports/fix evidence:** [PR 18](https://github.com/Refzlund/haulers-dream/pull/18), [PR 30](https://github.com/Refzlund/haulers-dream/pull/30), [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [#64](https://github.com/Refzlund/haulers-dream/issues/64), [PR 65](https://github.com/Refzlund/haulers-dream/pull/65), [#88](https://github.com/Refzlund/haulers-dream/issues/88), [PR 89](https://github.com/Refzlund/haulers-dream/pull/89), [#125](https://github.com/Refzlund/haulers-dream/issues/125), [PR 130](https://github.com/Refzlund/haulers-dream/pull/130), [#219](https://github.com/Refzlund/haulers-dream/issues/219), [PR 220](https://github.com/Refzlund/haulers-dream/pull/220), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C009](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045781797) [C026](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265816104) [C052](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547597615) [C054](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937127158868339) [C123](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359491450) [C304](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006733303) [C341](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006679512) [C342](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006678769) [C345](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677599) [C346](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677242) [C348](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677178) [T03-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/) [T09-R11](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877544840).

<a id="l16"></a>

### L16 — Deliver-resources order proceeds to building with Build unassigned

**Medium. Open v1.24 issue; related permissions history, not exact recurrence.**

#259 expects delivery-only behavior when the pawn has no Build assignment. Prior fixes distinguish incapable pawns, capable-but-unassigned pawns and hauling permissions. A work-tab assignment is not the same as incapability.

**Next discriminating check:** Test delivery-only commands with Build assigned/unassigned and capability/skill limits, with the unassigned-planner option both ways. Separate delivery authorization from permission to construct afterward.

**Open GitHub:** [#259](https://github.com/Refzlund/haulers-dream/issues/259).

**Prior reports/fix evidence:** [#176](https://github.com/Refzlund/haulers-dream/issues/176), [PR 179](https://github.com/Refzlund/haulers-dream/pull/179), [#229](https://github.com/Refzlund/haulers-dream/issues/229), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

<a id="l17"></a>

### L17 — Pack-animal arrival unloading inconsistently reaches storage accounting

**High. Open minimal reproduction; version unstated.**

#260 and C020 are the same Harmony + HD horse/caravan test. PR #89 documented an earlier arrival-unload hook that never ran because it checked the wrong job. That makes this a valuable integration-path lead, but does not establish the same mechanism today.

**Next discriminating check:** Run the documented horse return test on v1.24.0. Log actual arrival/unload job definitions and capacity commitments for animal cargo, including partial delivery and interruption.

**Open GitHub:** [#260](https://github.com/Refzlund/haulers-dream/issues/260).

**Prior reports/fix evidence:** [PR 89](https://github.com/Refzlund/haulers-dream/pull/89), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C020](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043344698).

<a id="l18"></a>

### L18 — Mechs recharge before unloading

**Medium. New named interaction after earlier general request.**

C271 originally requested unloading before recharge; C024 now identifies WVC Work Modes self-charging first. This may be another job-selection path rather than a regression of the ordinary recharge behavior.

**Next discriminating check:** Compare vanilla charging with WVC work-mode transitions and depleted versus idle batteries; confirm the point at which carried goods should be deposited.

**Steam evidence:** [C024](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265879048) [C271](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006838303).

<a id="l19"></a>

### L19 — Portal/shuttle loading shrinks to single units; oversized-stack monopolies

**High regression coverage. Proven historical recurrence; newer examples need version/path checks.**

PR #169 did not fully resolve #167; PR #241 corrected the divisor, then #244 corrected the unbounded-budget case excluded by that fix. #247’s 18/14/10 report was v1.19.0 and closed with an update request. Defensive Network and SRTS reports differ: PR #252 attributes SRTS serialization to exclusive reservation of one huge stack. Incoming transport claims T04 were addressed by #189.

**Next discriminating check:** Test finite/unbounded carrying settings, one/multiple eligible haulers, passengers, non-hauling mechs, huge stacks and every transport adapter. Measure total trips and item conservation. PR #244 also flags food-retention tails and a mixed vanilla/HD demand-clamping hazard as separate follow-up leads.

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [PR 37](https://github.com/Refzlund/haulers-dream/pull/37), [PR 71](https://github.com/Refzlund/haulers-dream/pull/71), [PR 129](https://github.com/Refzlund/haulers-dream/pull/129), [#164](https://github.com/Refzlund/haulers-dream/issues/164), [#167](https://github.com/Refzlund/haulers-dream/issues/167), [#168](https://github.com/Refzlund/haulers-dream/issues/168), [PR 169](https://github.com/Refzlund/haulers-dream/pull/169), [#171](https://github.com/Refzlund/haulers-dream/issues/171), [PR 179](https://github.com/Refzlund/haulers-dream/pull/179), [#188](https://github.com/Refzlund/haulers-dream/issues/188), [PR 189](https://github.com/Refzlund/haulers-dream/pull/189), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241), [PR 244](https://github.com/Refzlund/haulers-dream/pull/244), [#247](https://github.com/Refzlund/haulers-dream/issues/247), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C039](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434161796340050) [C041](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434161796152792) [C044](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547656455) [C045](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547636700) [C057](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669348970) [C190](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030092645) [C197](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030065462) [C198](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030062179) [C199](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030061610) [C226](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436590546) [C229](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436581880) [C244](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436468720) [C245](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436467670) [C256](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436422673) [C257](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436422405) [C265](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006872320) [C332](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006690089) [C333](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689566) [C386](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006647845) [C387](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006647728) [C389](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006632871) [C393](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006611421) [T03-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/) [T04-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/571543307930407446/) [T04-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/571543307930407446/#c571543307930426638) [T04-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/571543307930407446/#c571543307930454568) [T09-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877511686).

<a id="l20"></a>

### L20 — Bulk corpses through map portals

**Medium. Open feature request on v1.23.**

#257 asks to take cave-quest insect corpses through a map portal in bulk. Corpse sweeping and corpse weight options in v1.22/v1.23 do not automatically cover portal manifests. This is not enough evidence to call the stackable cave-loot fix broken.

**Next discriminating check:** Check corpse admission and budget on the portal loading path and document whether the requested behavior is supported. Test multiple lightweight corpses separately from one heavy body.

**Open GitHub:** [#257](https://github.com/Refzlund/haulers-dream/issues/257).

**Prior reports/fix evidence:** [PR 241](https://github.com/Refzlund/haulers-dream/pull/241), [PR 244](https://github.com/Refzlund/haulers-dream/pull/244). Merged/closed distinctions are listed in the historical register below.

<a id="l21"></a>

### L21 — Bulk unloading transporters

**Medium. Open PR #267; proposed, not released.**

The non-draft PR adds a bulk-unload toggle and prioritized order plus loading/unloading exclusion. It targets CompTransporter, excludes Vehicle Framework and live pawns, and is not a fix already available in v1.24.0. It does not by itself resolve pack-animal unloading or loading corpses through portals.

**Next discriminating check:** Review the PR against current loading and storage claims; verify interrupted/chained orders, mixed cargo, corpse settings and Multiplayer. Author gameplay checks are reported, but Multiplayer/mod compatibility remain untested.

**Open GitHub:** [PR 267](https://github.com/Refzlund/haulers-dream/pull/267).

<a id="l22"></a>

### L22 — Orphan unfinished bills can freeze work after loading

**High. Open draft PR #148; unshipped proposal.**

The contributor diagnoses WorkbenchConnect saving a live production bill without its unsaved billStack, leaving an unfinished thing bound to an unusable bill. The proposed guard unbinds those objects. Build/test claims are present, but the PR explicitly awaits an in-game corrupted-save reproduction and has not merged.

**Next discriminating check:** Obtain a failing save and verify its actual exception against the proposed mechanism; preserve ingredients and work progress, and exercise saving/loading and job-held bill references. Keep it distinct from the BWM product-counting trace unless evidence connects them.

**Open GitHub:** [PR 148](https://github.com/Refzlund/haulers-dream/pull/148).

<a id="l23"></a>

### L23 — Removing HD or replacing PUAH leaves UI/load failures

**High investigation. Historical unresolved safety reports; old promise withdrawn.**

Startup-black-screen reports, invisible UI after loading an active-job save, and PUAH migration failures are separate scenarios. PR #45 addressed PUAH migration. The save-disable feature announced in C319/PR #17 was reverted by #23 before its pending release; #22 remains parked. Neither that announcement nor draft #148 establishes universal safe removal. Community cleanup successes changed several variables.

**Next discriminating check:** Reproduce each migration/removal scenario on disposable copies with the actual published build and active jobs identified. Verify a supported removal sequence end-to-end; do not generalize one successful cleanup to every save.

**Prior reports/fix evidence:** [PR 14](https://github.com/Refzlund/haulers-dream/pull/14), [PR 17](https://github.com/Refzlund/haulers-dream/pull/17), [PR 22](https://github.com/Refzlund/haulers-dream/pull/22), [PR 23](https://github.com/Refzlund/haulers-dream/pull/23), [PR 45](https://github.com/Refzlund/haulers-dream/pull/45), [PR 57](https://github.com/Refzlund/haulers-dream/pull/57), [PR 148](https://github.com/Refzlund/haulers-dream/pull/148). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C115](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369309819) [C116](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369307981) [C117](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369307090) [C134](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702209362) [C138](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702200061) [C171](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030239543) [C172](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030230507) [C247](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436433810) [C248](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436433374) [C258](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436422126) [C259](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436420594) [C286](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006752874) [C287](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006750572) [C288](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006750251) [C319](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006697316) [C324](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006694048) [C325](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006693488) [C326](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006693170) [C343](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006678422) [C355](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006675226) [C357](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674844) [C358](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674729) [C359](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674550) [C373](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006664773) [C374](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006664069) [C378](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006661818) [C379](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006661766) [C382](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006659399).

<a id="l24"></a>

### L24 — Equipment, ammo and loadout pickup/unload loops

**High regression coverage. Repeated retention family; several fixes and an unmerged later proposal.**

Sidearms, Pocket Sand, Yayo ammo, Smart Medicine, Dubs water, Item Policy, CE generic loadouts and tool mods exercise different keep rules. PR #20/#21 did not end all Sidearms reports: #222 led to #226. PR #205 addressed generic CE meal slots. PR #249 describes remaining CE temporary-cargo ownership problems but was closed September 4 without merging. Survival Tools Reborn support shipped in v1.24.0. Richard’s latest bulk-activation issue is not confirmed Sidearms causation.

**Next discriminating check:** Use one retention ownership model and test exact quantities, mixed generic categories, personal stock sharing a def with temporary cargo, merge/split and interruption. Track who marks an item as personally kept and who clears that mark.

**Prior reports/fix evidence:** [PR 12](https://github.com/Refzlund/haulers-dream/pull/12), [PR 16](https://github.com/Refzlund/haulers-dream/pull/16), [PR 20](https://github.com/Refzlund/haulers-dream/pull/20), [PR 21](https://github.com/Refzlund/haulers-dream/pull/21), [PR 39](https://github.com/Refzlund/haulers-dream/pull/39), [#72](https://github.com/Refzlund/haulers-dream/issues/72), [#81](https://github.com/Refzlund/haulers-dream/issues/81), [PR 82](https://github.com/Refzlund/haulers-dream/pull/82), [PR 105](https://github.com/Refzlund/haulers-dream/pull/105), [PR 153](https://github.com/Refzlund/haulers-dream/pull/153), [#204](https://github.com/Refzlund/haulers-dream/issues/204), [PR 205](https://github.com/Refzlund/haulers-dream/pull/205), [#222](https://github.com/Refzlund/haulers-dream/issues/222), [PR 226](https://github.com/Refzlund/haulers-dream/pull/226), [PR 249](https://github.com/Refzlund/haulers-dream/pull/249), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C040](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434161796256496) [C084](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554181301) [C118](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369305721) [C120](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369290087) [C156](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813422440) [C158](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813405926) [C159](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813405723) [C174](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030166311) [C175](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030151413) [C176](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030151345) [C177](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030151134) [C178](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030150926) [C180](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030144720) [C360](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674547) [C364](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006672299) [C365](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006672231) [C370](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006666785) [C371](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006666018) [C375](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006663464) [C376](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006662426) [C377](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006661836) [C381](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006660785) [C383](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006658716) [C384](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006657112) [C388](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006638500) [T07-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365425003) [T07-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365428556) [T07-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365430383) [T07-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365442336) [T07-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365450782) [T11-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/) [T11-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365441995) [T11-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365445300) [T11-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365447747) [T11-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365450775) [T11-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365469942) [T11-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365470351) [T11-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365499496).

<a id="l25"></a>

### L25 — Harvested crops are dropped or lose cargo ownership

**High regression coverage. Explicit historical recurrence; no exact latest-build renewal.**

#62 was addressed by #65, then #87 reported the problem again with psychoid leaves. PR #89 identifies stale/unhealed cargo tags after split/merge and a raw-food timer that makes quicktest unlike established saves. Later stockpiling complaints mention crops but do not prove this exact floor-drop failure returned.

**Next discriminating check:** Keep an aged-save, multi-stack psychoid regression case. Follow tagged quantities through merge/split, vanilla inventory cleanup and loading into carriers.

**Prior reports/fix evidence:** [PR 28](https://github.com/Refzlund/haulers-dream/pull/28), [#62](https://github.com/Refzlund/haulers-dream/issues/62), [PR 65](https://github.com/Refzlund/haulers-dream/pull/65), [#87](https://github.com/Refzlund/haulers-dream/issues/87), [PR 89](https://github.com/Refzlund/haulers-dream/pull/89). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C293](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006744453) [C294](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006744448) [C302](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006735336) [C305](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006731173) [C306](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006725895) [C399](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304578838) [C401](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304577939) [C403](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304576711) [C415](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304497039) [C416](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304496959).

<a id="l26"></a>

### L26 — Ingredient gather/unload loops and capacity-limited batches

**High regression coverage. Historical multi-cause crafting family.**

Mixed recipes, unstackable stone chunks, target-count bills and CE bulk/weight each produced superficially similar bench loops. PR #35, #65 and #179 address different mechanisms. Smaller batches helped the CE report; disabling inventory crafting helped others. Do not merge these with #258’s failure to gather merely because both involve Common Sense.

**Next discriminating check:** Test recipes with mixed ingredients, unstackables, one round too heavy, several feasible rounds, CE bulk limits and target-count modes. Require finished products and bounded trips, not only a valid planned batch.

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [#63](https://github.com/Refzlund/haulers-dream/issues/63), [PR 65](https://github.com/Refzlund/haulers-dream/pull/65), [#85](https://github.com/Refzlund/haulers-dream/issues/85), [#86](https://github.com/Refzlund/haulers-dream/issues/86), [PR 89](https://github.com/Refzlund/haulers-dream/pull/89), [PR 179](https://github.com/Refzlund/haulers-dream/pull/179). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C119](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369304818) [C121](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369279811) [C124](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359472762) [C127](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702285559) [C243](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436472168) [C300](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006736965) [C301](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006736764) [C302](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006735336) [C303](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006735204) [T09-R22](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877708643).

<a id="l27"></a>

### L27 — Automatic batch mode, interruption, pause thresholds and visibility

**Medium. Requests and historical fixes; some user diagnosis corrected.**

Users want automatic sequential batches with partial progress preserved, correct pause/unpause thresholds and visible batch state. C136/C137 identify Nice Bill Tab hiding the batch setting, so that pair is not an unresolved execution bug. PR #31 addressed thresholds; #154 addressed visibility; #237 was included in #241 for numeric batch input.

**Next discriminating check:** Retest automatic versus explicitly planned bills under interruptions and pause/resume limits, including modded repeat-mode menus. Document which options change gathering and which change repetition.

**Prior reports/fix evidence:** [PR 24](https://github.com/Refzlund/haulers-dream/pull/24), [PR 31](https://github.com/Refzlund/haulers-dream/pull/31), [PR 67](https://github.com/Refzlund/haulers-dream/pull/67), [PR 68](https://github.com/Refzlund/haulers-dream/pull/68), [PR 70](https://github.com/Refzlund/haulers-dream/pull/70), [PR 154](https://github.com/Refzlund/haulers-dream/pull/154), [#237](https://github.com/Refzlund/haulers-dream/issues/237), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C132](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702227728) [C133](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702213296) [C136](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702208300) [C137](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702206223) [C202](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030010632) [C206](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153327852) [C260](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436399897) [C273](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006813715) [C309](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006719199) [C310](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006715438) [C313](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006712376) [C314](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006711893) [C315](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006711828) [C316](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006711520) [C317](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006707420) [T09-R09](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877542673) [T09-R15](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877555754).

<a id="l28"></a>

### L28 — Mech gestators/core production return inputs or stall

**High. Historical concrete loop plus newer broad gestation complaint.**

C262/T09-R03 describe inventory-sourced ingredients being returned, with a toggle workaround; Grab Your Tool was suspected but not isolated. C014’s later blocked gestation is accompanied by lag/UI problems and should not automatically reopen the exact old recipe loop.

**Next discriminating check:** Separate delivery to gestation containers from bill-based core crafting. Compare inventory-material sharing on/off with the smallest recipe/mod setup and record the job that returns the input.

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C014](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999435100) [C262](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436397781) [T09-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365575227) [T09-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365587953) [T09-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365603206) [T09-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365605496).

<a id="l29"></a>

### L29 — Third-party bench repeat modes and crafting integrations

**Medium. Historical compatibility requests with claimed fixes.**

Two users independently reported Everybody Gets One; Cook Carefully, Recycle It/Recycle This and invisible bench ingredients also appear. PR #39 covers EGO/Cook Carefully compatibility; #53 covers a recycling integration. The Steam recycling report explicitly used an older build. Ingredient Threshold and Compositable Loadouts have separate PR history.

**Next discriminating check:** Maintain a bench-mode compatibility table and smoke-test the installed fork/version; distinguish intended inventory-held visuals from inability to complete a recipe.

**Prior reports/fix evidence:** [PR 32](https://github.com/Refzlund/haulers-dream/pull/32), [PR 39](https://github.com/Refzlund/haulers-dream/pull/39), [PR 53](https://github.com/Refzlund/haulers-dream/pull/53), [PR 91](https://github.com/Refzlund/haulers-dream/pull/91), [#92](https://github.com/Refzlund/haulers-dream/issues/92), [PR 142](https://github.com/Refzlund/haulers-dream/pull/142). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [T07-R10](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877481380) [T07-R11](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877481408) [T07-R12](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877481571) [T07-R13](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877482070) [T07-R14](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877490159) [T07-R17](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877686761) [T09-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365528150) [T09-R23](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877729321).

<a id="l30"></a>

### L30 — Matter Network and Storage Network bulk behavior

**Medium. New Matter Network report; older distinct Storage Network integration.**

C018 names Matter Network. T07-R09 names Storage Network, for which #39 introduced an opt-in virtual-server bulk loader. The names must not be treated as interchangeable, and neither is automatically RimIOT.

**Next discriminating check:** Identify exact package IDs/forks and the adapter used. Check whether the older Storage Network opt-in is enabled before classifying single-stack behavior as regression; reproduce Matter Network separately.

**Prior reports/fix evidence:** [PR 33](https://github.com/Refzlund/haulers-dream/pull/33), [PR 39](https://github.com/Refzlund/haulers-dream/pull/39). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C018](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043389321) [T07-R09](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877466015).

<a id="l31"></a>

### L31 — Keyz/Allow Tool urgent hauling does not use bulk inventory

**Medium. Repeated support questions and fixes; latest retest incomplete.**

C085/C074/C067 report single-item urgent hauling, sometimes under CE. The author announced v1.20 and v1.21 work; C064 confirms finding settings after update, not that hauling behavior passed. Earlier #39 compatibility assurances were broader than these later reports support.

**Next discriminating check:** Verify each Keyz/Allow Tool package, workgiver and urgent toggle with CE on/off. Confirm actual pickup quantity and task completion.

**Prior reports/fix evidence:** [PR 39](https://github.com/Refzlund/haulers-dream/pull/39), [PR 202](https://github.com/Refzlund/haulers-dream/pull/202), [PR 226](https://github.com/Refzlund/haulers-dream/pull/226). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C064](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587836392) [C067](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587805235) [C068](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587804654) [C069](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587802755) [C071](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587799336) [C074](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587730535) [C083](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554193440) [C085](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554158731) [C264](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436394380) [C394](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304585364).

<a id="l32"></a>

### L32 — Medical priorities, rituals and animal-work supplies

**High regression coverage. Several distinct historical fixes.**

Doctoring/rescue/firefighting priority, repeated medical rest, ritual bioferrite and taming kibble are separate critical-work constraints. C153 confirms the earlier medical-rest/profile fixes. PR #35 covered Lord-directed activity; #107 protected emergency work; #175 refined elective medical detours; #241 retained animal-interaction food. #255’s reserved kibble is a different failure.

**Next discriminating check:** Test emergency versus elective medical tasks, rest, rituals and taming with required carried supplies, including queued orders and opportunistic pickups mid-trip.

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [PR 107](https://github.com/Refzlund/haulers-dream/pull/107), [PR 175](https://github.com/Refzlund/haulers-dream/pull/175), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C062](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810592556235056) [C153](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813452386) [C154](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813432053) [C155](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813425664) [C168](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324086504) [C169](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324082915) [C190](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030092645) [C199](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030061610) [C250](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436433073) [T09-R12](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877553475) [T09-R13](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877553721).

<a id="l33"></a>

### L33 — Corpse hauling, stripping and wild-animal finishing

**Medium. Historical changes plus a distinct post-update gap.**

Corpse sweeps/options shipped in v1.22/v1.23; forced stripping and queued-order interruptions were addressed in #241. Earlier manual leave-on-corpse behavior needed #212 then further correction. C007 reports finishing wild animals does not trigger haul-after-slaughter; this is a separate event path, not evidence that small-corpse batching regressed.

**Next discriminating check:** Test slaughter versus finishing, manual and automatic stripping, tainted gear policies, queued orders, human/animal corpse mass and single-body graves.

**Prior reports/fix evidence:** [PR 5](https://github.com/Refzlund/haulers-dream/pull/5), [PR 37](https://github.com/Refzlund/haulers-dream/pull/37), [#187](https://github.com/Refzlund/haulers-dream/issues/187), [PR 189](https://github.com/Refzlund/haulers-dream/pull/189), [#211](https://github.com/Refzlund/haulers-dream/issues/211), [PR 212](https://github.com/Refzlund/haulers-dream/pull/212), [PR 226](https://github.com/Refzlund/haulers-dream/pull/226), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241), [PR 244](https://github.com/Refzlund/haulers-dream/pull/244). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C007](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045885677) [C050](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547626689) [C053](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547560380) [C055](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669378657) [C058](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669337768) [C059](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669249939) [C151](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813475611) [C228](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436585782) [C235](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436530306) [C318](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006697529) [C331](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006690841) [C339](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006683158) [C392](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006614487) [C407](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304528643) [T07-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365473080) [T07-R08](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365473520).

<a id="l34"></a>

### L34 — Reachability, dangerous extra targets and no-storage fallback

**High regression coverage. Historical fixes; latest retest absent.**

T02’s personally modified impassable shelves exposed destination path-failure recovery; the developer accepted HD responsibility and announced a fix. PR #35 distinguishes deadly-region exposure from allowed-area filtering. #231/#241 address distant fallback drops. PR #252 adds dynamic forbidding checks after planning.

**Next discriminating check:** Test target reachability and safety at planning, while walking and on arrival; include changed doors/forbiddance, spacesuit state, blocked shelves and no accepting storage.

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [#231](https://github.com/Refzlund/haulers-dream/issues/231), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241), [#250](https://github.com/Refzlund/haulers-dream/issues/250), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C388](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006638500) [T02-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/) [T02-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c581678423012823874) [T02-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c581678423012923070) [T02-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c581678423012923383) [T02-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c581678423012956683) [T02-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c582804662258798212) [T09-R19](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877624293).

<a id="l35"></a>

### L35 — Guests lose possessions or become stuck loading vehicles

**High regression coverage. v1.24 fixes/hardening; one half attributed elsewhere by investigation.**

C034 combines guest-unloading and vehicle-loading symptoms. PR #252 fixes HD guest unload permissions, but its investigation attributes the observed guest vehicle loop to Hospitality/VF and adds a narrower HD loader gate. It explicitly leaves pre-existing guest UnloadEverything flags unchanged because ownership cannot be determined.

**Next discriminating check:** Test colonists, prisoners, guests, rescued/quest pawns and old saved unload flags. Capture the actual vehicle loader before assigning causation.

**Prior reports/fix evidence:** [#123](https://github.com/Refzlund/haulers-dream/issues/123), [PR 128](https://github.com/Refzlund/haulers-dream/pull/128), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C034](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320071338).

<a id="l36"></a>

### L36 — Nonhuman hauling eligibility, capacity and specialist balance

**Medium. Mixed feature requests and historical fixes.**

Housekeeper cats, robots, agrihands, constructoids and tunnellers raise eligibility and balance questions. Mech stats/CE overrides were revised more than once: #56’s unconditional override contradicted defaults and #119 made it opt-in. Per-category yield controls were introduced via #80. Big & Small persistence is tracked separately in L09.

**Next discriminating check:** Keep a pawn-type matrix for eligibility, stats and work capabilities. Document defaults separately from optional specialist hauling and test capacity with CE at multiplier 1 and above.

**Prior reports/fix evidence:** [PR 45](https://github.com/Refzlund/haulers-dream/pull/45), [PR 53](https://github.com/Refzlund/haulers-dream/pull/53), [#54](https://github.com/Refzlund/haulers-dream/issues/54), [PR 56](https://github.com/Refzlund/haulers-dream/pull/56), [#79](https://github.com/Refzlund/haulers-dream/issues/79), [PR 80](https://github.com/Refzlund/haulers-dream/pull/80), [#118](https://github.com/Refzlund/haulers-dream/issues/118), [PR 119](https://github.com/Refzlund/haulers-dream/pull/119), [PR 198](https://github.com/Refzlund/haulers-dream/pull/198). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C215](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153117409) [C217](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436662874) [C218](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436655503) [C268](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006838458) [C272](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006828325) [C295](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006743366) [C298](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742687) [C299](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742328) [C305](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006731173) [C306](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006725895) [C390](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006632324) [C399](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304578838).

<a id="l37"></a>

### L37 — Performance, hitches and large-mod-list overhead

**Medium. Some fixes and positive reports; no comparative benchmark.**

Perspective Shift’s shelf/outside-Home hitch led to v1.11.1; other users report microstutter, one-FPS saves and settings/search/menu slowdowns. PR #139/#189/#198 revisit UI/search costs. Positive experiences and developer impressions do not establish performance versus PUAH/While You’re Up.

**Next discriminating check:** Benchmark the same save at idle, heavy work and settings/menu interactions, separating FPS, TPS and pause duration. Include representative small/large mod sets and cold/warm caches.

**Prior reports/fix evidence:** [#76](https://github.com/Refzlund/haulers-dream/issues/76), [PR 77](https://github.com/Refzlund/haulers-dream/pull/77), [#138](https://github.com/Refzlund/haulers-dream/issues/138), [PR 139](https://github.com/Refzlund/haulers-dream/pull/139), [PR 189](https://github.com/Refzlund/haulers-dream/pull/189), [PR 198](https://github.com/Refzlund/haulers-dream/pull/198). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C046](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547636150) [C047](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547635203) [C065](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587833993) [C090](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554130223) [C094](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554113050) [C140](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702186624) [C141](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702173390) [C185](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030116097) [C187](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030098272) [C214](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153129746) [C291](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006745031) [C372](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006665711) [C408](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304519301) [C409](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304509634) [C413](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304504270) [T06-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/) [T06-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/#c572667758936853463) [T06-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/#c572667758936853503) [T06-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/#c572667758936945046) [T06-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/#c572667758936967371).

<a id="l38"></a>

### L38 — Diagnostics, issue-report flow and unreliable mod attribution

**High. Repeated diagnostic corrections; affects all triage.**

Report-server failures, clipped errors, attachment limits, ThreadAbort logging and patch-tagger warnings appear throughout. PR #252 retracts #235’s earlier blame: the alert inferred fault from patch ownership while HD’s finalizer could truncate the source trace. Therefore neither an HD prefix nor a named other mod establishes causation. C021’s synchronization warning is also preserved in L04.

**Next discriminating check:** Log first exceptions with intact origin stacks and exact installed version. Separate patch participation, job-scan failure and proven cause; verify the report tool captures fresh-session logs and full useful attachments.

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [PR 48](https://github.com/Refzlund/haulers-dream/pull/48), [PR 77](https://github.com/Refzlund/haulers-dream/pull/77), [PR 135](https://github.com/Refzlund/haulers-dream/pull/135), [PR 139](https://github.com/Refzlund/haulers-dream/pull/139), [PR 189](https://github.com/Refzlund/haulers-dream/pull/189), [#197](https://github.com/Refzlund/haulers-dream/issues/197), [PR 198](https://github.com/Refzlund/haulers-dream/pull/198), [PR 209](https://github.com/Refzlund/haulers-dream/pull/209), [#235](https://github.com/Refzlund/haulers-dream/issues/235), [#236](https://github.com/Refzlund/haulers-dream/issues/236), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C033](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320101201) [C150](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813479880) [C164](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324185931) [C165](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324185108) [C179](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030145144) [C188](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030098137) [C211](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153229850) [C219](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436652347) [C225](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436628982) [C241](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436494176) [C412](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304508651) [T03-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640421351) [T03-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640436109) [T03-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640449430) [T09-R17](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877602512) [T09-R18](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877604503) [T09-R25](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793348713827029).

<a id="l39"></a>

### L39 — Translations, hardcoded strings and Workshop metadata

**Low. Historical requests with implementation history.**

Users requested translated settings, locale support and Workshop tags. C239’s hardcoded-English complaint predates the localization work. PR #41 added localization, #240 Traditional Chinese and #254 a glossary/consistency pass. A matching translation key set alone does not prove translated values are current.

**Next discriminating check:** Verify runtime strings, placeholders and changed meanings in the active language, including new PR #267 labels if it is merged.

**Prior reports/fix evidence:** [PR 8](https://github.com/Refzlund/haulers-dream/pull/8), [PR 41](https://github.com/Refzlund/haulers-dream/pull/41), [PR 240](https://github.com/Refzlund/haulers-dream/pull/240), [PR 251](https://github.com/Refzlund/haulers-dream/pull/251), [PR 254](https://github.com/Refzlund/haulers-dream/pull/254). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C222](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436636604) [C237](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436526079) [C239](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436515128) [C246](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436452419) [C402](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304577378) [C406](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304550815).

<a id="l40"></a>

### L40 — Manual pickup, exact keep quantities and container access

**Medium. Requests largely implemented; specific gaps require retest.**

Users missed PUAH pickup, including drafted pickup, eggboxes, cellars, stored goods and corpses. PR #53/#112 improved access; #198 added exact keep quantities. C149 confirms controls appeared. The earlier temporary pickup improvement was later narrowed to drugs (L24). Do not confuse pickup-to-keep with haul-to-better-storage.

**Next discriminating check:** Test ground, best-stored and virtual/container items with drafted/undrafted pawns; confirm exact retained count and intentional unload semantics.

**Prior reports/fix evidence:** [PR 53](https://github.com/Refzlund/haulers-dream/pull/53), [#81](https://github.com/Refzlund/haulers-dream/issues/81), [PR 82](https://github.com/Refzlund/haulers-dream/pull/82), [#103](https://github.com/Refzlund/haulers-dream/issues/103), [PR 112](https://github.com/Refzlund/haulers-dream/pull/112), [PR 198](https://github.com/Refzlund/haulers-dream/pull/198), [#225](https://github.com/Refzlund/haulers-dream/issues/225), [PR 226](https://github.com/Refzlund/haulers-dream/pull/226), [#232](https://github.com/Refzlund/haulers-dream/issues/232), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C088](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554138902) [C089](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554138662) [C091](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554130140) [C092](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554129621) [C097](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554102569) [C103](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369507942) [C107](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369460422) [C149](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813483547) [C150](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813479880) [C151](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813475611) [C152](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813474803) [C160](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813403208) [C161](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813390622) [C162](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813382117) [C173](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030189336) [C174](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030166311) [C180](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030144720) [C181](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030142375) [C182](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030137735) [C183](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030135926) [C184](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030133178) [C208](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153316293) [C209](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153315664) [C212](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153205904) [C213](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153174594) [C227](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436588772) [C289](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006747407) [C293](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006744453) [C294](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006744448) [T09-R14](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877555484).

<a id="l41"></a>

### L41 — Carried work results never unload or are unusable

**Medium. Early reports and later distinct retention fixes.**

C419/C415 identify deconstruction bronze remaining in inventory; C416 asks about modded crops. Other cases involve crafter outputs and Compositable Loadouts (#201/#233). These differ from immediate floor-dropping and from personal equipment that should be kept.

**Next discriminating check:** Check item provenance and unloadable surplus after work ends, including modded materials, meals, apparel and unavailable storage; ensure retained personal stock is not counted as incoming bill output.

**Prior reports/fix evidence:** [PR 7](https://github.com/Refzlund/haulers-dream/pull/7), [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [#200](https://github.com/Refzlund/haulers-dream/issues/200), [#201](https://github.com/Refzlund/haulers-dream/issues/201), [PR 202](https://github.com/Refzlund/haulers-dream/pull/202), [#233](https://github.com/Refzlund/haulers-dream/issues/233), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C403](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304576711) [C415](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304497039) [C416](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304496959) [C418](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304493444) [C419](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304492754).

<a id="l42"></a>

### L42 — RV/interior unloading, furniture trips and nomadic vehicle cargo

**Medium. Historical fix claims and implemented feature requests.**

Flyrija’s C242/T09-R08 are a cross-post; T09-R16 is independent corroboration. PR #35 replaced a wrong pocket-map assumption with presence-of-storage routing. Later requests concern eating/medical supplies from vehicle cargo and scope on non-home maps; v1.20 added options. These are separate from CompTransporter PR #267.

**Next discriminating check:** Test RV/interior maps with/without reachable player storage and pack animals, plus away-map furniture collection and optional vehicle food/medicine use.

**Prior reports/fix evidence:** [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [PR 39](https://github.com/Refzlund/haulers-dream/pull/39), [PR 45](https://github.com/Refzlund/haulers-dream/pull/45), [PR 202](https://github.com/Refzlund/haulers-dream/pull/202), [PR 226](https://github.com/Refzlund/haulers-dream/pull/226). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C073](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587740210) [C083](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554193440) [C086](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554145675) [C087](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554144844) [C242](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436487166) [C244](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436468720) [C245](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436467670) [T09-R08](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877532596) [T09-R16](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877570230).

<a id="l43"></a>

### L43 — Bulk refuelling fails for modded pots or ship engines

**Medium. Historical reports with two-stage correction.**

Medieval Overhaul ragout pots and SoS engines exposed impassable-root/partial-fuel paths. PR #35 initially fell back to vanilla for impassable buildings; #39 moved the sweep root to the pawn to restore bulk behavior. A claimed compatibility fix is not the same as a reporter retest.

**Next discriminating check:** Test actual fuel progress on impassable and high-capacity refuelables with a partial reachable load, including automatic and forced refuel and strict carry limits.

**Prior reports/fix evidence:** [#34](https://github.com/Refzlund/haulers-dream/issues/34), [PR 35](https://github.com/Refzlund/haulers-dream/pull/35), [PR 39](https://github.com/Refzlund/haulers-dream/pull/39), [PR 228](https://github.com/Refzlund/haulers-dream/pull/228). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [T09-R10](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877544273) [T09-R17](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877602512).

<a id="l44"></a>

### L44 — Fishing-yield support

**Low. Implemented historically; no renewed failure found.**

C351/C170 requested fish collection and the developer initially acknowledged uncertainty. GitHub PR #91 subsequently added fishing catches. This updates the earlier Steam-only ledger’s uncertain status.

**Next discriminating check:** Retain an ordinary fishing-yield smoke test; only investigate a new failure with the fishing mod/game version identified.

**Prior reports/fix evidence:** [PR 91](https://github.com/Refzlund/haulers-dream/pull/91). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C168](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324086504) [C170](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030272460) [C342](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006678769) [C351](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006676295).

<a id="l45"></a>

### L45 — Replacement scope and named compatibility questions

**Low. Documentation/compatibility questions, not established defects.**

Build From Storage is described as compatible and separate; While You Are Nearby remains separate because it reorders work. Questions cover Build From Inventory, Meals On Wheels, PUAH, stripping, perishable hauling and Skipdoor Pathing. The Skipdoor answer was tentative; Please Haul Perishables lacks a specific resolution in the source exchange. Cooking-sort replacement questions belong here and L51.

**Next discriminating check:** Publish an exact feature-level replacement list with opt-in settings and tested versions, preserving unanswered compatibility questions rather than presenting them as guaranteed support.

**Prior reports/fix evidence:** [PR 39](https://github.com/Refzlund/haulers-dream/pull/39). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C001](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292592327) [C028](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265733261) [C029](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265729951) [C043](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547669974) [C125](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359472587) [C126](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359468454) [C145](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540720495) [C163](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324208420) [C166](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324122551) [C167](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324090011) [C185](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030116097) [C189](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030097956) [C254](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436426548) [C255](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436426346) [C263](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436396905) [C266](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006846353) [C267](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006843566) [C308](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006720571) [C335](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689551) [C336](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689535) [C337](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006688271) [C338](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006685247) [C339](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006683158) [C340](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006681131) [C368](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006670371) [C369](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006669909) [C371](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006666018) [C385](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006650342) [C389](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006632871) [C391](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006628091) [C410](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304509463) [C414](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304501528) [T10-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365521665/) [T10-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365521665/#c564785528365527137).

<a id="l46"></a>

### L46 — Planning interaction, routes and repetitive clicks

**Low. Feature requests with implementation history.**

Requests include sowing areas, remembered/default plans, one-click repeated plans and multi-target route support. Some interaction costs are deliberate UI behavior; #95 and the related feature work cover parts of this family.

**Next discriminating check:** Evaluate the remaining click sequence with users’ repeated-work examples and retain multi-target/sowing behavior tests.

**Prior reports/fix evidence:** [PR 67](https://github.com/Refzlund/haulers-dream/pull/67), [PR 95](https://github.com/Refzlund/haulers-dream/pull/95), [#96](https://github.com/Refzlund/haulers-dream/issues/96), [PR 109](https://github.com/Refzlund/haulers-dream/pull/109), [#110](https://github.com/Refzlund/haulers-dream/issues/110). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C160](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813403208) [C186](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030116095) [C191](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030073698) [C192](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030073648) [C202](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030010632) [C203](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153367696).

<a id="l47"></a>

### L47 — Configuration, profiles and pawn gizmos

**Medium. Several implemented requests; historical recurrence in profile persistence.**

Users ask to hide hauling/gear buttons, disable auto-opening Gear, and understand unavailable settings. #140 needed an actual gizmo-sort fix in #179 after an earlier incorrect attribution; #224/#238 led to #241 for Gear behavior and saved collection-profile choices. C155/C153’s earlier localized default-profile issue is distinct from #238 persistence.

**Next discriminating check:** Round-trip profiles through save, restart and locale changes; verify hidden/off controls affect the intended path and that defaults match descriptions.

**Prior reports/fix evidence:** [#59](https://github.com/Refzlund/haulers-dream/issues/59), [PR 60](https://github.com/Refzlund/haulers-dream/pull/60), [PR 109](https://github.com/Refzlund/haulers-dream/pull/109), [#140](https://github.com/Refzlund/haulers-dream/issues/140), [PR 154](https://github.com/Refzlund/haulers-dream/pull/154), [PR 179](https://github.com/Refzlund/haulers-dream/pull/179), [#215](https://github.com/Refzlund/haulers-dream/issues/215), [PR 218](https://github.com/Refzlund/haulers-dream/pull/218), [#224](https://github.com/Refzlund/haulers-dream/issues/224), [#238](https://github.com/Refzlund/haulers-dream/issues/238), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C064](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587836392) [C069](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587802755) [C071](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587799336) [C075](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587723984) [C133](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702213296) [C153](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813452386) [C154](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813432053) [C155](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813425664) [C217](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436662874) [C221](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436639557) [C236](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436526230) [C249](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436433087) [C402](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304577378).

<a id="l48"></a>

### L48 — Scope, balance and feature-growth requests

**Low. Product feedback.**

Users value fewer separate mods, while disagreeing over specialist-mech hauling, overload balance and broader building/work integrations. Calls for a separate beta and slower feature expansion are release-process feedback, not individual crash evidence.

**Next discriminating check:** Prioritize regression fixtures before extending the same integration paths; document optional balance changes and consider a stable/beta release distinction.

**Steam evidence:** [C072](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587775149) [C073](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587740210) [C119](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369304818) [C121](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369279811) [C123](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359491450) [C125](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359472587) [C126](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359468454) [C128](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702282008) [C129](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702281949) [C230](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436559100) [C231](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436546953) [C232](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436544357) [C233](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436535063) [C251](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436430179) [C252](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436430077) [C255](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436426346) [C272](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006828325) [C295](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006743366) [C299](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742328) [C307](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006723314) [C332](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006690089) [C334](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689555) [C336](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689535) [C337](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006688271) [C411](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304509449).

<a id="l49"></a>

### L49 — Praise, trust, AI disclosure and maintenance expectations

**Context. Non-defect feedback retained.**

Includes positive usability/consolidation reports, performance impressions, requests for stability, AI/process criticism and discussion of developer understanding. General accusations are not technical evidence of a specific defect. Developer availability/backlog messages are context, not fix claims.

**Next discriminating check:** Use the technical recurrence evidence to improve release verification and communication; retain sentiment without counting each reply as a separate bug.

**Steam evidence:** [C013](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999473479) [C016](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813130434387941) [C030](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434705716618225) [C035](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320061673) [C042](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547690648) [C048](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547634125) [C049](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547630172) [C051](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547626162) [C056](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669355738) [C066](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587818028) [C070](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587801023) [C079](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047359821223299) [C080](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047359821104356) [C081](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047359821098215) [C082](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554194166) [C093](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554122604) [C095](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554108590) [C096](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554108424) [C098](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554102564) [C099](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554101955) [C100](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369520862) [C101](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369511787) [C102](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369510275) [C104](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369486599) [C105](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369475021) [C106](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369460428) [C110](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369423860) [C111](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369414966) [C131](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702253717) [C135](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702208488) [C139](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702187809) [C142](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702140311) [C143](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540759102) [C144](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540730868) [C146](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540709780) [C147](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540629168) [C148](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813542166) [C157](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813410075) [C200](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030019709) [C201](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030010723) [C204](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153340759) [C207](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153321883) [C216](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436666690) [C220](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436649264) [C224](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436630029) [C238](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436525169) [C253](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436427708) [C261](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436397840) [C269](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006838320) [C270](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006838306) [C275](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006804334) [C276](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006804313) [C277](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006765821) [C278](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006765115) [C279](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006764241) [C280](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006764136) [C281](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006763249) [C282](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006759910) [C283](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006756678) [C284](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006756670) [C285](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006756634) [C290](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006746538) [C296](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742772) [C297](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742761) [C321](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006696048) [C322](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006696035) [C323](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006694715) [C327](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006692842) [C328](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006692665) [C329](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006691297) [C330](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006691170) [C344](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677826) [C347](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677236) [C349](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677131) [C350](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006676463) [C352](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006676130) [C353](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006676124) [C354](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006675418) [C356](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006675165) [C361](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674276) [C363](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006673054) [C366](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006671329) [C367](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006670618) [C395](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304583043) [C396](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304582914) [C397](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304582911) [C398](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304581942) [C404](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304571226) [C405](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304550935) [C420](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304492470) [C421](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304472245) [T09-R21](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877686642).

<a id="l50"></a>

### L50 — Organizational, unrelated or unreadable entries

**Context. Coverage-only records.**

Thread setup/lock notices, off-topic exchanges and two moderation-hidden bodies are retained for complete coverage. The missing wildlife/raids report supplies no basis to connect HD; another commenter’s attribution is also unverified.

**Next discriminating check:** No technical diagnosis from unavailable or nonspecific text. Obtain a concrete reproduction if the wildlife/events concern is raised again.

**Steam evidence:** [C060](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810592556369209) [C061](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810592556361247) [C112](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369336803) [C113](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369320921) [C114](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369320499) [C122](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359496591) [C205](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153330907) [C240](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436497964) [C292](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006744485) [C311](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006713343) [C312](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006713325) [C320](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006697005) [T01-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691115322) [T02-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c582804662258798304) [T07-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/) [T07-R19](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793348713836954) [T08-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527634/) [T08-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527634/#c573793348713836914) [T09-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/) [T09-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365548548) [T09-R26](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793348713836888).

<a id="l51"></a>

### L51 — Most-stocked/spoilage ingredient ordering

**Medium regression coverage. Confirmed historical ineffective fix; no new exact failure.**

#192 and T03-R26 cover cooking sort under Common Sense. PR #193’s advertised sort patch never bound; #195 corrected the reflected class/fork lookup and shipped v1.18.2. C043/C331 also ask whether spoilage cooking/butchering mods are replaced; those broader replacement questions are not proof that this specific sort remains broken.

**Next discriminating check:** Verify reflection binding for each supported Common Sense fork and observe actual ingredient choice, preserving freshness ordering within a def. Treat any fallback as a disclosed reduced capability.

**Prior reports/fix evidence:** [#137](https://github.com/Refzlund/haulers-dream/issues/137), [PR 151](https://github.com/Refzlund/haulers-dream/pull/151), [#192](https://github.com/Refzlund/haulers-dream/issues/192), [PR 193](https://github.com/Refzlund/haulers-dream/pull/193), [PR 195](https://github.com/Refzlund/haulers-dream/pull/195). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C043](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547669974) [C318](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006697529) [C331](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006690841) [T03-R26](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930510227) [T03-R27](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930511397).

<a id="l52"></a>

### L52 — CE/strict carry budgets and heavy chunks

**Medium. Capacity limits plus historical override corrections.**

Single heavy chunks may be correct under CE; stackable-chunk handling still needed #127 after earlier explanations (#85/#86). #118/#119 corrected the unconditional mech override. C078 confirms a CE problem improved, without establishing every CE path works.

**Next discriminating check:** Test live weight and bulk separately, with stacked/unstacked chunks and capacity settings at boundary values; explain expected single-item outcomes.

**Prior reports/fix evidence:** [#54](https://github.com/Refzlund/haulers-dream/issues/54), [PR 56](https://github.com/Refzlund/haulers-dream/pull/56), [#85](https://github.com/Refzlund/haulers-dream/issues/85), [#86](https://github.com/Refzlund/haulers-dream/issues/86), [PR 89](https://github.com/Refzlund/haulers-dream/pull/89), [#118](https://github.com/Refzlund/haulers-dream/issues/118), [PR 119](https://github.com/Refzlund/haulers-dream/pull/119), [#124](https://github.com/Refzlund/haulers-dream/issues/124), [PR 127](https://github.com/Refzlund/haulers-dream/pull/127), [PR 228](https://github.com/Refzlund/haulers-dream/pull/228). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C072](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587775149) [C078](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047359821240321) [C118](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369305721) [C119](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369304818) [C120](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369290087) [C121](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369279811) [C124](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359472762) [C127](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702285559) [C215](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153117409) [C218](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436655503) [C305](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006731173) [C334](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689555) [C362](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006673764).

<a id="l53"></a>

### L53 — Forced jobs and third-party interruption behavior

**Medium. Compatibility leads and historical queue fixes.**

Better Autocasting for VPE was suspected of interrupting queues; #39 documented a compatibility assessment but no matching successful user retest. Achtung compatibility was requested. C055’s queued strips later received #241 protections. These are not the same as ordinary end-of-work unloading.

**Next discriminating check:** Interrupt and resume representative queued HD jobs via combat/autocast and forced work; verify cargo and reservation cleanup and preserve the user’s queued order.

**Prior reports/fix evidence:** [PR 39](https://github.com/Refzlund/haulers-dream/pull/39), [PR 241](https://github.com/Refzlund/haulers-dream/pull/241). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C055](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669378657) [C368](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006670371) [C371](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006666018) [C412](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304508651) [C417](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304494570).

<a id="l54"></a>

### L54 — Pickup delays and harvest presentation

**Low. Feature confirmed; scope regression later corrected.**

The optional progress/delay request was implemented and T05-R03 confirms it. Applying it to every tiny cleanup stack then caused slowdown (T03-R09); #153 narrowed default delay scope and #161 handled standing pauses. #189 made manual/growing-zone yield presentation more consistent.

**Next discriminating check:** Keep timing checks for deliberate pickup versus automatic cleanup and transport loading, including another pawn collecting the same yield.

**Prior reports/fix evidence:** [#121](https://github.com/Refzlund/haulers-dream/issues/121), [PR 131](https://github.com/Refzlund/haulers-dream/pull/131), [PR 153](https://github.com/Refzlund/haulers-dream/pull/153), [#160](https://github.com/Refzlund/haulers-dream/issues/160), [PR 161](https://github.com/Refzlund/haulers-dream/pull/161), [PR 165](https://github.com/Refzlund/haulers-dream/pull/165), [#187](https://github.com/Refzlund/haulers-dream/issues/187), [PR 189](https://github.com/Refzlund/haulers-dream/pull/189). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C210](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153295615) [T03-R09](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640496737) [T03-R10](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640518850) [T03-R11](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640532134) [T03-R18](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572669129317696254) [T03-R20](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930317422) [T03-R21](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930339470) [T03-R22](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930384644) [T03-R23](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930426370) [T03-R24](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930442645) [T03-R25](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930454489) [T05-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/) [T05-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/#c572668602725035719) [T05-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/#c572668858640381090) [T05-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/#c572668858640427846).

<a id="l55"></a>

### L55 — Multiplayer and threading compatibility

**Medium regression coverage. Support introduced; later deterministic-state fixes.**

Early Multiplayer requests were followed by #43 and the developer’s announcement. Missing-API startup failures without Multiplayer needed #51. #89 documents a client-local menu mutation caught in review; #252 fixes mixed-cargo ordering across clients. Those pre-release discoveries are not independent field recurrence reports. PR #267 does not claim Multiplayer testing.

**Next discriminating check:** Verify multiplayer replay/save joining, client-only menus, deterministic cargo order and absence of optional dependencies. Test thread-sensitive work scans separately from multiplayer synchronization.

**Prior reports/fix evidence:** [PR 43](https://github.com/Refzlund/haulers-dream/pull/43), [PR 51](https://github.com/Refzlund/haulers-dream/pull/51), [PR 89](https://github.com/Refzlund/haulers-dream/pull/89), [PR 181](https://github.com/Refzlund/haulers-dream/pull/181), [PR 252](https://github.com/Refzlund/haulers-dream/pull/252). Merged/closed distinctions are listed in the historical register below.

**Steam evidence:** [C129](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702281949) [C399](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304578838) [C400](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304578033) [C403](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304576711) [T07-R15](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877534478) [T07-R16](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877627339) [T07-R18](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877726197).

## Complete open GitHub register

All times in this register are **CEST (UTC+02:00)**. A version marked unstated is not inferred from filing date. Active-mod counts come from the submitted report, not a reproduced test. All 17 conversation comments are paraphrased below, including nontechnical and automated entries. Linked external attachments are preserved as evidence leads; their contents were not all independently audited.

<a id="gh148"></a>

### PR #148

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/pull/148)** · [L22](#l22)

**Original title:** fix: self-heal UnfinishedThings bound to stack-less bills (pawn-freeze on load)

**State:** OPEN — draft. **Author/reporter:** flixzf. **Created:** 2026-07-07 13:45:55 CEST. **Updated:** 2026-07-07 16:33:43 CEST.

Draft from a contributor proposing repair of unfinished things bound to bills whose billStack is missing after load, associated with WorkbenchConnect. Proposed load/save guards and a later load backstop unbind orphaned bills while preserving work and ingredients. The author reports 2,080 tests but explicitly needs a corrupted-save in-game reproduction; a sibling job-held bill case is outside the proposed guard. Still open and unmerged.

**PR metadata:** head `6430509acf2f759ca1849efd86062459ab45cf01`; 3 changed files returned; no submitted reviews, inline comments or linked closing issues returned. Merge-state field: `CLEAN`. No check results were returned in the captured check rollup, so CI success is not established by this snapshot. Author-reported test results are claims from the PR body. No merge or release is implied by the changeset bot.

**Conversation comments: 2.**

- [GH148-C4903415050](https://github.com/Refzlund/haulers-dream/pull/148#issuecomment-4903415050) — 2026-07-07 13:45:58 CEST — changeset-bot[bot]: Changeset bot detects a proposed patch. Its future-release wording does not establish merge or publication.

- [GH148-C4904961695](https://github.com/Refzlund/haulers-dream/pull/148#issuecomment-4904961695) — 2026-07-07 16:33:43 CEST — Refzlund: Maintainer thanks the contributor and asks them to request review when ready; no approval or gameplay confirmation.

<a id="gh255"></a>

### Issue #255

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/255)** · [L12](#l12)

**Original title:** [Bug] My pawns sometimes reserve items indefinetily, while they keep doing something else. In this case Patricia keeps t

**State:** OPEN. **Author/reporter:** RocketRacoon. **Created:** 2026-08-09 20:13:30 CEST. **Updated:** 2026-08-15 20:08:26 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 1355.

Kibble remains reserved by a pawn while she sleeps/does unrelated work; no errors reported. Reporter RocketRacoon (GitHub AlexVran) supplied a screenshot with the report. The maintainer’s initial alternative-mod hypothesis was followed by the reporter’s observation that removing HD stopped recurrence. No original incident logs or minimal reproduction establish sole cause.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/TtgotPRjvvf0cdAB.log) · [Player.log](https://reports.refzlund.com/files/16hvrtTMFwuAFDHX.log)

**Conversation comments: 3.**

- [GH255-C5234743392](https://github.com/Refzlund/haulers-dream/issues/255#issuecomment-5234743392) — 2026-08-10 02:46:26 CEST — Refzlund: Maintainer tentatively suspects another animal-job mod and requests a minimal reproduction; explicitly uncertain.

- [GH255-C5236650604](https://github.com/Refzlund/haulers-dream/issues/255#issuecomment-5236650604) — 2026-08-10 08:26:14 CEST — AlexVran: Reporter removes HD to observe recurrence; says reloading immediately clears the reservation. No original logs retained.

- [GH255-C5303554026](https://github.com/Refzlund/haulers-dream/issues/255#issuecomment-5303554026) — 2026-08-15 20:08:26 CEST — AlexVran: Reporter has not seen the issue recur since removing HD; cannot substantiate cause further because the incident logs are unavailable.

<a id="gh256"></a>

### Issue #256

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/256)** · [L04](#l04)

**Original title:** [Compatibility] pawns keep trying to merge items on a loop, a conflict with as above so below 2 from what i can tell: [H

**State:** OPEN. **Author/reporter:** Rylan. **Created:** 2026-08-10 15:01:19 CEST. **Updated:** 2026-08-10 15:54:45 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 1163.

Rylan reports an item-merging loop and suspects As Above So Below 2. The embedded rox-wool warning says the same stack was bulk-anchored six times without reducing its count; its call stack reaches BuildBulkJob through WorkGiver_HaulGeneral.HasJobOnThing during candidate scanning. That warning is evidence of the guard firing, not proof of RimIOT or another mod as the cause.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/gExj9CQhrYNDfRXh.log) · [Player.log](https://reports.refzlund.com/files/hb7Z0et5XpPwhKjM.log)

**Conversation comments: 1.**

- [GH256-C5241237484](https://github.com/Refzlund/haulers-dream/issues/256#issuecomment-5241237484) — 2026-08-10 15:54:45 CEST — forwarded-reports[bot]: Reporter says disabling bulk hauling stops the observed loop.

<a id="gh257"></a>

### Issue #257

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/257)** · [L20](#l20)

**Original title:** [Feature] 追加して欲しいのは、マップポータルへの死体一括運搬の機能

**State:** OPEN. **Author/reporter:** Tdog. **Created:** 2026-08-10 16:59:52 CEST. **Updated:** 2026-08-10 16:59:52 CEST.

**Reported HD version:** 1.23.0.0. **Active mods:** 138.

Japanese request to bulk-carry insect corpses through map portals after cave quests; currently each corpse makes its own trip. Separate portal loading/feature-coverage question from corpse sweeping into ordinary storage.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/H3c7K0nsC8c5R7HA.log) · [Player.log](https://reports.refzlund.com/files/034sQ1nf97RviC4e.log)

**Conversation comments: 0.**

<a id="gh258"></a>

### Issue #258

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/258)** · [L01](#l01)

**Original title:** [Bug] Pawns don't collect all items before crafting when the gizmo button is enabled, the settings in the options window

**State:** OPEN. **Author/reporter:** Lensrub. **Created:** 2026-08-13 19:47:38 CEST. **Updated:** 2026-08-13 19:47:38 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 192.

Lensrub enables the workbench gathering gizmo and global HD options while disabling Common Sense’s own ingredient gathering, but pawns still do not collect everything before crafting. This is the same remaining behavior acknowledged on #243 and claimed fixed in v1.24.0.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/E8wkFNzmgXKFy07W.log) · [Player.log](https://reports.refzlund.com/files/6SmQkvVtAZ8039dc.log)

**Conversation comments: 0.**

<a id="gh259"></a>

### Issue #259

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/259)** · [L16](#l16)

**Original title:** [Bug] What happened: When clicking "Deliver resources" on a construction project by a pawn that does not have the "Build

**State:** OPEN. **Author/reporter:** Lensrub. **Created:** 2026-08-16 20:14:37 CEST. **Updated:** 2026-08-16 20:14:37 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 194.

Lensrub orders Deliver resources with Build unassigned. Pawn delivers and then starts building; expectation is delivery only followed by other assigned work. Distinguish work assignment from inability to perform construction.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/cbZ0169BHjT113uY.log) · [Player.log](https://reports.refzlund.com/files/FyRFBCMFEPQoPMWj.log)

**Conversation comments: 0.**

<a id="gh260"></a>

### Issue #260

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/260)** · [L17](#l17)

**Original title:** storage-commit getting hit inconsistently when unloading animals

**State:** OPEN. **Author/reporter:** nullpat. **Created:** 2026-08-17 02:01:53 CEST. **Updated:** 2026-08-17 03:10:27 CEST.

**Reported HD version:** unstated. **Active mods:** not given as a count.

nullpat supplies a minimal Harmony + HD test: spawn and tame a horse, create allowed storage, load/send a caravan and return it, then observe inconsistent storage-commit participation while unloading animal inventory. A 10% minimum-space setting is mentioned. The issue does not declare an HD version.

**Conversation comments: 0.**

<a id="gh261"></a>

### Issue #261

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/261)** · [L04](#l04)

**Original title:** [Bug] I cannot edit my last report problem is not gone when uncheck "top existing staks" still apples and smoe leaves ar

**State:** OPEN. **Author/reporter:** HaPpY. **Created:** 2026-08-17 17:24:21 CEST. **Updated:** 2026-08-18 19:11:29 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 214.

HaPpY cannot edit the previous report and says disabling Top existing stacks does not stop erratic apples/smokeleaf stocking. The prior report is not identified clearly enough to equate it with #269. Steam C019 is a cross-post of this complaint.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/rMZ95ndKKGTDvTEA.log) · [Player.log](https://reports.refzlund.com/files/ECby7MNHXsDDBBtH.log)

**Conversation comments: 1.**

- [GH261-C5331573201](https://github.com/Refzlund/haulers-dream/issues/261#issuecomment-5331573201) — 2026-08-18 19:11:29 CEST — forwarded-reports[bot]: A single dot; no additional technical information.

<a id="gh262"></a>

### Issue #262

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/262)** · [L10](#l10)

**Original title:** Intermittent GUI crash on load

**State:** OPEN. **Author/reporter:** mikefirebeard. **Created:** 2026-08-19 13:23:12 CEST. **Updated:** 2026-08-22 10:07:27 CEST.

**Reported HD version:** unstated. **Active mods:** not given as a count.

FireBeard/mikefirebeard reports intermittent GUI failure after load: interface disappears and Escape does not open options. Initially removal of TD Enhancement appears helpful, but later testing expands to other mods and exceptions. Do not treat every removed mod as an isolated incompatible mod. The manually filed issue does not state an HD version; #264/#265 in the same campaign do.

**Conversation comments: 4.**

- [GH262-C5341477114](https://github.com/Refzlund/haulers-dream/issues/262#issuecomment-5341477114) — 2026-08-19 13:26:45 CEST — mikefirebeard: Adds a Player.log attachment: [attachment](https://github.com/user-attachments/files/31220941/Player.log)

- [GH262-C5369956723](https://github.com/Refzlund/haulers-dream/issues/262#issuecomment-5369956723) — 2026-08-21 14:45:07 CEST — mikefirebeard: Further tests now crash/error regularly. Removing HugsLib, Allow Tool and Defensive Positions enables quicktest in one reduced setup. Mentions PlaySettings.DoPlaySettingsGlobalControls and HugsLib OnGUI exceptions; this is an observation, not an isolated cause.

- [GH262-C5377860369](https://github.com/Refzlund/haulers-dream/issues/262#issuecomment-5377860369) — 2026-08-22 06:26:10 CEST — mikefirebeard: Adds another log from a changed mod list including HD and TD Enhancement: [attachment](https://github.com/user-attachments/files/31327597/Player.log)

- [GH262-C5379178652](https://github.com/Refzlund/haulers-dream/issues/262#issuecomment-5379178652) — 2026-08-22 10:07:27 CEST — mikefirebeard: To continue the current save, removes Surgery Never Fail, Dubs Mint Minimap, CM Color Coded Mood Bar, Interaction Bubbles and Smarter Construction together. No individual conflict isolation.

<a id="gh263"></a>

### Issue #263

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/263)** · [L09](#l09)

**Original title:** [Compatibility] Which mod conflicts with Hauler's Dream: "Big and Small - Sapient Animals" - Has an item (BS_Sapienator)

**State:** OPEN. **Author/reporter:** ErikRedbeard. **Created:** 2026-08-20 20:04:42 CEST. **Updated:** 2026-08-20 20:29:39 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 405.

ErikRedbeard uses Big and Small — Sapient Animals and BS_Sapienator. Newly made sapient haulers work; previously converted animals/mechs lose bulk behavior after save/reload or a fresh application start. Race changes alter the behavior. Follow-up reproduces with a reduced HD + Big & Small setup and required dependencies.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/QDdfsV9bwTfQ7W3a.log) · [Player.log](https://reports.refzlund.com/files/awHMGRfj0nzVAR5Q.log)

**Conversation comments: 1.**

- [GH263-C5360067014](https://github.com/Refzlund/haulers-dream/issues/263#issuecomment-5360067014) — 2026-08-20 20:29:39 CEST — forwarded-reports[bot]: Reporter reproduces with HD, Big & Small Sapient Animals and requirements using a lifter bot; reduced-mod confirmation strengthens the reload-specific report.

<a id="gh264"></a>

### Issue #264

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/264)** · [L10](#l10)

**Original title:** [Bug] Multiple exceptions in log. Happens 1/2 of time when application restarted and load dev quicktest. I could access 

**State:** OPEN. **Author/reporter:** FireBeard. **Created:** 2026-08-22 06:44:38 CEST. **Updated:** 2026-08-22 06:44:38 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 28.

FireBeard reports exceptions on roughly half of full application restarts followed by a developer quicktest, with only 28 active mods. Mod options are accessible in this variant. Related to #262/#265, but preserve the differing UI outcome.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/81qNqakX1iw3B3W7.log) · [Player.log](https://reports.refzlund.com/files/wAFPZgnZ8V4z2c9Y.log)

**Conversation comments: 0.**

<a id="gh265"></a>

### Issue #265

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/265)** · [L10](#l10)

**Original title:** [Bug] Multiple exceptions in log running a dev quicktest when Smarter Construction is loaded after Hauler's Dream. Loadi

**State:** OPEN. **Author/reporter:** FireBeard. **Created:** 2026-08-22 08:33:20 CEST. **Updated:** 2026-08-22 10:21:25 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 25.

FireBeard reports dev-quicktest exceptions with Smarter Construction loaded after HD (25 active mods). Putting Smarter Construction first initially seems helpful, then the follow-up explicitly says crashes still occur about half the time. A load-order cure is not established.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/rhzeBCo8feYjtKVA.log) · [Player.log](https://reports.refzlund.com/files/1rAcQNnKrnsiJWwC.log)

**Conversation comments: 1.**

- [GH265-C5379263383](https://github.com/Refzlund/haulers-dream/issues/265#issuecomment-5379263383) — 2026-08-22 10:21:25 CEST — mikefirebeard: Retracts the load-order workaround: the game still crashes about half the time after changing order.

<a id="gh266"></a>

### Issue #266

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/266)** · [L13](#l13)

**Original title:** [Report] When ordering a pawn to "haul everything", the pawn reserves all items lying nearby. If you send another pawn t

**State:** OPEN. **Author/reporter:** Lensrub. **Created:** 2026-08-22 17:30:53 CEST. **Updated:** 2026-08-22 17:30:53 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 196.

Lensrub reports that Haul everything immediately reserves all nearby targets. Forcing a second pawn to take one target cancels the first pawn’s entire sweep, making it difficult to divide an area manually. Requests a less disruptive reservation/assignment behavior.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/Jzfdk6JmAvmarXrr.log) · [Player.log](https://reports.refzlund.com/files/fT4CimtyGxQ0ykeJ.log)

**Conversation comments: 0.**

<a id="gh267"></a>

### PR #267

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/pull/267)** · [L21](#l21)

**Original title:** feat: bulk-unload transporters (toggle + prioritized order, load/unload mutual exclusion

**State:** OPEN. **Author/reporter:** nullpat. **Created:** 2026-08-25 10:50:00 CEST. **Updated:** 2026-08-27 01:52:39 CEST.

Contributor nullpat proposes bulk unloading for CompTransporter: a toggle, chained prioritized unload, automatic clearing when empty and mutual exclusion with loading. Includes corpse options and CE/hands fallback; excludes Vehicle Framework and live pawns. Author reports gameplay checks and plans more dogfooding; Multiplayer/mod-compatibility tests remain absent. Descriptive polish, translation consistency and duplicated-driver code are review items. Open, not draft, unmerged.

**PR metadata:** head `4e2b4b34101512080864d19323dc0b18da250ae9`; 57 changed files returned; no submitted reviews, inline comments or linked closing issues returned. Merge-state field: `UNSTABLE`. No check results were returned in the captured check rollup, so CI success is not established by this snapshot. Author-reported test results are claims from the PR body. No merge or release is implied by the changeset bot.

**Conversation comments: 2.**

- [GH267-C5407844472](https://github.com/Refzlund/haulers-dream/pull/267#issuecomment-5407844472) — 2026-08-25 10:50:04 CEST — changeset-bot[bot]: Changeset bot detects a proposed minor release; this is pending-PR metadata, not evidence the feature shipped.

- [GH267-C5407936551](https://github.com/Refzlund/haulers-dream/pull/267#issuecomment-5407936551) — 2026-08-25 10:57:07 CEST — nullpat: Contributor will dogfood the branch for several more days and discloses AI-tool provenance; no maintainer approval or completed compatibility test is recorded.

<a id="gh268"></a>

### Issue #268

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/268)** · [L04](#l04)

**Original title:** [Bug] Not sure if bug or mod compatibility issue. My colonists keep hauling and dropping food from/into the crates.

**State:** OPEN. **Author/reporter:** Enig. **Created:** 2026-08-26 20:52:08 CEST. **Updated:** 2026-08-29 12:54:25 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 338.

Enig reports repeated food pickup/drop into crates. Disabling Common Sense advanced/stockpile ingredient behavior and HD Move supplies closer did not fix it. Disabling bulk hauling changes behavior. An apparent Stack gap 25–75 workaround was subsequently retracted; only bulk hauling remaining disabled avoids the observed loop.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/aHzDxBkZTM2TeaNK.log) · [Player.log](https://reports.refzlund.com/files/kJAP7S3jBnJ04jen.log)

**Conversation comments: 2.**

- [GH268-C5430010324](https://github.com/Refzlund/haulers-dream/issues/268#issuecomment-5430010324) — 2026-08-26 21:20:46 CEST — forwarded-reports[bot]: Reports that disabling bulk hauling leaves single-item hauling to the nearest higher-priority stove storage; enabling bulk again with Stack gap 25–75 appears to help temporarily.

- [GH268-C5461920982](https://github.com/Refzlund/haulers-dream/issues/268#issuecomment-5461920982) — 2026-08-29 12:54:25 CEST — forwarded-reports[bot]: Explicitly retracts that workaround: the loop returns, and only keeping bulk hauling disabled avoids it.

<a id="gh269"></a>

### Issue #269

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/269)** · [L03](#l03)

**Original title:** [Bug] Why my pawns move back and forth and refill the stacks to full capacity, for example in the case of a hopper shelf

**State:** OPEN. **Author/reporter:** HaPpY. **Created:** 2026-08-29 22:13:13 CEST. **Updated:** 2026-08-29 22:13:13 CEST.

**Reported HD version:** 1.24.0.0. **Active mods:** 232.

HaPpY reports repeated full-capacity refilling and wasted back-and-forth trips, especially hopper shelves, and requests Storage Refill Hysteresis support. Steam C005/C006 are the same report. This concerns refill thresholds and per-stack limits, not necessarily multi-pawn over-commit.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/D3qC5AD2HYodb3ue.log) · [Player.log](https://reports.refzlund.com/files/kqqHoki857nyyPjz.log)

**Conversation comments: 0.**

<a id="gh270"></a>

### Issue #270

**[Original GitHub item](https://github.com/Refzlund/haulers-dream/issues/270)** · [L02](#l02)

**Original title:** [Report] 当高优先级的仓库缺货时，需要从低优先级仓库搬运过去，这时候会有很多人去搬运，即使已经有人在搬了，手上的货物还没入库，就会有人一直去取货。然后那边仓库满了之后，后面搬过来的货物就放不下了，就全堆在地上了

**State:** OPEN. **Author/reporter:** 子鱼丶. **Created:** 2026-09-01 16:00:19 CEST. **Updated:** 2026-09-01 16:00:19 CEST.

**Reported HD version:** 1.21.0.0. **Active mods:** 666.

Chinese report: when high-priority storage has a shortage, multiple pawns keep fetching from lower-priority storage without accounting for cargo already in flight. The destination fills before later arrivals; surplus is dropped on the floor. The reported HD version is 1.21.0.0, so the September posting date cannot establish failure of v1.24.0.

**Links supplied in the body:** [Hauler's Dream](https://reports.refzlund.com/files/VGQ7F2vNt3803wdR.log) · [Player.log](https://reports.refzlund.com/files/Ai7K2DdtXrHwnwQq.log)

**Conversation comments: 0.**

## Historical GitHub evidence register

These are supporting reports and PRs referenced by the grouped ledger. **Closed does not mean merged or fixed.** Dates are creation and merge times in CEST; release claims are supported separately by release links in the recurrence analysis. A closed issue’s latest disposition can supersede prose written earlier in a PR. The full historical repository was collected to find these chains; this table does not claim every closed issue was independently reproduced or exhaustively diagnosed.

| GitHub source | Title | State / actual merge | Created (CEST) |
|---|---|---|---|
| [PR 5](https://github.com/Refzlund/haulers-dream/pull/5) | fix: feature verification sweep - Strip on haul + Planned crafting | Merged 2026-06-12 01:32:56 | 2026-06-11 22:21:31 |
| [PR 7](https://github.com/Refzlund/haulers-dream/pull/7) | fix: prompt inventory unloading + restore mod-options scrollbar | Merged 2026-06-13 23:54:28 | 2026-06-13 11:50:59 |
| [PR 8](https://github.com/Refzlund/haulers-dream/pull/8) | chore: tool to set Steam Workshop tags (Mod, 1.6) | Merged 2026-06-13 12:58:15 | 2026-06-13 12:17:46 |
| [PR 12](https://github.com/Refzlund/haulers-dream/pull/12) | fix: inventory unload loops | Merged 2026-06-14 12:22:18 | 2026-06-14 11:17:42 |
| [PR 14](https://github.com/Refzlund/haulers-dream/pull/14) | Investigate invisible-UI + work-interrupt reports; harden the unload alert | Merged 2026-06-14 13:02:21 | 2026-06-14 12:48:41 |
| [PR 16](https://github.com/Refzlund/haulers-dream/pull/16) | Fix pawns freezing in 'unloading inventory' over Yayo's Combat 3 ammo | Merged 2026-06-14 17:51:09 | 2026-06-14 14:06:27 |
| [PR 17](https://github.com/Refzlund/haulers-dream/pull/17) | Automatically keep saves safe to disable Hauler's Dream from | Merged 2026-06-14 17:55:50 | 2026-06-14 16:08:30 |
| [PR 18](https://github.com/Refzlund/haulers-dream/pull/18) | Batch material delivery across a planned construction route | Merged 2026-06-14 17:49:47 | 2026-06-14 17:18:28 |
| [PR 20](https://github.com/Refzlund/haulers-dream/pull/20) | fix: stop colonists from unloading their "Simple Sidearms" sidearm | Merged 2026-06-14 18:55:11 | 2026-06-14 18:31:25 |
| [PR 21](https://github.com/Refzlund/haulers-dream/pull/21) | fix: keep sidearms, unload non-sidearm equipment | Merged 2026-06-14 21:54:47 | 2026-06-14 19:40:29 |
| [PR 22](https://github.com/Refzlund/haulers-dream/pull/22) | [Parked] Save-disable safety + clean removal — full approach (more risk than reward; needs redesign) | Closed, **not merged** | 2026-06-14 22:27:47 |
| [PR 23](https://github.com/Refzlund/haulers-dream/pull/23) | revert: park the save-disable / clean-removal approach (#17) | Merged 2026-06-15 00:00:56 | 2026-06-15 00:00:44 |
| [PR 24](https://github.com/Refzlund/haulers-dream/pull/24) | feat: batch bill mode | Merged 2026-06-15 02:29:58 | 2026-06-15 02:27:24 |
| [PR 28](https://github.com/Refzlund/haulers-dream/pull/28) | fix: don't drop scooped yields for hauling-priority-0 pawns (e.g. growers) | Merged 2026-06-17 19:46:54 | 2026-06-17 19:38:46 |
| [PR 30](https://github.com/Refzlund/haulers-dream/pull/30) | fix: load inventory to deliver construction material to multiple nearby sites | Merged 2026-06-17 20:53:42 | 2026-06-17 20:49:01 |
| [PR 31](https://github.com/Refzlund/haulers-dream/pull/31) | fix: batch crafting ignored "pause when satisfied" / "unpause at" | Merged 2026-06-17 21:16:57 | 2026-06-17 21:10:41 |
| [PR 32](https://github.com/Refzlund/haulers-dream/pull/32) | Compat: Everybody Gets One — restore its repeat modes + correct counting | Closed, **not merged** | 2026-06-17 21:39:31 |
| [PR 33](https://github.com/Refzlund/haulers-dream/pull/33) | Compat: Storage Network — opt-in bulk-load from network servers (+ correct the spawned-storage claim) | Closed, **not merged** | 2026-06-18 11:36:36 |
| [#34](https://github.com/Refzlund/haulers-dream/issues/34) | NullReferenceException in WorkGiver 'Refuel' caused by Advanced Power Plus (Advanced Nuclear Generator) | Issue closed | 2026-06-18 18:03:32 |
| [PR 35](https://github.com/Refzlund/haulers-dream/pull/35) | fix: batch crafting, transport loading, refuel, rituals, RV unload &amp; more (reported bugs) | Merged 2026-06-19 11:34:29 | 2026-06-19 03:05:39 |
| [PR 37](https://github.com/Refzlund/haulers-dream/pull/37) | fix: corpse haul loop, transport-load refusal, stored-goods bulk-load (reported bugs) | Merged 2026-06-19 20:28:38 | 2026-06-19 19:01:58 |
| [PR 39](https://github.com/Refzlund/haulers-dream/pull/39) | feat: mod compatibility pass (16 mods) + #34 refuel fix + Migration tab | Merged 2026-06-20 00:30:24 | 2026-06-19 21:33:39 |
| [PR 41](https://github.com/Refzlund/haulers-dream/pull/41) | feat: full localization + translations for 14 languages | Merged 2026-06-20 08:51:43 | 2026-06-20 03:54:40 |
| [PR 43](https://github.com/Refzlund/haulers-dream/pull/43) | feat: RimWorld Multiplayer compatibility | Merged 2026-06-20 10:48:44 | 2026-06-20 10:22:49 |
| [PR 45](https://github.com/Refzlund/haulers-dream/pull/45) | fix: mech haul scaling, PUAH-migration crash, auto-haul toggle, VF cargo timing | Merged 2026-06-20 12:19:35 | 2026-06-20 12:18:14 |
| [PR 48](https://github.com/Refzlund/haulers-dream/pull/48) | feat: in-game issue reporting (bug/feature/compat) + My Reports thread | Merged 2026-06-21 23:04:25 | 2026-06-21 22:01:42 |
| [PR 51](https://github.com/Refzlund/haulers-dream/pull/51) | fix: startup crash without Multiplayer (#6) + foreign work-giver work-scan brick (#7) | Merged 2026-06-22 03:11:38 | 2026-06-22 03:10:34 |
| [PR 53](https://github.com/Refzlund/haulers-dream/pull/53) | feat/fix: mech carry capacity, eggbox NRE, drafted pickup, DoBill loop, Recycle This compat (#1-#5) | Merged 2026-06-22 10:27:16 | 2026-06-22 04:22:34 |
| [#54](https://github.com/Refzlund/haulers-dream/issues/54) | [Report] Supplementary pictures regarding the carrying capacity issue of mechanical body transportation | Issue closed | 2026-06-22 10:22:05 |
| [PR 56](https://github.com/Refzlund/haulers-dream/pull/56) | fix: mechanoid carrying capacity under Combat Extended (#54) | Merged 2026-06-22 11:43:57 | 2026-06-22 11:29:30 |
| [PR 57](https://github.com/Refzlund/haulers-dream/pull/57) | fix: DoBill load crash (jobless pawn) when saved mid Common Sense pre-craft cleaning | Closed, **not merged** | 2026-06-22 13:26:27 |
| [#59](https://github.com/Refzlund/haulers-dream/issues/59) | [Bug] Error when trying to close the settings window. | Issue closed | 2026-06-22 20:08:25 |
| [PR 60](https://github.com/Refzlund/haulers-dream/pull/60) | fix: settings window close error (#59) + storage-search null robustness (#58) | Merged 2026-06-22 23:11:54 | 2026-06-22 22:37:44 |
| [#62](https://github.com/Refzlund/haulers-dream/issues/62) | [Bug] Drobs iteams after compliting collective them when started new job | Issue closed | 2026-06-23 18:43:31 |
| [#63](https://github.com/Refzlund/haulers-dream/issues/63) | [Compatibility] With the "Bulk Stonecutting (Forked)" mod when you try to use a bulk stone cutting order the pawn just r | Issue closed | 2026-06-23 19:59:51 |
| [#64](https://github.com/Refzlund/haulers-dream/issues/64) | [Bug] Order planner for building | Issue closed | 2026-06-23 21:01:00 |
| [PR 65](https://github.com/Refzlund/haulers-dream/pull/65) | fix: dropped scoop-yields (#62), bulk-stonecut loop (#63), build-route planner bricking blueprints (#64) | Merged 2026-06-23 22:48:07 | 2026-06-23 22:11:35 |
| [PR 67](https://github.com/Refzlund/haulers-dream/pull/67) | feat: batch auto-finish + overshoot, sow route planner, batch dropdown tooltips | Merged 2026-06-24 00:44:12 | 2026-06-24 00:09:47 |
| [PR 68](https://github.com/Refzlund/haulers-dream/pull/68) | feat: optional batch crafting under Common Sense | Merged 2026-06-24 00:44:32 | 2026-06-24 00:31:29 |
| [PR 70](https://github.com/Refzlund/haulers-dream/pull/70) | feat: batch under Common Sense on by default + hide batch UI when off | Merged 2026-06-24 01:20:08 | 2026-06-24 01:18:20 |
| [PR 71](https://github.com/Refzlund/haulers-dream/pull/71) | fix: Workshop icons + shuttle board-wait + ritual target + passenger bulk-load | Merged 2026-06-24 22:27:54 | 2026-06-24 19:15:59 |
| [#72](https://github.com/Refzlund/haulers-dream/issues/72) | [Compatibility] When I try to drop a weapon from a pawn's inventory that was equipped as a sidearm, and then order the s | Issue closed | 2026-06-24 19:38:01 |
| [PR 73](https://github.com/Refzlund/haulers-dream/pull/73) | fix: pawns no longer run home to unload after a single mined/harvested block | Merged 2026-06-24 22:26:15 | 2026-06-24 21:41:11 |
| [#75](https://github.com/Refzlund/haulers-dream/issues/75) | 外出工作搬运。Going out to work and transporting items | Issue closed | 2026-06-25 04:47:23 |
| [#76](https://github.com/Refzlund/haulers-dream/issues/76) | [Bug] Activated only this mod since last session. Started a new colony, built a shelf after wandering a bit to make sure | Issue closed | 2026-06-25 05:26:25 |
| [PR 77](https://github.com/Refzlund/haulers-dream/pull/77) | fix: per-second hitch (issue #76) + false log-writer I/O error on shutdown | Merged 2026-06-25 14:21:56 | 2026-06-25 14:19:52 |
| [#79](https://github.com/Refzlund/haulers-dream/issues/79) | [Feature] I have a suggestion for improving the "collect work results into inventory" settings. | Issue closed | 2026-06-25 20:57:31 |
| [PR 80](https://github.com/Refzlund/haulers-dream/pull/80) | Per-category control over collecting work results | Merged 2026-06-26 01:52:48 | 2026-06-25 21:57:09 |
| [#81](https://github.com/Refzlund/haulers-dream/issues/81) | [Bug] Pawn won't carry smokeleaf joint to inventory when selecting pickup, they immediately drop it. | Issue closed | 2026-06-25 23:55:34 |
| [PR 82](https://github.com/Refzlund/haulers-dream/pull/82) | Keep a picked-up drug in inventory until it's hauled to storage | Merged 2026-06-26 01:52:24 | 2026-06-26 01:48:42 |
| [#84](https://github.com/Refzlund/haulers-dream/issues/84) | [Bug] 小人收割稻米时，每收割一次会自动装载+立刻卸载到附近 | Issue closed | 2026-06-26 14:10:29 |
| [#85](https://github.com/Refzlund/haulers-dream/issues/85) | [Report] While hauling stone chunks, pawns dont use inventory space to haul more than one stone hunk | Issue closed | 2026-06-26 20:17:06 |
| [#86](https://github.com/Refzlund/haulers-dream/issues/86) | [Report] While working on bulk stonecuting job - pawns bring stone chunks one by one, instead of using inventory to brin | Issue closed | 2026-06-26 20:17:51 |
| [#87](https://github.com/Refzlund/haulers-dream/issues/87) | [Bug] The error with dropping crops is occurring again, but now with psychoid leaves. The reports should be attached. | Issue closed | 2026-06-26 20:49:01 |
| [#88](https://github.com/Refzlund/haulers-dream/issues/88) | [Bug] [2026-06-27 21:35:28.823] [ERROR] JobDriver threw exception in toil TakeToInventory's initAction for pawn DMS_Mech | Issue closed | 2026-06-27 16:15:27 |
| [PR 89](https://github.com/Refzlund/haulers-dream/pull/89) | Fix dropped crops (#87), one-stack unloading at full capacity (#84), and a construct-delivery error (#88) | Merged 2026-06-28 04:11:22 | 2026-06-28 00:31:21 |
| [PR 91](https://github.com/Refzlund/haulers-dream/pull/91) | feat: collect fishing catches, and set item unload rules by category, support Compositable Loadouts | Merged 2026-06-28 16:27:05 | 2026-06-28 14:36:27 |
| [#92](https://github.com/Refzlund/haulers-dream/issues/92) | [Compatibility] Compositable Loadouts bill modes are missing when Hauler's Dream is installed.  Similar to Everybody Get | Issue closed | 2026-06-28 14:36:41 |
| [PR 95](https://github.com/Refzlund/haulers-dream/pull/95) | feat: main-menu report notifications and route-planner improvements | Merged 2026-07-01 13:14:27 | 2026-06-29 03:01:30 |
| [#96](https://github.com/Refzlund/haulers-dream/issues/96) | [Feature] Suggestion for the planner: multi-target support. | Issue closed | 2026-06-29 16:08:27 |
| [#103](https://github.com/Refzlund/haulers-dream/issues/103) | [Bug] Any time I tell a pawn to pick up an item (for the purposes of keeping it in their inventory) they immediately wan | Issue closed | 2026-07-01 04:26:39 |
| [PR 105](https://github.com/Refzlund/haulers-dream/pull/105) | Keep pawn-carried tools (Grab Your Tool / Tools O' Plenty compatibility) | Merged 2026-07-01 18:14:27 | 2026-07-01 17:43:01 |
| [PR 107](https://github.com/Refzlund/haulers-dream/pull/107) | fix: stop hauling from preempting doctoring, rescue, and firefighting | Merged 2026-07-01 20:15:50 | 2026-07-01 20:07:06 |
| [PR 109](https://github.com/Refzlund/haulers-dream/pull/109) | fix: "Custom (unsaved)" default config, and only show the remove-floor route option on marked floors | Merged 2026-07-01 23:02:22 | 2026-07-01 22:14:27 |
| [#110](https://github.com/Refzlund/haulers-dream/issues/110) | Regarding the "remove floor" order | Issue closed | 2026-07-01 22:28:13 |
| [PR 112](https://github.com/Refzlund/haulers-dream/pull/112) | fix: pick up and keep orders missing on corpses, container items, and piles | Merged 2026-07-02 12:58:45 | 2026-07-02 12:15:42 |
| [#114](https://github.com/Refzlund/haulers-dream/issues/114) | [Feature] 有允许内容一样但高优先级的储存组A和低优先级的B。我观察到在A中材料有少量消耗后，许多pawn会从B中搬运一整组，但是在A只放下两三个、然后把剩余的搬回B；A中的材料则由许多pawn一次一次逐渐填满。我认为这是效率有待提 | Issue closed | 2026-07-03 17:17:06 |
| [PR 116](https://github.com/Refzlund/haulers-dream/pull/116) | fix: over-hauling into near-full storage, and one-at-a-time CE ammo into shelves | Merged 2026-07-03 23:14:03 | 2026-07-03 23:05:27 |
| [#118](https://github.com/Refzlund/haulers-dream/issues/118) | [Compatibility] 在已加载combat extended的情况下，机械体的负重依旧被hauler’s dream的计算方式用携带量完全覆盖，而并非如设置中所声明的那样禁用该功能，被combat extended接管。（很抱歉， | Issue closed | 2026-07-04 06:06:48 |
| [PR 119](https://github.com/Refzlund/haulers-dream/pull/119) | fix: mech carry weight under Combat Extended is inert by default again | Merged 2026-07-04 12:59:12 | 2026-07-04 12:50:57 |
| [#121](https://github.com/Refzlund/haulers-dream/issues/121) | Feature Request: Optional pickup delay/progress bar | Issue closed | 2026-07-04 16:54:53 |
| [#123](https://github.com/Refzlund/haulers-dream/issues/123) | [Feature] Suggestion about temporary quest pawns from other factions. | Issue closed | 2026-07-04 21:20:22 |
| [#124](https://github.com/Refzlund/haulers-dream/issues/124) | [Compatibility] I have enabled "Always" under "Bulk hauling" but am having an issue with chunks, when marking chunks to  | Issue closed | 2026-07-05 01:57:14 |
| [#125](https://github.com/Refzlund/haulers-dream/issues/125) | [Bug] Can't make buildings with textile. I think the pawn tries to build them, but they haven't haul the resource yet, s | Issue closed | 2026-07-05 03:39:31 |
| [PR 127](https://github.com/Refzlund/haulers-dream/pull/127) | fix: chunks hauled one at a time under Combat Extended when a mod makes chunks stackable | Merged 2026-07-06 07:53:06 | 2026-07-06 01:59:03 |
| [PR 128](https://github.com/Refzlund/haulers-dream/pull/128) | feat: quest guests drop picked-up items when they leave your control | Merged 2026-07-06 07:53:53 | 2026-07-06 06:39:09 |
| [PR 129](https://github.com/Refzlund/haulers-dream/pull/129) | fix: colonists share portal loot gathering instead of one claiming it all | Merged 2026-07-06 07:53:09 | 2026-07-06 06:50:43 |
| [PR 130](https://github.com/Refzlund/haulers-dream/pull/130) | fix: building from inventory stalling under Combat Extended, and builders ignoring carried materials | Merged 2026-07-06 07:53:17 | 2026-07-06 06:56:39 |
| [PR 131](https://github.com/Refzlund/haulers-dream/pull/131) | feat: vanilla-like pickup delay with progress bar, on by default | Merged 2026-07-06 07:54:12 | 2026-07-06 07:04:20 |
| [PR 133](https://github.com/Refzlund/haulers-dream/pull/133) | fix: pawns pacing forever with a hauled item when no destination keeps accepting it | Merged 2026-07-06 07:53:14 | 2026-07-06 07:42:33 |
| [PR 135](https://github.com/Refzlund/haulers-dream/pull/135) | fix: log the real origin stack when an exception passes through a patched method | Merged 2026-07-06 16:16:27 | 2026-07-06 15:59:17 |
| [#137](https://github.com/Refzlund/haulers-dream/issues/137) | [Feature] My colony has an overabundance of food, with fruits and vegetables stockpiled to capacity. However, colonists  | Issue closed | 2026-07-06 19:33:37 |
| [#138](https://github.com/Refzlund/haulers-dream/issues/138) | [Bug] Lack of check for available space in the best stockpile when picking up. | Issue closed | 2026-07-06 19:55:00 |
| [PR 139](https://github.com/Refzlund/haulers-dream/pull/139) | fix: reporter dialog and reliability, plus bulk-haul over-hauling and settings-search FPS | Merged 2026-07-06 23:23:46 | 2026-07-06 21:27:02 |
| [#140](https://github.com/Refzlund/haulers-dream/issues/140) | [Bug] Issues with the unload button. | Issue closed | 2026-07-06 21:54:38 |
| [PR 142](https://github.com/Refzlund/haulers-dream/pull/142) | compat: keep the Ingredient Threshold repeat mode selectable alongside Hauler's Dream | Merged 2026-07-07 00:52:30 | 2026-07-07 00:50:59 |
| [#144](https://github.com/Refzlund/haulers-dream/issues/144) | [Bug] Kostr184's report. Colonists are still entering a loop while carrying something in prison/hospital, like on my you | Issue closed | 2026-07-07 03:13:36 |
| [PR 145](https://github.com/Refzlund/haulers-dream/pull/145) | fix: stop colonists still looping with a hemogen pack in prisons and hospitals | Merged 2026-07-07 11:16:07 | 2026-07-07 11:05:46 |
| [PR 147](https://github.com/Refzlund/haulers-dream/pull/147) | Actively steer looping haulers out of the loop, not just pause the item | Closed, **not merged** | 2026-07-07 12:22:14 |
| [PR 149](https://github.com/Refzlund/haulers-dream/pull/149) | fix: re-route stacking haulers in hand when their shared cell fills, instead of dropping and looping | Merged 2026-07-07 14:35:08 | 2026-07-07 14:18:26 |
| [PR 151](https://github.com/Refzlund/haulers-dream/pull/151) | feat: cook with the most-stocked ingredient first (opt-in) | Merged 2026-07-07 16:05:09 | 2026-07-07 15:35:45 |
| [#152](https://github.com/Refzlund/haulers-dream/issues/152) | [Bug] kostr184's report. Sorry for duplication, I don't know if logs will be applied if I just answer on previous.  1.16 | Issue closed | 2026-07-07 16:01:06 |
| [PR 153](https://github.com/Refzlund/haulers-dream/pull/153) | fix: keep-stock unload loop (#152); scope pickup delay to vanilla so cleanup is instant | Merged 2026-07-07 23:08:03 | 2026-07-07 19:59:13 |
| [PR 154](https://github.com/Refzlund/haulers-dream/pull/154) | fix: show batch state on the bill repeat-mode button | Merged 2026-07-07 23:08:40 | 2026-07-07 22:59:06 |
| [#160](https://github.com/Refzlund/haulers-dream/issues/160) | [Bug] Colonists are stucking "Standing" for ~10 seconds while harvesting fields. Exactly Mie stuck in there (idk if it w | Issue closed | 2026-07-08 12:39:48 |
| [PR 161](https://github.com/Refzlund/haulers-dream/pull/161) | Fix pickup pause on bulk-sweep orders and a harvesting/mining freeze | Merged 2026-07-08 18:51:47 | 2026-07-08 17:20:52 |
| [#162](https://github.com/Refzlund/haulers-dream/issues/162) | [Bug] Colonists are still entering endless loop in prison/hospital while hauling extracted things | Issue closed | 2026-07-08 17:21:38 |
| [#164](https://github.com/Refzlund/haulers-dream/issues/164) | [Bug] 指派运输舱内装填大量物品（如300钢铁）时，会有多个pawn向运输舱搬运，并在运输舱装填完毕后不中断多余的搬运，导致pawn工作浪费，且运输舱过装填、无法发射而需要手动重选。主脑节点呼叫的机械运输舱与玩家建造的运输舱均有此情况， | Issue closed | 2026-07-08 18:54:57 |
| [PR 165](https://github.com/Refzlund/haulers-dream/pull/165) | fix: coordinate self-pickup so pawns take the nearest yield | Merged 2026-07-08 19:19:31 | 2026-07-08 19:18:36 |
| [#167](https://github.com/Refzlund/haulers-dream/issues/167) | [Bug] 在临时地图向穿梭机装载物品时，搬运任务被分配给不同的pawn，有的pawn闲逛而有的pawn的任务无法一次搬运完成，造成效率的降低。此外，当pawn第二次搬运时，出现了一次往返仅搬运一个物品（例如，干肉饼*1）的情况，经检查该p | Issue closed | 2026-07-08 19:25:16 |
| [#168](https://github.com/Refzlund/haulers-dream/issues/168) | [Bug] 当pawn被指定装载进运输舱后，若右键强制其进行另一个运输舱装载任务，发现其一次仅搬运一组物品而不再使用可用负重，取消装载pawn并重新强制任务后恢复正常。前次反馈的二次搬运bug可能与此有关联。 | Issue closed | 2026-07-08 19:36:52 |
| [PR 169](https://github.com/Refzlund/haulers-dream/pull/169) | fix: coordinate transporter/portal bulk-loading (overfill, stranded cargo, temp-map hogging) | Merged 2026-07-08 22:15:50 | 2026-07-08 21:04:05 |
| [#171](https://github.com/Refzlund/haulers-dream/issues/171) | [Feature] 装载穿梭机、运输舱等容器时，现在的逻辑是优先拾取离容器更近的物品。然而探索结束后，pawn常位于离穿梭机最远的位置。此时安排装载，pawn不会就近拾取物品，而是拾取离自己最远而离穿梭机最近的物品，进行折返跑，实际降低了装 | Issue closed | 2026-07-09 06:28:07 |
| [PR 175](https://github.com/Refzlund/haulers-dream/pull/175) | fix: hospital/prison pacing loop (haul-aside ping-pong) + hauling-efficiency follow-ups | Merged 2026-07-10 03:42:32 | 2026-07-09 14:39:01 |
| [#176](https://github.com/Refzlund/haulers-dream/issues/176) | [Feature] Option to disable the planner for work orders for incompetent pawns. | Issue closed | 2026-07-09 18:35:50 |
| [#177](https://github.com/Refzlund/haulers-dream/issues/177) | [Compatibility] There is a compatibility issue with RIMIOT. My colonists get stuck in an infinite loop at the IOT intera | Issue closed | 2026-07-09 20:53:00 |
| [PR 179](https://github.com/Refzlund/haulers-dream/pull/179) | fix: batch-craft CE loop, RimIOT compat, planner gating, shuttle load order, gizmo order | Merged 2026-07-10 12:00:11 | 2026-07-10 06:12:16 |
| [PR 181](https://github.com/Refzlund/haulers-dream/pull/181) | fix: multithreading-mod + Common Sense compatibility (investigation round) | Merged 2026-07-10 15:18:06 | 2026-07-10 14:06:58 |
| [#184](https://github.com/Refzlund/haulers-dream/issues/184) | [Bug] My pawns are still stuck looping endlessly between the two jobs "haul all nearby items" and "unload inventory" at  | Issue closed | 2026-07-10 19:38:53 |
| [PR 185](https://github.com/Refzlund/haulers-dream/pull/185) | fix: stop the RimIOT interface-terminal haul/unload loop (#184) | Merged 2026-07-10 22:24:06 | 2026-07-10 22:15:52 |
| [#187](https://github.com/Refzlund/haulers-dream/issues/187) | [Bug] Issue when stripping corpses and haul after drilling. | Issue closed | 2026-07-11 12:10:48 |
| [#188](https://github.com/Refzlund/haulers-dream/issues/188) | Opportunistic loading should account for already incoming carried cargo | Issue closed | 2026-07-11 14:36:26 |
| [PR 189](https://github.com/Refzlund/haulers-dream/pull/189) | fix: yield-collection consistency, corpse-strip keep, drill collection, search FPS, opportunistic-load claim (#187, #188, #138) | Merged 2026-07-12 03:38:06 | 2026-07-11 22:41:42 |
| [#192](https://github.com/Refzlund/haulers-dream/issues/192) | [Bug] After this latest update, my colonists are still stuck in an infinite task loop at the RIMIOT interaction terminal | Issue closed | 2026-07-12 08:16:06 |
| [PR 193](https://github.com/Refzlund/haulers-dream/pull/193) | fix: RimIOT terminal haul loop + most-stocked cook sort under Common Sense (#192) | Merged 2026-07-12 16:06:59 | 2026-07-12 16:01:22 |
| [PR 195](https://github.com/Refzlund/haulers-dream/pull/195) | fix: stray Common Sense warning + make most-stocked cook sort bind across CS forks (#192 follow-up) | Merged 2026-07-12 23:12:30 | 2026-07-12 23:03:58 |
| [#197](https://github.com/Refzlund/haulers-dream/issues/197) | [Compatibility] Im havint trouble in summoning a humanoid mech from the mod Dead Man Switch, with the voidlink from WVC  | Issue closed | 2026-07-13 01:15:12 |
| [PR 198](https://github.com/Refzlund/haulers-dream/pull/198) | Keep-in-inventory amounts + Gear-tab control; mech work-type compat (#197); menu perf (#138) | Merged 2026-07-13 12:59:55 | 2026-07-13 10:12:33 |
| [#200](https://github.com/Refzlund/haulers-dream/issues/200) | [Compatibility] compositable loadouts | Issue closed | 2026-07-13 15:56:44 |
| [#201](https://github.com/Refzlund/haulers-dream/issues/201) | [Bug] Hendricks picks up crafted items into her inventory while doing crafting jobs, but does not unload them into a sto | Issue closed | 2026-07-14 07:05:02 |
| [PR 202](https://github.com/Refzlund/haulers-dream/pull/202) | feat: away-from-home vehicle sourcing, plus crafter/compat/alert fixes | Merged 2026-07-14 18:03:15 | 2026-07-14 18:01:52 |
| [#204](https://github.com/Refzlund/haulers-dream/issues/204) | [Bug] Loadout meals being returned to fridge. Constant loop | Issue closed | 2026-07-14 18:06:37 |
| [PR 205](https://github.com/Refzlund/haulers-dream/pull/205) | fix: CE generic loadout slots now counted in keep-stock (meal loop) | Merged 2026-07-15 02:17:44 | 2026-07-15 02:05:28 |
| [PR 209](https://github.com/Refzlund/haulers-dream/pull/209) | fix: increase Player.log attachment from 400 KB to backend's 5 MB cap | Merged 2026-07-15 16:30:57 | 2026-07-15 16:29:18 |
| [#211](https://github.com/Refzlund/haulers-dream/issues/211) | [Bug] Issue when stripping corpses when "leave on corpse" option on | Issue closed | 2026-07-15 20:42:14 |
| [PR 212](https://github.com/Refzlund/haulers-dream/pull/212) | fix: leave-on-corpse tainted apparel policy for manual strip orders (#211) | Merged 2026-07-15 22:36:58 | 2026-07-15 22:22:45 |
| [#214](https://github.com/Refzlund/haulers-dream/issues/214) | [Bug] After this version update, my colonists once again get stuck in an infinite loop of two jobs at the RIMIOT interac | Issue closed | 2026-07-16 00:56:57 |
| [#215](https://github.com/Refzlund/haulers-dream/issues/215) | [Feature] Improvement for the unload button. | Issue closed | 2026-07-16 12:02:13 |
| [PR 216](https://github.com/Refzlund/haulers-dream/pull/216) | fix: stop the RimIOT terminal haul/unload loop at its root (#214) | Merged 2026-07-16 12:36:48 | 2026-07-16 12:16:55 |
| [PR 218](https://github.com/Refzlund/haulers-dream/pull/218) | fix: unload gizmo left-click unloads now, shift-click queues (#215) | Merged 2026-07-16 14:46:24 | 2026-07-16 14:35:43 |
| [#219](https://github.com/Refzlund/haulers-dream/issues/219) | [Bug] Builder behavior when resources are being delivered by another pawn. | Issue closed | 2026-07-16 18:19:34 |
| [PR 220](https://github.com/Refzlund/haulers-dream/pull/220) | fix: reserve a forced construction for its builder (#219) | Merged 2026-07-16 21:35:03 | 2026-07-16 21:31:27 |
| [#222](https://github.com/Refzlund/haulers-dream/issues/222) | [Bug] Boss man, me again. Another issue. With sidearms when pawns have two weapons, they try to unload their sword or kn | Issue closed | 2026-07-17 20:27:37 |
| [#224](https://github.com/Refzlund/haulers-dream/issues/224) | Possible compatibility issue: Inventory/Gear tab automatically opens when selecting a pawn carrying items | Issue closed | 2026-07-19 17:39:16 |
| [#225](https://github.com/Refzlund/haulers-dream/issues/225) | [Bug] 1. Tell pawn to pick up 2x medicine | Issue closed | 2026-07-19 18:06:54 |
| [PR 226](https://github.com/Refzlund/haulers-dream/pull/226) | feat: urgent-haul bulk pickup, cremation strip, non-home scope; fix sidearm unload, haul item/all, keep surplus | Merged 2026-07-20 10:50:39 | 2026-07-20 09:43:26 |
| [PR 228](https://github.com/Refzlund/haulers-dream/pull/228) | feat: strict carry weight: clarify tooltips, clamp bulk-refuel, add a "Max carry weight" cap | Merged 2026-07-20 18:02:18 | 2026-07-20 11:49:40 |
| [#229](https://github.com/Refzlund/haulers-dream/issues/229) | [Report] Possible exploits | Issue closed | 2026-07-20 22:10:15 |
| [#230](https://github.com/Refzlund/haulers-dream/issues/230) | [Feature] Individual button for workbenches to cancel the collection of materials. | Issue closed | 2026-07-21 22:32:22 |
| [#231](https://github.com/Refzlund/haulers-dream/issues/231) | [Bug] I've noticed that when there's no free storage to put items in, pawns decide to place them on the ground. I just d | Issue closed | 2026-07-23 23:02:32 |
| [#232](https://github.com/Refzlund/haulers-dream/issues/232) | [Compatibility] 1.选中殖民者右键点击中性私酿会报错。（中性私酿是Rimsenal Xenotype Pack - Harana里面新增的一种酒） | Issue closed | 2026-07-24 10:29:18 |
| [#233](https://github.com/Refzlund/haulers-dream/issues/233) | [Compatibility] What happened: | Issue closed | 2026-07-26 18:40:34 |
| [#235](https://github.com/Refzlund/haulers-dream/issues/235) | [Bug] All colonists are wandering idle, not tending to wounds, but do eat. When I force a pawn to mine, they don't produ | Issue closed | 2026-07-28 09:21:42 |
| [#236](https://github.com/Refzlund/haulers-dream/issues/236) | [Bug] idk exactly, something between hauler dream and keyz allow tools: | Issue closed | 2026-07-28 22:09:26 |
| [#237](https://github.com/Refzlund/haulers-dream/issues/237) | [Feature] パッチ制作をスライダーだけじゃなく数字入力もできるようにしてほしい | Issue closed | 2026-07-30 05:11:10 |
| [#238](https://github.com/Refzlund/haulers-dream/issues/238) | [Bug] Saved profiles keep reverting to default | Issue closed | 2026-08-01 07:26:52 |
| [PR 240](https://github.com/Refzlund/haulers-dream/pull/240) | Add Traditional Chinese translation | Merged 2026-08-02 11:24:21 | 2026-08-02 00:38:16 |
| [PR 241](https://github.com/Refzlund/haulers-dream/pull/241) | fix: resolve all eleven open issues (#224, #229–#238) | Merged 2026-08-02 16:18:24 | 2026-08-02 12:18:08 |
| [#243](https://github.com/Refzlund/haulers-dream/issues/243) | [Bug] Collecting all items before starting crafting. | Issue closed | 2026-08-02 17:24:23 |
| [PR 244](https://github.com/Refzlund/haulers-dream/pull/244) | mix: corpse hauling options, plus the plan-craft gate and cave-exit hauling fixes | Merged 2026-08-02 21:29:57 | 2026-08-02 19:15:22 |
| [#247](https://github.com/Refzlund/haulers-dream/issues/247) | [Bug] 在地下仓库中向地面转运75活铁，只有一个搬运机能够进行搬运工作。搬运机没有一次将所有物品搬运，而是每次仅搬运少量物品（18、14、10），多次往返，搬运没有充分利用背包负重、甚至没有手持物品，效率较低。 | Issue closed | 2026-08-03 18:00:20 |
| [#248](https://github.com/Refzlund/haulers-dream/issues/248) | [Bug] 图中高优先级的货架有空缺后，有多个pawn同时向货架补货，造成多余。第一个pawn将货架布满后，后续的所有pawn无法卸货而将携带的物品放回原先拾取的地点。即，后续的所有pawn进行了一来一回两次无意义的搬运。 | Issue closed | 2026-08-03 18:12:55 |
| [PR 249](https://github.com/Refzlund/haulers-dream/pull/249) | fix: stop Combat Extended from dropping temporary haul cargo | Closed, **not merged** | 2026-08-05 11:59:04 |
| [#250](https://github.com/Refzlund/haulers-dream/issues/250) | [Bug] When I forbid an item while a pawn is doing the "haul everything nearby" job that this mod adds, that pawn will st | Issue closed | 2026-08-06 00:04:37 |
| [PR 251](https://github.com/Refzlund/haulers-dream/pull/251) | Update chinese translation for Meals on Wheels | Merged 2026-08-09 03:38:44 | 2026-08-09 00:27:39 |
| [PR 252](https://github.com/Refzlund/haulers-dream/pull/252) | mix: one storage ledger, forbidden-item safety, guest permissions, Common Sense gathering, and an attribution we take back | Merged 2026-08-09 15:03:03 | 2026-08-09 07:13:03 |
| [PR 254](https://github.com/Refzlund/haulers-dream/pull/254) | lang: a consistency and glossary pass over the Traditional Chinese translation | Merged 2026-08-09 16:09:34 | 2026-08-09 16:04:06 |

## Complete Steam source register

Every captured entry follows. **After** means posted after the v1.24.0 Workshop timestamp; it does not assert the installed mod version. Labels C001–C421 and T01–T11 retain the earlier collection’s identifiers. Each link opens the original comment/post, and the Ledger column links the interpretation above. The paraphrases retain the original statement; current corrections and retractions are in the ledger and recurrence analysis.

### Workshop comments C001–C050

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C001](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292592327) | 2026-09-05 21:56:49 | After | -=GoW=-Dennis | [L03](#l03), [L45](#l45) | Asks about Skipdoor Pathing and Stack gap compatibility. |
| [C002](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940297913156104) | 2026-09-02 12:07:27 | After | Richard Ramirez | [L08](#l08) | Bulk-haul activation still disappears intermittently; removing Simple Sidearms only helped temporarily. |
| [C003](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001633990) | 2026-08-30 21:26:17 | After | Maya Fey | [L07](#l07) | Confirms premature work-to-haul switching without Simple Sidearms installed. |
| [C004](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001626310) | 2026-08-30 19:35:14 | After | TripleZer0 | [L07](#l07) | Harvesting, woodcutting and deconstruction stop after one task to haul; praises interface. |
| [C005](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001556231) | 2026-08-29 22:06:27 | After | HaPpY | [L03](#l03) | Repeated full-stack top-ups create unnecessary trips, including at hopper shelves. |
| [C006](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001556021) | 2026-08-29 22:03:08 | After | HaPpY | [L03](#l03) | Requests Storage Refill Hysteresis compatibility. |
| [C007](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045885677) | 2026-08-27 09:36:53 | After | Arthur GC | [L33](#l33) | Finishing wild animals does not trigger hauling; no errors observed. |
| [C008](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045859360) | 2026-08-27 00:15:02 | After | Richard Ramirez | [L08](#l08) | Initially reports improvement after removing Simple Sidearms; later retracts permanent resolution. |
| [C009](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045781797) | 2026-08-26 03:54:20 | After | Triel | [L15](#l15) | Requests construction and delivery order based on proximity to the pawn. |
| [C010](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889007307) | 2026-08-25 16:29:53 | After | Richard Ramirez | [L08](#l08) | Bulk hauling works in quicktest but fails in the established save. |
| [C011](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889005481) | 2026-08-25 16:01:42 | After | Richard Ramirez | [L08](#l08) | Asks whether ordinary prioritized hauling should still collect surrounding items. |
| [C012](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813722889002872) | 2026-08-25 15:20:49 | After | Richard Ramirez | [L08](#l08) | Automatic and prioritized hauling no longer activate nearby collection; explicit bulk command works. |
| [C013](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999473479) | 2026-08-24 13:27:19 | After | I'm A Giraffe | [L49](#l49) | Developer acknowledges accumulated feedback and delays due to outside work. |
| [C014](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999435100) | 2026-08-23 23:31:13 | After | Draconis🐊 | [L10](#l10), [L28](#l28) | Reports severe lag and blocked mech gestation relieved after removing mood-bar and construction mods. |
| [C015](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813437999285398) | 2026-08-22 10:09:31 | After | FireBeard | [L10](#l10) | Reports a five-mod removal workaround; individual conflicts were not isolated. |
| [C016](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813130434387941) | 2026-08-22 04:58:16 | After | AnneCrankin90s | [L49](#l49) | Expresses distrust about AI-assisted development and historical save damage; supplies no new reproduction. |
| [C017](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591813130434155325) | 2026-08-19 13:35:36 | After | FireBeard | [L10](#l10) | Suspects TD Enhancement conflict and links issue 262. |
| [C018](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043389321) | 2026-08-17 17:23:56 | After | R3surak | [L30](#l30) | Matter Network interaction falls back to vanilla single-item hauling. |
| [C019](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043387830) | 2026-08-17 17:02:22 | After | HaPpY | [L04](#l04) | Intermittent erratic stocking behavior with apples and smoke leaves. |
| [C020](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043344698) | 2026-08-17 02:02:33 | After | nullpat | [L17](#l17) | Minimal Harmony-plus-HD caravan/horse test shows inconsistent bulk unloading; links issue 260. |
| [C021](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043335749) | 2026-08-16 23:59:49 | After | nullpat | [L04](#l04) | Reports repeated cloth-haul and workgiver synchronization warnings; otherwise praises improvement. |
| [C022](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043260054) | 2026-08-16 05:45:13 | After | Triel | [L07](#l07) | Wants growers to finish a growing area before leaving to unload. |
| [C023](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265884141) | 2026-08-14 20:53:41 | After | ErikRedbeard | [L09](#l09) | Sentient animals lose bulk hauling after saving and reloading. |
| [C024](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265879048) | 2026-08-14 19:43:26 | After | talias | [L18](#l18) | WVC Work Modes sends idle mechs to recharge before inventory is emptied. |
| [C025](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265832567) | 2026-08-14 03:53:47 | After | ErikRedbeard | [L09](#l09) | Big &amp; Small sapient mechs lose bulk hauling; fresh spawns work and race changes affect it. |
| [C026](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265816104) | 2026-08-13 22:27:18 | After | Richard Ramirez | [L15](#l15) | Materials beside construction are hauled away; asks whether vanilla causes this. |
| [C027](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265812007) | 2026-08-13 21:22:12 | After | Richard Ramirez | [L14](#l14) | Planning cuts for blighted crops also marks healthy plants for cutting. |
| [C028](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265733261) | 2026-08-12 20:20:29 | After | I'm A Giraffe | [L45](#l45) | Developer says Build From Storage is compatible and is not replaced. |
| [C029](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265729951) | 2026-08-12 19:22:13 | After | talias | [L45](#l45) | Asks whether Build From Storage is replaced or compatible. |
| [C030](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434705716618225) | 2026-08-11 02:32:59 | After | Ketaros | [L49](#l49) | Praises consolidating several mods into one. |
| [C031](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320103811) | 2026-08-09 17:17:11 | After | SleepingPigNeverSleep | [L03](#l03) | Corrects earlier Stack gap endorsement: actually using OgreStack; recalls a Common Sense interaction. |
| [C032](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320101744) | 2026-08-09 16:47:05 | After | Richard Ramirez | [L03](#l03) | Reports Stack gap slider reducing stored item counts; switched to KanbanStockpileContinue. |
| [C033](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320101201) | 2026-08-09 16:37:50 | After | I'm A Giraffe | [L38](#l38) | Developer announces update and requests reports with attached logs. |
| [C034](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320071338) | 2026-08-09 06:49:13 | Before | SleepingPigNeverSleep | [L35](#l35) | Guests can be unloaded and become stuck loading vehicles; initially misidentifies Stack gap usage. |
| [C035](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320061673) | 2026-08-09 03:06:20 | Before | I'm A Giraffe | [L49](#l49) | Developer says pending reports are being reviewed before release. |
| [C036](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320047245) | 2026-08-08 22:40:56 | Before | Richard Ramirez | [L03](#l03) | Adds KanbanStockpileContinue to storage-limit compatibility candidates. |
| [C037](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320044501) | 2026-08-08 21:55:07 | Before | Richard Ramirez | [L02](#l02), [L03](#l03) | Reports ignored stockpile limits and Stack gap item loss; wants evenly distributed stock. |
| [C038](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434434320044020) | 2026-08-08 21:47:07 | Before | Richard Ramirez | [L02](#l02), [L03](#l03) | Asks whether four storage-limit mods are respected; reports oscillating deliveries. |
| [C039](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434161796340050) | 2026-08-07 17:16:10 | Before | SleepingPigNeverSleep | [L19](#l19) | Defensive Network spaceship loading handles a large first load, then single leftover units. |
| [C040](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434161796256496) | 2026-08-06 18:36:13 | Before | 幸运星 | [L24](#l24) | Requests Survival Tools Reborn support. |
| [C041](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c588434161796152792) | 2026-08-05 11:22:48 | Before | kousaka4656 | [L19](#l19) | SRTS loading of oversized stacks monopolizes one pawn; asks for parallel loading. |
| [C042](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547690648) | 2026-08-03 10:16:18 | Before | meat | [L49](#l49) | Praises the speed of a fix. |
| [C043](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547669974) | 2026-08-03 02:16:52 | Before | Richard Ramirez | [L45](#l45), [L51](#l51) | Requests accurate replacement list for spoilage-priority cooking and butchering mods. |
| [C044](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547656455) | 2026-08-02 22:55:23 | Before | I'm A Giraffe | [L19](#l19) | Developer says cave-exit single-item loading was addressed. |
| [C045](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547636700) | 2026-08-02 18:42:49 | Before | The Grand Mugwump | [L19](#l19) | Cave-exit loading moves individual units; disabling HD removes the behavior. |
| [C046](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547636150) | 2026-08-02 18:35:25 | Before | I'm A Giraffe | [L37](#l37) | Developer describes performance goals but supplies no comparative benchmark. |
| [C047](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547635203) | 2026-08-02 18:22:27 | Before | Ikanam | [L37](#l37) | Asks about performance cost. |
| [C048](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547634125) | 2026-08-02 18:07:29 | Before | I'm A Giraffe | [L49](#l49) | Developer responds to feedback about lengthy change notes. |
| [C049](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547630172) | 2026-08-02 17:16:56 | Before | Lordlony | [L49](#l49) | Amused by unusually extensive change notes. |
| [C050](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547626689) | 2026-08-02 16:28:23 | Before | I'm A Giraffe | [L33](#l33) | Developer explains corpse weight and announces additional corpse-hauling controls. |

### Workshop comments C051–C100

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C051](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547626162) | 2026-08-02 16:20:44 | Before | I'm A Giraffe | [L49](#l49) | Developer announces a release addressing reports. |
| [C052](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547597615) | 2026-08-02 07:52:25 | Before | meat | [L15](#l15) | Builders unload after every wall segment; user disables mod. |
| [C053](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937421547560380) | 2026-08-01 21:46:28 | Before | SleepingPigNeverSleep | [L33](#l33) | Corpse stripping intermittently fails, including forced orders. |
| [C054](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592937127158868339) | 2026-07-30 16:32:20 | Before | Richard Ramirez | [L15](#l15) | Wants carried materials delivered to nearby blueprints before distant storage. |
| [C055](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669378657) | 2026-07-27 16:32:51 | Before | Arthur GC | [L33](#l33), [L53](#l53) | Queued stripping orders are interrupted by storage trips between corpses. |
| [C056](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669355738) | 2026-07-27 08:35:05 | Before | I'm A Giraffe | [L49](#l49) | Developer acknowledges reports while travelling. |
| [C057](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669348970) | 2026-07-27 05:41:33 | Before | jock♂ | [L19](#l19) | Transport and capsule loading moves one unit of each resource per trip. |
| [C058](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669337768) | 2026-07-27 00:55:32 | Before | Richard Ramirez | [L33](#l33) | Requests nearby-item collection when the initial haul target is a corpse. |
| [C059](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810883669249939) | 2026-07-25 20:40:28 | Before | Rendor | [L33](#l33) | Asks for multiple corpses per haul for colonists and mechs. |
| [C060](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810592556369209) | 2026-07-24 00:26:31 | Before | MinusZubi | [L50](#l50) | Another commenter attributes missing wildlife/events to a different mod without evidence. |
| [C061](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810592556361247) | 2026-07-23 22:13:27 | Before | The Bard of Hearts | [L50](#l50) | Reports absent wildlife and raids; uncertain whether HD is involved. |
| [C062](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810592556235056) | 2026-07-22 14:14:41 | Before | Arthur GC | [L32](#l32) | Taming/training orders provoke unloading of required kibble. |
| [C063](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c591810592556219193) | 2026-07-22 09:44:51 | Before | FireBeard | [L10](#l10) | Adds construction-mod load-order detail to idle-pawn report; cause remains uncertain. |
| [C064](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587836392) | 2026-07-21 05:53:24 | Before | Arthur GC | [L31](#l31), [L47](#l47) | Confirms urgent-haul settings appeared after updating; behavior still awaiting testing. |
| [C065](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587833993) | 2026-07-21 04:45:25 | Before | jock♂ | [L37](#l37) | Reports approximately one FPS after adding HD to a save. |
| [C066](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587818028) | 2026-07-20 22:54:55 | Before | Moger | [L49](#l49) | Praises extensive release notes. |
| [C067](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587805235) | 2026-07-20 19:46:50 | Before | Arthur GC | [L31](#l31) | Cannot find urgent-bulk settings; Keyz urgent jobs still collect singly. |
| [C068](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587804654) | 2026-07-20 19:38:04 | Before | I'm A Giraffe | [L31](#l31) | Developer locates urgent bulk-haul settings introduced in v1.21.0. |
| [C069](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587802755) | 2026-07-20 19:10:12 | Before | Arthur GC | [L31](#l31), [L47](#l47) | Cannot locate suggested vicinity and opportunistic-haul settings. |
| [C070](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587801023) | 2026-07-20 18:42:47 | Before | I'm A Giraffe | [L49](#l49) | Developer provides an availability update. |
| [C071](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587799336) | 2026-07-20 18:17:05 | Before | I'm A Giraffe | [L31](#l31), [L47](#l47) | Developer explains gear-tab setting, urgent batching, controlled-map option and weight limits. |
| [C072](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587775149) | 2026-07-20 10:19:21 | Before | [B&amp;E] Killer Pineapple | [L48](#l48), [L52](#l52) | Questions high item quantities despite strict carry-weight setting with delayed collection. |
| [C073](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587740210) | 2026-07-19 21:19:48 | Before | JAS | [L42](#l42), [L48](#l48) | Requests a disable option suitable for a nomadic game without a permanent home map. |
| [C074](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587730535) | 2026-07-19 19:15:44 | Before | Arthur GC | [L31](#l31) | Keyz urgent hauling ignores backpacks and carries individual items. |
| [C075](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587723984) | 2026-07-19 17:42:12 | Before | NocturneLune | [L47](#l47) | Gear tab opens automatically when selecting an inventory-carrying pawn; wants it disabled. |
| [C076](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587717813) | 2026-07-19 16:17:21 | Before | I'm A Giraffe | [L10](#l10) | Developer cannot reproduce the suspected Smarter Construction conflict and requests a report. |
| [C077](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047664587706475) | 2026-07-19 13:50:42 | Before | FireBeard | [L10](#l10) | Reports idle pawns with Smarter Construction. |
| [C078](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047359821240321) | 2026-07-16 20:11:10 | Before | MinusZubi | [L52](#l52) | Confirms a Combat Extended issue is fixed. |
| [C079](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047359821223299) | 2026-07-16 15:51:24 | Before | Moger | [L49](#l49) | Strong endorsement; considers the mod essential. |
| [C080](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047359821104356) | 2026-07-15 04:33:52 | Before | MAY | [L49](#l49) | Expression of appreciation. |
| [C081](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c576047359821098215) | 2026-07-15 02:54:20 | Before | camomo bbl enjoyer | [L49](#l49) | Expression of appreciation. |
| [C082](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554194166) | 2026-07-14 18:17:42 | Before | 你好，我在学习说话 | [L49](#l49) | Thanks developer and intends to test. |
| [C083](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554193440) | 2026-07-14 18:06:06 | Before | I'm A Giraffe | [L31](#l31), [L42](#l42) | Developer announces v1.20.0 vehicle-use options and fixes to urgent hauling and alerts. |
| [C084](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554181301) | 2026-07-14 14:28:41 | Before | panaver10 | [L24](#l24) | Unload failure alert blames Simple Sidearms despite advertised compatibility. |
| [C085](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554158731) | 2026-07-14 04:49:36 | Before | MAY | [L31](#l31) | Combat Extended plus Keyz urgent hauling stops using backpacks. |
| [C086](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554145675) | 2026-07-14 00:29:59 | Before | I'm A Giraffe | [L42](#l42) | Developer explains protected vehicle cargo and proposes optional use while away. |
| [C087](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554144844) | 2026-07-14 00:16:44 | Before | JAS | [L42](#l42) | Requests eating and medical supplies directly from vehicle cargo during nomadic play. |
| [C088](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554138902) | 2026-07-13 22:42:31 | Before | I'm A Giraffe | [L40](#l40) | Developer explains selecting a retained inventory quantity. |
| [C089](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554138662) | 2026-07-13 22:38:32 | Before | Arthur GC | [L40](#l40) | Asks what the retained-quantity control looks like. |
| [C090](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554130223) | 2026-07-13 20:37:41 | Before | I'm A Giraffe | [L37](#l37) | Developer offers a favorable performance impression without comparative measurements. |
| [C091](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554130140) | 2026-07-13 20:36:20 | Before | I'm A Giraffe | [L40](#l40) | Developer checks installed version for missing inventory controls. |
| [C092](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554129621) | 2026-07-13 20:28:35 | Before | Arthur GC | [L40](#l40) | Requests a specific retained quantity rather than all-or-nothing retention. |
| [C093](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554122604) | 2026-07-13 17:57:35 | Before | MAY | [L49](#l49) | Expression of appreciation and encouragement. |
| [C094](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554113050) | 2026-07-13 15:49:24 | Before | 你好，我在学习说话 | [L37](#l37) | Asks for performance comparison with Pick Up And Haul and While You're Up. |
| [C095](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554108590) | 2026-07-13 14:43:31 | Before | Narlindir | [L49](#l49) | Accepts AI assistance when guided by developer understanding. |
| [C096](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554108424) | 2026-07-13 14:40:55 | Before | The Bard of Hearts | [L49](#l49) | Expression of appreciation. |
| [C097](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554102569) | 2026-07-13 13:04:13 | Before | I'm A Giraffe | [L40](#l40) | Developer announces quantity-based retention and responds to community discussion. |
| [C098](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554102564) | 2026-07-13 13:04:11 | Before | I'm A Giraffe | [L49](#l49) | Developer thanks commenters. |
| [C099](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298981554101955) | 2026-07-13 12:53:18 | Before | Marty in the multyvers | [L49](#l49) | Praises a shorter mod list. |
| [C100](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369520862) | 2026-07-12 21:52:40 | Before | The Grand Mugwump | [L49](#l49) | Praises batch planning and successful Perspective Shift use; supports bounded AI assistance. |

### Workshop comments C101–C150

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C101](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369511787) | 2026-07-12 20:03:41 | Before | The Bard of Hearts | [L49](#l49) | Expression of appreciation. |
| [C102](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369510275) | 2026-07-12 19:48:20 | Before | Andreas | [L49](#l49) | Praises the project as a needed community improvement. |
| [C103](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369507942) | 2026-07-12 19:24:51 | Before | I'm A Giraffe | [L40](#l40) | Developer accepts retained-quantity suggestion. |
| [C104](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369486599) | 2026-07-12 14:51:09 | Before | I'm A Giraffe | [L49](#l49) | Developer describes original implementation, architecture review and AI-assisted process. |
| [C105](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369475021) | 2026-07-12 12:02:09 | Before | Narlindir | [L49](#l49) | Asks whether code is original, bundled, or generated without sufficient understanding. |
| [C106](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369460428) | 2026-07-12 05:52:51 | Before | The Bard of Hearts | [L49](#l49) | Expression of appreciation. |
| [C107](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369460422) | 2026-07-12 05:52:41 | Before | The Bard of Hearts | [L40](#l40) | Cannot pick up an exact quantity of silver; requests quantity selection. |
| [C108](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369459714) | 2026-07-12 05:37:05 | Before | The Bard of Hearts | [L07](#l07) | Retracts the preceding deconstruction complaint as a transient glitch. |
| [C109](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369459476) | 2026-07-12 05:31:56 | Before | The Bard of Hearts | [L07](#l07) | Reports deconstruction refusal, later retracted. |
| [C110](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369423860) | 2026-07-11 19:45:40 | Before | I'm A Giraffe | [L49](#l49) | Developer identifies an optional donation route. |
| [C111](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369414966) | 2026-07-11 18:09:00 | Before | LisanAlGaib | [L49](#l49) | Offers a donation in appreciation of maintenance and replies. |
| [C112](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369336803) | 2026-07-10 23:25:58 | Before | I'm A Giraffe | [L50](#l50) | Developer moderates an off-topic personal dispute. |
| [C113](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369320921) | 2026-07-10 20:15:05 | Before | MisterJebo | [L50](#l50) | Speculates on a separate broader work mod and discusses a naming dispute. |
| [C114](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369320499) | 2026-07-10 20:10:41 | Before | henk | [L50](#l50) | Off-topic personal dispute; no mod behavior reported. |
| [C115](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369309819) | 2026-07-10 17:56:24 | Before | hyenatown | [L23](#l23) | Reports successful uninstall after UI-mod/bill/job cleanup; causal explanation is speculative. |
| [C116](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369307981) | 2026-07-10 17:24:31 | Before | I'm A Giraffe | [L23](#l23) | Developer requests evidence for uninstall failures and cannot reproduce locally. |
| [C117](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369307090) | 2026-07-10 17:11:43 | Before | hyenatown | [L23](#l23) | Requests safe removal instructions. |
| [C118](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369305721) | 2026-07-10 16:50:04 | Before | I'm A Giraffe | [L24](#l24), [L52](#l52) | Developer says partial-batch and CE-description improvements shipped; considers overload options. |
| [C119](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369304818) | 2026-07-10 16:35:59 | Before | LisanAlGaib | [L26](#l26), [L48](#l48), [L52](#l52) | Requests feasible partial batches, optional hauling overload behavior and clearer CE limits. |
| [C120](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369290087) | 2026-07-10 11:59:47 | Before | I'm A Giraffe | [L24](#l24), [L52](#l52) | Developer explains Combat Extended chunk-weight restrictions. |
| [C121](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c578298707369279811) | 2026-07-10 07:40:35 | Before | Andreas | [L26](#l26), [L48](#l48), [L52](#l52) | Wants hauling of multiple chunks even with stack mods. |
| [C122](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359496591) | 2026-07-09 17:31:25 | Before | MisterJebo | [L50](#l50) | Off-topic request about shortening a username. |
| [C123](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359491450) | 2026-07-09 16:09:24 | Before | henk | [L15](#l15), [L48](#l48) | Suggests a companion mod focused on building. |
| [C124](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359472762) | 2026-07-09 10:24:56 | Before | I'm A Giraffe | [L26](#l26), [L52](#l52) | Developer proposes capacity-aware batch sizing and clearer limitations. |
| [C125](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359472587) | 2026-07-09 10:20:30 | Before | I'm A Giraffe | [L45](#l45), [L48](#l48) | Developer explains hauling scope and favors compatibility over unrestricted feature expansion. |
| [C126](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571543229359468454) | 2026-07-09 08:38:19 | Before | MisterJebo | [L45](#l45), [L48](#l48) | Community member expects broader building suggestions to be outside scope. |
| [C127](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702285559) | 2026-07-08 19:43:53 | Before | LisanAlGaib | [L26](#l26), [L52](#l52) | Combat Extended oversized batches cause ingredient pickup/unload loops; smaller batches help. |
| [C128](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702282008) | 2026-07-08 18:53:49 | Before | henk | [L48](#l48) | Prefers fewer separate mods. |
| [C129](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702281949) | 2026-07-08 18:52:57 | Before | henk | [L48](#l48), [L55](#l55) | Requests broader building/QoL integration and compatibility with work and threading mods. |
| [C130](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702267458) | 2026-07-08 15:12:08 | Before | Araphre | [L01](#l01) | Requests Common Sense cleaning integration; reports more errors when both mods are used. |
| [C131](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702253717) | 2026-07-08 10:54:50 | Before | Murphy233 | [L49](#l49) | Expression of appreciation and encouragement. |
| [C132](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702227728) | 2026-07-08 01:27:58 | Before | Lux | [L27](#l27) | Thanks developer for clearer batching controls. |
| [C133](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702213296) | 2026-07-07 22:00:48 | Before | I'm A Giraffe | [L27](#l27), [L47](#l47) | Developer promises clearer indication of batching state. |
| [C134](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702209362) | 2026-07-07 21:13:02 | Before | MisterJebo | [L23](#l23) | Community response on removal safety; requests reports if it fails. |
| [C135](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702208488) | 2026-07-07 21:01:21 | Before | Lux | [L49](#l49) | Praises HD as a successor to Pick Up And Haul. |
| [C136](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702208300) | 2026-07-07 20:59:11 | Before | Lux | [L27](#l27) | Identifies hidden batch mode in Nice Bill Tab as the source of crafting confusion. |
| [C137](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702206223) | 2026-07-07 20:31:17 | Before | Lux | [L27](#l27) | Cannot obtain continuous prioritized crafting; later traces this to hidden batch settings. |
| [C138](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702200061) | 2026-07-07 19:10:09 | Before | John Tough Bone | [L23](#l23) | Asks about removing the mod during an existing save. |
| [C139](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702187809) | 2026-07-07 16:24:47 | Before | I'm A Giraffe | [L49](#l49) | Developer welcomes compatibility feedback after a defensive exchange. |
| [C140](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702186624) | 2026-07-07 16:07:34 | Before | MisterJebo | [L37](#l37) | Community member speculates that performance criticism reflects translation or another mod. |
| [C141](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702173390) | 2026-07-07 12:08:01 | Before | Бензина нет | [L37](#l37) | Requests optimization for large mod lists. |
| [C142](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702140311) | 2026-07-07 00:33:05 | Before | Wilihey | [L49](#l49) | Strong endorsement. |
| [C143](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540759102) | 2026-07-06 00:28:23 | Before | I'm A Giraffe | [L49](#l49) | Developer lighthearted response to a description edit. |
| [C144](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540730868) | 2026-07-05 17:26:59 | Before | henk | [L49](#l49) | Notes a rapid description correction. |
| [C145](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540720495) | 2026-07-05 14:42:17 | Before | henk | [L45](#l45) | Requests Build From Inventory be added to replacement list. |
| [C146](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540709780) | 2026-07-05 11:25:53 | Before | Arbolito | [L49](#l49) | Strong endorsement and wish for wider recognition. |
| [C147](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542703540629168) | 2026-07-04 08:43:41 | Before | W | [L49](#l49) | Strong endorsement of hauling behavior. |
| [C148](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813542166) | 2026-07-03 01:29:42 | Before | Willo | [L49](#l49) | Praises configurability and detailed explanations. |
| [C149](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813483547) | 2026-07-02 13:16:30 | Before | Castle | [L40](#l40) | Confirms the inventory-retention option is now visible. |
| [C150](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813479880) | 2026-07-02 12:18:50 | Before | I'm A Giraffe | [L38](#l38), [L40](#l40) | Developer acknowledges missing pickup options and requests logs. |

### Workshop comments C151–C200

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C151](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813475611) | 2026-07-02 11:01:50 | Before | The Bard of Hearts | [L33](#l33), [L40](#l40) | Most pickup options begin working, but a small rabbit corpse still lacks one. |
| [C152](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813474803) | 2026-07-02 10:46:40 | Before | The Bard of Hearts | [L40](#l40) | Missing pickup and retention commands, especially for stored items. |
| [C153](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813452386) | 2026-07-02 01:04:50 | Before | Pillar | [L32](#l32), [L47](#l47) | Confirms both reported v1.15.1 issues were resolved. |
| [C154](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813432053) | 2026-07-01 20:19:28 | Before | I'm A Giraffe | [L32](#l32), [L47](#l47) | Developer announces v1.15.1 fixes for profile localization and medical resting. |
| [C155](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813425664) | 2026-07-01 19:00:47 | Before | Pillar | [L32](#l32), [L47](#l47) | Chinese default profile appears unsaved; untreated patients repeatedly rise and lie down. |
| [C156](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813422440) | 2026-07-01 18:17:57 | Before | I'm A Giraffe | [L24](#l24) | Developer announces Grab Your Tool compatibility and tool retention support. |
| [C157](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813410075) | 2026-07-01 15:14:38 | Before | The Bard of Hearts | [L49](#l49) | Thanks developer for the update. |
| [C158](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813405926) | 2026-07-01 14:07:19 | Before | I'm A Giraffe | [L24](#l24) | Developer points to item-specific unload exclusions. |
| [C159](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813405723) | 2026-07-01 14:03:53 | Before | Castle | [L24](#l24) | Requests retaining Tools O' Plenty tools. |
| [C160](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813403208) | 2026-07-01 13:18:09 | Before | I'm A Giraffe | [L40](#l40), [L46](#l46) | Developer announces remembered plans and inventory retention. |
| [C161](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813390622) | 2026-07-01 08:55:18 | Before | I'm A Giraffe | [L40](#l40) | Developer explains retention policies and asks about the intended pickup use. |
| [C162](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542424813382117) | 2026-07-01 05:02:48 | Before | The Bard of Hearts | [L40](#l40) | Manual pickup immediately triggers unloading; wants items retained. |
| [C163](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324208420) | 2026-06-29 23:36:30 | Before | mjdecker123 | [L45](#l45) | Acknowledges compatibility clarification. |
| [C164](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324185931) | 2026-06-29 18:36:31 | Before | MisterJebo | [L38](#l38) | Community guidance on attaching logs with the in-game report button. |
| [C165](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324185108) | 2026-06-29 18:25:31 | Before | 波普奇妙之旅 | [L38](#l38) | Reports Vehicle Framework exception-tagger patch failure; described as diagnostic-only. |
| [C166](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324122551) | 2026-06-28 22:46:57 | Before | I'm A Giraffe | [L45](#l45) | Developer says smarter construction/deconstruction mods remain separate and compatible. |
| [C167](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324090011) | 2026-06-28 14:45:16 | Before | mjdecker123 | [L45](#l45) | Asks whether smarter construction/deconstruction mods are replaced. |
| [C168](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324086504) | 2026-06-28 13:43:21 | Before | I'm A Giraffe | [L32](#l32), [L44](#l44) | Developer requests logs for priority problems and acknowledges fishing request. |
| [C169](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542161324082915) | 2026-06-28 12:42:40 | Before | VegetableKing | [L32](#l32) | Doctoring, rescue and firefighting do not respect highest work priorities. |
| [C170](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030272460) | 2026-06-27 03:51:51 | Before | Fortirus | [L44](#l44) | Renews request for fishing support. |
| [C171](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030239543) | 2026-06-26 19:08:46 | Before | I'm A Giraffe | [L23](#l23) | Developer suggests clearing active jobs before removal and seeks clarification on container pickup. |
| [C172](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030230507) | 2026-06-26 16:58:06 | Before | Ariana | [L23](#l23) | Reports missing classes, crashes and null references after removal; requests cleanup instructions. |
| [C173](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030189336) | 2026-06-26 05:07:08 | Before | lovenan | [L40](#l40) | Wants direct item pickup from a cellar without ejecting contents first. |
| [C174](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030166311) | 2026-06-25 23:56:10 | Before | NoctisTheBogWitch | [L24](#l24), [L40](#l40) | Narrows pickup/unload problem to smokeleaf joints and drugs. |
| [C175](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030151413) | 2026-06-25 21:16:06 | Before | NoctisTheBogWitch | [L24](#l24) | Confirms a restart occurred during apparent improvement. |
| [C176](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030151345) | 2026-06-25 21:15:24 | Before | I'm A Giraffe | [L24](#l24) | Developer checks whether restart/update explains changed behavior. |
| [C177](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030151134) | 2026-06-25 21:13:24 | Before | NoctisTheBogWitch | [L24](#l24) | Temporarily reports pickup issue resolved; later narrows remaining failure to drugs. |
| [C178](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030150926) | 2026-06-25 21:11:23 | Before | I'm A Giraffe | [L24](#l24) | Developer requests reproduction and logs for pickup/unload loop. |
| [C179](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030145144) | 2026-06-25 20:11:53 | Before | NoctisTheBogWitch | [L38](#l38) | Says a bug report is being prepared. |
| [C180](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030144720) | 2026-06-25 20:07:15 | Before | NoctisTheBogWitch | [L24](#l24), [L40](#l40) | Manual pickup immediately unloads; Adaptive Primitive Storage also installed. |
| [C181](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030142375) | 2026-06-25 19:43:33 | Before | I'm A Giraffe | [L40](#l40) | Developer asks which operation is intended for stored items. |
| [C182](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030137735) | 2026-06-25 18:53:00 | Before | lovenan | [L40](#l40) | Adaptive Primitive Storage cellar has no direct pickup action. |
| [C183](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030135926) | 2026-06-25 18:32:44 | Before | I'm A Giraffe | [L40](#l40) | Developer asks for container identity and explains unload exclusions. |
| [C184](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030133178) | 2026-06-25 17:55:36 | Before | lovenan | [L40](#l40) | Requests container pickup and retained inventory. |
| [C185](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030116097) | 2026-06-25 13:05:22 | Before | I'm A Giraffe | [L37](#l37), [L45](#l45) | Developer describes replacement scope, configuration and performance aims. |
| [C186](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030116095) | 2026-06-25 13:05:18 | Before | I'm A Giraffe | [L46](#l46) | Developer explores reusing plan settings to reduce interaction steps. |
| [C187](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030098272) | 2026-06-25 05:46:34 | Before | lovenan | [L37](#l37) | Asks whether HD costs less performance than Pick Up And Haul. |
| [C188](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030098137) | 2026-06-25 05:43:45 | Before | evil yaoi wizard | [L38](#l38) | Reports debug file logging aborting after an I/O/thread-abort error. |
| [C189](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030097956) | 2026-06-25 05:40:10 | Before | lovenan | [L45](#l45) | Asks whether replacement claims cover every feature of replaced mods. |
| [C190](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030092645) | 2026-06-25 03:34:28 | Before | huge | [L19](#l19), [L32](#l32) | Clarifies older shuttle/ritual report and identifies animal-training food as the unloaded item. |
| [C191](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030073698) | 2026-06-24 22:06:39 | Before | Winterstein | [L46](#l46) | Clarifies that the planning popup adds an extra interaction. |
| [C192](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030073648) | 2026-06-24 22:06:00 | Before | Winterstein | [L46](#l46) | Requests a one-click repeat/default planning action. |
| [C193](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030069946) | 2026-06-24 21:09:35 | Before | I'm A Giraffe | [L07](#l07) | Developer identifies premature yield hauling as a recent regression and starts a fix. |
| [C194](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030069473) | 2026-06-24 21:04:10 | Before | Winterstein | [L07](#l07) | Woodcutters and growers haul home after minimal work. |
| [C195](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030065715) | 2026-06-24 20:08:03 | Before | VegetableKing | [L07](#l07) | Requests more sensible task commitment instead of long trips for tiny jobs. |
| [C196](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030065479) | 2026-06-24 20:04:07 | Before | VegetableKing | [L07](#l07) | Miners haul home after one mined block despite available inventory capacity. |
| [C197](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030065462) | 2026-06-24 20:03:45 | Before | Julia Hart | [L19](#l19) | Shuttle passengers use vanilla loading; even one stack is split into individual units. |
| [C198](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030062179) | 2026-06-24 19:10:20 | Before | I'm A Giraffe | [L19](#l19) | Developer asks which version produced shuttle/ritual failures. |
| [C199](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030061610) | 2026-06-24 19:01:35 | Before | huge | [L19](#l19), [L32](#l32) | Passengers fail to board loaded shuttles; ritual targets unload inventory and interrupt rituals. |
| [C200](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030019709) | 2026-06-24 04:45:15 | Before | Murphy233 | [L49](#l49) | Expression of appreciation. |

### Workshop comments C201–C250

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C201](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030010723) | 2026-06-24 01:14:16 | Before | I'm A Giraffe | [L49](#l49) | Developer acknowledges positive follow-ups. |
| [C202](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571541859030010632) | 2026-06-24 01:12:59 | Before | I'm A Giraffe | [L27](#l27), [L46](#l46) | Developer plans sowing support and improved automatic batching. |
| [C203](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153367696) | 2026-06-23 19:15:23 | Before | Lensrub | [L07](#l07), [L46](#l46) | Requests planning for sowing a growing area. |
| [C204](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153340759) | 2026-06-23 11:41:21 | Before | Gundodo | [L49](#l49) | Reports improved stability after initial release concerns. |
| [C205](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153330907) | 2026-06-23 07:39:10 | Before | MisterJebo | [L50](#l50) | Off-topic reaction to another user's mod list. |
| [C206](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153327852) | 2026-06-23 06:18:59 | Before | Althenar | [L27](#l27) | Automatic crafting still fetches individual recipes despite an unlimited batch setting. |
| [C207](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153321883) | 2026-06-23 03:49:50 | Before | Araphre | [L49](#l49) | Reports successful use with roughly 75 mods, including Giddy-up and RJW. |
| [C208](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153316293) | 2026-06-23 01:44:13 | Before | Lensrub | [L40](#l40) | Community member says manual pickup functionality was added. |
| [C209](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153315664) | 2026-06-23 01:32:34 | Before | Starempire42 | [L40](#l40) | Supports the earlier manual-pickup request. |
| [C210](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153295615) | 2026-06-22 20:38:26 | Before | NoctisTheBogWitch | [L54](#l54) | Reports fewer erratic pawn loops, with occasional short standing pauses remaining. |
| [C211](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153229850) | 2026-06-21 23:26:09 | Before | I'm A Giraffe | [L38](#l38) | Developer redirects new reports to the in-game tool for logs and issue tracking. |
| [C212](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153205904) | 2026-06-21 17:38:31 | Before | Lensrub | [L40](#l40) | Requests manual/drafted pickup, including retrieving food assigned to prisoners. |
| [C213](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153174594) | 2026-06-21 07:05:34 | Before | 晓誓 | [L40](#l40) | Right-clicking eggs in an egg box produces errors. |
| [C214](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153129746) | 2026-06-20 17:35:32 | Before | gezi | [L37](#l37) | Reports good actual performance. |
| [C215](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037949153117409) | 2026-06-20 14:30:49 | Before | gezi | [L36](#l36), [L52](#l52) | Requests mech inventory limits reflect each mech's carrying stat rather than identical defaults. |
| [C216](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436666690) | 2026-06-20 13:43:15 | Before | gezi | [L49](#l49) | Expression of appreciation. |
| [C217](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436662874) | 2026-06-20 12:33:28 | Before | I'm A Giraffe | [L36](#l36), [L47](#l47) | Developer says latest update addresses gizmo, null-reference and mech-capacity feedback. |
| [C218](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436655503) | 2026-06-20 10:02:53 | Before | gezi | [L36](#l36), [L52](#l52) | Questions low mech carry limits and asks them to scale with capability. |
| [C219](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436652347) | 2026-06-20 08:49:10 | Before | Touch of the 'Tism | [L10](#l10), [L38](#l38) | Reports numerous null-reference exceptions and supplies a log link. |
| [C220](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436649264) | 2026-06-20 07:36:23 | Before | Virstag | [L49](#l49) | Praises detailed change notes. |
| [C221](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436639557) | 2026-06-20 03:50:24 | Before | moo | [L47](#l47) | Requests hiding the pawn hauling-preference gizmo. |
| [C222](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436636604) | 2026-06-20 02:41:28 | Before | I'm A Giraffe | [L39](#l39) | Developer announces translation support and contribution instructions. |
| [C223](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436630832) | 2026-06-20 00:48:48 | Before | Lama man | [L10](#l10) | Speculates that a problem depends on an existing save. |
| [C224](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436630029) | 2026-06-20 00:33:57 | Before | I'm A Giraffe | [L49](#l49) | Developer broadly announces fixes and compatibility improvements. |
| [C225](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436628982) | 2026-06-20 00:13:52 | Before | I'm A Giraffe | [L38](#l38) | Developer requests a mod list for suspected incompatibility. |
| [C226](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436590546) | 2026-06-19 13:38:19 | Before | 好运常来 | [L19](#l19) | Bulk transport loading excludes stored items even with only Harmony otherwise enabled. |
| [C227](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436588772) | 2026-06-19 13:06:34 | Before | Lama man | [L40](#l40) | Misses the manual-pickup function when replacing Pick Up And Haul. |
| [C228](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436585782) | 2026-06-19 12:09:53 | Before | 幸运星 | [L33](#l33) | Reports repeated corpse-haul job creation within one tick. |
| [C229](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436581880) | 2026-06-19 10:55:58 | Before | 超级大肥猪卡比兽 | [L19](#l19) | Pawns refuse transport-pod loading, including forced orders; removing HD helps. |
| [C230](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436559100) | 2026-06-19 02:13:22 | Before | mjdecker123 | [L48](#l48) | Supports a separate beta Workshop item. |
| [C231](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436546953) | 2026-06-18 22:26:22 | Before | VitaKaninen | [L48](#l48) | Suggests publishing a separate beta for testing before stable updates. |
| [C232](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436544357) | 2026-06-18 21:45:02 | Before | I'm A Giraffe | [L48](#l48) | Developer acknowledges premature release and prioritizes stability after feature growth. |
| [C233](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436535063) | 2026-06-18 19:25:10 | Before | Nirahiel | [L48](#l48) | Criticizes new features introducing new bugs. |
| [C234](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436532476) | 2026-06-18 18:50:30 | Before | Lensrub | [L10](#l10) | Reports hauling and pollution-cleaning refusal; save/reinstall sequence appears to help. |
| [C235](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436530306) | 2026-06-18 18:18:46 | Before | Martius | [L33](#l33) | Reports corpses and loot not being hauled. |
| [C236](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436526230) | 2026-06-18 17:21:47 | Before | 吱吱宸 | [L47](#l47) | Requests hiding the output-hauling gizmo. |
| [C237](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436526079) | 2026-06-18 17:19:57 | Before | 吱吱宸 | [L39](#l39) | Points to an existing Chinese translation. |
| [C238](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436525169) | 2026-06-18 17:07:45 | Before | Bizarro | [L49](#l49) | Interested in adopting the mod after further stabilization. |
| [C239](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436515128) | 2026-06-18 14:41:54 | Before | 胡真菱 | [L39](#l39) | Reports extracted translations remain English; suspects strings embedded in the DLL. |
| [C240](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436497964) | 2026-06-18 09:42:36 | Before | 你好，我在学习说话 | [L50](#l50) | Body unavailable: Steam moderation placeholder. |
| [C241](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436494176) | 2026-06-18 08:23:54 | Before | MisterJebo | [L38](#l38) | Directs another user to the bug-report discussion. |
| [C242](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436487166) | 2026-06-18 05:49:23 | Before | Flyrija | [L42](#l42) | RV pocket-map unload loop, repeated caravan furniture trips, and request for storage unload control. |
| [C243](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436472168) | 2026-06-18 00:42:17 | Before | Nirahiel | [L26](#l26) | Mixed ingredient types break batch recipes and cause materials to be returned. |
| [C244](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436468720) | 2026-06-17 23:36:05 | Before | Nirahiel | [L19](#l19), [L42](#l42) | Explains vehicle bulk loading to another user. |
| [C245](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436467670) | 2026-06-17 23:20:25 | Before | Locke | [L19](#l19), [L42](#l42) | Asks how vehicle bulk loading works. |
| [C246](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436452419) | 2026-06-17 19:44:48 | Before | 小烏鴉Max | [L39](#l39) | Announces a Traditional Chinese translation contribution. |
| [C247](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436433810) | 2026-06-17 15:06:04 | Before | iF | [L23](#l23) | Offers an uncertain staged mod-swap workaround for disappearing UI. |
| [C248](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436433374) | 2026-06-17 14:58:25 | Before | iF | [L23](#l23) | Attributes missing UI to removing Pick Up And Haul; reports drafting/unloading workaround. |
| [C249](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436433087) | 2026-06-17 14:53:30 | Before | I'm A Giraffe | [L47](#l47) | Developer announces settings overhaul and shareable profiles. |
| [C250](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436433073) | 2026-06-17 14:53:09 | Before | iF | [L32](#l32) | Patients do not unload before medical rest; asks whether intentional. |

### Workshop comments C251–C300

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C251](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436430179) | 2026-06-17 13:57:50 | Before | MisterJebo | [L48](#l48) | Asks another commenter to explain a balance complaint. |
| [C252](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436430077) | 2026-06-17 13:55:37 | Before | T.N.Tobin | [L48](#l48) | Considers the mod overpowered. |
| [C253](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436427708) | 2026-06-17 13:08:35 | Before | Winterstein | [L49](#l49) | Interested once stable; supports AI use with architecture and review. |
| [C254](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436426548) | 2026-06-17 12:46:20 | Before | MisterJebo | [L45](#l45) | Community guidance on removing replaced mods and requesting missing features. |
| [C255](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436426346) | 2026-06-17 12:42:03 | Before | Starempire42 | [L45](#l45), [L48](#l48) | Asks whether to remove Build From Inventory and Meals On Wheels after integration. |
| [C256](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436422673) | 2026-06-17 11:30:05 | Before | MisterJebo | [L19](#l19) | Community member says bulk transport loading is planned. |
| [C257](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436422405) | 2026-06-17 11:24:35 | Before | Lensrub | [L19](#l19) | Requests multi-item transport-pod loading. |
| [C258](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436422126) | 2026-06-17 11:19:18 | Before | MisterJebo | [L23](#l23) | Speculates on save damage after changing multiple mods. |
| [C259](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436420594) | 2026-06-17 10:47:46 | Before | 超级大肥猪卡比兽 | [L23](#l23) | Reports UI returned after reinstall and other mod changes; cannot identify cause. |
| [C260](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436399897) | 2026-06-17 02:52:25 | Before | dannway | [L27](#l27) | Batch crafting ignores bill pause/resume thresholds. |
| [C261](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436397840) | 2026-06-17 02:05:39 | Before | I'm A Giraffe | [L49](#l49) | Developer announces a large pending release covering feedback. |
| [C262](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436397781) | 2026-06-17 02:04:12 | Before | Type Writer | [L28](#l28) | Crafting from inventory causes mech gestator delivery loops; disabling the option helps. |
| [C263](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436396905) | 2026-06-17 01:46:42 | Before | BlindmanDWMT | [L45](#l45) | Asks how completely HD replaces Pick Up And Haul. |
| [C264](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c567037624436394380) | 2026-06-17 00:57:03 | Before | RickGrimes74 | [L31](#l31) | Requests Keyz urgent-haul support; praises batch crafting. |
| [C265](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006872320) | 2026-06-16 23:12:11 | Before | lchbBinProfi97 | [L19](#l19) | Asks about shuttle refuelling/restocking and pit-gate bulk transport. |
| [C266](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006846353) | 2026-06-16 16:46:40 | Before | MisterJebo | [L45](#l45) | Directs compatibility questions to the mod description. |
| [C267](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006843566) | 2026-06-16 15:56:47 | Before | mjdecker123 | [L45](#l45) | Asks whether While You Are Nearby is replaced or compatible. |
| [C268](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006838458) | 2026-06-16 14:33:09 | Before | I'm A Giraffe | [L36](#l36) | Developer acknowledges mech unloading, workbench batching and storage compatibility questions. |
| [C269](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006838320) | 2026-06-16 14:30:45 | Before | I'm A Giraffe | [L49](#l49) | Developer describes the need for maturity and ongoing compatibility fixes. |
| [C270](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006838306) | 2026-06-16 14:30:35 | Before | I'm A Giraffe | [L49](#l49) | Developer discusses AI supervision and problem-solving requirements. |
| [C271](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006838303) | 2026-06-16 14:30:34 | Before | iF | [L18](#l18) | Requests mech unloading before recharging. |
| [C272](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006828325) | 2026-06-16 11:11:16 | Before | MisterJebo | [L36](#l36), [L48](#l48) | Questions whether agrihands should haul their harvest. |
| [C273](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006813715) | 2026-06-16 06:18:09 | Before | joey | [L27](#l27) | Requests batch support for modded workbenches. |
| [C274](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006807557) | 2026-06-16 04:17:22 | Before | Andreas | [L03](#l03) | Asks about storage-capacity modifiers, hysteresis and sorting compatibility. |
| [C275](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006804334) | 2026-06-16 03:18:00 | Before | Kpatrol88 | [L49](#l49) | Expresses conditional trust and willingness to report stability or bugs. |
| [C276](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006804313) | 2026-06-16 03:17:44 | Before | Kpatrol88 | [L49](#l49) | Appreciates AI disclosure but is cautious about untested generated mods and save safety. |
| [C277](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006765821) | 2026-06-15 18:36:53 | Before | ignis | [L49](#l49) | Discusses benefits and limitations of supervised generated code. |
| [C278](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006765115) | 2026-06-15 18:27:05 | Before | Maux | [L49](#l49) | Values informed choice and hopes for broader compatibility. |
| [C279](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006764241) | 2026-06-15 18:15:22 | Before | I'm A Giraffe | [L49](#l49) | Developer discusses architecture, testing and AI-tool experience. |
| [C280](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006764136) | 2026-06-15 18:13:57 | Before | Maux | [L49](#l49) | Requests clarity about where AI is used and emphasizes differing experiences. |
| [C281](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006763249) | 2026-06-15 18:02:13 | Before | Maux | [L49](#l49) | Questions depth of implementation understanding while appreciating transparency. |
| [C282](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006759910) | 2026-06-15 17:13:46 | Before | MisterJebo | [L49](#l49) | Explains why users commonly have large mod lists. |
| [C283](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006756678) | 2026-06-15 16:25:39 | Before | I'm A Giraffe | [L49](#l49) | Developer provides background on adopting AI-assisted development. |
| [C284](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006756670) | 2026-06-15 16:25:33 | Before | I'm A Giraffe | [L49](#l49) | Developer describes review scope and identifies large-list compatibility as a testing gap. |
| [C285](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006756634) | 2026-06-15 16:25:05 | Before | I'm A Giraffe | [L49](#l49) | Developer describes programming background. |
| [C286](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006752874) | 2026-06-15 15:26:48 | Before | Nirahiel | [L23](#l23) | Suggests drafting pawns/mechs before removal; explicitly speculative. |
| [C287](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006750572) | 2026-06-15 14:50:25 | Before | MisterJebo | [L23](#l23) | Expects the developer to address removal issues. |
| [C288](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006750251) | 2026-06-15 14:45:12 | Before | ignis | [L23](#l23) | Warns against removing with active jobs and offers an unverified cleanup procedure. |
| [C289](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006747407) | 2026-06-15 13:55:04 | Before | 超级突刺 | [L40](#l40) | Suggests Pick Up at Home as a temporary manual-pickup workaround. |
| [C290](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006746538) | 2026-06-15 13:40:31 | Before | I'm A Giraffe | [L49](#l49) | Developer acknowledges accumulated reports. |
| [C291](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006745031) | 2026-06-15 13:13:16 | Before | ignis | [L37](#l37) | Reports camera and pawn microstutter despite little TPS impact. |
| [C292](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006744485) | 2026-06-15 13:03:28 | Before | 超级突刺 | [L50](#l50) | Developer mention with no additional report. |
| [C293](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006744453) | 2026-06-15 13:02:43 | Before | 超级突刺 | [L25](#l25), [L40](#l40) | Chinese duplicate of missing material-pickup complaint. |
| [C294](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006744448) | 2026-06-15 13:02:39 | Before | 超级突刺 | [L25](#l25), [L40](#l40) | Materials such as steel lack the pickup option available in Pick Up And Haul. |
| [C295](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006743366) | 2026-06-15 12:38:22 | Before | MisterJebo | [L36](#l36), [L48](#l48) | Questions constructoids hauling construction inputs and deconstruction output. |
| [C296](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742772) | 2026-06-15 12:23:26 | Before | Maux | [L49](#l49) | Requests clarity about understanding individual generated methods and classes. |
| [C297](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742761) | 2026-06-15 12:23:11 | Before | Maux | [L49](#l49) | Distinguishes principled AI opposition from concerns about unreviewed code and save safety. |
| [C298](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742687) | 2026-06-15 12:20:47 | Before | ignis | [L36](#l36) | Housekeeper cats use single-item hauling instead of inventories. |
| [C299](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006742328) | 2026-06-15 12:12:43 | Before | MisterJebo | [L36](#l36), [L48](#l48) | Questions harvest-hauling balance for agrihands and tunnellers. |
| [C300](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006736965) | 2026-06-15 10:12:47 | Before | 出来吧l皮卡丘 | [L26](#l26) | Narrows crafting loop to the carried-crafting-materials option. |

### Workshop comments C301–C350

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C301](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006736764) | 2026-06-15 10:08:16 | Before | 出来吧l皮卡丘 | [L26](#l26) | Crafting repeatedly gathers ingredients, approaches bench and unloads them. |
| [C302](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006735336) | 2026-06-15 09:31:16 | Before | KIEndlleas | [L25](#l25), [L26](#l26) | Chinese follow-up: forcing work restores behavior; no error identifies cause. |
| [C303](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006735204) | 2026-06-15 09:27:44 | Before | KIEndlleas | [L26](#l26) | Chinese report of the same ingredient pickup/unload crafting loop. |
| [C304](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006733303) | 2026-06-15 08:43:06 | Before | Nirahiel | [L15](#l15) | Asks whether builders still top up after each wall segment. |
| [C305](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006731173) | 2026-06-15 07:42:25 | Before | ignis | [L25](#l25), [L36](#l36), [L52](#l52) | Questions combined hand/inventory mass limits and lack of inventory stack merging. |
| [C306](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006725895) | 2026-06-15 05:22:16 | Before | ignis | [L07](#l07), [L25](#l25), [L36](#l36) | Yield collection interrupts skilled work; tunnellers unload early; requests per-pawn control. |
| [C307](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006723314) | 2026-06-15 04:13:44 | Before | Murphy233 | [L48](#l48) | Encourages continued work on fewer hauling trips. |
| [C308](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006720571) | 2026-06-15 03:04:26 | Before | I'm A Giraffe | [L45](#l45) | Developer lists planned integrations and excludes replacing While You Are Nearby. |
| [C309](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006719199) | 2026-06-15 02:33:37 | Before | I'm A Giraffe | [L27](#l27) | Developer announces automatic batch-bill changes and earlier fixes. |
| [C310](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006715438) | 2026-06-15 01:09:28 | Before | Nirahiel | [L27](#l27) | Explains why sequential inventory-based batching is preferable to giant combined recipes. |
| [C311](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006713343) | 2026-06-15 00:27:19 | Before | I'm A Giraffe | [L50](#l50) | Developer asks to move off-topic discussion elsewhere. |
| [C312](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006713325) | 2026-06-15 00:27:05 | Before | avaster | [L50](#l50) | Makes an unsupported malicious-intent allegation; no technical evidence supplied. |
| [C313](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006712376) | 2026-06-15 00:11:04 | Before | I'm A Giraffe | [L27](#l27) | Developer proposes configuring batch behavior in bills. |
| [C314](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006711893) | 2026-06-15 00:03:07 | Before | Nirahiel | [L27](#l27) | Clarifies preference for planning multiple individual recipes. |
| [C315](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006711828) | 2026-06-15 00:02:01 | Before | Nirahiel | [L27](#l27) | Requests interruptible sequential batches with ingredients fetched once. |
| [C316](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006711520) | 2026-06-14 23:57:37 | Before | I'm A Giraffe | [L27](#l27) | Developer clarifies requested automatic batch behavior. |
| [C317](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006707420) | 2026-06-14 22:59:39 | Before | Nirahiel | [L27](#l27) | Requests automatic planned-style batch crafting respecting meal stock thresholds. |
| [C318](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006697529) | 2026-06-14 20:46:01 | Before | I'm A Giraffe | [L33](#l33), [L51](#l51) | Developer agrees to consider spoilage-aware butchering alongside slaughter hauling. |
| [C319](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006697316) | 2026-06-14 20:43:19 | Before | I'm A Giraffe | [L23](#l23) | Developer announces save handling intended to avoid orphaned custom jobs after removal. |
| [C320](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006697005) | 2026-06-14 20:39:47 | Before | MisterJebo | [L50](#l50) | Says an earlier question was buried in discussion. |
| [C321](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006696048) | 2026-06-14 20:27:52 | Before | I'm A Giraffe | [L49](#l49) | Developer responds to distrust and requests concrete bug reports. |
| [C322](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006696035) | 2026-06-14 20:27:43 | Before | I'm A Giraffe | [L49](#l49) | Developer disputes claims that replies are generated; community debate. |
| [C323](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006694715) | 2026-06-14 20:10:52 | Before | AnneCrankin90s | [L49](#l49) | Questions trust, experience and response authenticity. |
| [C324](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006694048) | 2026-06-14 20:02:06 | Before | MONEY-HIPPO | [L23](#l23) | Clarifies UI failure occurs when loading without HD after saving during an unload job. |
| [C325](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006693488) | 2026-06-14 19:53:54 | Before | I'm A Giraffe | [L23](#l23) | Developer asks for clarification of removal sequence. |
| [C326](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006693170) | 2026-06-14 19:49:37 | Before | MONEY-HIPPO | [L23](#l23) | Identifies active unload jobs in saved games as a UI-removal failure trigger. |
| [C327](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006692842) | 2026-06-14 19:44:51 | Before | I'm A Giraffe | [L49](#l49) | Developer argues for supervised AI use; no additional mod behavior reported. |
| [C328](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006692665) | 2026-06-14 19:42:03 | Before | I'm A Giraffe | [L49](#l49) | Developer discusses AI benefits and misuse. |
| [C329](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006691297) | 2026-06-14 19:20:38 | Before | AnneCrankin90s | [L49](#l49) | States principled opposition to AI. |
| [C330](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006691170) | 2026-06-14 19:18:36 | Before | AnneCrankin90s | [L49](#l49) | Criticizes developer responsibility for generated code. |
| [C331](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006690841) | 2026-06-14 19:13:00 | Before | MisterJebo | [L33](#l33), [L51](#l51) | Requests spoilage-priority butchering integration. |
| [C332](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006690089) | 2026-06-14 19:00:35 | Before | Nirahiel | [L19](#l19), [L48](#l48) | Supports including bulk transport loading in the mod's scope. |
| [C333](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689566) | 2026-06-14 18:52:27 | Before | I'm A Giraffe | [L19](#l19) | Developer agrees demand justifies transport-loading integration. |
| [C334](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689555) | 2026-06-14 18:52:22 | Before | I'm A Giraffe | [L48](#l48), [L52](#l52) | Developer explains configurable overload and travel-efficiency tradeoffs. |
| [C335](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689551) | 2026-06-14 18:52:17 | Before | MisterJebo | [L45](#l45) | Asks about transport, inventory building, mobile meals and nearby-job mods. |
| [C336](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006689535) | 2026-06-14 18:52:04 | Before | I'm A Giraffe | [L45](#l45), [L48](#l48) | Developer explains modular hauling-consolidation scope. |
| [C337](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006688271) | 2026-06-14 18:31:51 | Before | MisterJebo | [L45](#l45), [L48](#l48) | Asks whether consolidation aims to improve efficiency, compatibility and performance. |
| [C338](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006685247) | 2026-06-14 17:44:32 | Before | Nirahiel | [L45](#l45) | Reports removing overlapping mods because HD covers their functions. |
| [C339](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006683158) | 2026-06-14 17:08:06 | Before | NakaruSoul | [L33](#l33), [L45](#l45) | Asks whether Auto Strip on Haul should be removed. |
| [C340](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006681131) | 2026-06-14 16:33:21 | Before | Redegg | [L45](#l45) | Asks whether Pick Up And Haul should be removed. |
| [C341](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006679512) | 2026-06-14 16:07:02 | Before | !.Petey.! | [L15](#l15) | Wants larger sensible deliveries to construction sites. |
| [C342](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006678769) | 2026-06-14 15:53:57 | Before | I'm A Giraffe | [L15](#l15), [L44](#l44) | Developer acknowledges fishing uncertainty and planned-construction bug. |
| [C343](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006678422) | 2026-06-14 15:48:36 | Before | I'm A Giraffe | [L23](#l23) | Developer cannot reproduce black-screen removal failure and discusses message style. |
| [C344](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677826) | 2026-06-14 15:37:15 | Before | avaster | [L49](#l49) | Claims writing style indicates generated responses; no technical report. |
| [C345](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677599) | 2026-06-14 15:32:57 | Before | Nirahiel | [L15](#l15) | Reiterates planned construction returning for tiny material top-ups. |
| [C346](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677242) | 2026-06-14 15:26:43 | Before | I'm A Giraffe | [L15](#l15) | Developer humorous response to inefficient construction report. |
| [C347](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677236) | 2026-06-14 15:26:34 | Before | Miquella's Simp 0001 | [L49](#l49) | Asks others not to prolong an argument. |
| [C348](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677178) | 2026-06-14 15:25:36 | Before | Nirahiel | [L15](#l15) | Planned wall construction refills inventory after every individual segment. |
| [C349](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006677131) | 2026-06-14 15:24:43 | Before | I'm A Giraffe | [L49](#l49) | Developer discusses expectations, bug reports and criticism. |
| [C350](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006676463) | 2026-06-14 15:12:20 | Before | avaster | [L49](#l49) | Accuses replies of being generated; no additional mod behavior. |

### Workshop comments C351–C400

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C351](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006676295) | 2026-06-14 15:09:38 | Before | Fortirus | [L44](#l44) | Asks about fishing support. |
| [C352](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006676130) | 2026-06-14 15:06:26 | Before | I'm A Giraffe | [L49](#l49) | Developer responds to skepticism and stresses ongoing fixes. |
| [C353](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006676124) | 2026-06-14 15:06:21 | Before | I'm A Giraffe | [L49](#l49) | Developer discusses review and testing of AI-assisted work. |
| [C354](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006675418) | 2026-06-14 14:53:43 | Before | avaster | [L49](#l49) | Argues generated code creates complexity and bugs; general criticism. |
| [C355](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006675226) | 2026-06-14 14:50:09 | Before | I'm A Giraffe | [L23](#l23) | Developer requests logs for an unreproduced black-screen report. |
| [C356](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006675165) | 2026-06-14 14:49:09 | Before | BigNoogles | [L49](#l49) | Nontechnical reaction only. |
| [C357](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674844) | 2026-06-14 14:42:55 | Before | Saph | [L23](#l23) | Clarifies alleged removal failure occurs before loading a save, at game startup. |
| [C358](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674729) | 2026-06-14 14:40:47 | Before | Nirahiel | [L23](#l23) | Community advice to preserve backups when changing mods. |
| [C359](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674550) | 2026-06-14 14:36:51 | Before | Saph | [L23](#l23) | Reports black-screen game startup after removal; criticizes generated implementation. |
| [C360](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674547) | 2026-06-14 14:36:51 | Before | I'm A Giraffe | [L24](#l24) | Developer explains item exclusions and surplus-unload setting for sidearms. |
| [C361](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006674276) | 2026-06-14 14:31:08 | Before | I'm A Giraffe | [L49](#l49) | Developer discloses AI implementation and remaining testing limits. |
| [C362](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006673764) | 2026-06-14 14:23:12 | Before | I'm A Giraffe | [L52](#l52) | Developer explains overload constraints in response to inefficient-trip criticism. |
| [C363](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006673054) | 2026-06-14 14:10:18 | Before | Nirahiel | [L49](#l49) | Points to AI disclosure in description. |
| [C364](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006672299) | 2026-06-14 13:56:28 | Before | Dealer Mangan | [L07](#l07), [L24](#l24) | Criticizes inefficient trips and unwanted Pocket Sand/Simple Sidearms unloading. |
| [C365](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006672231) | 2026-06-14 13:55:16 | Before | Etymos | [L24](#l24) | Confirms unload deadlock improved but sidearms are still occasionally stored. |
| [C366](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006671329) | 2026-06-14 13:41:09 | Before | avaster | [L49](#l49) | Brief negative remark about AI use. |
| [C367](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006670618) | 2026-06-14 13:30:10 | Before | Nirahiel | [L49](#l49) | Plans to test and report issues. |
| [C368](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006670371) | 2026-06-14 13:27:43 | Before | I'm A Giraffe | [L45](#l45), [L53](#l53) | Developer anticipates further integration and welcomes Achtung compatibility reports. |
| [C369](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006669909) | 2026-06-14 13:20:07 | Before | Nirahiel | [L45](#l45) | Asks about replacing hauling mods and covering bulk transport loading. |
| [C370](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006666785) | 2026-06-14 12:26:23 | Before | I'm A Giraffe | [L24](#l24) | Developer announces inventory-policy compatibility and ammo/unload-loop fixes. |
| [C371](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006666018) | 2026-06-14 12:14:32 | Before | Nirahiel | [L24](#l24), [L45](#l45), [L53](#l53) | Wants Simple Sidearms and Achtung forced-job compatibility information. |
| [C372](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006665711) | 2026-06-14 12:07:58 | Before | 缪尔艾拉 | [L37](#l37) | Asks about large-colony performance versus Pick Up And Haul. |
| [C373](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006664773) | 2026-06-14 11:50:10 | Before | I'm A Giraffe | [L23](#l23) | Developer prepares sidearm/ammo fix and investigates missing UI after removal. |
| [C374](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006664069) | 2026-06-14 11:37:27 | Before | Etymos | [L23](#l23) | Reports vanished UI after removing HD from a save. |
| [C375](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006663464) | 2026-06-14 11:25:49 | Before | Ruki | [L24](#l24) | Yayo's Combat ammo causes caravan unload deadlock; grenade-carrying mod also suspected. |
| [C376](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006662426) | 2026-06-14 11:07:15 | Before | Etymos | [L24](#l24) | Reports Simple Sidearms-related unload deadlock. |
| [C377](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006661836) | 2026-06-14 10:57:00 | Before | I'm A Giraffe | [L24](#l24) | Developer acknowledges an unloading regression and works on a fix. |
| [C378](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006661818) | 2026-06-14 10:56:42 | Before | I'm A Giraffe | [L23](#l23) | Developer asks for controlled mod-enable/disable comparison for missing UI. |
| [C379](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006661766) | 2026-06-14 10:55:47 | Before | I'm A Giraffe | [L23](#l23) | Duplicate developer request for mod list and controlled comparison. |
| [C380](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006661093) | 2026-06-14 10:44:40 | Before | 残响 | [L07](#l07) | Inventory unloading repeatedly interrupts work. |
| [C381](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006660785) | 2026-06-14 10:39:19 | Before | Saph | [L24](#l24) | Reports an infinite attempt to unload remembered sidearms after mid-save installation. |
| [C382](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006659399) | 2026-06-14 10:14:02 | Before | 超级大肥猪卡比兽 | [L23](#l23) | Replacing Pick Up And Haul makes UI invisible but still clickable. |
| [C383](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006658716) | 2026-06-14 10:00:42 | Before | Winterstein | [L24](#l24) | Asks about Pocket Sand weapon-retention compatibility. |
| [C384](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006657112) | 2026-06-14 09:31:49 | Before | I'm A Giraffe | [L24](#l24) | Developer explains tension between retained equipment and forgotten cargo. |
| [C385](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006650342) | 2026-06-14 06:57:30 | Before | Murphy233 | [L45](#l45) | Asks whether Pick Up And Haul is now unnecessary. |
| [C386](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006647845) | 2026-06-14 05:49:30 | Before | 吱吱宸 | [L19](#l19) | Acknowledges earlier answer on transport integration. |
| [C387](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006647728) | 2026-06-14 05:46:27 | Before | 吱吱宸 | [L19](#l19) | Asks whether bulk transport loading is included. |
| [C388](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006638500) | 2026-06-14 01:59:10 | Before | Silvershroud | [L24](#l24), [L34](#l34) | Surplus unload causes sidearm loops and distant drops of goods without storage. |
| [C389](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006632871) | 2026-06-14 00:08:53 | Before | I'm A Giraffe | [L19](#l19), [L45](#l45) | Developer tentatively answers Skipdoor Pathing compatibility and transport integration questions. |
| [C390](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006632324) | 2026-06-14 00:00:44 | Before | I'm A Giraffe | [L36](#l36) | Developer announces material/mech/animal compatibility, caravan, settings and unload improvements. |
| [C391](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006628091) | 2026-06-13 22:57:10 | Before | -=GoW=-Dennis | [L45](#l45) | Asks about Skipdoor Pathing (Unlimited) compatibility. |
| [C392](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006614487) | 2026-06-13 19:51:39 | Before | Just A Woodchuck | [L33](#l33) | Cannot disable auto stripping because its setting is inaccessible. |
| [C393](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659628006611421) | 2026-06-13 19:11:48 | Before | Audion | [L19](#l19) | Requests bulk loading for shuttles, submap exits and pods. |
| [C394](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304585364) | 2026-06-13 15:29:12 | Before | ChainmailPickaxe | [L31](#l31) | Requests confirmation of Keyz and Allow Tool urgent-haul compatibility. |
| [C395](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304583043) | 2026-06-13 14:45:26 | Before | Fodofox | [L49](#l49) | Accepts AI tools when within the developer's ability to assess the result. |
| [C396](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304582914) | 2026-06-13 14:42:04 | Before | I'm A Giraffe | [L49](#l49) | Developer asserts personal review capability and professional background. |
| [C397](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304582911) | 2026-06-13 14:42:01 | Before | I'm A Giraffe | [L49](#l49) | Developer provides AI-use disclosure and describes review/compatibility process. |
| [C398](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304581942) | 2026-06-13 14:22:33 | Before | Fodofox | [L49](#l49) | Asks whether implementation is original and raises concerns from another mod's removal problems. |
| [C399](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304578838) | 2026-06-13 13:17:05 | Before | SpaceDorf | [L25](#l25), [L36](#l36), [L55](#l55) | Wants modded material, mech, animal and robot support before adopting. |
| [C400](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304578033) | 2026-06-13 12:58:30 | Before | Zapdude | [L55](#l55) | Supports broad compatibility work and possible Multiplayer testing. |

### Workshop comments C401–C421

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [C401](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304577939) | 2026-06-13 12:56:24 | Before | I'm A Giraffe | [L25](#l25) | Developer prioritizes modded materials and plans Multiplayer investigation. |
| [C402](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304577378) | 2026-06-13 12:45:04 | Before | I'm A Giraffe | [L39](#l39), [L47](#l47) | Developer identifies missing settings scrollbar and confirms tag correction. |
| [C403](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304576711) | 2026-06-13 12:29:41 | Before | Zapdude | [L25](#l25), [L41](#l41), [L55](#l55) | Warns unsupported materials could remain trapped in inventory; asks about Multiplayer. |
| [C404](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304571226) | 2026-06-13 10:29:55 | Before | BigNoogles | [L49](#l49) | Plans to add the mod. |
| [C405](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304550935) | 2026-06-13 01:47:46 | Before | joey | [L49](#l49) | Expression of appreciation. |
| [C406](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304550815) | 2026-06-13 01:45:19 | Before | Sadako | [L39](#l39) | Requests Workshop tags. |
| [C407](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304528643) | 2026-06-12 19:34:21 | Before | SpaceDorf | [L33](#l33) | Requests an auto-strip off switch to keep corpses intact. |
| [C408](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304519301) | 2026-06-12 17:05:32 | Before | 枫落霜桥 | [L37](#l37) | Interested in features but concerned about performance. |
| [C409](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304509634) | 2026-06-12 14:31:15 | Before | I'm A Giraffe | [L37](#l37) | Developer says comparative performance had not yet been benchmarked. |
| [C410](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304509463) | 2026-06-12 14:27:58 | Before | I'm A Giraffe | [L45](#l45) | Developer says While You Are Nearby is separate because it reorders work. |
| [C411](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304509449) | 2026-06-12 14:27:46 | Before | I'm A Giraffe | [L48](#l48) | Developer considers adding opportunistic pickup alongside existing opportunistic unloading. |
| [C412](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304508651) | 2026-06-12 14:12:21 | Before | I'm A Giraffe | [L38](#l38), [L53](#l53) | Developer recognizes special-item and queued-job compatibility gaps and requests mod details. |
| [C413](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304504270) | 2026-06-12 12:48:10 | Before | gezi | [L37](#l37) | Asks about performance. |
| [C414](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304501528) | 2026-06-12 11:56:10 | Before | henk | [L45](#l45) | Asks whether While You're Up and While You Are Nearby are replaced. |
| [C415](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304497039) | 2026-06-12 10:22:11 | Before | Jaggid Edje | [L25](#l25), [L41](#l41) | Clarifies un-unloaded deconstruction material is modded bronze. |
| [C416](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304496959) | 2026-06-12 10:20:10 | Before | HawnHan | [L25](#l25), [L41](#l41) | Asks whether modded crop yields can become impossible to unload. |
| [C417](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304494570) | 2026-06-12 09:30:51 | Before | Jaggid Edje | [L53](#l53) | Suspects Better Autocasting for VPE interrupts queued hauling/unloading. |
| [C418](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304493444) | 2026-06-12 09:06:25 | Before | I'm A Giraffe | [L41](#l41) | Developer acknowledges missed unloading and starts a patch. |
| [C419](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304492754) | 2026-06-12 08:51:38 | Before | Jaggid Edje | [L41](#l41) | Deconstruction materials remain in inventories through a full day of normal activity. |
| [C420](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304492470) | 2026-06-12 08:45:47 | Before | oldnewone | [L49](#l49) | Tentative enthusiasm. |
| [C421](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c563659290304472245) | 2026-06-12 02:16:45 | Before | kReoSo | [L49](#l49) | Praises combining inventory hauling with shared-stack delivery. |

### T01 — [Bug / regression] #114 is still reproducible after the v1.22.0 fix — multiple haulers over-haul into nearly-full storage

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/) · Unlocked · 2 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T01-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/) | 2026-08-07 23:02:32 | Before | Eversset | [L02](#l02) | Reports concurrent over-delivery after v1.22.0; asks for complete incoming-capacity accounting and in-game verification. Predates v1.24.0. |
| [T01-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691114229) | 2026-08-17 16:11:30 | After | 枫落霜桥 | [L11](#l11) | Separate report suspects Better Workbench Management; product-counting exception interrupts a lifter's haul. |
| [T01-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691115322) | 2026-08-17 16:22:09 | After | 枫落霜桥 | [L50](#l50) | Body unavailable: Steam moderation placeholder. |

### T02 — Bug Report: No Path And Standing When Trying To Haul

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/) · Locked · 6 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T02-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/) | 2026-08-01 13:28:51 | Before | Richard Ramirez | [L34](#l34) | Unreachable shelf stacks leave haulers standing; initially suspects Tidy Storage. |
| [T02-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c581678423012823874) | 2026-08-01 13:39:05 | Before | Richard Ramirez | [L34](#l34) | Clarifies shelves were personally modified to be impassable; asks about lost access. |
| [T02-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c581678423012923070) | 2026-08-02 16:55:26 | Before | I'm A Giraffe | [L34](#l34) | Developer attributes the loop to HD path-failure recovery and says it was fixed. |
| [T02-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c581678423012923383) | 2026-08-02 17:00:31 | Before | Richard Ramirez | [L34](#l34) | Reporter agrees to test the fix. |
| [T02-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c581678423012956683) | 2026-08-02 23:53:56 | Before | Richard Ramirez | [L34](#l34) | Reporter changes shelf path cost and asks whether the issue originated in vanilla. |
| [T02-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c582804662258798212) | 2026-08-05 11:22:07 | Before | I'm A Giraffe | [L34](#l34) | Developer explains HD's destination-selection responsibility. |
| [T02-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/581678423012823308/#c582804662258798304) | 2026-08-05 11:23:26 | Before | I'm A Giraffe | [L50](#l50) | Developer locks topic to direct new reports to the in-game report tool. |

### T03 — Bug report

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/) · Locked · 27 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T03-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/) | 2026-07-05 10:58:56 | Before | Kostr184 | [L06](#l06), [L15](#l15), [L19](#l19) | Reports ignored construction inventory, poorly shared dungeon-exit loading, and hospital hemopack loops. |
| [T03-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668602725147613) | 2026-07-06 00:45:28 | Before | I'm A Giraffe | [L06](#l06) | Developer acknowledges the three bugs. |
| [T03-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640380927) | 2026-07-06 08:02:46 | Before | I'm A Giraffe | [L06](#l06) | Developer initially says v1.16 addresses the reports. |
| [T03-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640421351) | 2026-07-06 20:02:15 | Before | Kostr184 | [L06](#l06), [L38](#l38) | Reporter says v1.16.1 still fails; report server also fails and error text is clipped. |
| [T03-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640436109) | 2026-07-06 23:26:39 | Before | I'm A Giraffe | [L06](#l06), [L38](#l38) | Developer checks duplicate versions and attempts a report-server certificate fix. |
| [T03-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640449430) | 2026-07-07 03:17:37 | Before | Kostr184 | [L06](#l06), [L38](#l38) | Minimal-mod v1.16.3 still fails; report submission now works. |
| [T03-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640473661) | 2026-07-07 12:01:38 | Before | I'm A Giraffe | [L06](#l06) | Developer revises diagnosis and explains destination-reservation recovery attempt. |
| [T03-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640489340) | 2026-07-07 16:02:16 | Before | Kostr184 | [L06](#l06) | Reporter says hospital failure persists in v1.16.4. |
| [T03-R08](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640490667) | 2026-07-07 16:21:38 | Before | I'm A Giraffe | [L06](#l06) | Developer announces another approach in v1.16.5. |
| [T03-R09](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640496737) | 2026-07-07 17:42:10 | Before | Kostr184 | [L06](#l06), [L54](#l54) | Hospital problem persists; per-stack pickup delays slow tiny floor-removal yields. |
| [T03-R10](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640518850) | 2026-07-07 23:10:13 | Before | I'm A Giraffe | [L06](#l06), [L54](#l54) | Developer says v1.16.6 fixes the issue and pickup-delay behavior. |
| [T03-R11](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640532134) | 2026-07-08 03:48:53 | Before | Kostr184 | [L06](#l06), [L54](#l54) | Hospital loop persists in v1.16.7; harvest workers pause after others collect their yields. |
| [T03-R12](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640547816) | 2026-07-08 11:06:03 | Before | I'm A Giraffe | [L06](#l06) | Developer requests save/settings and further harvesting evidence. |
| [T03-R13](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640550189) | 2026-07-08 12:03:17 | Before | Kostr184 | [L06](#l06) | Supplies save/video; contrasts manual harvest with growing-zone output handling. |
| [T03-R14](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640568095) | 2026-07-08 16:59:56 | Before | I'm A Giraffe | [L06](#l06) | Developer asks for a reduced DLC test and works on remaining problems. |
| [T03-R15](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640570100) | 2026-07-08 17:24:20 | Before | Kostr184 | [L06](#l06) | Reporter reproduces using the developer's DLC set and minimal mods. |
| [T03-R16](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572668858640583316) | 2026-07-08 20:16:59 | Before | I'm A Giraffe | [L06](#l06) | Developer requests the reduced save after failing to reproduce locally. |
| [T03-R17](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572669129317693223) | 2026-07-09 10:06:42 | Before | I'm A Giraffe | [L06](#l06) | Developer reproduces hospital loop and investigates further. |
| [T03-R18](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c572669129317696254) | 2026-07-09 11:21:26 | Before | Kostr184 | [L54](#l54) | Reporter confirms floor pickup and harvest standing improved; notes remaining harvest inconsistency. |
| [T03-R19](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930312327) | 2026-07-10 03:44:47 | Before | I'm A Giraffe | [L06](#l06) | Developer announces hospital-loop fix in v1.16.12. |
| [T03-R20](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930317422) | 2026-07-10 06:20:13 | Before | Kostr184 | [L06](#l06), [L54](#l54) | Reporter confirms core problems fixed; still wants consistent harvested-yield handling. |
| [T03-R21](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930339470) | 2026-07-10 15:29:17 | Before | I'm A Giraffe | [L54](#l54) | Developer asks for clarification and points to yield settings. |
| [T03-R22](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930384644) | 2026-07-11 05:46:24 | Before | Kostr184 | [L54](#l54) | Reporter explains growing-zone yields drop and are later swept, unlike immediate-looking manual harvests. |
| [T03-R23](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930426370) | 2026-07-11 19:21:51 | Before | I'm A Giraffe | [L54](#l54) | Developer acknowledges clarification. |
| [T03-R24](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930442645) | 2026-07-11 23:09:45 | Before | I'm A Giraffe | [L54](#l54) | Developer explains collection modes and plans more consistent presentation. |
| [T03-R25](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930454489) | 2026-07-12 03:38:19 | Before | I'm A Giraffe | [L54](#l54) | Developer announces v1.18.0 harvest consistency and optional direct-harvest delay. |
| [T03-R26](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930510227) | 2026-07-12 22:55:30 | Before | Maal | [L51](#l51) | Reports unresolved Common Sense ingredient-sorting hook and links logs. |
| [T03-R27](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725080778/#c571543307930511397) | 2026-07-12 23:14:33 | Before | I'm A Giraffe | [L51](#l51) | Developer says the Common Sense hook issue was patched. |

### T04 — Opportunistic loading should account for already incoming carried cargo

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/571543307930407446/) · Locked · 2 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T04-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/571543307930407446/) | 2026-07-11 14:37:07 | Before | Eversset | [L19](#l19) | Requests incoming-cargo reservations to prevent several haulers diverting for the same tiny transport deficit. |
| [T04-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/571543307930407446/#c571543307930426638) | 2026-07-11 19:25:25 | Before | I'm A Giraffe | [L19](#l19) | Developer accepts incoming-cargo coordination request. |
| [T04-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/571543307930407446/#c571543307930454568) | 2026-07-12 03:40:31 | Before | I'm A Giraffe | [L19](#l19) | Developer requests feedback on the implementation in v1.18.0; no later reporter confirmation shown. |

### T05 — Feature Request: Optional pickup delay/progress bar

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/) · Locked · 3 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T05-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/) | 2026-07-04 16:50:54 | Before | Eversset | [L54](#l54) | Requests optional pickup progress/delay while retaining hauling features. |
| [T05-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/#c572668602725035719) | 2026-07-04 21:50:29 | Before | 1039384785 | [L54](#l54) | Another user raises potential overlap with LWM's Deep Storage. |
| [T05-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/#c572668858640381090) | 2026-07-06 08:05:51 | Before | I'm A Giraffe | [L54](#l54) | Developer announces configurable pickup delay, initially enabled by default. |
| [T05-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572668602725013351/#c572668858640427846) | 2026-07-06 21:27:33 | Before | Eversset | [L54](#l54) | Reporter confirms the delay works and improves the feel of pickup. |

### T06 — Bug Report

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/) · Locked · 4 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T06-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/) | 2026-06-24 01:19:03 | Before | Bwerna | [L37](#l37) | Perspective Shift solo colony hitches outside home area after building; disabling HD helps. |
| [T06-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/#c572667758936853463) | 2026-06-24 01:21:48 | Before | I'm A Giraffe | [L37](#l37) | Developer asks for an in-game report with logs. |
| [T06-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/#c572667758936853503) | 2026-06-24 01:22:35 | Before | I'm A Giraffe | [L37](#l37) | Developer asks for gameplay before capturing diagnostic logs. |
| [T06-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/#c572667758936945046) | 2026-06-25 05:27:28 | Before | Bwerna | [L37](#l37) | Reporter submits logs and suspects shelves. |
| [T06-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/572667758936853314/#c572667758936967371) | 2026-06-25 14:35:20 | Before | I'm A Giraffe | [L37](#l37) | Developer says v1.11.1 addresses the problem; no later user confirmation shown. |

### T07 — Compatability Request

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/) · Locked · 19 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T07-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/) | 2026-06-12 14:09:13 | Before | I'm A Giraffe | [L50](#l50) | Developer opens a place for suspected compatibility reports. |
| [T07-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365425003) | 2026-06-14 03:22:01 | Before | Isajii | [L24](#l24) | Smart Medicine supplies and Simple Sidearms equipment enter unload/pickup loops. |
| [T07-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365428556) | 2026-06-14 04:40:55 | Before | Tortus | [L24](#l24) | Dubs Bad Hygiene water enters an unload loop. |
| [T07-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365430383) | 2026-06-14 05:19:04 | Before | Mayfly | [L24](#l24) | ItemPolicy-managed items are repeatedly picked up and put down. |
| [T07-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365442336) | 2026-06-14 09:42:05 | Before | I'm A Giraffe | [L24](#l24) | Developer explains conflict between retained equipment and automatic unloading. |
| [T07-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365450782) | 2026-06-14 12:26:30 | Before | I'm A Giraffe | [L24](#l24) | Developer announces retention compatibility and unloading-policy fixes. |
| [T07-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365458930) | 2026-06-14 14:54:11 | Before | ignis | [L01](#l01) | Common Sense ingredient-gathering interaction causes crafting loops even when its option is disabled. |
| [T07-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365473080) | 2026-06-14 18:47:47 | Before | MisterJebo | [L33](#l33) | Requests integrating Vanilla Fix: Haul After Slaughter. |
| [T07-R08](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365473520) | 2026-06-14 18:53:54 | Before | I'm A Giraffe | [L33](#l33) | Developer confirms slaughter-hauling integration is planned. |
| [T07-R09](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877466015) | 2026-06-17 07:39:52 | Before | Kassia Iz BezdnbI | [L30](#l30) | Storage Network stock uses individual stacks instead of full bulk collection. |
| [T07-R10](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877481380) | 2026-06-17 13:28:27 | Before | MisterJebo | [L29](#l29) | Everybody Gets One clothing bill integration does not appear. |
| [T07-R11](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877481408) | 2026-06-17 13:29:02 | Before | Fajdek | [L29](#l29) | Independently reports Everybody Gets One not working. |
| [T07-R12](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877481571) | 2026-06-17 13:32:43 | Before | MisterJebo | [L29](#l29) | Acknowledges duplicate simultaneous compatibility reports. |
| [T07-R13](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877482070) | 2026-06-17 13:42:57 | Before | Fajdek | [L29](#l29) | Cook Carefully fails during batch crafting. |
| [T07-R14](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877490159) | 2026-06-17 16:15:11 | Before | I'm A Giraffe | [L29](#l29) | Developer says compatibility changes are forthcoming. |
| [T07-R15](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877534478) | 2026-06-18 06:50:11 | Before | Tequila Sunset | [L55](#l55) | Asks whether Multiplayer support is planned. |
| [T07-R16](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877627339) | 2026-06-19 10:26:20 | Before | I'm A Giraffe | [L55](#l55) | Developer confirms Multiplayer support is planned. |
| [T07-R17](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877686761) | 2026-06-20 00:35:49 | Before | I'm A Giraffe | [L29](#l29) | Developer says requested compatibility strengthened, with Multiplayer still pending. |
| [T07-R18](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793023877726197) | 2026-06-20 12:34:20 | Before | I'm A Giraffe | [L55](#l55) | Developer announces Multiplayer compatibility support. |
| [T07-R19](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c573793348713836954) | 2026-06-21 23:27:09 | Before | I'm A Giraffe | [L50](#l50) | Developer redirects new feedback to the in-game reporting tool. |

### T08 — Integration Request (for organisation)

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527634/) · Locked · 1 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T08-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527634/) | 2026-06-15 14:28:53 | Before | MisterJebo | [L50](#l50) | Organizational reminder to check existing integrations before requesting one. |
| [T08-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527634/#c573793348713836914) | 2026-06-21 23:26:42 | Before | I'm A Giraffe | [L50](#l50) | Developer redirects new requests to the in-game reporting tool. |

### T09 — Bug report (for organisation)

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/) · Locked · 26 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T09-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/) | 2026-06-15 14:31:35 | Before | MisterJebo | [L50](#l50) | Creates a general bug-report thread. |
| [T09-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365528150) | 2026-06-15 14:36:36 | Before | MisterJebo | [L29](#l29) | Butchering and stonecutting ingredients stay invisible in inventory instead of appearing on the work surface. |
| [T09-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365548548) | 2026-06-15 19:50:54 | Before | I'm A Giraffe | [L50](#l50) | Developer acknowledges reports. |
| [T09-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365575227) | 2026-06-16 03:32:17 | Before | Tamura Hibiki | [L28](#l28) | Mech gestation and core crafting return carried materials to storage instead of consuming them. |
| [T09-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365587953) | 2026-06-16 08:50:05 | Before | MisterJebo | [L28](#l28) | Another user cannot reproduce the gestator/core problem and suggests checking conflicts. |
| [T09-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365603206) | 2026-06-16 13:35:53 | Before | Tamura Hibiki | [L28](#l28) | Reporter suspects Grab Your Tool; no isolated confirmation. |
| [T09-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c564785528365605496) | 2026-06-16 14:15:30 | Before | I'm A Giraffe | [L28](#l28) | Developer investigates gestator/core crafting. |
| [T09-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877511686) | 2026-06-17 22:01:15 | Before | arms_2003 | [L19](#l19) | Pawns fail to board normal and ancient transport pods even without other mods. |
| [T09-R08](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877532596) | 2026-06-18 05:53:14 | Before | Flyrija | [L42](#l42) | Duplicates RV unload loop and caravan furniture-trip report; requests storage unload control. |
| [T09-R09](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877542673) | 2026-06-18 10:43:36 | Before | Tarrant | [L27](#l27) | Automatic batch crafting reverts to vanilla on new saves without logged errors. |
| [T09-R10](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877544273) | 2026-06-18 11:21:51 | Before | dannway | [L43](#l43) | Bulk refuelling blocks Medieval Overhaul ragout pots, including forced refuel. |
| [T09-R11](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877544840) | 2026-06-18 11:35:38 | Before | Kato | [L15](#l15) | Blueprint deliveries alternate across a wall; later rules out a simple setting fix and separates work-priority conflict. |
| [T09-R12](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877553475) | 2026-06-18 14:29:19 | Before | Nirahiel | [L32](#l32) | Unloading required bioferrite interrupts rituals. |
| [T09-R13](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877553721) | 2026-06-18 14:33:58 | Before | Nirahiel | [L32](#l32) | Reporter identifies unloading non-HD cargo as a possible ritual trigger. |
| [T09-R14](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877555484) | 2026-06-18 15:05:15 | Before | NoctisTheBogWitch | [L40](#l40) | Manual pickup becomes hauling and fails for items already in storage. |
| [T09-R15](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877555754) | 2026-06-18 15:10:16 | Before | Nirahiel | [L27](#l27) | Automatic meal batching fails while manually planned batches still work. |
| [T09-R16](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877570230) | 2026-06-18 18:49:54 | Before | Dragon | [L42](#l42) | Independent confirmation of RV/small-map inventory drop-and-pickup loops. |
| [T09-R17](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877602512) | 2026-06-19 02:36:24 | Before | Pausbrak | [L38](#l38), [L43](#l43) | Bulk refuelling breaks Save Our Ship engines; requests clearer default diagnostics. |
| [T09-R18](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877604503) | 2026-06-19 03:10:59 | Before | I'm A Giraffe | [L38](#l38) | Developer announces pending fixes and accepts better error attribution. |
| [T09-R19](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877624293) | 2026-06-19 09:27:39 | Before | Pausbrak | [L34](#l34) | Batch jobs add targets outside allowed zones; especially dangerous in unprotected space. |
| [T09-R20](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877682591) | 2026-06-19 23:34:32 | Before | 󠀠 | [L10](#l10) | Minimal-mod startup warnings identify textures/materials loaded without proper startup initialization. |
| [T09-R21](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877686642) | 2026-06-20 00:34:13 | Before | I'm A Giraffe | [L49](#l49) | Developer broadly announces fixes and compatibility improvements. |
| [T09-R22](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877708643) | 2026-06-20 06:57:05 | Before | dannway | [L26](#l26) | Medieval Overhaul pumpkin-pie batches loop gathering ingredients with target-count bills. |
| [T09-R23](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877729321) | 2026-06-20 13:24:43 | Before | NoctisTheBogWitch | [L29](#l29) | Recycle It no longer works; reporter explicitly is not on latest version. |
| [T09-R24](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793348713825908) | 2026-06-21 21:07:38 | Before | CheaterEater | [L10](#l10) | Mod startup fails on missing Multiplayer API type even without Multiplayer installed. |
| [T09-R25](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793348713827029) | 2026-06-21 21:21:47 | Before | NoctisTheBogWitch | [L10](#l10), [L38](#l38) | Work selection stalls; supplied trace begins in Haul Explicitly and passes through Vehicle Map Framework. |
| [T09-R26](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793348713836888) | 2026-06-21 23:26:25 | Before | I'm A Giraffe | [L50](#l50) | Developer redirects new reports to the in-game reporting tool. |

### T10 — VERY confused about this mod's compats

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365521665/) · Locked · 1 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T10-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365521665/) | 2026-06-15 12:34:19 | Before | aliveinyourmind | [L45](#l45) | Asks about replacing PUAH/stripping mods and compatibility with urgent and perishable hauling. |
| [T10-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365521665/#c564785528365527137) | 2026-06-15 14:21:03 | Before | MisterJebo | [L45](#l45) | Community reply discusses coverage and maturity; perishable-hauling question remains unanswered. |

### T11 — Issue where pawns are trying to drop their ammo.

[Original topic](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/) · Locked · 7 replies collected.

| Ref / Steam source | Posted (CEST) | Update relation | Author | Ledger | Paraphrase |
|---|---|---|---|---|---|
| [T11-OP](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/) | 2026-06-14 07:57:10 | Before | RNG-esus | [L24](#l24) | Ammo repeatedly unloads and is retrieved, preventing work; CE/weapon mods suspected. |
| [T11-R01](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365441995) | 2026-06-14 09:35:02 | Before | I'm A Giraffe | [L24](#l24) | Developer proposes ammo-retention controls. |
| [T11-R02](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365445300) | 2026-06-14 10:39:54 | Before | MisterJebo | [L24](#l24) | Suggests honoring CE loadouts and equipment retention quantities. |
| [T11-R03](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365447747) | 2026-06-14 11:24:42 | Before | I'm A Giraffe | [L24](#l24) | Developer accepts retention-policy suggestion. |
| [T11-R04](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365450775) | 2026-06-14 12:26:17 | Before | I'm A Giraffe | [L24](#l24) | Developer announces ammo and retained-inventory compatibility fixes. |
| [T11-R05](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365469942) | 2026-06-14 18:00:16 | Before | RNG-esus | [L24](#l24) | Requests storage-filter-style category controls instead of configuring every ammo type. |
| [T11-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365470351) | 2026-06-14 18:06:46 | Before | RNG-esus | [L24](#l24) | Clarifies category-wide retention and equipped-weapon ammo options. |
| [T11-R07](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365437292/#c564785528365499496) | 2026-06-15 02:31:39 | Before | I'm A Giraffe | [L24](#l24) | Developer agrees to improve retention controls. |

## Snapshot audit

- All 530 unique Steam IDs have one source-register row and at least one ledger mapping; all 528 readable bodies have a paraphrase. Two hidden bodies are marked unavailable.
- Every discussion’s reply count matches its captured total; all 11 opening posts are present.
- The final GitHub open-item fetch matches the initial collection: 15 issues and 2 PRs. All 17 have a ledger record and a source-register entry.
- All 17 conversation-comment IDs on those open items have a summary. Both open PRs have zero returned submitted reviews and zero inline review comments.
- GitHub issue closure and PR merge metadata were checked separately; #148/#267 remain open, and #22/#57/#147/#249 were not merged. #32/#33/#245 were closed unmerged but their work is described as incorporated elsewhere in the referenced merged PRs.
- The local checkout’s v1.16.9 package/changelog was not used as the deployed-version cutoff; Steam and GitHub publication metadata establish v1.24.0.
- This is a read-only research snapshot and a local ledger. No issue/PR states were changed and no public replies were posted.


## Append-only feedback additions: C422–C426

The initial 564-entry snapshot and C001–C421 identifiers remain preserved. C422 was verified at the goal-start refresh; C423–C426 are the four user-supplied Vorshlumpf comments, independently matched to live Steam HTML on 8 September. This targeted addition does not claim a new complete GitHub/topic refresh. Current coverage is 569 primary sources.

[C422](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292699148) — 2026-09-07 00:47:50 CEST, I'm A Giraffe, L49: Maintainer refers to the August 24 availability update, thanks users, says feedback since the last mod update is being gathered, and plans to answer questions after the next update is released. This is process context, not a fix claim.

| Source / date (Copenhagen) | Workstreams | Preserved feedback |
|---|---|---|
| [C423](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292709966) / 2026-09-07 04:32:54 CEST | L01, L39, L45, L47, L54 | Resource gathering does not work in an old, heavily modded game; reporter re-added Harvest and Haul. Several feature descriptions are clipped. General praise and learning-curve context are retained. |
| [C424](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292755974) / 2026-09-07 18:32:23 CEST | L01, L26, L36, L38, L43, L45, L53 | Misc. Robots++ do not bulk-haul into inventory or expose a direct nearby-haul command. Requests RIMMSqol/drafted-command and Custom Alerts integration; asks about own-inventory crafting and requests own-inventory refuelling. |
| [C425](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292756958) / 2026-09-07 18:44:04 CEST | L02, L38, L40, L46 | Continuation: picking up an item/stack displays the nearby-haul activity. Requests dropping a specified quantity and explicit point-to-point hauling, particularly to a selected shelf. Praises six existing feature areas. |
| [C426](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292789520) / 2026-09-08 01:41:45 CEST | L15, L36 | Follow-up: a robot gathered enough material to fully construct a building. This narrows the earlier robot report; the hidden nearby-haul job is a reporter hypothesis, not an established cause or a retraction. |

All 13 distinct new subjects have explicit acceptance criteria in [the implementation plan](plans/vorshlumpf-feedback-2026-09-08.md), with full bodies and linked mod identities in [the source supplement](feedback-vorshlumpf-2026-09-08.json). [The machine-readable register](plans/feedback-sources.json) contains source/workstream/child obligations and reverse mappings. All are mandatory for the same PR; every full acceptance row remains open. Linked focused plans retain bounded implementation and verification progress, including the independently reviewed partial-drop UI and representative automatic robot hauling. These results do not resolve an entire source by association.

The robot construction success qualifies the earlier robot failure rather than resolving it. Hidden-job causation is only the reporter's hypothesis. Own-inventory crafting, ingredient acquisition, yield collection, own-inventory fuel and another carrier's shared inventory remain separate paths. RIMMSqol, Custom Alerts, partial dropping and exact-shelf hauling require distinct implementation evidence. Prior L01/L06/L36/L43/L39/L47 fix claims and L40/L46 feature history must be reviewed as described in the new plan before assigning recurrence or declaring coverage.


### Robot mapping correction, 8 September 2026

C424-S02 remains a complete manual robot command obligation under **L36**. Its earlier L06 association was an inventory mapping error: L06 covers hospital/prison hemogen and organ pacing. The old parent `AC-L06-C424` and child `AC-L06-C424-S02` are preserved as superseded aliases of `AC-L36-C424` and `AC-L36-C424-S02`; no requested behavior was retired or completed. L08/C002 is a comparison only: that report says explicit bulk commands work while ordinary/automatic activation fails, so it does not establish the cause of C424's missing explicit robot command.

The [focused robot plan](plans/misc-robots-hauling-support.md) records the actual linked mod sources, two independent HD source gaps, specialist work/control boundaries, prior-fix distinctions and the pending runtime matrix. C426 remains a qualified construction success control and does not retract C424. Actual-game confirmation and independent review of this coordinated correction remain pending. Original source bodies and the original 565-source/655-criterion inventory are preserved.
