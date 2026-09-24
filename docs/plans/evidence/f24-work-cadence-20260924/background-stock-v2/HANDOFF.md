# F24 background-stock correction: selected host v3

2026-09-24. Fixture-only correction, ready for root's paired native execution. No Prepare or native launch was performed by this agent. Original product baseline/candidate, controller, host Bootstrap, project and launch script are unchanged; `unchanged-inputs.json` proves all eight product DLL/PDB files and the four unchanged fixture inputs still match the previous selection. `scene.diff` is the complete original-to-current scene change.

Use the original F24 case, baseline/candidate product roots and expectations from `../HANDOFF.md`, with this replacement host. The controller reads the evidence-root `../selected-inputs.json` at Prepare; root is copying this folder's finalized selection into that exact admission path after preserving the original in `selected-inputs.before.json`. The first Prepare refused the stale original-host pin before creating a run; no admission guard was relaxed.

`C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f24-20260924/HarnessBuild-v3/Assemblies/HaulersDream.RuntimeHarness.dll`

- Harness SHA256: `196E40627B880921E830579C25016DDE286F271EE2AFD2D071396653611D04CC`.
- Harness MVID: `e32664e7-0d2a-4923-90c0-925ea43a2cf6`.
- Current WorkCadence source SHA256: `7AD0441671D5E0862E820C76DEA22570E1310E9ECF5EE750843F763589C67F5E`.
- This folder's `selected-inputs.json` SHA256: `E7B663FC1F3DDD70B5EBE4714052AC65D1F29C8ABABD5673909E07FB428D702A`.
- Fresh v3 build: zero warnings/errors, 5.20 seconds. `build-v3-process.json` records the joined synchronous child. `image-reader-v3.json` and `harness-v3-metadata.json` record the independent native PS5.1 metadata-only reader, PID 26472, joined exit zero.

Before ClearArea, every existing spawned Cloth/WoodLog stack on the map is recorded with its exact object, ID, count, cell, rotation and native map owner, then despawned into a private non-merging ThingOwner. Parking is visible in `f24-background-stock-parked` events. The existing zero-background-stock precondition remains in place afterward. Physical produced-output accounting still examines the map and actor's actual inventory/hands; it does not subtract or silently exclude surviving background stock.

After fixture yields/objects and zones are removed, cleanup returns each preserved original stack to its exact cell and rotation and checks its ID/count, original map owner, map membership and lack of holder residue. Each successful return and final empty holder is a named assertion; exceptions fail cleanup while restoration continues for other stacks. Captures precede mutation, so setup failures can restore partially parked stock. All normal work selection, actual native harvest/cut-to-sow/deconstruct, capacity, physical conservation, causal baseline and unchanged default-grace phase criteria remain unchanged. This correction addresses an invalid fixture precondition, not a product result.

The first correction build v2 and its source are retained. Final preflight caught a null-owner assumption for spawned things: this native version assigns them to `map.GetDirectlyHeldThings()`. The v3 correction requires that actual original map owner before parking and after restoration. Neither earlier host was launched. Original host `HarnessBuild` and original source `WorkCadence.before.cs.txt` are preserved. The first metadata reader attempt was also retained in `image-reader.json`; its noncanonical forward-slash input path was rejected before producing a metadata result. The normalized-path retry and final v3 metadata checks passed. No native guard or reader was weakened.

Root independently reviewed the complete current source and accepted the background parking/cleanup and map-owner correction. This handoff is execution readiness only. F24 remains open pending actual paired native evidence, complete logs and final disposition.
