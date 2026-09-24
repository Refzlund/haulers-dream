# F24 — finish productive work before returning to storage

24 September 2026. The received premature-storage-trip reports C003/C004/C022 are resolved in commit `137a905`. The correction leaves capacity, interval unloading, medical checks and ordinary downtime policy intact.

Two scheduler boundaries had incorrectly been treated as the end of work. An empty emergency-only work search does not mean the later ordinary work search is empty. Likewise, RimWorld's one-tick Wait_MaintainPosture after a successful job is a transition before the next work search. HD now waits for the ordinary work decision instead of inserting a storage trip at those boundaries.

The retained [native baseline](f24-work-cadence-20260924/native/23dff1e0a30a45639c918378987da499/raw/result.json) demonstrates a premature unload during the one-tick transition: eight of17 harvest targets finish,80 cloth enters inventory, then the idle backstop queues a storage trip with nine plants remaining. The original emergency-only expectation failed and remains preserved; the actual trace identified the second boundary. The narrow candidate corrects both boundaries.

The final [independent runtime review](f24-work-cadence-20260924/native/4e2e01ba4d1a4695b02b1f94064ca7c4/independent-runtime-review.md) accepts59/59 assertions and653 events with zero captured Unity errors:

| Actual work | Observed result |
|---|---|
|17 cotton plants, zero unloading grace | Three native harvest sections of8/6/3;170 cloth stored after productive work finishes. |
| Grow-zone tree clearing | Two ordinary GrowerSow jobs cut two trees and their actual native stumps;49 wood stored; both replacement crops sown. |
| Deconstruction | Two ordinary wooden-wall jobs produce and store exactly6 wood. |
|17 cotton plants, default timing |171 cloth stored; final unload starts3244 ticks after pickup, respecting the unchanged2500-tick grace. |

No storage unload starts while productive targets remain. The review checks all86 changing physical snapshots,126 live eligibility/gate samples, actual job identities, conservation and cleanup. Stump output is tied to the actual native returned/queued objects, not an inferred balance or fixed yield. Earlier fixture accounting failures remain recorded. All parked stock returns exactly; private processes join without switching desktops and protected files remain unchanged.

The [three-file selection review](f24-work-cadence-20260924/commit-review/independent-selection-review.md) accepts only the two scheduler changes and release note. Both complete selected C# files equal the tested source after newline normalization. The isolated slice builds with no warnings/errors; the [commit audit](f24-work-cadence-20260924/commit-review/commit-audit.json) verifies exact reviewed bytes. A separate native run of that smaller isolated assembly is not claimed.

Recurrence assessment: older work-continuation changes did not cover every scheduler boundary. This trace identifies a transition mistaken for genuine downtime, rather than proving all similar earlier reports share one cause. The unavailable historical reporter save is not reconstructed by this fixture.

**Separate unresolved observation:** run9d927540ecb04375ab8bb44fbce48d36 finished all productive targets but left91 cloth in inventory. This was stranding, not a premature trip. The later diagnostic and final runs do not reproduce it; neither proves a medical cause or fixes that observation. Its original evidence and follow-up remain in [open diagnostic leads](open-diagnostic-leads.md). Final assembled-build work/cargo checks remain in [final integration](../final-integration-checks.md). No merge or release has occurred.
