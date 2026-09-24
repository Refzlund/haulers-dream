# Part 1 validation, 24 September 2026

The collected development source builds successfully; this is not a release certification or a claim that all remaining feedback is fixed. Report-level native results and limits are recorded in the [ledger](feedback-since-last-update.md), [source map](part-2-pr-source-map.md) and [handoff](part-2-handoff.md).

| Check | Result |
|---|---|
| `dotnet build Source/HaulersDream.sln -c Release` with an explicitly absent `RimWorldModsDir` | Passed, zero warnings/errors; includes final opaque prompt and native shelf walkability corrections. Uses the repository's pinned reference package. No live mod deployment occurred. |
| `dotnet test Source/HaulersDream.sln -c Release` with the same deployment guard | 3,037 passed, zero failed/skipped. Core/test sources were unchanged by the final presentation-only update. |
| Actual-game runtime harness build | Passed, zero warnings/errors. The base harness and isolated native fixtures use separately reviewed actual installed-game references. |
| All13 repository guards | Passed. Translation keys/placeholders match across English and15 translations. Settings-drift emits10 nonfatal documented visibility/legacy-field notices. |
| Updated ownership guard | Actual source passes; four private-copy mutants fail as required: hosted-robot admission, another host-faction read, missing JobOn faction refusal, and missing prisoner host check. Restored source passes. |
| F11 quantity drop | Report-specific closure accepted: UI63/63, original-save restart45/45, providers82/82, final fault recovery135/135. All387 units and original intent/settings remain stable302ticks; one declared diagnostic, zero unexpected captured errors. |
| F13 current capacity correction | Same-host controlled baseline fails45/48 at the intended truncated-zero boundary; candidate54/54 passes actual selected-shelf delivery/shared-claim deduction and300-tick stability. Original passability failures remain failed. Basic lifecycle/save and provider/UI evidence is separately recorded. |
| F12 current prompt correction | Root inspected actual human Italian and robot screenshots: opaque prompts render above the native controls and all lines are legible. Corrected robot run passes56/56. Human held-Shift input remains unverified; the separate checkbox companion reaches the control but fails its queue-state postcondition94/96. Native polled input was not captured, so no product cause is asserted. Remaining human UI controls were not reached. |

Graphical launches use `scripts/run-on-test-desktop.py` and the family controller on an inactive private desktop, with private game/mod/save directories. Actual selected assembly hashes/MVIDs, original save identities, event sequences, physical items, process joins and protected-file verification are retained. The input desktop stayed Default. No foreground input or desktop switch was used.

Original raw TRX/build/guard logs and runtime captures remain machine-local under `docs/plans/evidence/` and the precise roots in the handoff. Published summaries and fixture sources do not replace those raw captures for another independent audit. See the [publication index](part-2-evidence-index.md) for the source-only archive and retained-evidence boundaries.

Actual two-client Multiplayer, final combined-product native interactions, and all open report work remain Part2 obligations. A successful isolated slice is not evidence that every changed component has been exercised together.
