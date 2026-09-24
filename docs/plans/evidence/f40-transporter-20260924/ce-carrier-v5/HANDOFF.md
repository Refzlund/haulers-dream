# F40 CE carrier v5: exact native job observations

Ready for root source review; no Prepare/native launch performed. Product, Core, CE, test definitions, settings, controller and physical/save assertions are unchanged. Only the host's native caller/start observations differ.

The retained v4 run `3bc0553108404825a11f39964074c398` remains **failed50/53, U0**. Carrier13 succeeded at182; one-tick posture23 began at182; original queuedWait14 began185 and succeeded479; native hands recoveryHaulToCell21 succeeded1058; HDUnload22 began1061 and succeeded1067; all12 were stored and stable through1370. V4's stack-name predicate returned false for posture23 despite the actual matching native sequence. Its base `JobDriver.Notify_Starting` patch also missed the HaulToCell override. Thus the reported queue failure is an observer defect, not evidence that a recovery job ran before Wait14. The exact v4 checkpoint remains retained with SHA `0426ECE381CEECE01EEA4758FA9C0B312F8A0B889555375F54532AF3AE924232`.

V5 observes actual `Pawn_JobTracker.StartJob` entry and return. Entry copies ID/def/expiry before pooling; return witnesses that immutable ID as current, already ended, or genuinely queued. Every start request participates in the conservative queue-order check, including HaulToCell. It does not use the mutable `Job` after native return.

An exact per-tracker `EndCurrentJob` prefix/void-finalizer scope replaces the stack-string predicate. The only ordering exemption remains the exact native `Wait_MaintainPosture`/`JobDriver_WaitMaintainPosture`, expiry1, after actual successful predecessor cleanup. It additionally requires the native successful-end branch arguments and stationary pather. Nested calls restore their previous scope on both normal and exceptional exits. Original carrier and originalWait success, originalWait before all other recovery requests, actual HD unload, all12 stored, and300stable ticks remain required.

Review `source.diff` (three files), `src/TransporterPersistence.cs`, `input-audit.json`, and the native source at `../operation/Pawn_JobTracker.actual.cs`. The10,102-check audit binds preserved v4 source/raw, unchanged product/providers, the actual v5 compiled source and3.52s build with0warnings/errors. No v4 result is promoted to a pass. Use a fresh producer and its original checkpoint for v5 restart.

- Host SHA `0EDB20F2EE1BCC68B1C1997F1BDF7640E2C926E2FAD87CB729B4422247B48E80`; MVID `514d25c8-4f75-4fd2-aad7-4fb21886085d`.
- Selection SHA `4C3C50F21AF1C9424319AFC1FC98E8C8A89267157ED46A07524B194AA86A5D3E`.
- Diff SHA `CACB844F98CFE0758BF38574A7763AA5090F7935CAEB11922876D4942E41209D`.
- Unchanged product HD `9739F033B90CE7722CE6A61FC874EAB46F59E6B7950EE757AA7F37C8949A82BC`, Core `83EB8EB0E348A3FB6B0066640BAA1278C2B1ED6844EA31A774D3AFE18DAD43C6`.

Root uses the same short `C:/HDQA/runtime-temp` and private desktop pipeline. This author wrote the fixture correction; independent root review remains required before native use.
