# F24 v6: yielding immature trees for native sow-zone clearing

24 September2026. Host-only fixture correction built and pinned. **Candidate-v5 product is unchanged. No Prepare or native game launch was performed.** Root owns independent source acceptance and the next native run. F24 remains open until all four phases pass.

## Retained finding and native justification

V5 run `01203bea1d83410aa727afef77caada9` completed phase0: all17 cotton targets in native harvest sections8/6/3,194 cloth produced and stored as75+75+44, with no premature storage trip. At phase1 the fixture seeded two mature poplars. The real ordinary work scanner correctly selected Harvest138 at tick2621. The fixture then failed its unchanged requirement for a GrowerSow clearing job; this is a fixture setup error, not evidence of another product defect. The failed result/events and all process/protected-tree records are retained and hashed in `retained-v5-native-failure.json`.

The native `WorkGiver_GrowerHarvest.HasJobOnCell` rejects any plant whose LifeStage is not Mature. `Plant.LifeStage` requires Growth>0.999 for maturity, whereas `HarvestableNow` uses Growth>harvestMinGrowth and `CanYieldNow` requires a positive harvest yield and no blight. Native TreeBase uses harvestMinGrowth0.40 and blockAdjacentSow=true; Poplar inherits both and has harvestYield27/harvestWork800. Native GrowerSow checks the configured growing season, unwanted blocker, allowSow/allowCut, ordinary ownership/reservation/cutting permissions, then creates its own CutPlant job. It does not require the blocker to be mature. Exact decompilation and copied Core XML inputs are retained under `native-inputs/`, with hashes in `native-inputs.json`; the decompiled Assembly-CSharp is SHA `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`.

## Exact correction

`fixture-v5-to-v6.diff` changes only phase1 tree setup and the host output directory. Phase1 seeds Growth0.75; the existing positive CanYieldNow/YieldNow and output-def guards remain, and a new guard requires LifeStage.Growing, blockAdjacentSow, valid season and a tree different from the cotton zone's desired plant. Two `f24-clearing-seed` events report actual growth/stage/yield eligibility. All cotton phases remain at Growth1.0.

The productive-job oracle still requires non-forced ordinary GrowerSow work in phase1. No productive job is created/injected by the fixture, no giver expectation is broadened, and neither target retirement, real native work amount, ground output, SelfPickup, storage conservation, two actual sows, cadence nor cleanup acceptance is relaxed. V5 source/build/controller/selection/handoff are preserved in `v5-before-clearing/`; original v5 build and metadata remain untouched.

## Frozen inputs

| Input | Identity |
| --- | --- |
| New host | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/HarnessBuild-v6/Assemblies/HaulersDream.RuntimeHarness.dll` |
| Host SHA256 / MVID | `15DE3CCDE8D83E109E2EB90BC442D42179C2FD7664D7E55E3D2BA568DB9C7286` / `75bfa93c-4847-40bc-8006-7a4cc082f575` |
| Scene SHA256 | `77A4C6A3C0F511C5F531BF638151E0480A0419B1523AF9FB9B5E67DE6E79E27F` |
| Active selection SHA256 | `8F1EF716C880FF7C6A1BDECF424DE3CA4182C06EC6038E8DA7BFFDC1E8B74669` |
| Unchanged candidate | `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/candidate-v5-Product` |
| Candidate HD SHA256 / MVID | `93A65549BC110B61DB3FE6D89C67B654C9B59AE9281C2FB817E273CA0806E94F` / `7d1ada73-bec4-44be-85ec-4c94c47d5642` |
| Candidate Core SHA256 / MVID | `D8B9A6AC73860480F337E276CD597498CEF1B2A86D1B931B763E0917375D45A1` / `02ed3197-56c0-489a-b275-1d725c8f2bce` |

Host build passes in6.09seconds, zero warnings/errors, joined exit0. Native metadata reader PID18184 joined exit0; before/after hashes and MVID agree. Controller/build/metadata-reader PowerShell parse without errors. `pin-audit.json` passes1,252 checks, covering the unchanged80 host sources,21 controller files, both99-file product payloads, retained v5 host, frozen candidate and prior-candidate inputs, preserved fixture and exact new build-source records. The active root `selected-inputs.json` and this directory's retained copy are byte-identical. `record-selection.py` and the preparation writer are retained one-time provenance writers, not native execution entry points.

## Next native action

Use the existing `../fixture/controller/scripts/runtime-test.ps1`, explicit protected player root, fresh generated run ID, `-Action Prepare -CaseId F24-WORK-CADENCE -HdSource Built -ExpectedBehavior satisfied -NegativeControl None`, unchanged candidate-v5 product and the v6 host above. The controller reads the active selection automatically. Keep TEMP/TMP under `C:/Users/Arthur/AppData/Local/HaulersDreamQA/runtime-temp`. Launch only through the existing inactive-desktop wrapper with its owned process job and finite timeout, then retain joined receipts and Verify.

Required behavior is unchanged: phase0 seventeen cotton plants across at least three native capped sections; phase1 two yielding poplars cleared by native GrowerSow and both cells sown; phase2 two ordinary wood-wall deconstructions; phase3 seventeen cotton plants under shipped grace2500/one-hour interval/opportunistic-unload settings. Read all real work/yield/ground/pickup/storage/conservation/cadence/cleanup evidence. The earlier failed baseline/candidate traces remain discovery evidence; no baseline rerun is required solely for this setup correction.
