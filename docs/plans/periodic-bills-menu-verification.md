# F45 focused menu verification recipe — PB-03 / PB-09

Authored 20 September 2026. **Not executed.** This is the focused recipe for the source implementation in `BillRepeatMenuActions.cs`, `BillRepeatMenuProviders.cs`, `BillRepeatMenuComposition.cs` and the existing batch-menu patch. It supplements [the governing plan](periodic-bills-compatibility.md), not its scheduling, save/restart, clone, UI or multiplayer acceptance requirements.

## Inputs and execution boundary

Use the existing protected, isolated native-runtime mechanism. Parent owns a fresh candidate build after independent source review. Pin the final candidate HD/Core pair, native game and Harmony, and record the complete runtime menu patch inventory, priority/order and actual loaded provider images. No live mod edits or colony saves. The paused computer-use boundary still governs rendered/UI work.

The actual packages retained by the independent design review are:

| Provider | Workshop | Actual 1.6 image SHA-256 |
|---|---|---|
| PB | 3685837355 | `AAE78E0DDD7CE5BA70CB13481E5F71E0FC6D47700BE27054DD19358C7EA64D9A` |
| EGO Continued | 3530806680 | `034BE2930AA2591A52B23920F2FF3F3671749B0198D5C49A2503EB9BB901B960` |
| CL | 2679126859 | `A7CE3AD20F2C276FBBC8B2124E9C389FBACD8108AB5098C6E046F88346764332` |
| IT 1.0.8 | 3677669154 | `4C4A4005E591EF3025E481A01F784499297E7875C24A7E3EC0C965A30B65480F` |

PB is under `%TEMP%/haulersdream-periodic-bills-investigation-20260919/workshop-content`. The other packages are under `%TEMP%/haulersdream-periodic-bills-design-independent-20260919/workshop-content`. Read their load folders and dependencies when preparing the fixture. EGO needs TD Find Lib Continued (3529443295 / `Memegoddess.TDFindLib`), which the metadata-only design review did not acquire. Acquire/pin it privately before the actual EGO fixture. Do not silently use another EGO/CL/IT image as proof for these bindings.

Construct legitimate disposable bills on a map/workbench after Def and mod startup. Use an eligible countable recipe, an uncountable recipe, a single-ingredient recipe and a multi-ingredient recipe as required below. Record their actual Defs/counter classes. In every ordinary scenario invoke the **patched native** `BillRepeatModeUtility.MakeConfigFloatMenu(bill)` and select the delegate offered by its resulting menu. Manually calling a private replacement prefix would bypass the issue. Helper-only controls below are expressly separate from real-menu acceptance.

Capture the incoming constructor list and the actual constructed menu list, object/delegate identity, disabled/null actions, option priority/order, action module/MVID/token, captured bill and any captured Def. Select by proven action/Def identity; translated labels are evidence for later UI checks, not the selector. Count constructor/window-open calls and option/action calls. For guarded paths instrument the actual counter invocation without replacing the provider's selection logic; require one eligibility evaluation per click where its original action performs one.

Before each selection snapshot repeatMode, HD batch flag/size/overshoot, targetCount, repeatCount, includeEquipped, and PB's four configuration fields when present. A fixture reset may set up an initial state directly, but the operation under test must use the offered action. Retain both snapshots, observed callbacks and full logs on failure.

## Required discriminating observations

