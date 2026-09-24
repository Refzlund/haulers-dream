# Independent source coverage audit: corrections reviewed

Reviewer: source_coverage_qa, 2026-09-07. Scope: inventory and individual subjects, not implementation or gameplay. The original findings below are preserved from the first review; the corrected inventory has now received a fresh independent re-audit.

## Re-audit result

Independent re-audit found no remaining defects in the reviewed inventory corrections. All 565 source IDs/URLs, baseline paraphrases and 654 original mappings were preserved. The 47 supplemental subjects, 45 added mappings, 699 total mappings and 655 pending acceptance criteria reconciled with raw captures and reverse mappings. Approval covers source inventory and dispositions only; implementation and gameplay verification remain unfinished.

Final classification is 470 pending, 93 context-only and two unavailable/hidden records. None of the 655 active acceptance criteria has acquired implementation-completion evidence from this audit. The earlier counts in the preserved initial review below describe the pre-correction inventory.

## Verified

565 unique IDs/URLs: 531 refreshed Steam records, 17 open GitHub bodies, 17 GitHub conversation comments. All 530 baseline Steam mappings/paraphrases and 34 baseline GitHub paraphrases are preserved. All 55 workstreams, including L05's historical obligation, remain present. All 559 acceptance definitions are pending with zero observed completion evidence. Native Steam IDs/Unix times and GitHub times, reported versions and mod counts match captures. Literal JSON UTC times are correct; PowerShell's automatic date conversion caused an apparent offset discrepancy, which is not a source defect. All 94 context-only raw bodies were read.

## Classification corrections

| Source | Required correction |
|---|---|
| C397 | Raw first sentence reports inability to update Workshop description because of an error. Preserve baseline AI/disclosure paraphrase; add historical publication failure under L39 and pending disposition. An external Steam problem may require no HD implementation, but that has not been established. |
| C042 | Fix acknowledgement, not unqualified praise. Same author meat reported construction unloading after each wall in C052 the preceding day. Promote to pending historical acknowledgement with provisional C052/L15 relationship. Antecedent is not conclusively identified and this is not a verified retest. |
| C307 | Raw praise/encouragement to continue updates contains no requested behavior. Explicit non-actionable disposition is justified; do not invent an L48 implementation from encouragement. |

## Supplemental individual obligations

Preserve every baseline paraphrase and mapping. Add supplemental subject text and mappings instead of rewriting the historical capture. Each actionable or historical technical subject stays pending until its actual evidence is evaluated.

| Source | Subject and workstream |
|---|---|
| GH267 | Raw actionable section asks for consistent prioritization semantics: bulk unloading chains until drained while bulk loading performs one visit. Add explicit behavioral consistency acceptance under L19/L21, distinct from translation wording or duplicated driver review. Baseline 'Descriptive polish' omits this request. |
| T01-R01 | Unable to create own discussion, posted inside another topic: L38 alongside L11. |
| C199 | Cannot find Report button or identify which mod hides it: L38 alongside transport/ritual subjects. |
| GH261 | Cannot edit prior report: explicit L38 obligation, not only prose under L04. |
| T03-R07 | Uncertain whether replying attaches new logs: L38 reporting workflow/docs. |
| C171 | Second paragraph asks about direct container pickup: L40, linked C173/C182. Separate from uninstall advice. |
| T03-R12 | Manual pickup delay and uncertain Harvest and Haul compatibility: L54/L45 alongside L06. |
| T03-R13 | Vanilla floor-removal pickup evidence and manual harvest versus growing-zone handling: L54. Retain linked save/video as evidence for these subjects. |
| C306 | Requests per-pawn yield gizmo instead of buried global toggle: L47. |
| C265 | Shuttle refuelling, separate from transport loading: L43. |
| C021 | Separate CanGiveJob/JobOnX synchronization warning and removing-mods support question: L38, not only cloth loop. |
| T04-OP | Explicit pickup-delay update success claim: L54 historical retest claim alongside L19; not final-build verification. |

## Maintainer claim coverage

These sources remain individually auditable. A broad acknowledgement or fix announcement does not prove all listed behavior passed.

| Source | Missing subject mappings |
|---|---|
| C071 | Controlled-map vehicle-use option and strict weight versus stack-count explanation: L42/L52. |
| C083 | Acknowledges unload alert/Simple Sidearms addressed: L24/L38. |
| C118 | Claims feasible partial batching shipped: L26. |
| C217 | Latest update claim includes C219 null-reference report: L10/L38. |
| C268 | Mech unloading, modded-workbench batching, storage-limit compatibility: L18/L27/L03 alongside L36. |
| C373 | Promises Simple Sidearms/CE ammo compatibility before removal-failure discussion: L24. |
| C390 | Claims caravan/pack-animal handling, settings scrolling/auto-strip, urgent hauling, Common Sense, Autocast, no-storage alerts. Map each to the corresponding existing workstream, not only L36. |
| C401 | Multiplayer investigation promise: L55 alongside L25. |
| T03-R01/T03-R02 | Acknowledge/claim fixes for all three OP subjects: L15/L19 alongside L06. Later hospital retests do not implicitly retest the other two. |
| T03-R14 | Other-two-issues acknowledgement refers to pickup/harvest discussion: L54. |
| T07-R17 | Multiplayer remains excluded from announced compatibility update: L55. |
| T09-R18 | Separately acknowledges construction routing, ritual unloading and refuelling/diagnostic feedback; retain corresponding links alongside L38. |

C320 correctly remains unresolved: it says an earlier comment was buried but does not identify it. C331's spoilage-butchering question is plausible, but the same author has other earlier questions, including C335. Do not turn this into a confident duplicate mapping.

Recompute mapping/acceptance/disposition counts after corrections. Keep source counts/IDs and all baseline fields intact. No new subject receives completed implementation status from this audit.
