# F45 / GH271 — report resolution

**Resolved on 20 September 2026 in commit `cb61d20`.** The published Hauler's Dream menu suppressed Periodic Bills' repeat mode. The correction preserves the actual creator's options and delegates, adds HD batch choices for the same bill, and clears batching only after an accepted ordinary selection returns normally. The original menu remains usable if the inspected action contract cannot bind.

Root adopts the independent [finite closure scope](f45-report-closure-review.md) and the following completed reviews. No required report-specific check remains. Final presentation, multiplayer completion and assembled-build checks remain explicit in [final integration](../final-integration-checks.md); a later regression reopens the report.

| Required evidence | Accepted result |
|---|---|
| Original missing option | PB alone supplied the option; the published HD build suppressed it. The setup and later fixture-oracle failures remain retained separately. [Baseline review](f45-menu-20260920/runtime-review.md). |
| Corrected native menus/actions | Both PB/HD orders passed 18 cases each; the actual native creator passed 16 cases. Original delegates, cancellations, rejected selections and batch transitions were checked. [Candidate review](f45-menu-20260920/candidate-review.md). |
| Productive scheduling | Eight real native crafting iterations through HD gathering, exact ingredient/product accounting, simple and bulk recipes, suspension and cooldown. Deadline jumps are labelled; this is not an uninterrupted day simulation. [Production review](f45-production-20260920/runtime-review.md). |
| Existing providers | All 52 native EGO/CL, PB-winning and IT-winning cases accepted; actual provider actions, guards, setup and unknown contributions preserved. [Shared-provider review](f45-shared-menu-20260920/runtime-review.md). |
| Saved state and restart | Four separate native processes loaded actual partial-cycle/cooldown checkpoints without reseeding PB state; 210 assertions and six further native completions preserved exact accounting. [Restart review](f45-restart-20260920/runtime-review.md). |
| Composition/action boundaries | Eleven cases, 167 operation checks and 127 events: nested/exceptional scopes, zero/two menus, output idempotence, unrelated bills, contained failure and original-action return/throw semantics. [Boundary review](f45-boundaries-20260920/runtime-review.md). |
| First-call binding refusal | One fresh-process case, 12 checks and 26 events: genuine contract rejection rolled back attempted HD observers, preserved foreign patches and retained usable original options/actions. The expected warning remains recorded. [Refusal review](f45-contract-rejection-20260920/runtime-review.md). |
| Copied-bill attribution | PB alone reproduces the same 7/3 → 1/1 settings loss after clone initialization as HD+PB. This is an existing upstream limitation, not a repaired behavior or remaining HD menu defect. [Comparison review](f45-pb-clone-20260920/runtime-review.md). |

The recurring cause is the exclusive menu-replacement design retained through earlier Everybody Gets One, Compositable Loadouts and Ingredient Threshold repairs. Preserving the actual contributed options removes that assumption. This is a recurring architectural omission across integrations, not proof that Periodic Bills itself had previously been fixed and regressed. See the [diagnosis and prior fixes](../periodic-bills-compatibility.md).

The change does not repair PB's upstream clone identity handling or claim universal support for arbitrary patched action bodies. It does not synchronize foreign PB/IT actions. Source/compiled review and actual supported-provider evidence justify this report's closure; the final PR must still validate the changed HD multiplayer completion route and the assembled image. No issue was closed publicly and no mod was released.
