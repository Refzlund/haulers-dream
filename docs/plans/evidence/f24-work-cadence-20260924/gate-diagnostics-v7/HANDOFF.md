# F24 v7: observe the actual eligibility gate

24 September 2026. Host-only diagnostics built and pinned. **No health/world/job mutation, acceptance weakening, product edit, Prepare or native launch.** Root owns the next native action. Candidate-v5 product and all v6 scene inputs/oracles remain unchanged.

## Retained v6 failure and limits of the diagnosis

Native `9d927540ecb04375ab8bb44fbce48d36` failed the finite budget at35006, with43/45 assertions passing and zero captured Unity errors. All17 cotton plants complete in phase0; native yield totals194. SelfPickup35 succeeds at888 with91 in inventory. The remaining nine yields create four distinct ground stacks totaling103. No second SelfPickup, HD unload or `f24-unload-check` event occurs. Native incidental HaulToCell87/190 place those103 in valid storage and then resume queued GotoWander work. Inventory remains91 at the deadline. The attempted phase1 tree correction is never reached in this run.

The absence of a second immediate pickup is compatible with existing harvest-section logic: four pending ground stacks are below frozen `HarvestSectionPolicy.SectionSize=8`, so continuing clustered harvest waits for an idle flush. It does not establish that intake eligibility changed. The first storage trip begins only after all productive targets finish; this is a different failure from the original premature-unload defect.

The source's medical predicate (`HealthAIUtility.ShouldSeekMedicalRest || InBed`) gates both `IsUnloadCheckpoint` before any checker call and `TryGetEndOfRunUnloadJob` before tag handling. Native no-queue, nondrafted ordinary wander is observed, but no medical status, temperature, tag/pending roster, surplus or private checkpoint result was recorded. Medical suppression is a plausible common explanation, **not an established cause**. Generated-pawn health and ambient conditions were not constrained by the old fixture, which controlled skills, capability, mass and needs. A declared random seed does not preserve every generated-world input across new quicktests. Existing evidence cannot establish the missing live predicate without another observation. No storage/claim or product defect is asserted.

## Observation-only change

`fixture-v6-to-v7.diff` adds `f24-gate-state` snapshots at phase setup and changed native job/work/checker/idle boundaries, plus one check at each250-tick boundary. Identical snapshot values are suppressed. Fields record actual native medical-rest and in-bed predicates, the existing private read-only HD checkpoint result, draft/mental/Lord/duty status, HD eligibility/candidate decisions, health names/body parts/severity (two decimals), bleeding, ambient temperature, physical cargo counts, `PeekHashSet` membership, pending-pickup identities, per-stack keep/surplus, carrying mass/capacity, component presence and relevant settings.

These observations do not call self-healing `GetHashSet`, create or rescan a job, probe destinations/reservations, replace any predicate or mutate health. Existing `InventorySurplus.SurplusOf` is the product's read-only quantity query used by rendering/alerts; no unload destination query is made. The private checkpoint method only reads job/queue/medical state. Native observers run through the existing exception-reporting mechanism.

All four acceptance phases, exact givers, finite tick/wall budget, physical conservation, true native pickup/unload and default-setting cadence requirements remain byte-for-byte unchanged. Only scene diagnostics and the new v7 host output directory change. The actual failed v6 evidence is retained and hashed in `retained-v6-native-failure.json`; nine original v6 fixture/controller/selection/build-metadata/handoff files are preserved in `v6-before-diagnostics/`. Original v6 DLL/build receipts remain untouched.

## Frozen handoff

| Input | Identity |
| --- | --- |
| Host | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/HarnessBuild-v7/Assemblies/HaulersDream.RuntimeHarness.dll` |
| Host SHA256 / MVID | `A0769794B8D34C78BB61CDE0CECB2BF3E39FE167ACF2DA8947B62014B4E90C8B` / `0e2affc4-9d89-4215-a9b9-aa2bc2f1f4ce` |
| Scene SHA256 | `0FC0C680E3E5BC1A4F2AC1664AB665AC48C1712C48C9212866931E62A36924E6` |
| Active selection SHA256 | `9F83DFFB4D1CAEE92F02BC3C6223DD74A1E238A1283F59AF2DBAA5D51B990434` |
| Unchanged product | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/candidate-v5-Product` |
| Candidate HD / Core | `93A65549BC110B61DB3FE6D89C67B654C9B59AE9281C2FB817E273CA0806E94F` / `D8B9A6AC73860480F337E276CD597498CEF1B2A86D1B931B763E0917375D45A1` |

Build succeeded in13.38seconds with zero warnings/errors and joined exit0. Metadata reader16216 joined exit0 with unchanged before/after image hashes. Build/controller/reader PowerShell parse with zero errors. `pin-audit.json` passes1,252 checks covering unchanged shared host/controller/product payloads, frozen candidate sources, retained v6 image, preserved evidence and exact new build-source receipts. Root active selection and retained v7 selection are identical.

Use the existing controller with the active selection and v7 host for a fresh private F24-WORK-CADENCE satisfied/None run, and the same inactive-desktop launch/join/Verify discipline. Interpret its actual new observations first. If medical or temperature suppression is proven, choose a narrow fixture-input correction that leaves the real product guard intact; do not silently bypass it or retrospectively relabel v6. If all relevant gates remain admissible, follow the observed tag/pending/surplus state to the actual missing handoff. Do not substitute longer timeouts or random repeat-until-pass for diagnosis.
