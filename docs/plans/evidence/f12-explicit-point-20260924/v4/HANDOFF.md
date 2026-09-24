# F12 explicit bare-point backend — source/build handoff

2026-09-24, queued_save_finish. This is the first implemented F12 backend slice; native execution, final UI, shelf admission and F12/F13 closure remain pending. Root owns Prepare, native execution, review, staging and commit.

The actual synchronized Issue/Resume/Cancel entry points now create per-pawn monotonic saved intents and one dedicated native hand-carry job. The total is conserved across bounded trips, source identity never retargets, native target-only equality cannot swallow distinct intents, and native reservation acquisition refuses forced fallback/stealing. Bare cells use current physical capacity; storage/provider cells are deliberately excluded until the named shelf-admission slice. Existing Keep and personal inventory are not intake or delivery authority.

`v4/source-slice.diff` is the complete 14-file before-relative change. Four shared files are compared against the exact pending versions captured in `before/`; ten files are new. The shared changes are only the Comp partial/deep-save hook, positive queued-save exemption, separate named MP registration, and new job membership in the custom-driver/no-recursion sets. F38 source and other pending work were not edited. `v4/v3-to-v4.diff` isolates the final scoped native split observation.

Quantity credit requires exact attempted = retained + observed delivery. Placement uses a whole known hands parcel, avoiding ThingOwner's hidden partial-count split. Exceptions preserve their original identity while exact detached stock is retained in a deep-saved private owner. Native cleanup drop/merge is recovery custody and never destination credit. Cleanup observes only the captured parcel's actual TryPlaceDirect attempts, and only exact resident receivers actually entered through native Thing.TryAbsorbStack. Receiver growth and parcel reduction must agree. Cancellation attempts a native Near drop for private recovery cargo; a false drop retains that exact parcel and a repeated Cancel may retry release without reviving intent. Queue capture/restore is distinct from queue clear; suspension retains the actual job. Missing/ambiguous load links block rather than creating jobs.

The final load hook drains only comps observed during that Game's PostLoadInit, after Game.FinalizeInit and before GameComponentUtility.LoadedGame. Weak Game keys and exceptional LoadGame removal avoid retaining abandoned games. Saving nulls destroyed references only in local serialization variables, without mutating current order state. Full physical completion remains terminal even if a callback subsequently throws. The scoped Thing.SplitOff postfix captures the native base result before ThingWithComps.PostSplitOff callbacks can hide it with an exception.

## Validation and frozen inputs

- V4 native-reference build: **0 warnings/errors**, 20.33 seconds, deployment disabled. V1 compile failure is retained; V2/V3 passing outputs remain.
- Focused net48 quantity/conservation tests: **11/11**, no skips. V4 retains the exact tested V3 Core helper/test bytes (`freeze-audit.json`). These tests prove the arithmetic boundary, not native job/save behavior.
- Audit: **657 frozen source inputs, 85 installed/reference copies, 121 clean runtime files, 14 owned files** verified. Runtime Product excludes Source, obj and build tooling.
- Product root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f12-explicit-point-v4-20260924/Product`.
- HD SHA256 `2CA99C7644F182A7FFF8D593ACE98398B07E3E805112F76D83606EF8D14E765A`; MVID `52ca19df-246d-4809-bc7a-52e5f13d75a0`.
- Core SHA256 `3F9BAEDBD9C7B5CFBE43D85AC11797300B608E052ECACD2FA94D17AEFBA8E3B3`; MVID `7f4a7c66-a012-4b43-ac41-9e981e8270c7`.
- Installed native SHA256 `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`.

Use `v4/selection.json`, `runtime-inputs.json`, `source-inputs.json`, `actual-reference-inputs.json`, `metadata.json`, `freeze-audit.json`, build/process logs and the retained V3 TRX. No native scenario has run and no Prepare has occurred.

## Source evidence and review corrections

The installed `ReachabilityImmediate.native.txt` resolves ClosestTouch and uses native touch/path rules; the driver now uses that predicate rather than geometric adjacency. Add a blocked-corner runtime control where a route exists around the wall but pickup must wait until actual touch is legal.

`f12-f13-next-scope-20260924/independent-native/{Pawn_CarryTracker,ThingOwner,GenPlace,ReservationManager}.cs.txt` provide the exact whole-parcel, Near iteration, false-with-partial-placement and forced reservation fallback boundaries. `f24-work-cadence-20260924/leavings-observer-review/Thing.cs.txt` lines1574–1635 and `f02-own-inventory-20260924/independent-review/ThingWithComps.native.txt` lines515–542 show physical merge and base-split/callback order. The exact split observer is limited to our active source/count/order scope; it does not promise recovery from arbitrary third-party replacement SplitOff implementations that never return/call the native base.

`f38-handoff-source-20260924/capacity-replay/load-counter-source/Verse.Game.txt` has FinalizeLoading at611, FinalizeInit at636 and LoadedGame at646. ExecuteWhenFinished would be too late for the pre-tick snapshot, so it is not used for reconciliation.

Actual installed CE `CompInventory.native.txt` under `f11-focused-audit-20260924` counts equipment, apparel and personal inventory; CanFitInInventory is backpack authority, not native hands. The hand-carry driver uses actual Pawn_CarryTracker.MaxStackSpaceEver and its normal provider patches. It does not call CE's inventory fit check or register hands as held personal stock. This is a source-grounded boundary correction, not a claim that CE runtime compatibility passed.

## Immediate next deliverable

Author the bounded native fixture against this frozen product: real7 of15; real100 total with native hands capacity forcing multiple trips; two distinct7/9 orders behind non-idle work; an original checkpoint containing genuine current hands cargo plus both queued IDs; fresh-process pre-tick identity/physical comparison followed by native completion. Use a real native item with destination stack capacity above100 (e.g. Chemfuel stack150 if the generated actor's actual hands bound is below100), never an overstacked source or manually set delivered count. If the native bound does not force multiple trips, fail setup explicitly rather than silently treating a single trip as success. Preserve original save/record bytes and selected images.

The same focused lifecycle follow-up must cover capture/restore versus queue clear, actual interrupted drop/merge, partial progress cancellation and retained canceled state after reload, the reviewed blocked-corner control, no-steal reservation control, source replacement/growth, unchanged Keep/personal stock, and the actual partial/throwing placement boundary. Do not create a broad mod matrix. Final menu/quantity/targeter/progress/Resume/Cancel translations, exact shelf/shared admission, actor profiles and supported MP/provider replay remain explicitly named later slices.
