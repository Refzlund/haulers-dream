# F12 human UI v4 — narrow observer correction

Ready for root source review. No Prepare/native action by this author. Original v3 `8ce2c13f9fe1460cafe108f958b7e1b9` stays failed. Its actual exception is fixture `Boundary()` → native `ForbidUtility.IsForbidden(Thing,Pawn)` → `InAllowedArea` → `Pawn_PlayerSettings.EffectiveAreaRestrictionInPawnCurrentMap` → `allowedAreas.TryGetValue(pawn.MapHeld)`, after the declared native `actor.DeSpawn()` made that key null. The same native method later reads `pawn.MapHeld.lordManager`; this predicate needs an actual map.

`source.diff` changes only the fixture boundary's floor-item representation and build path/contract. It always records exact ID/count/cell and raw `CompForbiddable.Forbidden`. It records pawn-map ID and query applicability; the unchanged real pawn-specific forbidden predicate runs only for a spawned pawn on the floor item's map. Otherwise its result is explicitly null, not a false permission result. All floor objects remain compared, alongside unchanged jobs, queues, orders/next ID, reservations, tags, Keep, inventory/hands and authoritative command counters. No input, product, native result or order field is modified.

V3's actual text-focus/stale-render diagnostics and all Return/closure/no-command/physical oracles remain byte-identical. Native source was checked against installed Assembly-CSharp5CF1B5BE; `ForbidUtility` is also retained at `../../f02-own-inventory-20260924/independent-review/NativeForbidUtility.txt`. This is a fixture observer fault, not evidence of a product forbidden-state defect.

- Host SHA `8CD340497F52C57BD1BF540DD726A0CAF108C5A26E28C602211A1D3FF05A97E7`, MVID `bccf08b1-fde8-43ff-906f-5fc822a3efd7`.
- Selection SHA `4AA492C0B2618A73221F3890BBBF718D2B746AC26201131EE43FBD40E16EC8C6`.
- Diff SHA `6B9914B4E895D6C94B6D055451C3DA16C50A1891405C165395EE3D48C88C4C8F`.
- Build4.08s, zero warnings/errors;594 preservation/compiled/input/snapshot checks pass.

Use this folder's unchanged controller/launch, closed case F12-UI, Built/satisfied/None, exact candidate and host paths from selection, and short TEMP/TMP `C:/HDQA/runtime-temp`. Actual native acceptance remains pending. Original v2 unfocused Return remains unresolved; v3's input progress does not retroactively pass it.
