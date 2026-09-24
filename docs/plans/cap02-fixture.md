# CAP02: first native physical projector fixture

Status: bounded native component accepted on HD CE0508/Core84C6 and harness35CC, run `c6cab78edf9f489cb6dedd9eece93873`. It passes427 nested assertions,21 physical observations,38 eligibility rows and38 operations, with zero captured Unity errors. Actual evidence and full logs received independent review. After three validator gaps were corrected, root freshly reviewed helper7A084EB against fixture/product source and passed12/12 additional controls in both PowerShell versions. Final full Verify confirms exited process and unchanged1451 copied files/63 protected records; its only automatic candidates are the independently reviewed exact Mono startup diagnostics106–107. This manual bounded acceptance does not suppress other log entries or accept other scenarios. The initial failed run and all earlier validator findings remain preserved in the run log. Root owns evidence acceptance.

`CAP02` is the runtime case ID for this bounded first projector fixture. It does not by itself close every similarly numbered storage-plan acceptance item. It is a **changed-build adapter fixture**, with no published-build comparison: the published DLL does not expose the new API. Missing methods cannot count as an expected baseline gap or as corrected behavior.

## Files and integration contract

- `tools/RuntimeHarness/StorageProjectionScenario.cs`: `StorageProjectionScenario.Run(Map map, string expectedBehavior)` returns `StorageProjectionResult` synchronously on the initialized Unity main thread. The only accepted expectation is `satisfied`.
- `tools/RuntimeHarness/StorageProjectionBridge.cs`: exact reflection bridge to the one actually loaded `HaulersDream` and `HaulersDream.Core` assemblies. No static product reference or substitute Core computation exists in the harness.
- Root integration adds the CAP02 branch and `storageProjection` result field to Bootstrap, plus explicit Built/satisfied/native-only case guards and dedicated validator entry points to the controller. No allocator or admission behavior is changed by this fixture.

Root integration should add explicit CAP02 selection, permit only a changed/built HD selection and `satisfied`, persist the dedicated result, and finish according to that result plus the existing independent startup/runtime-error assertions. Use the established private-copy runtime with the explicit real player-save-data protected roots; do not reuse or change a prepared immutable run. Minimal mods are Harmony, Core, HD, and this harness. ASF is deliberately refused by the fixture and remains a separate matrix. The fixture does not launch or terminate the process itself; Bootstrap owns terminal handling.

`fixtureValid` describes physical setup and successful execution of the fixture/bridge. A returned unsupported, deferred, incomplete, or wrong product result is a behavior failure, with recorded typed status; it does not pass merely because setup was valid. Missing reflection bindings or unexpected exceptions make the case inconclusive. `requestedBehaviorSatisfied` and `expectationMatched` require every behavior assertion and fixture precondition. A caught exception alone is never the result: the case emits a structured exception assertion and terminal scenario result.

## Actual API and identity requirements

The bridge requires exact constructors for `StorageProjectionEnvironment`, `StorageProjectionRequest`, and `StorageParcelProbe`; exact signatures and return types for catalog `Create`/`PrepareMember`, projector `Open`, and scope `ObserveCell`/`ObserveEligibility`/`Recheck`/`Dispose`. It checks the catalog out parameter and actual `IDisposable` implementation. DTO getters are checked against their declared types before reading. Bound methods/constructors record metadata tokens and module IDs. Required types must originate in the one selected loaded assembly, not forwarded substitutes.

Record loaded HD, Core, game, Harmony, and harness full assembly identities, actual file locations, SHA-256 hashes, and MVIDs. These complement Bootstrap's manifest-to-loaded-file checks; the fixture does not replace them with a hardcoded development hash. Record catalog native identity and actual patch inventory. The current reader recognizes executing game `1.6.4871 rev591`; preserve Bootstrap's separate `Version.txt` literal (`rev590`) in run evidence.

The fixture supplies one explicit session GUID and generation 1, valid only for this synchronous map fixture. Each scope remains in one game tick and is disposed in `using`/`finally`. Requests use the real concrete unlinked SlotGroup, `CellObservation`, `WithinSelectedGroup`, and explicit `Opportunistic` filter context. Real floor Things have `ParentHolder == map`, remain spawned on that map, and are paired with an actual ordinary spawned human carrier and player faction. No manually supplied capacity, delivering flag, materialized parcel, or job result is used.

## Deterministic physical setup

After the real disposable player-home map initializes, despawn generated pawns/landing objects. Clear one bounded outdoor concrete rectangle with no existing zones. Generate one normal human actor with work disabled, empty inventory/carry, and no current job. No simulation ticks advance in this adapter fixture.

Use four exact native `Building_Storage` objects from `ShelfSmall`, unlinked, wooden, player-owned, one cell each. Verify the actual patched `GetMaxItemsAllowedInCell` returns 3. Accepted filters explicitly include all fixture cargo/fillers, at Critical priority. Seed using real `ThingMaker`/`GenSpawn` and verify actual identity/count/cell.

