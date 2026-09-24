# F12 lifecycle host v4 — ready for source review

This is a separate fixture against the original frozen **backend v5**. No product, UI v2, first productive-chain fixture, ledger, or native runtime was changed. No Prepare or native launch has been performed for these roles. Root owns execution and checkpoint admission.

Build completed in **1.96 seconds, zero warnings/errors**. `ready-audit.json` passes **412/412** checks: 85 frozen host/scene source pairs, 85 actual reference pairs, 121 unchanged backend runtime files, all ten selected images, 19 controller scripts and the launcher. `controller-audit.json` parses all 20 scripts without errors and evaluates only the actual preferences template: pauseOnLoad is False/True/False for producer/restart/fault. All three retain runInBackground=True. `metadata.json` records the actual image MVIDs/references using reflection-only reads.

| Binding | Exact value |
| --- | --- |
| selection.json SHA256 | C8EC0F574175ACA5AC93640B0759D2E79FDFCB0B1E989A02BD42E0A0343B8135 |
| Host SHA256 | 00C29B06D14E63DE7AAB0067AF1592B49F0420153D57BEC4804E39F14D10A040 |
| Host MVID | cc229878-dc08-4bc1-985c-7d9f4f60b11d |
| Controller SHA256 | 07530E1D5249B4C37E8EBE22C9EEF839C333BB8B9A07477EEDAA65F34EC93FDD |
| Backend HD SHA256 | EE7BA2A17D3CA22F15F71335BCBAC2AC3BF0D8389C477F0EA4EA1C1301094091 |
| Backend Core SHA256 | 7889981A8740E4C1BA681D61026F1635493362AD1A06B7A67628D41D8DA01B76 |
| Native Assembly-CSharp SHA256 | 5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A |
| Task TEMP/TMP | C:/HDQA/runtime-temp |

The host lives at `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f12-explicit-lifecycle-v4-20260924/Assemblies/HaulersDream.RuntimeHarness.dll`. Selection binds the original backend runtime and all nine unchanged non-host images. The separate controller is `controller/scripts/runtime-test.ps1`; use its existing private-desktop execution route only, with Built/satisfied/None and the selected paths. No UI or companion provider is included.

## Bounded native roles

- **F12-LIFE:** actual current plus queued 7/9 orders; native capture with distinct wrapper identities and unchanged Job references; restore; intentional queue clear and cancellation; live foreign reservation without stealing; newly forbidden walking source; a blocked diagonal with a reachable longer route; actual seven-unit hands interruption into a declared 20-unit resident, then exact remainder resumption; actual three-of-seven partial placement into a newly filled destination, cleanup and cancellation; 300 stable ticks; native canceled-state save.
- **F12-LIFE-RESTART:** only an independently accepted original F12-LIFE `SaveData/Saves/F12LifecycleCheckpoint.rws` and matching `evidence/checkpoint.txt`. Controller requires a passing producer, exact image bindings and identical retained save. Before the first tick, compare all saved order fields/references, next ID, actual current/queued jobs/driver/toil, physical identities/counts/cells, personal/Keep/private/hands and controlled world temperature. A currently eligible actor then attempts Resume on canceled orders; require no mutation and 300 real ticks without explicit work or cargo changes.
- **F12-LIFE-FAULT:** separate fresh scene using the same actual seven-unit pickup and 72/75 destination filling. Throw one specifically labelled exception immediately after the actual receiver merged three units. Require the same exception instance through product Place and native JobUtility, exact three delivered/four retained, native cleanup and cancellation, and 300 stable ticks. This is controlled fault injection, not a provider compatibility result.

The normal producer/restart require **zero Unity errors**. The separate fault role requires **exactly one retained Unity error**, containing both `HD_F12_CONTROLLED_NATIVE_MERGE_AFTER_PHYSICAL` and `Exception in JobDriver fixed tick`, with positive physical and identical-primary-exception receipts. The host rechecks that rule after its threaded capture closes. Controller Verify requires the unique corresponding assertion and still retains manual log review. No error is suppressed; all other errors fail.

## Review boundaries

Read `CONTRACT.md`, the five `src/ExplicitLifecycleScene*.cs` files, `host-adapter.diff`, and `controller-adapter.diff`. The scene uses actual native jobs, pickup/drop, queue APIs and synchronized command entry. Its declared additions are physical source stacks, resident stacks, walls and controlled scene inputs. It never assigns an order state, manufactures a Job identity, changes stack limits/carry capacity, reconstructs XML or bypasses product medical guards.

Relevant installed native source is retained at:

- `../f07-rimmsqol-20260920/blocked-queue-v13/native-source/Pawn_JobTracker.cs.txt`, lines 475–557: reservations and driver cleanup precede whole carried-thing drop; capture/restore/clear use native JobQueue APIs. The adjacent `JobQueue.cs.txt` shows new captured wrappers around the same Job references.
- `../f12-f13-next-scope-20260924/independent-native/GenPlace.cs.txt`, `ThingOwner.cs.txt`, and `Pawn_CarryTracker.cs.txt`: whole-parcel Direct placement can physically merge a partial amount and return false; cleanup uses actual Near placement. Observers use typed output arguments, never object[] synchronization.
- `native-source/JobDriver.native.txt` and `JobUtility.native.txt`: the actual driver catches the thrown primary, passes it to native error recovery, logs it, ends the job Errored, and starts the native Wait recovery. The fixture observes this path and returns the primary exception unchanged.

Raw native XML inspection remains an **independent checkpoint admission step**: compare each saved order field with the record, native omitted defaults, source/piece/remainder references, next ID, terminal state and native current/queue linkage. The automated XML check only establishes each actual pawn's deep order count; it does not claim this full XML audit.

`v1/` preserves the first local-variable naming compile failure. `v2/` preserves its successful correction. `v2-to-v3.diff` adds the already-promised native cloned-wrapper assertion and actor-eligibility assertion before canceled Resume. `v3-to-v4.diff` only moves the declared corner walls/source to the west/south side: the earlier queue-control Steel remains east of the actor and could otherwise collide with setup before movement. All prior source copies/builds remain retained.

UI rendering/input, exact shelf/shared destination capacity, provider/network profiles and overall F12/F13 closure remain separate required work. This host does not repeat or replace the independently accepted first productive/save chain.
