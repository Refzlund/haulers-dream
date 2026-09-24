Address all received Hauler’s Dream feedback end-to-end in one thoroughly validated PR, using `docs/plans/feedback-since-last-update.md` as the mandatory starting inventory. This includes defects, recurring problems, compatibility reports, support requests, requested features, and documentation or usability shortcomings.

The objective is complete coverage with careful implementation. The number of remaining tasks must never lower the investigation, implementation, or verification standard applied to an individual task. Work in manageable batches for as long as necessary.

Work completely autonomously. Delegate uncertainties and design questions to investigative subagents, obtain evidence and recommendations, and make the decisions yourself. Do not ask me routine questions or wait for direction between phases.

1. Establish the complete scope and durable plan.

Read the ledger and its source references. Refresh Steam feedback and GitHub issues, PRs, and relevant discussions at the start; record the collection cutoff. Reconcile the actual published mod, current upstream code, local changes, and pending PRs before deciding what is already implemented.

Create or update `docs/plans/*.md` with a master index, implementation plan, and completion matrix. Preserve this working record throughout the goal so progress survives context changes.

Every individual feedback source must map to an explicit disposition and, where actionable, an acceptance criterion. Group duplicates without losing their distinct reproduction details, environments, requested behavior, or follow-up corrections. The 55 grouped ledger topics are navigation aids, not substitutes for covering their individual contents.

Distinguish defects, compatibility gaps, feature requests, questions, historical resolutions, duplicates, and non-actionable discussion. Do not invent work from praise or unrelated discussion, but do not silently discard any requested behavior.

2. Give each actionable item the attention of a focused task.

For each item, establish:

- What the user actually experienced or requested.
- The affected versions, settings, mods, pawn types, maps, and execution paths.
- The intended behavior and measurable acceptance criteria.
- Relevant earlier reports, attempted fixes, retractions, and successful or failed retests.
- The proposed implementation and its interactions with other work.
- The evidence required to call the item complete.

Use focused investigative, implementation, and independent QA assignments. Avoid handing a large unrelated collection to one reviewer and treating a broad approval as individual coverage. Investigators and reviewers must inspect relevant sources and code, not merely accept another agent’s explanation.

3. Investigate recurring problems before attempting another fix.

For any issue previously reported as fixed, determine why the earlier work failed to eliminate it. Distinguish an incomplete fix, a genuine regression, an old installed version, and a similar symptom with a different cause.

Prefer the actual failing reproduction and execution trace over a plausible theory. Examine executed jobs, their inputs, state transitions, reservations, item ownership, and outcomes. Include successful jobs that repeatedly make no useful progress.

When the available evidence cannot distinguish competing explanations, add targeted diagnostics or construct a discriminating reproduction before choosing a fix. Do not substitute another warning, exception suppression, cooldown, blacklist, or broad compatibility exclusion for resolving the underlying problem. Use containment where justified, but track it separately from a completed resolution.

4. Implement the full requested behavior carefully.

Fix confirmed HD defects, complete missing integrations, investigate named compatibility concerns, and implement supported feature requests with appropriate controls, defaults, documentation, and translations.

Preserve player intent, item conservation, save compatibility, performance, and existing supported behavior. Check automatic, forced, queued, interrupted, and resumed work wherever relevant.

Investigate compatibility against the actual mod or supported fork. Do not infer causation merely from a mod appearing in a stack trace, and do not consider removing that mod an adequate compatibility solution.

Evaluate pending PRs as proposed work. Reuse appropriate contributions after reviewing and validating them; an open, closed, or merged PR is not by itself evidence that the requested behavior works in the published mod.

Do not reject or defer an item because it is difficult, time-consuming, less prominent, or one of many tasks. If a literal request is contradictory, technically impossible, or would damage required behavior, investigate alternatives and document the evidence and chosen resolution. Such decisions require independent review and must remain explicit in the completion matrix.

5. Maintain a fixed quality standard throughout.

Apply the same completion requirements to the last item as to the first. Do not replace careful work with superficial patches, cosmetic tests, vague compatibility assurances, or “probably fixed” conclusions as the backlog gets smaller.

A shared-system fix may resolve several items, but each affected acceptance criterion still requires its own evidence. Shared infrastructure changes also require reviewing the other paths that depend on them.

Have independent QA challenge the diagnosis, implementation, and verification for each coherent change. Resolve findings, then obtain a fresh review of the corrected result. Passing a build or receiving an agent’s approval alone is insufficient.

6. Verify the actual user scenarios.

Where reproducible, demonstrate the original failure on the relevant prior build or an equivalent regression fixture, then verify the corrected behavior.

Use meaningful automated tests and appropriate in-game verification. Cover the runtime integration and the inputs reaching it, not only isolated helper functions. Exercise relevant settings combinations, supported mod integrations, multiple pawns, aged saves, save/reload, full application restart, and interruption or cancellation.

Use disposable saves and isolated test data. Do not risk the user’s actual colonies or overwrite unrelated work.

Do not claim verification that was not performed. Missing required gameplay evidence, inaccessible dependencies, and unresolved failures remain explicit unfinished work. Continue everything that can proceed independently; do not redefine completion to hide a blocker.

7. Let discoveries expand the plan.

As investigation, implementation, or QA exposes omitted requirements, sibling defects, incomplete integration paths, or regressions caused by this work, add them to the plan with acceptance criteria and ownership.

Reopen previously completed items whenever later changes invalidate their evidence. Keep the implementation, documentation, completion matrix, and test results consistent with the final code.

8. Finish with one comprehensively reviewed PR.

Keep the requested work in one integration branch and one PR, with reviewable commits and clear internal organization. Preserve unrelated local changes.

Before declaring completion, conduct:

- A source-by-source coverage audit against the complete feedback inventory.
- An independent final review of the integrated changes.
- Validation of interactions between the individual fixes and features.
- A recurrence audit showing how each repeatedly missed problem was addressed.
- A review of documentation, settings, translations, compatibility statements, and release notes against actual behavior.

The final PR must explain the resulting behavior, map every actionable feedback item to its resolution and verification, and state any evidence-backed limitations plainly. Do not bury incomplete work in the PR description.

Do not merge or publish the mod as part of this goal.

Completion means every source has been accounted for, every actionable acceptance criterion has been satisfied or received an independently reviewed evidence-backed disposition, all required verification has actually been performed, all QA findings are resolved, and the single PR is ready for human review.

Do not stop merely because a large amount of work has been completed, tests are green, or the remaining items are inconvenient. Continue until these completion conditions are met.