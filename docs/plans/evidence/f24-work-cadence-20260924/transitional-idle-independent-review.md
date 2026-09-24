# F24 transitional-idle diagnosis: independent review

24 September 2026. Reviewer: `queued_save_finish`. Scope: the actual trigger in retained native run `23dff1e0a30a45639c918378987da499`, the narrow checkpoint correction and the next fixture's causal expectation. No product/fixture edits, Prepare or native launches by this reviewer.

**Accept removing only `Wait_MaintainPosture` from `HaulersDreamGameComponent.IsUnloadCheckpoint`.** Retain the existing empty-emergency-scan guard. These address two separate boundaries; this run demonstrates the former, not the latter. It is not a candidate pass or a complete F24 acceptance.

## Actual native evidence

The result remains failed, with 44/47 assertions passing. Failed assertions are `f24/baseline-actual-trigger`, `f24/complete-work-before-storage` and the host's corresponding failed-return assertion. The six preserved background stacks and empty temporary holder pass restoration checks. Owned game PID4752 joined with exit0; the behavioral failure is retained rather than relabeled.

The setup event specifies grace0, interval0, opportunistic false, native capacity35 and gear mass0.8, with maximum possible output mass4.862. There is no full-pack justification. Native events give this exact chain:

| Event | Native state |
| --- | --- |
| 93, tick1250 | Actual SelfPickup23 succeeds; nine productive targets remain. |
| 94–95, tick1250 | Native Wait_MaintainPosture40 starts with queue0; all80 cloth are in inventory. |
| 96, tick1250 | `CheckIfShouldUnload` is called with forced=false, behind=false and immediate=false during that transition. |
| 97, tick1253 | Wait_MaintainPosture40 succeeds with queue1. |
| 98, tick1253 | Unload41 starts from the actual native queued-job node. Nine targets still remain. |
| 100–104, ticks1570–1596 | Both stacks physically reach storage; actual Unload41 succeeds, stored80 and remaining9. |

The full event stream contains no work scan between that pickup and this unload. Its only work raw results are the initial empty emergency and selected normal Harvest17 at tick8; there is no returned-work unload event for Unload41. The debug log independently records the nonforced queued unload. The v4 assertion claiming that this exact job came from an empty emergency scan correctly fails. Its description must not be treated as proof of that claim.

Retained native `Pawn_JobTracker.EndCurrentJob` (`f07-rimmsqol-20260920/blocked-queue-v13/native-source/Pawn_JobTracker.cs.txt:419`) explains the observed boundary: after cleanup, absent an earlier error/finalizer branch, a successful non-Goto/non-Wait_MaintainPosture job with a stationary path follower starts `Wait_MaintainPosture` with duration1 and returns before `TryFindAndStartJob`. Finishing that transition subsequently runs job selection. This is a work transition, not evidence that ordinary work is exhausted.

The old `IsUnloadCheckpoint` explicitly admits that def. The component calls the idle backstop at multiples of250 ticks, including1250. Its checker enqueues an unload ahead of the next ordinary work scan. Given the actual call flags, interval0, queue0 and source, attribution to this idle-backstop branch is supported. V4 did not record an explicit caller-depth marker or checker-exit queue-ID receipt; the next fixture can bind that causality directly.

## Narrow impact and next expectation

The reviewed working-source correction removes this one admitted def and explains the native transition. Normal Wait, Wait_Wander, GotoWander, no-current-job and eating/joy checkpoints remain. Draft/medical/queued-work/defless guards remain. Direct, full-pack and interval checker calls are unchanged, as is normal empty-work unloading. A true idle pawn reaches the ordinary scan/filler path after the transition; a productive pawn gets the opportunity to find its next work.

The earlier emergency guard is still necessary: after the transitional idle branch stops preempting selection, an empty emergency-only work search still cannot establish that ordinary work is exhausted. This run does not by itself prove the emergency correction at runtime, and the new candidate must preserve the fixture's productive continuation, physical storage, conservation and default-setting assertions.

For the next fixture version, capture the checker-entry current job/queue and checker-exit newly queued unload ID while the actor is in this transition with remaining work. Ideally scope that receipt to the actual idle-backstop call. The baseline causal assertion may accept either (a) that exact transition-created queued job, later observed starting and completing prematurely, or (b) the exact job returned from a recorded empty emergency scan. Require the causal ID to equal the physical premature unload's ID. Do not weaken the assertion to any premature storage trip, suppress the failed behavior assertion or change this v4 evidence. No expanded matrix is required to establish this specific correction.

Reviewed working GameComponent SHA256: `0BA4A4A2B1800FF4BBA88186ADB8B86858547688DACD6B6AB14D1E69D8FFD86C`. This is source acceptance; the older frozen candidate still contained the old checkpoint when inspected, so a newly frozen built candidate requires its own recorded input identity.

Evidence SHA256: events `252BAE9D6255B1C96022D21088E224CB810F3A1AAFC6E8A2785B0DBD0D9312FC`; result `9593268B73D685F1CDED5E26C322FE847F1A8D0CFD4F5E50F76765D7FD934FFD`.