| Case | Stimulus | Required result |
|---|---|---|
| Same-mode unbatch | Start a RepeatCount batch of size 7; select the plain RepeatCount offered by native, PB and IT creators respectively. Repeat for Forever and valid TargetCount. | The same Def reference is assigned and batching turns off; size becomes 0 under existing SetBatch semantics. Original action runs once. Overshoot and PB state are not erased. This must fail a before/after-Def-only implementation. |
| Rejected selection | Start a batch state on an uncountable recipe; invoke native TargetCount, EGO's two countable modes, and CL's offered action where applicable. Start a multi-ingredient recipe and invoke EGO surplus. | Their original rejection occurs once; no accepted store and no HD mutation. In the IT-winning path, the offered replacements are actual EGO/CL actions with the same guards. No duplicate guard evaluation. |
| PB disabled entry | Construct PB's menu for an uncountable recipe. | The original disabled/null TargetCount option remains the same option object, with unchanged presentation state. Do not invoke a disabled entry or treat a guessed label as identity. |
| CL accepted setup | Seed targetCount = 13, repeatCount = 9, includeEquipped = false and an HD batch flag; select CL. | Mode is the real W_PerTag; values become 1, 0, true; batching clears. The completion helper runs after these writes. Test both CL's original transpiler contribution and the repaired IT proxy. |
| Provider transitions | Plain → HD batch → PB; then PB → plain → HD batch. Repeat ordinary → EGO/CL/IT. | Supported batch actions still enable with the existing default/preserved size rules. Accepted plain/custom actions clear the flag. Custom modes remain excluded from HD's batch driver/row/button gate. PB configuration survives mode changes. |
| Settings and cancellation | Add an unknown settings option with a counted original delegate to the same known menu; select it. Separately open and close without selecting. | Same option/delegate identity; settings delegate runs once. No change to HD batch state from settings/cancellation. Existing size/overshoot options still open their own dialogs rather than acting as plain selections. |
| Unknown mode preservation | Add a synthetic custom-mode option with a counted original action alongside known native entries, through the native body contribution path. | Its object, action and presentation state survive. Its action runs once with its own effects. Do not claim HD understands an arbitrary unknown action's acceptance semantics; custom-mode batching exclusion still applies. |
| IT unknown Def | Add a synthetic repeat Def before IT enumerates AllDefs. | IT's exact captured-Def assignment entry is retained, not replaced by a guessed provider. Its known accepted assignment clears HD batching. This is distinct from a wholly unknown delegate. |
| Mixed providers, both replacement orders | Actual PB + IT + EGO + CL, with PB winning and IT winning the native method's prefix race in separate retained runs. Also EGO/CL/IT separately to establish their real originals. | Each of the nine known repeat Defs appears once. PB winning receives missing EGO/CL/IT contributions. IT winning keeps its PB/IT/simple native actions and gets authoritative EGO/CL plus guarded TargetCount replacements. No duplicate provider mode; adequate original option objects/delegates stay identical. One real menu creation/open per actual creator. |
| Unrelated nested menu | During the scoped native call, construct a list containing only a counted foreign settings action (and separately known actions capturing a different bill), then construct the real menu. | Unrelated list and constructor behavior remain unchanged. The real bill menu is augmented normally. Merely entering the scope cannot trigger augmentation. |
| Nested real calls / cleanup | An outer menu contribution invokes a real menu for a second bill; include separate throwing, zero-menu and two-matching-menu controlled creators. | Each recognized list is bound to its own bill. No residual scope after normal return or throw; the original exception is preserved. Zero-menu produces no synthetic window. Two recognized lists each get one composition, with original input lists untouched. Reusing HD's output list within the frame does not duplicate additions. |
| Foreign action failure / return control flow | Retain the real 12-method IL inventory; separately exercise a bounded action fixture with a rejected return branch, a successful same-mode store, and a throw after store before return. | Rejected return does not call completion; successful return does once; a body throw does not run completion. Labels entering a return pass through the inserted guard, CL trailing setup is preserved, and the original store remains exactly once. A synthetic method is not evidence that actual provider patching bound; inspect actual runtime patches too. |
| Contract mismatch | In a separate synthetic/unsupported provider fixture, deliberately alter the expected action count or add another repeatMode store. | A visible logged binding failure; original menu is retained, with no exclusive takeover or guessed observer. Do not report this as successful supported integration. |

The fixture must report actual PB/IT winner order from Harmony and observed creator execution, not infer it solely from a ModsConfig order. If a test-only priority override is used to force each branch, retain it and label that run as a controlled priority fixture, then keep the real load-order cases required by PB-01/PB-02 separate.

## Multiplayer and compiled checks

Independent compiled review should verify all actual action targets and the emitted accepted-marker IL, including stack balance, branch targets, original store/return counts and preserved provider code. The source author has not built or executed this transformation. A source-level match to retained IL is only a contract check.

For actual supported MP session evidence, record whether the original action is registered/synchronized and where its accepted store executes. Interactive local writes must use the existing SetBillBatch route; already replaying commands must apply the HD mutation in that command without issuing a nested command. Compare both clients' state and count mutations. A passing SP action does not establish that PB/IT or any newly constructed fallback action is synchronized. Retain an explicit limitation if the actual supported configuration cannot establish this; do not extrapolate from a field being native.

All cases above remain unexecuted at authorship. After source QA, root must retain actual build/compiled review and runtime evidence before updating their statuses. Passing these focused menu cases still does not close PB-04 through PB-08, PB-10, or F45 itself.
