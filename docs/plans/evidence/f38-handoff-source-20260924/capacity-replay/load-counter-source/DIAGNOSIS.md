# F38 original-save replay: native compressed-map ID allocation

The retained `a120df33098243cc8fdc8f1864a24680` remains **failed, 34/36 assertions, U0**. No F38 command ran. Its two failures are the pre-first-tick state guard and the resulting load-capture failure. The source fixture incorrectly required the producer's pre-load global ID allocator to equal the freshly loaded allocator. This does not show product nondeterminism.

`audit.py` reads the original producer save and the failed consumer's complete captured state. The save and copied Autostart both have SHA `E10F40C2418349AF3D7A46E108A2E31F2FB4B437590DFBE17A58193C08AA15CD`. The original map's 2,823-byte base64/Deflate payload decodes to 125,000 bytes: 62,500 little-endian ushort cells, exactly **3,183 nonzero entries**. All captured state fields match except `nextThingID`: **37,542 + 3,183 = 40,725**. The 41 other global counter fields, saved actors, jobs, targets, queues, Cloth identities/quantities/custody, zone and settings are unchanged. `audit.json` retains the exact comparison and input hashes.

The newly decompiled files here bind to actual installed `Assembly-CSharp.dll` SHA `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`, MVID `61e41735-6189-4da4-9d21-0260257b5097`, also present in the failed native result. The causal native call path is:

1. `Game.LoadGame()` calls `ExposeSmallComponents()`, including `uniqueIDsManager`, before loading the world/maps. It finalizes Scribe and then calls each map's `FinalizeLoading()` before `GameComponentUtility.LoadedGame()`.
2. `Map.FinalizeLoading()` materializes `compressor.ThingsToSpawnAfterLoad()` and merges those objects with fully serialized Things.
3. `MapFileCompressor.ThingsToSpawnAfterLoad()` reads each nonzero ushort as a ThingDef short hash and calls `ThingMaker.MakeThing`. Compressed cells retain a definition and position, not an original Thing ID.
4. `ThingMaker.MakeThing()` calls `Thing.PostMake()`, then `ThingIDMaker.GiveIDTo()`, then `UniqueIDsManager.GetNextThingID()`. Every non-Mote receives the next ID. Core's compressible definitions are natural buildings and chunks. Fully serialized `Thing.ExposeData()` instead restores the saved ID.

The whole load log has no compressed-map missing-def/collision/instantiation error and reports the actual maps.FinalizeLoading phase. The exact extra count is thus accounted for by normal map decompression; there is no unexplained residual allocation. No game counter, save byte, original result or selected product was changed in this diagnosis. There was no native rerun.

## Accepted correction contract

Keep the accepted producer as capacity evidence and as the immutable source of the original save. Run **two new fresh consumers of those same bytes**. Consumer 1 records the eleven existing canonical command-boundary strings; consumer 2 binds that successful consumer's actual images, complete copied inputs and receipts and compares all eleven strings exactly, including actual Thing/job IDs and scoped RNG. Neither consumer saves/reconstructs the scene or adjusts counters. This is the smallest pair with equivalent native load history.

For each consumer, before the first tick: retain the complete original expected string and complete actual string; require every saved field and other counter to match exactly; independently decode the pinned original compressed map; prove its 3,183 actual recreated cell/def/ID entries occupy exactly the native allocation interval `[37542,40725)`; require the loaded allocator to equal the saved allocator plus precisely that count. Record this admission separately from the unmodified canonical replay. Require native module SHA/MVID. Consumer 2 also requires exact equality to consumer 1's complete raw loaded pre-command state and allocation receipt. No general counter allowlist, ID renaming, reference rewriting or causal-ID normalization is needed.

All existing command, reservation, original-job, capacity 83/76/51/32, physical 150+7+25+8 and 300-tick stability oracles remain. A divergence between the two loaded consumers stays a failure. Do not compare consumer boundaries to the producer's no-load execution as though those environments had identical allocators. The accepted original handoff/restart evidence and failed captures remain unchanged.
