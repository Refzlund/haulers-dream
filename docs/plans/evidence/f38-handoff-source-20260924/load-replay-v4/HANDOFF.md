# F38 scoped native tick replay v4 — ready for root review

Separate fixture only; original E10F producer and all v3 inputs/captures remain unchanged. `../load-replay-v3/tick-entropy-diagnosis/DIAGNOSIS.md` and its 1,520-check audit preserve the failed premise and exact two-counter difference. The new host controls the missing native simulation RNG input; it does not claim the old unobserved allocations were particular objects.

## Exact bounded delta

Read `source.diff`. `CapacityReplay.cs` adds only two calls. `NativeTickReplay.cs` wraps the actual engine's `TickManager.DoSingleTick` from loaded tick8 until all eleven original command boundaries (expected end197). Seed is explicitly `424380 + nextTick*7919 + nestingDepth*101`. Prefix captures actual outer and post-push input RNG. Finalizer captures actual output RNG, always pops its own scope, asserts exact outer state/depth restoration, and returns the native exception unchanged. Actual observed input is written to the receipt and separately checked against the requested seed. No tick invocation, IDs/counter writes, native result substitution, health mutation, or state normalization occurs.

Full exact per-tick input/output/counter and bounded ThingIDMaker/GetNextLogID identity/native-stack receipts are retained incrementally. Ambient RNG is logged in events separately; it is not pretended equal across processes. Five reference receipts are now required: the original three plus native-tick-rng.txt and native-tick-allocations.txt. Comparison retains all eleven original raw boundaries, including every global counter, and additionally requires exact new receipts. Capacity83→76→51/free32, original job identity, TakeInventory personal25, kept7, physical150+7+25+8=190 and stable300 oracles are unchanged. After the eleventh boundary the temporary deterministic tick input/allocator window closes; ordinary physical recovery continues and all original outcome checks still apply. This is bounded command replay evidence, not a multiplayer session claim.

## Frozen inputs

- Selection `3E93EF73F8BC83FCF966CB1C7BC03E10555C080287B84FA995B2620942A90CE8`.
- Host `2798527D160A83F52FF016FCA3ED3CB06F8F228A6593FB1CE62EE528831BFC38`; MVID `67628243-3306-41de-a37f-77663c73af7c`.
- Actual final build `HarnessBuild-v4b`, 5.76s, zero warnings/errors. Initial v4 build and initial source are preserved; they are not selected.
- Product unchanged: HD `207C6A58E6852838C923012D75095F3B5FC788C474038FD6824F43BB74616D12`; Core `815AAE0C28D7732318D9F202296A9308F39013BE2F690750086AC2A78378416D`.
- Original save unchanged: `E10F40C2418349AF3D7A46E108A2E31F2FB4B437590DFBE17A58193C08AA15CD` from producer `bd616fc07307445084fcdd05a450ff39`.
- 851 source/build/pin checks, zero failures. Controller parser and actual wrong-role reference rejection:21/21. Metadata read without target execution. Native execution not performed by author.

## Root execution handoff

Use this directory's controller/scripts/runtime-test.ps1 and launch.ps1, case F38-REPLAY, same original CheckpointSave and CheckpointRecord. Consumer1 omits ReplayReferenceRun and records; accept the joined full evidence before creating consumer2 with its new exact controller-owned run path. The v3 reference is refused because it lacks this host and new acceptance receipts. Controller verifies/protects all five receipts and every original copied/config input. Expected first consumer assertions increase by2; comparison increases by13 more. Any exact mismatch remains a failure with full causal receipts for inspection. No automatic retry or promotion of the old failed captures.
