# Independent F20 paired runtime review

**Accepted for the demonstrated Storage Refill Hysteresis selection defect and its focused regression boundaries.** The candidate prevents HD's three destination shortcuts from reopening paused storage, while preserving native delivery, consumption-driven reopening and the provider's linked-group calculation. The failed baseline remains failed. This establishes a repair consistent with GH269/C005/C006; it does not replay the historical 232-mod save or establish the cause of every reported small trip.

| Actual run | Result | Assertions | Events |
|---|---|---|---|
| Baseline `9881e76c231a401fa3152bfd4a2bb3e3` | Failed | 52/59 pass; six policy failures plus aggregate host failure | 107 |
| Candidate `2fd9459447a94206ab9e6349c20cf000` | Passed | 59/59 pass | 107 |

Both complete results, event streams, Player and HD debug logs, manifests, native/desktop receipts and separate Verify outputs were reviewed. Event sequences are contiguous, with the correct run identity throughout. Each raw result exactly equals its retained Verify result. Both runs have five expected active mods and eleven exact manifest-matched images; the same native game, actual SRH 0.2.0 and fixture are used.

## What changed in actual native behavior

- **Paused selection and movement:** With 145/225 corn on a three-slot shelf and SRH set to 30–60%, native selection chooses the equal-priority fallback. Baseline HD refinement changes that back to the paused shelf; real scanner job 20 delivers the 12 corn there, reaching 157. Candidate job 24 targets the fallback and completes there at tick 370, leaving shelf 145 and fallback 12.
- **Same-tick toggle and cache:** At intermediate fullness, the public open→paused→open toggle occurs within a single paused tick. The baseline ignores the false latch in all three HD selectors. At candidate tick 472, native/HD selection, the direct stack selector with an unchanged fallback cache key, before-carry routing and en-route routing all reject the paused shelf and reopen correctly. The fixed-key false result is invalid, then returns the shelf on reopening; this exercises both cached positive revalidation and cached-miss reconsideration. The two midway calls are selection evidence, not dispatched midway jobs.
- **Assigned delivery is preserved:** A real job starts while the shelf is below the lower bound, then explicit concurrent-delivery setup adds 100 corn. The provider's normal sampling expires while the same job is carrying. Candidate job 35 remains current when SRH pauses at tick 676 and succeeds at 877, leaving all 152 corn in the shelf. Baseline also passes this boundary. The adapter does not turn the upper threshold into a delivery count cap.
- **Real automatic top-up:** During the 800-tick window with Hauling assigned, baseline automatic, nonforced `HaulGeneral` job 32 targets the paused shelf and adds six corn, giving 158. Candidate automatic job 41 starts at tick 880 from `JobGiver_Work`, without a queue or player-forced flag, and delivers all six to the fallback at 1498. Exactly one automatic haul starts and zero target the paused shelf; the shelf stays 152. This positive automatic work observation avoids mistaking pawn idleness for rejection.
- **Consumption and disabling:** Explicit consumption leaves 25; after normal sampling expiry, candidate job 52 resumes refilling and delivers 12, reaching 37 at tick 2329. Disabling SRH restores ordinary selection. The actual vanilla hopper worker returns null for an enabled 0–0 range and a real `HaulToCell` job when disabled; these query jobs are not dispatched. Baseline passes these unchanged paths too.
- **Linked mixed occupancy:** Two real linked shelves share the same settings/controller. The provider observes six slots and `37/75 + 75/75 = 1.493333` filled stacks. With a manual paused latch between 20–30%, candidate selection retains the fallback; baseline HD wrongly reselects the linked group. HD uses the provider's aggregate answer rather than a per-cell or per-definition approximation.

Physical corn conservation is checked on each observed pawn tick and each scenario update outside atomic native transfers. All additions and removals are disclosed stock stimuli. The productive jobs come from native scanner orders or the genuine automatic work scan and execute native hauling toils. Native pathing, storage capacity and haul counts are unchanged. Both runs complete every stage without fixture refusal, deadline, observer failure or cleanup assertion.

## Source, build and custody

The complete fixture source, four-file product delta, actual SRH decompiled implementation and native hopper worker were read. The provider gates the native per-group search and hopper work giver; ordinary `IsGoodStoreCell` remains available for existing deliveries. This matches the baseline's precise bypass and the narrow new-selection repair.

