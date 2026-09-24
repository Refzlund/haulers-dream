# F33 / GH259 — report resolution

**Resolved on 20 September 2026 in commit `14ce252`.** A forced native Hauling order previously selected HD's build-tethered delivery job even when Construction was unassigned. A HaulOnly route separately left native sustained Construction priority active. The two-file correction distinguishes the actual scanner's work type and gives replacement HaulOnly construction routes finite native dispatch without that sustained priority.

Root adopted the independent [final candidate review](f33-delivery-20260920/candidate-runtime-review.md). All four native scenes passed, with 69 assertions and zero captured Unity errors: unassigned delivery leaves frame work untouched; native Construction and explicit HaulBuild still complete real work; HaulOnly does not force a continuation, while a normally assigned pawn can still choose nonforced Construction. Exact materials, enroute release and cleanup were checked. No report-specific requirement remains.

The [failed baseline](f33-delivery-20260920/runtime-review.md) reproduces both causes and remains failed. The [environmentally interrupted candidate](f33-delivery-20260920/candidate-environment-review.md) is also retained, followed by a reviewed owned-arena correction and the complete passing candidate. None of those original results was rewritten. The generic verifier's manual-review and inherited event-marker flags are explicitly assessed in the final review.

This is a related intent/permission mistake, not proven recurrence of an exact prior GH259 fix. Earlier capability/assignment repairs did not establish that forced delivery was correctly distinguished from construction authorization. [Diagnosis and history](f33-delivery-intent-review.md).

One targeted assembled-route/Multiplayer interaction check remains beside F34 in [final integration](../final-integration-checks.md). Existing queues/Append, other route kinds, explicit build authorization and ordinary nonforced work are preserved; the change does not globally block Construction. The reporter's entire 194-mod installation was not executed. No public issue was closed and no mod release is claimed.
