# F33 v2 corrective delta review

**Accepted for the candidate native run; no concrete source/build blocker.** Independent review by `f45_build_recipe`, 2026-09-20. This follows the failed baseline and recommendation in `runtime-review.md`; it does not claim that the candidate has run or passed.

The `RouteExecutor.cs` delta implements the recommended narrow correction. `sustainPriority` is false only for a construction-delivery scanner with `alsoBuild == false`. Only `QueueReplace` receives it. That path retires the superseded priority before dispatch, including the native same-job early return, and uses the ordinary native `TryTakeOrderedJob` with the same tag. Existing workGiverDef assignment, forced/reservation/queue behavior and later explicit route stops remain in place. Build routes, other route kinds and `QueueAppend` retain their original paths. It neither disables autonomous Construction nor clears other work merely because a delivery job completes.

The final fixture delta adds a read-only direct-order observer, restricted to scene 3 and the actual actor, with the existing observed lead preventing duplicate capture from the prioritized wrapper's nested call. Scene 3 deliberately seeds native Construction priority before Replace, labels that state as a controlled stimulus, and checks that the actual product dispatch retired it. There is no post-dispatch fixture clear. The original four productive scenes and all their material/continuation assertions remain, with one additional priority assertion. The original baseline source/host/result remain preserved. The old wording “prioritized native dispatch” in the missing-observation exception is imprecise for this new direct branch but does not alter acceptance or evidence.

I checked the complete corrective diffs, final selected source hashes and actual output hashes. Both actual current build logs finish with zero warnings and zero errors. Product v2 records exactly two changed inputs out of 423; both changed workspace/copy hashes match, and the previous scanner-identity change is unchanged. Host Bootstrap/project hashes remain identical to the first reviewed build. Actual host MVID was independently read in reflection-only mode.

| Selected item | SHA256 / identity |
|---|---|
| RouteExecutor source | `074CE88751E77E7F4AB893FC4DF2FDD18BFB1424D1765006AFF22B49F422476C` |
| Final DeliveryIntent source | `0687B4581AC9054D9DB3BC178A9D72341DF65AE7897B198ECCF944D7365A3EBD` |
| Candidate HD DLL | `B6F29DE250445F0DC834C4A09232E47A7399DDAC8F00A325798767B3673CD662` |
| Candidate Core DLL | `2098CFCE61D612A86EA606EFEBF521DB59954DC60A8A7B847D6B2E99B13ACC00` |
| Final host DLL | `B3E52A3F4917A8E2C7F2E0307DDBD527CBD7CDF852530F6AB628F6B0AFCDE41B` |
| Final host PDB | `48BC826571B03BDEAB615F33287DBE96838B10547CE83287009669AE97C17CCA` |
| Final host MVID | `6487abb2-ee97-4946-a96a-8b7121e6b829` |

Next evidence is the actual candidate: scene 0 must deliver without building despite Build 0; scenes 1–2 must still build; scene 3 must retire prior sustained Construction, deliver, and avoid any forced build continuation while allowing normal autonomous work. Keep the original failed baseline and complete native evidence. No extra wrapper or broader matrix is required by this review.
