# Independent F36 source review

2026-09-24. **Approve the three-guard correction for the focused native pair. No blocking source finding.** This is source acceptance, not runtime compatibility acceptance. No product edits, builds, Prepare, native launch or ledger edits were performed by this reviewer.

Reviewed the actual provider decompile, native `Blueprint`, `Blueprint_Install`, `WorkGiver_ConstructDeliverResources` and its blueprint scanner, together with HD route selection/execution, construction conversion, batching and tethering. The actual acquired provider DLL still matches SHA-256 `47864AFBC11D68D4DB5D54825D0CD430515158AEAF187F004B416A709E216286`.

The diagnosis is correct. Route total demand calls `TotalMaterialCost()` directly; suffix demand calls inherited `Blueprint.ThingCountNeeded()`, which calls the install override; own-stock fallback calls it after the native scanner returns null. Native `Blueprint_Install.TotalMaterialCost()` emits the cited error and returns an empty list. Both minified installations and building reinstalls use this blueprint type.

The current candidate `RouteExecutor.cs`, SHA-256 `F56C008A2E4BED53A0177DC052C7F3A619EF70D6A550BE7D8FC87725471F42D3`, differs from the retained `074CE887...` before-copy only by the three type guards and one explanatory comment. Their placement is correct:

- Demand exclusions do not remove the stop or its job from the route, and leave the native install job's count of one untouched.
- The own-stock guard is inside `TryDeliverFromOwnStock`, after `BuildJobForStop` has already offered the target to its actual scanner. An early install rejection at the top of `BuildJobForStop` would be wrong; this candidate does not do that.
- Native install/reinstall, blocking-thing and floor-removal jobs remain available. Ordinary build blueprints and frames retain their existing material path.

The provider's designation prefix selects a matching actual packed building and creates a native install blueprint. Its delivery prefix only logs for an ordinary build blueprint and returns true; it does not introduce an alternative job lifecycle. The five HD resource-delivery postfixes reject installs before conversion. `ConstructionBatch` requires `IHaulEnroute`, which install blueprints do not implement. Tether suffix aggregation filters queued jobs to HD construction delivery definitions, so the ordinary native install job does not require a broader tether patch.

The proposed two-scene native witness is appropriately small. The mixed route should prove the first designation links the exact packed item, the second designation becomes an ordinary build because the only item is already assigned, both stops actually enter the real plan, the install job retains native target/count, and the original inner building survives as the installed object. Require one distinct new building and charge only its actual native blueprint cost. Record that cost before work rather than assuming a fixed wood count. Use a capable pawn and verify its native construction-success prerequisites so a botched ordinary construction cannot masquerade as an installation/material-accounting defect. Ensure reachable clear target cells without floor-removal/blocking work if the oracle specifically expects an immediate install job.

The unavailable-source selection check is valid: native `InstallJob` checks `IsForbidden(pawn)` independently of the forced flag and returns null; `NoCostFrameMakeJobFor` explicitly rejects install blueprints. Forbid the source after actual provider designation, retain raw stock, then call the public HD selection method with the actual resolved scanner. Require null, no product error and no mutation/dispatch. Forbid the packed source rather than the blueprint so the intended fallback boundary is exercised. Log observation must distinguish total/suffix route errors from this later selection error in the pre-correction baseline; preserve the baseline as failed even if productive jobs complete.

No prior failed-fix recurrence is established by C029/C028. The proposed complementary-feature guidance is supported; runtime/tested wording must wait for the actual native pair.
