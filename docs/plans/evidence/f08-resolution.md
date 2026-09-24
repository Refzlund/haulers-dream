# F08 — finding the inventory-gathering activity in Custom Alerts

**Resolved on 20 September 2026 as supported behavior with verified setup guidance.** Source: C424, subject S04, the supplied Vorshlumpf comment. The requested activity is already exposed through TD Find Lib's current-action selector. Its default available-only list hides a job when nobody currently performs it. **Current action → ALL OPTIONS → gathering items into inventory** selects the stable `HaulersDream_BulkHaul` identity. No new WorkGiver or product scheduling hook is required for this support request; the separate RIMMSqol request remains F07.

The player-facing setup is documented in the main README and COMPATIBILITY guide, committed as `de9f649`. The guide explains that the selector matches current inventory gathering, including explicit single-stack pickup. Queued orders, separate unloading and native hand-hauling do not match. It also identifies the old activity label and unchanged saved selector identity.

## Actual evidence

- [Current-package contract review](f08-alert-integration-review.md): actual Custom Alerts Continued, TD Find Lib Continued and TDS Bug Fixes Continued packages acquired on 20 September, including their native load-folder selection. The reporter's exact installed version is unknown.
- [Producer acceptance](f08-alerts-20260920/producer-v4-runtime-review.md): actual run `bfd06365cfae49749406579ffb90dd78`, native PID 21908. Both native UI pictures, 460 scheduled alert evaluations, real queued handoff, productive gathering, interruption, completion, single pickup, native hauling and exact material accounting passed independent review. Seventeen distinct F08 assertion IDs apply; repeated frame observations are not extra test cases.
- [Restart acceptance](f08-alerts-20260920/restart-runtime-review.md): `79c6457ddcd54ab887b0b663dece4967`, native PID 14404, loads that producer's actual save and checkpoint. Its LoadedGame callback occurs at saved tick 4049; the paused pre-fixture check is tick 4050, after the native PauseOnLoad tick. Before fixture mutation it finds the original pawn, sources, stockpile, stable JobDef, enabled/count/delay/map settings and one live registration. All 108 actual scheduled observations agree with current activity. The alert changes inactive → active → inactive while actual gathering/unloading stores all 40 units. Its five distinct F08 checks pass and no captured Unity error occurs. Root read and adopted the complete independent producer and restart reviews.

Both actual native processes and owning controllers joined with exit zero on inactive desktops. The input desktop stayed Default, no switch occurred and their owned jobs were empty. The original producer/restart results, logs, events and controller outputs remain intact. Generic Verify review flags are assessed against actual evidence; no raw result is rewritten to force automatic acceptance.

The three earlier producers remain failed: a cross-frame temporary-window assumption, a queued-from-idle assumption, then genuine map-Lord/world-pawn fixture ownership errors. None establishes an HD activity-selection defect or substitutes for the successful producer. The successful generated scene had no non-null Lord membership to release, and does not claim runtime coverage of that fixture branch.

## Recurrence and remaining integration

The evidence establishes an available-only discovery issue and accurate supported configuration. It does not establish recurrence of a previously repaired HD defect, nor verify the reporter's unknown historic installation. F10 separately corrected the shared activity wording without changing JobDef identity.

No report-specific work remains. Final assembled-build validation should make one compact check that this saved selector still finds the gathering job alongside the final F07 changes and F10 label. Physical interaction can be checked there without exposing a test window on the user's active desktop. Do not reopen a broad alert, language or mod matrix solely for this support disposition.
