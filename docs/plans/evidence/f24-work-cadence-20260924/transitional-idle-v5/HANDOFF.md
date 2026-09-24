# F24 v5: native transitional idle and work continuation

24 September 2026. Source review, bounded fixture update, product/host builds and image metadata reads are complete. **No Prepare or native launch was performed.** Root owns the native slot and independent runtime acceptance. F24 remains open.

## Actual finding and scope

`../transitional-idle-independent-review.md` reviews retained failed v4 run `23dff1e0a30a45639c918378987da499`. Its actual SelfPickup23 finished at tick1250, native Wait_MaintainPosture40 began, the idle backstop queued Unload41, and all80 cloth reached storage while nine productive targets remained. The original 44/47 result and its failed emergency-trigger assertion remain unchanged. Native EndCurrentJob uses this short posture as a transition before the next job scan; it does not establish idle time.

The new frozen candidate is copied from **all464 inputs of the previous frozen F24 candidate** and changes only IsUnloadCheckpoint's inclusion of Wait_MaintainPosture. No current working-tree files or ongoing F40 edits were copied. The previous emergency-scan guard is retained. Baseline-to-v5 differs in exactly two files/hunks: the empty-emergency guard in HarmonyPatches and the transitional-idle checkpoint correction. See `candidate-inputs.json`, `candidate-v4-to-v5.diff` and `baseline-to-candidate-v5.diff`. Each source hash was verified before and after building. Existing v1–v4 sources/builds/native results remain retained, and exact formerly active v4 scene/build/controller/selection/handoff are in `v4-before-transition`.

## Observer change

`fixture-v4-to-v5.diff` is the complete fixture delta. The observer brackets the real private `RunIdleBackstop` with a fixture-only depth counter, balanced by a finalizer that returns the original exception unchanged. Checker entry captures the current Job reference, prior queue IDs, remaining work and call flags. Checker exit records the exact newly queued unload ID only when that same native Wait_MaintainPosture continues, the call is inside the idle backstop, targets remain, the prior queue was empty and all forced/behind/immediate flags were false.

The baseline causal assertion requires that **same ID** to start from the native queue and then successfully deliver prematurely, or the existing exact job-ID proof for a returned empty-emergency unload. An unclassified premature unload still fails causal acceptance. The separate failed `complete-work-before-storage` assertion remains; baseline status is deliberately failed. No observer creates, resumes or edits a job, inventory, target or work result. The candidate's four physical/productive/cadence/cleanup phases and assertions are unchanged.

## Selected inputs

The controller still reads `../selected-inputs.json` relative to this handoff's parent F24 evidence root; a byte-identical copy is retained here. Selection SHA256: **`FF78CE645E8E1033CAA0FC1AC1E419D897F92A22CCCA1F98842A5157CA134212`**.

| Input | Identity |
| --- | --- |
| Host | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/HarnessBuild-v5/Assemblies/HaulersDream.RuntimeHarness.dll` |
| Host SHA256 / MVID | `5E5D00A38C9AC37705DC2585C2A762EB4B57EEAAA0468FCB1C822C97DDC26853` / `cc29f6c8-e38b-44bd-8816-7016e290302d` |
| Scene SHA256 | `01BA43759502F5B854C3A0E5CB5E2BE392E7B21BC9D15125576C5122D0E35AFA` |
| Baseline product | Existing `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/baseline-Product` |
| Baseline HD / Core SHA256 | `84F376DF36F0D1857466E4DA80BCA3DC28F8F90002E2B88B5265744F85E22F0A` / `6A0D33C7A17BF2BD1F82FD1CD21D82CCC664970BF3FE996E8617023B7C6F0330` |
| Candidate product | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/candidate-v5-Product` |
| Candidate HD SHA256 / MVID | `93A65549BC110B61DB3FE6D89C67B654C9B59AE9281C2FB817E273CA0806E94F` / `7d1ada73-bec4-44be-85ec-4c94c47d5642` |
| Candidate Core SHA256 / MVID | `D8B9A6AC73860480F337E276CD597498CEF1B2A86D1B931B763E0917375D45A1` / `02ed3197-56c0-489a-b275-1d725c8f2bce` |

Candidate build: 32.62 seconds, zero warnings/errors, owned PID 25944 joined with exit 0, deployment guard absent. Host build: 5.34 seconds, zero warnings/errors. All five bounded metadata-reader children joined with exit 0 and report passed; receipts in `metadata-processes.json`. Controller/build scripts parse with zero errors. Selection records all 80 host files, 21 controller files, scene and both 99-file product payloads. Final `audit-selection.py` passed **1,256 hash checks**, including every old frozen candidate input, every new compiled input, both complete selected payloads, and untouched v4 artifacts/events/failed result. Core source is unchanged; separate absolute build paths produce different Core image/PDB identities. Keep the role's matched HD/Core pair.

`freeze-inputs.py` and `record-selection.py` are retained one-time writers, not execution entry points. The first selection writer attempt stopped before writing selection because its expected two-file list was misordered; its exact-set assertion was corrected. Selection paths then retained their explicit LOCALAPPDATA spelling instead of Python's MSIX physical-path expansion. Neither correction changed source or compiled artifacts.

## Native execution and acceptance

Use `../fixture/controller/scripts/runtime-test.ps1` with `-Action Prepare -CaseId F24-WORK-CADENCE -HdSource Built -NegativeControl None`, explicit protected player root, and omitted RunDirectory. Set TEMP/TMP to `C:/Users/Arthur/AppData/Local/HaulersDreamQA/runtime-temp` before every controller action.

- Baseline: `-ExpectedBehavior baseline-gap`, existing baseline product above, **new v5 host**.
- Candidate: `-ExpectedBehavior satisfied`, candidate-v5 product above, **same v5 host**.

Root's selected next action is candidate execution using the already preserved v4 actual trace and failed emergency-only oracle as baseline discovery evidence. A new baseline replay is not required for this handoff. The new baseline classifier is available if a later concrete finding warrants another baseline, and does not retrospectively change v4's failed assertion.

Pass `../fixture/launch.ps1 -RunDirectory <new prepared run> -OutputDirectory <fresh evidence folder>` through the committed inactive-desktop runner and its owned kill-on-close job. Do not invoke graphical Launch on the input desktop. The wrapper retains its360-second process bound and owned join; the fixture retains300 seconds/35000 ticks. Run Verify after joined exit and retain all raw logs/events/assertions, manifests and desktop/process/protected-tree receipts.

The candidate must complete the existing four phases: seventeen cotton plants across at least three native capped harvest sections; two grow-zone poplars cut and their cells actually sown; two native wood-wall deconstructions; and another seventeen-cotton field with shipped grace2500, one-hour interval and opportunistic unloading enabled. Each requires genuine native work/output/ground placement/SelfPickup/storage/conservation. The first three zero-grace phases still require actual normal-empty-work unloading. The final phase still requires no premature trip and first unload at least2500 ticks after pickup. No direct productive job injection or unload-helper invocation substitutes for those actions.

Read every causal and physical assertion/event, exact pair pins, whole logs, background-stack and parked-pawn cleanup, owned process cleanup and protected-tree results. Keep the failed v4 result as discovery evidence; accept a v5 baseline only with the exact new causal receipt or exact emergency returned-job receipt. This is controlled native behavior, not a claim to reproduce a reporter's save or validate a later assembled product.
