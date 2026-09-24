# PB-only clone comparison: actual runtime review

**Accept the reproduction and upstream attribution; do not mark clone preservation repaired.** Run `750cc902a47641a4b9c0ad0491559f95` completed all 12 planned cells, including the seven checks in the final clone cell. Its 202 contiguous events agree with the result and individual cell files. Cleanup completed, no cells remain unexecuted, and captured errors/exception are empty. The owned native outcome records PID 30916 joined with exit 0, no deadline or retention error. The completed Verify report has empty problems, input/private/gate changes and read errors.

Evidence is retained in `native/750cc902a47641a4b9c0ad0491559f95/`: `cell-12.json`, `events.jsonl`, `result.json`, `Player.log`, `native-outcome.json` and `verify-report.json`. The admitted private package list contains Harmony, Core, Periodic Bills and the two test hosts, with no Hauler's Dream product. Actual PB is `AAE78E0DDD7CE5BA70CB13481E5F71E0FC6D47700BE27054DD19358C7EA64D9A`, MVID `c3971e5a-4c8e-4137-8147-593b90bd0e0e`, matching the production comparison.

## Observed native sequence

Events 192–200 execute the same native sequence as the HD-loaded production run: source `Clone()`, clipboard `Clone()`, `InitializeAfterClone()`, then `AddBill()`. Only the source is configured with amount 7 / interval 3. This occurs within a real GUI callback without a physical menu-input claim.

| Observation | Actual ID and PB state |
| --- | --- |
| Source before cloning | `Bill_CookMealSimple_2`, bill object 182 / data 183, amount 7 / interval 3 |
| Clipboard and second clone | Distinct bill objects 184 and 186, both temporary `Bill_CookMealSimple_-1`, shared data 185 retaining 7/3 |
| Initialized pasted bill, before creating data | Object 186 now `Bill_CookMealSimple_3`; `TryGetData` finds no entry |
| Actual `GetOrCreateData` | New data object 187 has defaults 1/1, produced 0 and completion −1 |
| Original objects afterward | Source object/data and complete source state unchanged; clipboard still retains temporary-key 7/3 |

The operation preserves the native window state. Final scene disposal removes the owned bills/benches and successfully checks PB stale-entry cleanup; this is not cleanup-induced loss masquerading as a clone failure. The loss was recorded before disposal.

The complete 1,857-line player log contains the fixture's Harmony download-URL notice, two early Mono fallback-load notices and profiler/allocator output. There is no accompanying clone/action exception or saved-reference warning. The fallback notices' origins are not established; they are not reported as a clean, notice-free log.

## Disposition

Corrected HD-loaded production run `0e63f30388344a16ad54559991bdca34` records the same source/temporary/final ID sequence and requested 7/3 → actual 1/1 in event 120. The PB-only run now supplies the previously missing comparison: Hauler's Dream is not necessary to produce this defect. PB settings remain associated with the temporary clone ID while native initialization changes the pasted bill's ID.

Close the **upstream-comparison investigation** with this concrete finding. Keep clone preservation explicitly classified as an existing upstream defect unless a separately reviewed compatibility repair is implemented and verified. Neither the reproduction cell's pass nor the successful production/restart checks means clone settings are preserved. This review does not close the whole F45 item, establish physical clipboard-button interaction, or authorize a public upstream report.
