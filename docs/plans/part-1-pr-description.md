## What this PR addresses

Collects the post-v1.24.0 feedback work completed so far, including the existing 20 branch commits, subsequent fixes/features, regression tests, runtime tools, translations and changesets. It fixes confirmed hauling, compatibility and persistence defects and preserves unfinished implementations for the next stage.

**This is a draft handoff PR.** The broad goal is paused. The catalogue covers 46 feedback groups / 78 sources after the 9 August 2026 release cutoff (plus active GitHub issues/PRs), last collected 19 September. The ledger distinguishes verified report resolutions from partial implementations and remaining combined-build/Multiplayer checks. It does not claim every report is fixed or that these changes have shipped.

Partial work is deliberately retained, including ordinary bill gathering, explicit hauling, transporter unloading and shared storage/cargo accounting. These candidates still need the named native, provider and input checks in Part 2 before merge/release readiness.

## GitHub issues addressed

| Issue | Fix and verification |
|---|---|
| #259 — delivery orders unexpectedly keep construction priority | Separate delivery intent from building. Native baseline failures and four candidate construction/delivery scenes verify HaulOnly, explicit building and ordinary work controls. |
| #263 — Big and Small sapient pawn hauling | Preserve HD's component on the provider's generated pawn definitions. Actual conversion, legacy-save upgrade, provider-absent control and fresh restart preserve cargo and pawn settings. |
| #266 — a second ordered hauler cancels the first nearby sweep | Hand off only the selected source; retain the first pawn's remaining sweep, cargo, queued work and claims. Verified native handoff, constrained storage and original-save continuation. |
| #269 — repeated refill trips with Storage Refill Hysteresis | Consult the provider's live refill gate in new and cached destination searches. Paired native tests verify closed/reopened/manual-latch behavior and existing delivery controls. |
| #271 — Periodic Bills compatibility | Compose the actual provider's repeat menu rather than replacing it. Both load orders, productive crafting, saved restarts and fallback boundaries are verified. |

Closes #259
Closes #263
Closes #266
Closes #269
Closes #271

#261 remains open: reporting guidance is addressed, but its stocking defect is not. The other open gameplay issues remain explicitly listed in Part 2.

## Steam feedback addressed

The [source map](https://github.com/Refzlund/haulers-dream/blob/codex/all-feedback/docs/plans/part-2-pr-source-map.md) links every individual comment and its evidence; the [ledger](https://github.com/Refzlund/haulers-dream/blob/codex/all-feedback/docs/plans/feedback-since-last-update.md) includes all in-scope comments and discussion posts.

- **vorshlumpf** — [gathering/H&H and clipped descriptions](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292709966): preserve new harvest attribution and kept/unrelated stock; make settings descriptions readable. [Robots, RIMMSqol, Custom Alerts and crafting](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292755974): add supported robot inventory/command access, preserve drafted and queued orders across saves, document activity discovery and own-stock crafting, and fix forbidden-inventory selection. [Enhancement requests](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292756958): add quantity-drop controls and explicit point/shelf hauling; their exact completion boundaries are in the ledger. [Robot construction acknowledgement](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592940620292789520): retain a verified construction regression case. Own-inventory refuelling remains partial.
- **HaPpY** — [refill report](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001556231), also #269: respect the actual SRH refill policy. Report-correction guidance is documented separately.
- **TripleZer0, Maya Fey and Triel** — [premature unloading](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001626310): distinguish an empty work search/native one-tick wait from finished productive work. Harvesting, grow-zone work and deconstruction are verified.
- **ErikRedbeard** — [sapient pawn compatibility](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265884141), also #263: preserve generated-pawn hauling components and saved state.
- **Richard Ramirez** — [blight cutting](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265812007): select eligible infected plants and refresh live route previews as infection changes.
- **Arthur GC** — [hauling after finishing wild animals](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939664045885677): observe supported Allow Tool/Keyz completion and append an eligible corpse haul without replacing prior work.
- **talias**, with **I'm A Giraffe's** clarification — [Build From Storage](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265729951): exclude packed-building install blueprints from raw-material route queries; verify native installation followed by ordinary construction.
- **nullpat** — [cloth/workgiver warning](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043335749): remove the query counter that incorrectly inferred completed hauling and caused HasJob/JobOn disagreement. Actual physical hauling loops remain separate open reports.
- **Steam discussion reporting reply** (author not retained in the capture) — [reporting access](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/582804662258990241/#c418424310691114229): document the in-game reporter and public GitHub alternatives. The reply's gameplay exception remains open.

## Contributions and recurring causes

Adapts **flixzf's #148** orphaned-bill repair, retaining unfinished work/ingredients, reciprocal references and unrelated jobs across repair and clean restart. Adapts **nullpat's #267** transporter bulk-unloading contribution, with ownership, transfer custody, saved recovery and load/unload coordination. Both retain attribution in their changesets; #267's remaining shuttle/VF/presentation checks are in Part 2. Neither contributor PR is represented as separately merged.

The recurrence review identifies repeated menu replacement as the underlying Periodic Bills integration pattern, query counts being mistaken for executed work, missed work-scheduler transitions, and separate settings-save/colony-save queue lifetimes. It preserves unproved or retracted historical diagnoses instead of presenting them as fixes.

## Validation and continuation

- Combined source build and runtime-harness build: zero warnings/errors. Core suite: **3,037 passed, zero failures/skips**. All 13 repository guards pass; ownership-guard mutation controls reject all four deliberately unsafe variants.
- Focused native runs retain actual assembly hashes, physical item accounting, original save identity, process receipts, expected failures and independent review. Quantity-drop recovery verifies five exception boundaries and all 387 items stable for 302 ticks; one declared diagnostic is preserved.
- Graphical tests run on an **inactive private Windows desktop**, with separate runtime/save/mod directories and protected-file checks. They do not switch the player's input desktop.
- Report-specific successes do not stand in for final combined gameplay or actual two-client Multiplayer testing. Those and all unfinished feedback remain in the [Part 2 handoff](https://github.com/Refzlund/haulers-dream/blob/codex/all-feedback/docs/plans/part-2-handoff.md), with ordered TODOs, exact runtime procedures, accepted evidence, source/archive locations and common failure patterns.

Changesets are included. This PR does not merge, publish a release or update the Workshop.
