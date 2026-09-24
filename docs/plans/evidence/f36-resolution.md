# F36 — Build From Storage

24 September 2026. Resolved and committed as `c785e66`. The exact paired results, implementation and guidance have [independent final acceptance](f36-build-storage-20260924/paired-runtime-review.md). Sources C029/C028 ask whether HD replaces or coexists with Build From Storage. They do not establish a historical failed fix.

Build From Storage remains useful for automatically choosing an existing packed building when a new matching building is designated. HD gathers raw materials for new construction. The features complement each other. The actual provider makes a native install blueprint for the reused building; a second designation becomes an ordinary blueprint when the only packed building is already assigned.

Three HD route paths incorrectly queried raw-material demand for install blueprints: total demand, suffix demand and the own-stock fallback after a native scanner refusal. The correction excludes installs only from those material paths. Native install jobs, targets and count1 remain intact; ordinary blueprints/frames retain their material handling.

The matched frozen products differ only in RouteExecutor.cs. Baseline `8ac7ddddef684afaa05b3d1b72f23d81` reproduces all three native material-cost errors. It remains failed47/52, including the independently diagnosed fixture error that expected installation to end Succeeded. Native installation consumes its packed target and ends Incompletable after placing the exact inner building. The original trace proves placement and material conservation; it is not rewritten as a passing run. [Native lifecycle diagnosis](f36-build-storage-20260924/install-end-review/REVIEW.md).

Candidate `43bb4ebe84c747199e30b90b412ca52c` passes52/52 with93 events, zero install-material queries and zero captured Unity errors. Its corrected read-only observer confirms native install27 places original Stool38726 once, consumes its wrapper/blueprint and retains it through61 stable ticks. The following HD material delivery and native FinishFrame create distinct Stool38734 using exactly25 wood:332→307. A forbidden packed source returns no job, makes no material query and remains unchanged. No job or completion was manufactured by the fixture.

Actual provider: Build From Storage1.0.4, package buildfromstorage.programmerlily.com, Workshop3523011187, DLL47864AFBC11D68D4DB5D54825D0CD430515158AEAF187F004B416A709E216286. Candidate HD4E8CEED9/Core60A83C39; host33A302DB. Source and build hashes, full results/events/logs and failed baseline are retained in [the evidence directory](f36-build-storage-20260924/).

Both native runs joined on inactive desktops, without switching the user's input desktop, cleanup errors or remaining owned processes. Both Verify results report protectedChanges=[]. Candidate Verify retains its manual-review requirement, missing generic scenario marker and early-log review candidates; the scenario's actual terminal status, full assertions and physical trace are the acceptance evidence. The missing generic marker is not silently manufactured.

Scope is the actual provider's designation behavior and the affected native HD route paths. No all-construction-mod, opaque-storage, old-save or physical-input claim is made. Final assembled route/Multiplayer checks remain in [final integration](../final-integration-checks.md).
