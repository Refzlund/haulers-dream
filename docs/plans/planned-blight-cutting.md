**Current status — resolved for the reported single-player misdesignation.** The accepted baseline/candidate boundary comparison and [native productive/preview run](evidence/f34-live-20260920/runtime-review.md) establish the correction. [Targeted final integration](final-integration-checks.md) retains Multiplayer replay and ordinary button-input checks. Dated checkpoints below preserve the earlier work and do not override this status.

# Planned cutting of blighted crops — F34

Status: implementation candidate applied and independently reviewed. The actual prior-build run reproduced healthy-plant selection/designation/cut-job failures. The candidate native run reports377 passing assertions, no failures and no captured Unity errors. Independent full comparison and the remaining real UI/productive-cutting/live-preview checks are pending; F34 remains open.

Source: [C027, 13 August 2026](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938395265812007). The player reports that using planned prioritized cutting to remove blight also marks healthy crops for cutting. This is inside the confirmed post-release scope.

## Cause and recurrence

Native CutAllBlight marks infected plants. HD's generic SameDefOrDesignated cutting selection then expands to healthy plants of the same crop, or other crops already marked for cutting. Designations are added before job eligibility is checked, so a job-only filter would still leave unwanted healthy-plant orders behind. Manual picks and Vein selection need the same intent check.

The broad selection rule exists in the initial route implementation and v1.24. No specific earlier fix for this blight report has been established. Classify it as a longstanding omission, not a demonstrated regression from an earlier successful fix.

## Implementation and review correction

Capture blight-only intent when resolving a cutting order from an infected anchor. Carry that intent through selection, preview, remembered commands and synced confirmation. Re-resolve at confirmation and check each target before designation/job creation. Preserve independently existing healthy-plant orders.

Poll selection for an open blight plan every 0.5 seconds of real time, including while paused. Recompute expensive route legs only when selection or existing planner inputs change. Revalidate plant blight/designation state before reusing a same-tick resolver entry.

Initial independent review found that requiring equal blight flags for secondary crop selection would restrict ordinary clearing. The applied correction is asymmetric: a blight plan requires a blighted secondary target; an ordinary plan accepts either state. The original proposal is retained separately. No new saved schema, job definition or user-facing string is introduced.

## Required verification

- Demonstrate the original unwanted healthy target/designation on the prior build using native CutAllBlight and mixed rice/corn, including healthy plants with existing cut orders.
- Verify all offered plant modes (Radius, Chained, Vein and Zone when available) and manual/secondary picks exclude healthy plants from a blight plan; healthy plants must not connect a blighted Vein. Plants do not offer Rooms; a direct invalid Rooms request must coerce to the allowed default.
- Confirm productive infected-plant cutting and unchanged healthy survivors. An empty route alone is not success.
- Verify new infection, destruction by another worker, anchor removal and replacement plants while a dialog is open and before confirmation. Preserve complete designation/job deltas.
- Exercise Append, Replace, remembered commands, supplied plans and AppendStops. Rejected stale intent must preserve existing queued work.
- Prove ordinary clearing still accepts healthy and infected secondary crop types and can deliberately cut healthy plants. Check unaffected harvest, mining and construction routes.
- Measure real selection/pathfinding calls: unchanged blight previews must not cause pathfinding every frame, and ordinary routes must not acquire the new polling.
- Verify same-tick designation/blight changes invalidate plant resolution, while unchanged plants and non-plant probes retain cache reuse.
- Verify actual preview UI, native queued-work lifecycle, cancellation, save/reload and Multiplayer replay of the changed command signature on the integrated candidate.

Native inspection found no ordinary Core operation that cures the same still-spawned crop after dispatch. Preserve normal native queued-job semantics; explicitly label debug blight removal as fault injection rather than claiming it is natural healing. Spread, other-worker removal and cancellation remain real required cases. Any later evidence of a supported cure integration must be assessed explicitly.

## Evidence

The frozen proposal and native lifecycle analysis are under `C:/Users/Arthur/AppData/Local/Temp/haulersdream-f34-blight-preview-author-20260920`; proposal manifest SHA256 `378E9757B464CD69C5D727D17032A9E721B0E3DEB3EECEBE04925807B06702CF`.

The applied correction and pre-edit snapshots are under `C:/Users/Arthur/AppData/Local/Temp/haulersdream-f34-asymmetric-root-20260920`, source manifest `22BFD90B7560E749D37CEB49574D6F3E2934BC8EFB30DF637CB100A7F31387CE`. The complete original review `D7F54828` retains its ordinary-clearing finding; fresh corrected-source review `1A22FD8D70F6FE739DD16F06468B402DF6B5BB9A72C9419FBAB73589B9E25C92` approves the actual applied bytes for compilation. Root read and adopted both reviews.

Actual isolated build `7271FFAF` under `C:/Users/Arthur/AppData/Local/Temp/haulersdream-f34-product-root-build-20260920` completed as owned process21296, joined exit0, with zero warnings/errors. Root review `3CAB3999` verifies all423 selected source/content copies, unchanged workspace/deployed assemblies and complete build streams. Relative to the earlier F45 product build, only the five reviewed source files differ. Output HD SHA256 `32D57A64B3B35E1DF652BA66BE2BEECE923C60B42201955E31B4C587F7385C88`; Core `D623F164CEB112A8BB702F907CE0C6442488A19C074045047190AD36F09A6F3A`. Nothing was deployed. Compiled/PDB review and all gameplay requirements above remain pending; a focused native fixture is being authored.