| Scene | Physical occupants | Expected resource output | Silver / Cloth |
| --- | --- | --- | --- |
| Occupied shelf | Full Steel, full WoodLog | Two real full stack rows; **one shared vacancy** | Both independently eligible for that same unconsumed vacancy |
| Empty shelf | None | Three vacancies in one cell-wide resource pool | Both eligible; each reports its actual def stack limit per new stack |
| Full shelf with partial | Full Steel, full WoodLog, Silver at `stackLimit - 7` | Zero vacancies; one real seven-unit deficit | Silver: exactly one seven-unit target edge; Cloth: actual native refusal |
| Full incompatible shelf | Full Steel, full WoodLog, full Uranium | Zero vacancies; no deficits | Both actual native refusals, with no edges |

Each scene has separate real floor Silver and Cloth parcels of seven units. The partial fixture independently calls actual `target.CanStackWith(Silver)` and `target.CanStackWith(Cloth)` to establish the directional positive/negative controls before projection. The projector's resulting target-directed predicate row must identify that same target for the successful Silver edge. This records genuine game compatibility, not a fake Core edge.

Explicit `PrepareMember` runs outside scopes. Its finite complete allowance is `8*(ThingDefCount+1)*(ThingCategoryCount+1)*(SpecialFilterCount+1)+1,000,000`; actual charged work/readiness/index count is recorded. Concrete building footprint warmup is explicitly requested here so capped reads do not rely on undocumented spawn hydration. This is preparation work, not a hot-operation timing claim.

For each shelf, run Silver→Cloth and Cloth→Silver in separate scopes with the same session and actual subjects. Each order observes physical resources, evaluates both actual subjects, rechecks each eligibility with a fresh native evaluation, and observes the resources again. Compare canonical resource keys, real stack identities/counts/deficits, and per-subject predicate/edge outcomes while excluding scope-local observation IDs, query IDs, and work counters. Neither probe allocates or consumes the vacancy. A shared resource eligible for both subjects is correct at this observation stage; competition is the later allocator's responsibility.

## Stockpile mismatch and recovery

Create a genuine one-cell `Zone_Stockpile` using its native constructor, `RegisterZone`, and `AddCell`. Verify native map registration, one physical slot, no occupants, and accepted floor cargo. Prepare its actual cell membership index and obtain usable physical/eligibility/recheck results.

The following mutations are **fixture controls only**:

1. While retaining the first observation, remove exactly the sole entry from `zone.cells` directly. Deliberately omit `RemoveCell` notifications so both zoneManager and haulDestinationManager still identify the original zone at the requested cell. Record this mismatch explicitly.
2. Require the old index/observation and recheck to report unusable `GroupChanged`.
3. Dispose the old scope. Explicitly prepare the now-empty actual list (zero indexed cells), open a fresh scope, and require the missing requested cell to remain unusable `GroupChanged`. A grid registration alone is insufficient membership proof even for a newly prepared index.
4. In `finally`, restore exactly the original sole list entry; do not issue duplicate native notifications because the registries never changed. Record the repair. Reprepare the one-cell index and obtain fresh usable physical/eligibility/recheck evidence.

The reader never performs these list mutations or repairs. Repair is in its own `finally`, so a disposal exception is retained as a failure but cannot skip restoration of this fixture-owned list. Before/after typed physical snapshots include actual parent identity, all shelf items, incoming floor identities/counts/custody, slot limits, both registrations, stockpile list contents, priority, settings/filter object identity, actual allowed defs, ordered special-filter entries and native quality/hit-point/mental-break ranges. Actor snapshots include actual identity/map/position/faction. The final snapshots must exactly match setup; empty actor custody/no current job and the unchanged tick are checked separately. These are bounded before/after invariants, not a complete history of every game field.

Each usable cell must have the exact actual session/map/parent/cell resource prefix and real Thing suffixes; keys must be distinct across physical scenes and stable across repeated observations. Every initial and fresh eligibility row must name the requested actual parcel and its cell observation. The initial zone's fresh recheck row is retained and checked completely. These requirements correct four independent source-review findings. Fresh reviewer `source_coverage_qa` approved scenario73823E06… and bridge9186CF09… against the actual installed native APIs/XML, retaining the bounded native outcome scope and every pending control below.

## Evidence and remaining scope

Events are `storage-projection-bind`, `storage-projection-physical-setup`, `storage-projection-operation`, `storage-projection-cell`, `storage-projection-eligibility`, `storage-projection-assertion`, explicit `fixture-mutation`/`fixture-repair`, and `storage-projection-result`. Per-cell rows contain resource identity, real quantities, status, and reported work counters. Eligibility rows contain each named actual predicate outcome, exact target edges, vacancy permission, fresh recheck output, and reported work. The counters are product-reported accounting; this fixture does not independently instrument every native call or certify elapsed-time bounds.

Acceptance still requires independent review, a guarded harness build, an actual private runtime run against the recorded changed HD/Core DLLs, full result/events/Player.log review, and controller verification of copied/protected hashes. Source presence is not runtime evidence.

Separately pending: ASF valid/invalid registry identity and cell controls, heterogeneous linked owners/filters, large-group paging, completed-member mutation across pages, oversized cells and continuation, worker/reentry/session/generation/wrong-scope/disposal negative controls, explicit nested legacy flag/filter restoration, and independent call-budget measurements. This fixture makes no allocation/admission/reservation-lifecycle/driver-convergence claim and does not close the original reporters' issues.
