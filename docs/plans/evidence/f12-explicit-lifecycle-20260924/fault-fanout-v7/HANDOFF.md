# F12 fault-only diagnostic fanout successor

Ready for root source review and a fresh `F12-LIFE-FAULT` run. No Prepare/native execution was performed. Use this selection only for the fault successor; retain accepted v6 lifecycle/save/restart evidence. The original failed run `45c38400bc8240b0b5d48ed48bb8fd77` remains failed and unchanged.

The original run physically credited three units, retained four, cancelled and stayed stable. Its final oracle incorrectly expected one console error. Retained events **59, 60 and 61**, all tick548, show the same injected marker in exactly these reports:

1. HD diagnostic for `Verse.Thing.TryAbsorbStack`.
2. HD diagnostic for `Verse.GenPlace.TryPlaceThing`.
3. Native `JobUtility` fixed-tick error for the actual `JobDriver_ExplicitHaul`, `Job_18`.

The source explains that fanout: `HDLog.UniversalExceptionFinalizer` records the first occurrence per `(patched method, exception type)` through `ErrOnce`; it does not suppress propagation. Native `Verse.AI.JobUtility.TryStartErrorRecoverJob` appends the exception and calls `Log.Error`. See the retained installed decompile `../native-source/Verse.AI.JobUtility.cs.txt`, lines 12–19, and product `HaulersDreamMod.cs`, method `UniversalExceptionFinalizer`. The v6 `PlaceAfter` and `ErrorRecoverBefore` already require `ReferenceEquals` to the original injected exception at the physical placement boundary and actual native recovery. Those checks, injection, all cargo/cleanup/cancellation assertions and all productive code are byte-for-byte unchanged.

[`source.diff`](source.diff) changes only the scene's diagnostic oracle, host wording and controller count/wording. The fault requires exactly three captured errors, in the stated order, each with its exact method/report prefix and the exact `InvalidOperationException` marker. The native report must name the actual recorded partial job ID and driver/toil. Existing exact exception identity, injection and physical-boundary flags are mandatory. Every non-fault case still requires zero errors. No diagnostic hook, suppression or generic three-error allowance was added.

- Host SHA256 `46A140A30D4DF1B745567BA44BC200357F4D9B77EF425A605758110E42688169`; MVID `9d29733e-48ff-4295-b1d6-7ce4fab5f0ff`.
- Selection SHA256 `15A4ABCC570D8D8EC4D67398418A39E8A5BCF55F6AEB77CEC4A7F102A75F8AB0`.
- Build 4.11 seconds, zero warnings/errors; controller parser zero errors.
- [`audit.json`](audit.json): 116 v6 inputs unchanged, 85 host/source pairs, 236 selected pins. Product and all non-host images unchanged.
- Read-only message-boundary checks admit the original exact fanout and reject extra/missing/duplicate/reversed reports, a wrong native job and a wrong marker. They verify classification only and do not relabel the old runtime result.

The copied `CONTRACT.md` describes the original lifecycle family. For this fault-only successor, its one-report assumption is superseded by the exact three-report requirement above; one exception is still injected exactly once. All other family scope and physical oracles remain unchanged.