### 20 September: native boundary fixture compiled

The eighteen-scene native fixture now compiles as a standalone companion. Independent review1987E0FE found that requesting Paused without reading it back could falsely claim a paused test; corrected sourceCF21FFCE/2886F178 requires actual PlayerHasControl, verifies the public speed setter, records real speed/tick and restores only acquired state. Fresh source approval58B66EC1 and separate root recipe approval08692656 were adopted.

Actual build261CC012 completed restore8040/build30080 joined0 with zero warnings/errors. DLL `F47BDA34FF0F1E124BB5206F20711789A7551C173EDB019ADCA1DCF4EB2C6D75`, PDB `FAC9A5172DEFCA73FED83445503999648C741EDDC22779571F8B5761417D3BF8`; output directory `C:/Users/Arthur/AppData/Local/Temp/haulersdream-f34-mixed-boundary-root-build-20260920`. Root actual review97ACAFF6 checks385source/reference/package copies,4120equal before/after inputs, complete streams and unchanged protection. Independent compiled/PDB/API review is underway. Minimal explicit-case host/controller integration is being authored with real player-control waiting. No F34 native scene has run; all original product acceptance requirements remain open.


## Matched causal baseline and host — 20 September

The originally proposed9956baseline differs from the candidate in unrelated refuel/command/menu work. It is superseded for causal F34 comparison. The fresh baseline copies all423candidate inputs and reverts only the five retained pre-F34files;418inputs and all125Coreinputs remain identical. Independent source/recipe review5EA07752 and actual successful build97E5B35A/rootreview72201A20 establish that selection. Actual baselineHD42766C0D/Core30F1CA32 and the candidate now have independent compiled/PDB/API approval10FEDF6A, fully read and adopted by root. This confirms the comparison prerequisites, not native behavior; runtime binding and the actual comparison remain pending.

The separately built F34hostA0A5474E/PDBC9DFC56D also succeeds with zero warnings/errors under source approval45388B8B, recipeB23782E3 and rootactualreview2B1D6B6D. Its companionF47BDA34 retains independent compiled approval0EFC2058. No native F34scene has run.

Controller review098289EA requires measured matched-baseline identities/counts and complete evidence census. Source-only successor0ADAE869 retains all336ordered companion assertions,3hostIDs and116snapshotmultiplicities. Independent source approval18C2F0B3 has been fully read and adopted; 37 synthetic reader controls per shell are authored but unexecuted. Measured role binding and both-shell control execution remain pending. Baseline failures must remain failed; productive cutting, UI, live preview cost, save/restart and Multiplayer acceptance remain open.


## 20 September: actual matched native comparison

Baseline36355997 (native16972) and candidateca489507 (native19012) both joined normally with exit0. Baseline has303assertions/31failures: native CutAllBlight initially marked only infected A/B/E/H/I, then HD newly marked healthy riceC40949 and queued CutPlantDesignated job18 for it. Thirteen scenes completed; the14th reached the expected old same-tick cached-null failure, leaving four unexecuted. Candidate reports377/377passing. These are paused selection/designation/job-boundary observations, not completed plant-cutting or rendered-UI evidence. Independent candidate/baseline review is underway.

[Actual results, events, complete logs, manifests, profiles and native outcomes](evidence/f34-20260920/retained-files.json) are retained under the workspace, with14byte-identical files (4,589,741bytes). This avoids relying solely on disposable TEMP directories. Original failed evidence is unchanged. No baseline rerun is necessary to demonstrate the reported misdesignation.

## Independent boundary acceptance and focused next check — 20 September

Root read and adopted [the full comparison review](evidence/f34-20260920/review.md): all18 candidate scenes pass; the prior build reproduces healthy-plant designation and native cut jobs. The existing successful suite will not be rerun without a relevant change. A single productive-cutting scenario now compiles inside the existing test host with zero warnings/errors. It opens the real owned dialog, confirms programmatically and observes real native cutting. Source review and execution are pending; no actual button-input result is claimed.

## Native productive and live-preview acceptance — 20 September

Run `6b1cbb6cd7a046c9923bb767305dc1d4` (PID3180) joined normally with exit0. The current source-matched product HD32D57A64/CoreD623F164 passed52 assertions with no failures or captured Unity errors. All102 events are retained. Native preview polling at paused tick6 reused unchanged legs, then rebuilt the exact target set five→four→five after explicitly injected B infection removal/reinfection. Original native cutting jobs17–21 were observed working and completed by tick421; all five infected originals were destroyed before cleanup, C/G remained alive and unmarked, D/F retained their independent orders. Root inspected the actual rendered dialog screenshot; confirmation was programmatic. Independent runtime review accepted this evidence with the earlier18 scenes.

The generic Verify result remains `not-verified` because it requires semantic review, expects the generic `scenario-observed` event and flags two pre-start Mono fallback notices. The specific F34 events and full independent review supply the missing semantic assessment; no rerun or controller-label change is needed. Protected input comparison found no changes. Full logs and the original Verify result remain preserved, with their limitations. No new saved schema or native job serialization was introduced; broad restart matrices are not prerequisites for this report. The changed Multiplayer boolean remains a focused final integration obligation.

The five product files and one-line changeset are recorded in integration-branch commit `59292db` (`fix: keep blight cutting plans limited to infected plants`). Other in-progress changes remain unstaged and preserved. No PR, merge or release is claimed.
