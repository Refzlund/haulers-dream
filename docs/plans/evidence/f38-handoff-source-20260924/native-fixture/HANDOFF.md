# F38 native fixture handoff — v5

The current selected host is v5, independently reviewed in [v5-native-warmup/INDEPENDENT-REVIEW.md](v5-native-warmup/INDEPENDENT-REVIEW.md). It adds bounded natural warm-up of actual unroofed room/cell temperatures before controlled pawns, cargo, observers or orders are created; all original handoff/restart/environment/health oracles remain unchanged. Exact delta: [v5-native-warmup/source.diff](v5-native-warmup/source.diff). All v4 inputs and failed runtime evidence remain preserved. No Prepare/native was performed by this review.

Host SHA `3C7A3FC75B1E45F29067AF44E34ED6DE3E92BD9F0BA2A6412BA98811A2738EA6`, MVID `3bb2a175-db1d-445a-bfa4-bba275e9db4f`, at the same inputs root under `HarnessBuild-v5/Assemblies/HaulersDream.RuntimeHarness.dll`. Current `selection.json` SHA `EE7E3367A33F7EC681B81F247C6677C1FAA20BDB124CDB9CD73C03DC2BF8F3E7`. Build: 0 warnings/errors, 9.87 s; 322 selected pins verified. Use this v5 host for the fresh pair/restart sequence below. Product/controller/provider/host-bootstrap pins are unchanged.

## Preserved v4 handoff

Ready for independent fixture source review. No Prepare, native launch, staging or commit performed. Root owns the private native slot. `selection.json`, `host-metadata.json`, `pin-audit.json`, `Bootstrap.diff` and `runtime-test.diff` are the input/review records.

## Frozen inputs

- Host: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f38-native-20260924/HarnessBuild-v4/Assemblies/HaulersDream.RuntimeHarness.dll`
- Host SHA-256: `6E1E2E624DB8A941C8AF83D145392E0DE141FA430A5291C6EA20207F1E911FF6`
- Host MVID: `c85fbfc2-3cec-46f1-9935-54ed44ed12d3`
- Actual installed game reference SHA-256: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`
- Controller SHA-256: `48B3607018AFD836C2760966DB0DEA39C88E2FD43FC5771E14B18BA87892031D`
- Unchanged HEAD `137a9050b429fd36aae1ab22a7aaa8d2077ee057` baseline root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f38-native-20260924/baseline-Product`. Main DLL `D38AD7568C607FDDCA18A3979A2D6EA9BF3B15848F337CBD1EBAFF89A9A04A8F`; Core `6DA4A666893603DD4783235ECB3A11230098F8F5238EB7EAF066B4783AE0A183`.
- Seven-file candidate root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f38-native-20260924/candidate-Product`. Main DLL `6D1A238164C39C16FE4CF80A9CDFF6DBCEAACC0E550C5B803D333C9E6B409346`; Core `815AAE0C28D7732318D9F202296A9308F39013BE2F690750086AC2A78378416D`. These are exactly the reviewed implementation-v1 binaries; no product changes were made during fixture authoring.

Baseline build: 35.04 seconds, zero warnings/errors. Final v4 host build: 5.17 seconds, zero warnings/errors. Earlier v1/v2/v3 build outputs remain. `v2-before-physical-witness` and `v3-before-keep-boundary` retain the previous source/selection. The v4 change places the actual seven-unit keep action one native tick before measuring source handoff, so an unrelated pre-keep memo cannot contaminate that oracle; the fixture never clears a product cache.

## What this fixture proves if it passes

The shared baseline/candidate producer uses the accepted F24 four-mod/ten-image private desktop pipeline. It first verifies real non-hauling displacement and direct cancellation still take the native whole-job path. It then builds B's real bulk plan before A reserves its incidental extras, retains that plan unchanged, and admits it only after A's real sweep has physically loaded 17 cloth and is walking toward source two.

After an actual keep-seven action and one native tick, B claims A's current 25-cloth source; D claims its future 31-cloth source. The original A job, cursor and final 41-cloth source must remain. B's stale incidental extra cannot cancel A or steal its remaining reservation. Native impossible-count Reserve and native planning/probes must leave existing plans/reservations/cargo unchanged. A's same-tick evidence must change from 107 to 51 units, retaining its ten held surplus and 41 remaining planned units; destination rows are retained.

C has a genuine long Goto, an ordinary reserved queued nearby command, a separately labelled soft native JobQueue input, and an unchanged later Goto. The soft input is an actual product-built plan enqueued without pre-reservation, with an explicit zero-reservation check; it is not claimed to come from the ordinary UI queue path. E claims the relevant sources. Current/future/reserved-queued/soft-queued retirements must preserve every original job ID and queue position. The soft plan becomes an empty normal no-op, without consuming the later command.

The candidate saves the actual colony at the paused handoff boundary. Both live before/after state and each exact native job XML target/count list must match. An independent full-process restart loads that original save and compares tick, pawn/job/cursor/toil/queue/cargo/keep/climate before the first native tick. A must resume its unchanged slot three and finish its own original job with 58 actual cloth (51 surplus/7 kept), preventing another pawn's delivery from masking a skipped remainder. Final physical totals are 107 cloth and 36 uranium stored, seven cloth kept, no other carry/inventory remainder, all original C commands completed, and 300 stable ticks without reclaim.

The temperature input uses the already-proven F40/F41 native surface-tile forecast/serialization approach: 80,001 native forecast samples, a serializable tile mean, actual 8–30°C observation and unchanged native health gates. No health/medical patches or forced productive jobs exist. The arena is open native terrain; no unproven room thermostat is assumed.

## Root-owned sequence

Use the existing TEMP/TMP discipline: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/runtime-temp`. Select the protected player root explicitly: `C:/Users/Arthur/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios`.

1. After source acceptance, Prepare `F38-HANDOFF` with `HdSource=Built`, `ExpectedBehavior=baseline-gap`, the baseline root and the v4 host. Its current-source-preservation and host-return assertions should fail from genuine native whole-job cancellation. A setup refusal is not reproduction.
2. Prepare the same case with `ExpectedBehavior=satisfied`, the candidate root and the same v4 host. A pass produces `SaveData/Saves/F38-SweepHandoff.rws`, `evidence/checkpoint.txt`, and an exact extra original save copy.
3. Prepare `F38-RESTART` with `ExpectedBehavior=satisfied`, the same candidate/host, and `CheckpointSave`/`CheckpointRecord` pointing to that exact original passing run. The controller binds original save/record/result/manifest/log into protected inputs, copies the save as `Autostart.rws`, uses literal `pauseOnLoad=True`, and removes `-quicktest`. It accepts no reconstructed save.
4. Launch only through this fixture's `launch.ps1`; retain returned native/controller/desktop receipts, raw evidence, original manifests and Verify output. Inspect complete assertions/events, exact save nodes, physical ownership, logs and cleanup independently.

## Remaining limits

This fixture has not run. A passing producer alone is not F38 completion. It contains the core handoff/restart chain, ample genuine storage, actual held-cargo evidence and the listed negative controls. The earlier design's separately constrained destination and repeated identical command replay are **not implemented in this first native fixture** and remain explicit acceptance work; the managed retirement replay is not a network test. Final integration owns broader Multiplayer/network checks. No compatibility matrix is being added.
