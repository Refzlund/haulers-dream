# F07 stale action observation boundary

V6 run `7c03226c341648488084dc3fc176583d` remains failed at its original `stale-click-rejected` equality check. It did not retain the two compared strings, so the actual changed field cannot be diagnosed from that failure. Native rejection sound/RNG is a code-supported explanation to distinguish in the next observation, not a proven cause of the previous failure.

The one-file change is `stale-observation.diff`. V6 source is preserved as `src/RimmsCommand.v6.cs.txt`; current source SHA-256 is `07802B3066257BDEDDEDEB9C1F45679F546FBB97653415E2AB730479C3B0D7A5`.

The actual retained option is still invoked after RIMMS disables drafted permission. The fixture now emits the complete selected before/after snapshots and native live-message lists before making any assertions. It compares nextJobID, current and queued jobs separately, actor position/map, each reservation's claimant/job/target/layer/count/limit, and physical goods including the blocked uranium. Goods now include actual position/map, destruction state and stable container identity, rather than only the owner type. Any difference in these gameplay fields still fails. RNG remains in both retained snapshots but is excluded from this action-only equality; the separate `PureQuery` retains its full strict comparison, including RNG and the stronger gameplay fields.

A second assertion requires one newly published native `RejectInput` message with the actual translated drafted-permission reason and current native tick/frame. Its ID, text, type, sound and timestamps are retained. The fixture only reads `Messages.liveMessages`; it does not create, clear, reset, suppress or edit a message, sound or RNG value. No new Harmony observer is required.

Relevant actual source: `NearbyHaulCommand.Dispatch` rechecks `CanOffer` before job construction; `PawnBlockReason` rejects the revoked drafted permission; `RejectLocal` calls native `Messages.Message`. Retained native `Verse.Messages.cs` invokes `PlayOneShotOnCamera` after appending an accepted message. `Verse.Message.ResetTimer` sets the actual frame/tick and its constructor allocates a message ID, not a job ID. These are action feedback semantics, distinct from pure menu queries.

No product, host Bootstrap/project, controller, command sequence or productive acceptance assertion changed. The previous `TEMP/hd-f07-20260920/RIMMS` package disappeared externally after v6. RCV/root recovered the complete 449-file package at `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f07-20260920/RIMMS`; its actual selected 1.6 DLL remains `1152B0C198D34D4BB346FC74A4C4B72856F21ADA71FF376C5E4FF5F00FC81BD1`. This task did not delete or restore any package/runtime directory.

`build.ps1` changes only the output suffix from build-v6 to build-v7 and the `F07Packages` reference root to that recovered folder. Existing frozen product references remain HD `D0C7FBE0…` / Core `186D8812…`. A single fresh build completed exit 0, zero warnings/errors, in 2.64 seconds. Full build log, selected sources and actual products are retained in `%TEMP%/hd-f07-20260920/build-v7`:

- DLL `3B89030C9A65DC2CDAA646D11A35F97951F484EBD49C473AC68515B8C3BB6E02`.
- PDB `82DEF48E83EBD4A25497CA37B42B8B6EE74831651C0CC0A9555A72E3DD0B64B1`.
- The existing reflection-only metadata reader records the actual MVID in `assembly-metadata.json` without loading or invoking target code.

Earlier builds/source/raw failures are untouched. Root should use the new host path with the recovered full packages for a fresh preparation. No Prepare or native launch was performed here; actual gameplay/restart acceptance is still pending.
