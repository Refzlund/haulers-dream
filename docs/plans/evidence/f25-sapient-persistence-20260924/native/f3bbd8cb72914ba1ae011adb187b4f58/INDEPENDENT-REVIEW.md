# F25 fresh restart — accepted for the reported persistence requirement

Independent review accepts **65/65 assertions, zero captured Unity errors and 194 contiguous events**. This is a new native process consuming the original accepted partial save, followed by actual surplus recovery, new bulk orders over the original remaining items, and 300 stable game ticks. No product, fixture, original evidence, ledger or saved game was changed by this review.

## Original checkpoint and executing inputs

The source is producer `20881fcc041e49dc95d107af6d381a05`, not a reconstructed save. Its original `F25-SapientCheckpoint.rws`, retained original copy and consumer `Autostart.rws` all hash to `AFBE964F9E9D18183289CBA6244B06541E4E5CC3D781E4769E77CD354BB69C25`. Original and consumer sidecars hash to `31DBAA3D266CBA646BB063014BD77A4C5C67A585621E2C65E697D752EDF24416`. The producer PID is 25772; this consumer PID is 25712.

All **15 actual executing module SHA-256/MVID/version identities** exactly match the accepted producer, including host `A795365034AC0BF09BE687DB96EAE76AFD03A292822D9803343C16C8A09A41F8`, HD `D38AD7568C607FDDCA18A3979A2D6EA9BF3B15848F337CBD1EBAFF89A9A04A8F`, Core `6DA4A666893603DD4783235ECB3A11230098F8F5238EB7EAF066B4783AE0A183`, and native Assembly-CSharp `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`. Installed version is rev590; the executing game reports rev591. The audit rehashes **365 selection pins, all 1,969 copied files**, original/raw evidence, metadata receipts and the metadata reader/interpreter.

The seven actual private packages are Harmony, Core, Biotech, Big & Small Framework, Sapient Animals, HD and the harness. Framework ships `0MultiplayerAPI`; that API image's presence is not active Multiplayer or a lockstep acceptance claim. Providers are the previously selected public builds, not the reporter's exact historical Workshop bytes.

## Saved state and physical recovery

Independent parsing of the unmodified native XML confirms tick 5833, unique original actor/item IDs and the following exact ownership:

| Actor | Tagged held stack x7 | Stored stack x5 | Forbidden floor remainder x11 | Keep / auto |
|---|---|---|---|---|
| `HL_Mech_Lifter36147` | `Cloth40105` | `Cloth40093` | `Cloth40106` | 2 / False |
| `HL_Monkey36152` | `Gold40109` | `Gold40098` | `Gold40110` | 2 / False |
| `Human36162` | `Steel40123` | `Steel40101` | `Steel40124` | 2 / False |

At LoadedGame tick **5833**, before a native tick, all three pawns retain their exact identities, generated definitions, inventory owners/counts, tag references, Keep dictionaries, auto=False, drafted=True, stored material and exact forbidden remainder positions. Each has exactly one defined/live HD component, and native lookup points to that live instance. They remain humanlike, hauling-capable and medically fit; the provider's race/poor-hands hediffs are retained. Native XML also retains last-yield ticks 4676/5178/5833; the live equality oracle does not separately assert those tick fields.

The genuine pause-on-load boundary is tick **5834**, explicitly `sameProcess=False`. The saved climate mean is preserved, with natural native room/outdoor warm-up to tick5891 before undrafting; no cargo, tags or preferences are reconstructed by the load fixture. Undrafting interrupts the saved native combat-wait jobs. Without enabling auto pickup, the actual unload paths deliver five surplus units from each inventory, leaving two kept units. At **9462**, each original stockpile contains ten and every original eleven-unit remainder is still forbidden and untouched.

The fixture then uses real player setters/commands to enable auto and allow each exact original remainder in turn. Bulk jobs **237, 359 and 442** actually succeed at ticks **10031, 11182 and 11941**, holding thirteen units before their unloads split off eleven. Physical stored totals reach 21 at **10829, 11722 and 12449**, with two original tagged units retained per actor. There is no replacement cargo. Every one of the **106 recorded physical states** has unique item identities and exactly 23 units of each measured material across map, inventory and hands. Final state is unchanged from **12449 to 12749**, with empty hands and no duplicate component.

Six actual unload jobs **115/129/148/285/391/465** end `Incompletable`, after their surplus has physically reached storage and only tagged Keep2 remains. Their end states contain no carried surplus. This is the frozen unload driver's retained termination behavior, not a claim that those jobs returned `Succeeded`; all three explicit bulk jobs do return `Succeeded`. The completion oracle is actual storage, kept quantity, conservation and stability, so these end conditions do not hide missing delivery or a loop.

## Logs, cleanup and disposition

All **2,255 Player.log lines** are covered: 1,764 profiling lines, 71 mod-discovery lines, 50 texture mipmap advisories, 300 allocator diagnostic lines, 12 blanks and 58 remaining lines. The latter were read in full alongside the complete HD debug log. The only error-like candidates are two familiar dynamic Mono fallback probes, four profiler labels containing “Error”, the terminal `captured-errors=0` line, and shutdown allocator bucket fallback counts. Four Direct3D timing advisories are also retained. No gameplay, XML/type-resolution, save/reference, component, cleanup exception or repeated-job error was found. Steam package discovery is not activation; actual loading explicitly lists the seven measured private packages.

Cleanup pauses the private game, removes its observer patches, clears its scene jobs and restores its private tile mean. Its disposable private map is not a player colony and the original saved files remain unchanged. Native **25712** and controller **24160** joined with exit0, no timeout, no cleanup error or surviving owned process. Input desktop remained `Default`; it was never switched. Verify reports `protectedChanges=[]` and exited process state. Its generic manual-review, missing `scenario-observed`, and log-candidate flags remain unchanged; the F25-specific evidence and this review satisfy the bounded manual review instead of relabeling Verify.

Together with accepted original producer `12a8…`, genuinely failing baseline LOAD `c33d…`, accepted upgrade/same-process load `20881…` and provider-absent control `a966…`, this completes the report-specific native acceptance for F25. The earlier setup failures and failed cross-actor remainder run stay failed. Baseline on these providers also lacks HD immediately after fresh conversion, so the result does **not** reproduce every detail of the reporter's fresh-working-then-reload-failing timeline. The demonstrated cause and correction address the generated-definition omission and actual conversion/load/restart behavior. Missing historical HD fields cannot be recovered from an older save. Final combined-PR integration remains a separate obligation.

Reproducible audit: `../../review-restart-native.py`; full machine checks and numbered log classification: `independent-restart-audit.json` and `independent-whole-log-review.json`.
