# F12 explicit point-haul UI: source boundaries

This additive slice uses the already frozen bare-point backend. It adds five C# files and two XML files in each of the repository's 16 locales (English included). No original command, driver, transfer, lifecycle, comp persistence, MP registration, existing keyed XML or runtime fixture file is edited.

`FloatMenuOptionProvider_ExplicitHaul` uses the game's auto-discovered provider pattern. Each selected spawned haulable item gets its own labelled option. `ExplicitHaulUi.Open` captures the actual pawn/map/source, source position/count/forbidden state and queue input. Quantity defaults to the opening stack size; the exact numeric field and slider choose a total, and one item skips the dialog. The queue checkbox begins with the captured native QueueOrder key state and carries through destination selection. A stale selection rejects without allocating an order. Only `ExplicitHaulCommand.Dispatch` submits the final exact cell/quantity to the existing synchronized method.

The new comp UI partial adds an equivalent source targeter for supported robots whose native `CanTakeOrder` excludes them from the map menu, plus an order list. It does not alter native control permissions. The list reads live delivered/total/state and calls the existing authoritative Resume/Cancel endpoints. Completed orders cannot be resumed or canceled; a canceled order with an exceptionally retained piece can retry its existing physical cleanup. UI enumeration and dialog cancellation do not call a world mutator.

Shelf and storage cells remain rejected by `ExplicitHaulCell`/`CanIssue`. The bare-cell targeter does not silently reinterpret a building as shelf support. A later exact-building adapter must pass concrete building identity through its own authoritative admission. F13 and shared destination capacity remain required work.

## Actual installed API evidence

- `../f05-f06-robots-20260924/source-review/RimWorld.Targeter.cs`: the callback overload at line131 accepts separate validation, caster, mouse attachment and OnGUI callbacks. `ProcessInputEvents` lines192–255 invokes the action only after validation, leaves targeting active after a rejected target, and handles right-click/Escape by `StopTargeting`. `ConfirmStillValid` line344 stops targeting when its caster is destroyed, changes map or is deselected. Opening a next stage calls `BeginTargeting`, which clears `needsStopTargetingCall`, preserving the next targeter when a one-item source is selected.
- `../f05-f06-lifecycle-20260924/source-review/FloatMenuContext-independent.cs.txt`: context construction removes pawns for which `CanTakeOrder` is false. This is why the comp source targeter is needed for the existing supported Misc. Robots path.
- `../f05-f06-lifecycle-20260924/source-review/FloatMenuOptionProvider-independent.cs.txt`: provider applicability enforces native drafted/multiselect/fog rules; selected pawn validation uses declared mech and drafted support. Product live actor admission still performs the actual haul-role/permission checks.
- `../f35-finish-wild-20260920/Verse.Pawn.cs.txt` line3329: native Pawn.GetGizmos yields base ThingWithComps gizmos. The new CompGetGizmosExtra partial follows the existing CompBenchGather pattern and needs no global Pawn patch.
- `../f11-quantity-lifecycle-20260924/Widgets.native.txt` lines3573–3621: native mouse-attached labels use an unbounded9999px rectangle without screen clamping. UIv2 therefore uses a wrapped380px prompt clamped to the current UI screen, restoring font/anchor/wrap/color afterward. Dialog content scrolls independently of measured bottom buttons.

All source additions compile against the selected installed Assembly-CSharp/Harmony reference snapshot used by backendv5. Compilation confirms signatures, not native UI rendering, event delivery, locale fit, replay or provider compatibility.

## Checks retained

`v1/build.log` records a successful22.88s native-reference compile with0 warnings/errors. The first packaging wrapper then rejected its own source audit because it had incorrectly included four pre-existing assembly/PDB outputs as source inputs. `source-inputs-before-output-classification.json` and `freeze-output-classification.json` preserve that failure and prove only those four compile outputs changed. The actual source stayed unchanged. The freeze script now excludes the entire generated1.6 output area; UIv2 runs the corrected pipeline. No product workaround or skipped source hash is involved.

`finalize.py` verifies all frozen source pairs, actual reference pairs and selected runtime content. It parses all16 keyed/DefInjected files, checks every new key is unique among its locale's existing keyed files, validates all formatting placeholders, checks referenced keys plus all dynamic state keys and the reused native quantity-error translation. These are structural checks; the rendering checklist remains open.
