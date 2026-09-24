# F38 — cooperative explicit haul handoff

Resolved24 September2026 in commit `9549cad` (`9549cad452a5b02e1863f3229c037efe74a1fd2d`). GH266 reports that a second explicit haul cancels another pawn's entire nearby sweep. The reproduced cause is native forced reservation displacement ending the existing job, rather than just relinquishing the selected item.

An exact, shape-checked reservation wrapper now hands off only the selected source. Current, future and queued pickup plans retain their original jobs, queue positions and remaining work. Retired entries keep their indices as Invalid/zero, so saved cursor positions remain meaningful. Held cargo, Keep quantities and destination capacity commitments remain protected. Incidental extra pickups cannot force-cancel another hauler's reservation. Unsupported native cancellation paths retain their original behavior.

The reporter proposed incremental reservation. The implementation satisfies the requested cooperation without replacing the entire planner. It introduces no new setting, menu or localization key. The patch changeset explains the resulting behavior.

| Requirement | Accepted evidence |
|---|---|
| Original whole-sweep cancellation | Retained baseline `ca6…` fails the current-source witness with the original job interrupted; setup-only failures remain separate. |
| Current/future/queued handoff; no stealing extras | Native candidate `c19dba9d9f0b4b79a1710bec0b73f725`,59/59,U0; actual source ownership, original jobs/cursors/queues, unsupported and failed-reservation controls. |
| Saved retired orders resume | Original-save restart `2425bb6f55af4f119538967085b1325a`,45/45,U0; original current job and ordered queue finish,107 stored+7 kept Cloth and36 Uranium preserved. |
| Shared capacity reflects actual held surplus | Producer `bd616fc07307445084fcdd05a450ff39`,54/54,U0; effective capacity76→51, actual next order32 and final150 stored+7 kept+25 personal+8 ground. |
| Repeatable mutation from the same saved input | [Fresh-process pair](f38-handoff-source-20260924/load-replay-v4/ROOT-PAIR-REVIEW.md),51/51 and64/64,U0. All11 raw command boundaries plus189 native simulation receipts match; actual physical outcomes remain stable300 ticks. |
| Source and exact commit | [Source reconciliation](f38-handoff-source-20260924/source-integration-review/RECOMMENDATION.md), focused25/25 managed checks and independent implementation/compiled reviews. Only the reviewed seven-file slice was committed; unrelated pending work is excluded. |

Earlier replay failures remain preserved and explained. Native map decompression necessarily allocates new object IDs; a no-load producer is not an identical loaded-state reference. Two subsequent fresh loads also exposed ambient native RNG as a missing simulation input. The final separate fixture supplies and records that input through actual native ticks, with exact outer restoration and no counter/state normalization. This is bounded local replay evidence; actual network acceptance remains in final integration.

Recurrence assessment: earlier #160 work addressed path-time skipping; F07 addressed queue authority. Neither corrected native forced reservation cancellation. Similar visible cancellation does not establish a regression of those earlier fixes. This change addresses the observed causal boundary directly, without warning suppression, cooldowns or broad mod exclusions.

The reported196-mod historical save is not reproduced in full. Supported source handoff is deliberately bounded to the verified native hauling/take-inventory and forced bulk-anchor routes; no arbitrary foreign job-driver compatibility promise is made. Final combined-build and actual Multiplayer checks remain explicit and can reopen this resolution if they reveal a regression.