All 368 frozen candidate source/project/property inputs were rehashed against their receipt. Independently comparing the frozen pair confirms only the three exact retained before-files differ and the new adapter is absent from baseline. Both clean runtime packages contain 99 files; their only differences are the four built DLL/PDB files. Selected fixture source and host DLL/PDB also match their receipts.

- Scenario source: `C061440130F4167F5C90CE7CC8B289F312242E79FE07DEDDCB72AF2953682BEC`.
- Shared host DLL: `2201689174AA52EE1A49AD8AD69B2BAE143ADDBE96743F5908F705F72D358C09`, MVID `55f27022-0252-4201-85ab-956331534bbc`.
- Actual SRH DLL: `BB43A26787A59ECD5286A951CB6D97572A1E6489AD3C1D7E742D2A1410976997`, MVID `4b417093-4991-4724-95cf-e140487a8ffb`.
- Baseline HD DLL: `B4B6370A044E8F06C441FFC7D930FEFA9E424F37E6CDB97857D52B948BAC08EC`.
- Candidate HD DLL: `633E98F58F48DBFA00FEC77747BC9382C97887C10FFB95CDF4E1B73425104F8C`, MVID `71995d35-9153-41ed-9a9a-978a9a96962e`.
- Candidate Core DLL: `43418C622E464B75208638A726B08E762A73ADE929BCBD671F2AF69073B126A4`.

The baseline native/controller PIDs 24428/24116 and candidate 1348/24408 all joined with exit 0. Both desktop receipts record no switch, only Default input-desktop samples, no cleanup errors and zero owned processes before close. Both separate Verify outputs have `protectedChanges: []`. Their generic status remains not-verified: the candidate requires this independent semantic/log review and lacks the inherited generic `scenario-observed` marker; the baseline additionally retains its actual failed assertions/status. This review does not rewrite either generic result.

Both 1,871-line Player logs were inspected through startup, gameplay, shutdown and allocator statistics, along with every HD debug line. Each has two early Mono fallback notices, the existing non-power-of-two Header texture warning and inactive-window Direct3D timing notices. No gameplay exception, failed cleanup, translation failure or compatibility binding warning appears. Each reports zero captured Unity errors. The debug storage commitments agree with the actual destination change. Workshop enumeration in startup does not mean those other packages were active; the exact active list and loaded-image checks pass.

Raw SHA-256 identifiers:

| Artifact | Baseline | Candidate |
|---|---|---|
| result.json | `7EE7B4ECB6413FB88F43C7770C23703A0FD9803049E55F4F6B3389AB1CA8CA31` | `C4DE349F3F792FA68F388540F7FA66F11891C594A6FB5E6BFE583F6CA12333B2` |
| events.jsonl | `FD705B84697568E3BEA7F4C41BB9472D99EE7882EB705A832642A32B81650592` | `0FA585A427EAD1C81B61CF7211F69BB4357BD4ECD924AA99C899D8C55CEF1571` |
| Player.log | `2D7062664210375FD3384417742B063DAB9FC5C0E4E1D689F188FED32F76FA4D` | `9D8D34D962E38DBAA845E046A698F4996642C56B9138206CD49B8B2497E44452` |

## Closure and player guidance

GH269/C005/C006 are three records of the same author's report, not three independent recurrences. Retained history does not prove this SRH gate was previously fixed and regressed. Classify it as a now-repaired compatibility gap with related storage history, keeping other overdelivery reports separate.

The current changeset accurately describes partial-stack/opportunistic selection and preservation of assigned deliveries. The newly added COMPATIBILITY.md entry was independently checked after root applied it. It correctly describes rejection and reopening of new destinations, the provider's shared-group calculation and continued completion of assigned deliveries. Its explanation that the upper threshold is not a hard limit on incoming items matches the actual provider and native regression trace. Both guidance and changeset are accepted without a VNPERP or historical-save runtime claim.

Actual VNPERP is not installed in this witness. Its retained current source defines the reported shelf as ordinary three-slot storage with a hopper flag, but that structural evidence is not a runtime test of the package, its dependencies or the historical save. This pair uses actual vanilla ShelfSmall and Hopper, actual SRH and one controlled human. Provider-absent startup belongs to final assembled-build validation; unsupported API versions, the 232-mod save and other delivery systems are not claimed here. No additional rerun is justified by a finding in this pair. Root owns final guidance, ledger integration and commit.
