# F38 corrected capacity producer: accepted for original-save replay

Independent review of `bd616fc07307445084fcdd05a450ff39`, 24 September 2026. **Accept this producer and its exact original checkpoint for F38-REPLAY.** This does not establish the second execution's equality or close all F38 work. The reviewer did not author this product/host and performed no Prepare, native launch, product/fixture change, staging or ledger update.

Reviewed the capacity contract, complete scene including observation/save/load/cleanup paths, unchanged replay controller admission, correction source and compiled comparison, failed `263bf…` diagnosis, all **54 assertions and 124 contiguous events**, all eleven complete boundary records, original native XML, physical custody, logs, actual module bindings and process/Verify receipts. `independent-audit.py` records **60/60 read-only checks**, including hashes for **812 selected input entries, 1,486 copied runtime files, ten actual images and twelve metadata bindings**. Retained raw evidence matches the original runtime evidence byte for byte.

## Why this resolves the failed producer boundary

The earlier `263bf…` remains failed **48/51, U0**. Native TakeInventory reserves with MaxPawns10, while the old successful-reservation ownership predicate required1; the old A sweep actually ended InterruptForced and replacement work happened to produce the same 51/32 arithmetic. That was not preservation of the original job.

The correction is confined to `ExplicitSourceOwned`: expected MaxPawns10 for the already checked exact TakeInventory def/driver;1 for the other supported drivers. Default layer, real source/owner/reservation, forced job, positive ownership count, exact old/new driver and job identity gates remain. The compiled comparison contains one changed method out of2,945; the original Core binary is selected. Compared with the failed native, nine actual images are identical and only HD changes to **207C6A58E6852838C923012D75095F3B5FC788C474038FD6824F43BB74616D12**, MVID `be57dd76-a1b4-4870-8722-d8cb3221942d`. Host remains **3390083F…** and Core **815AAE0C…**.

The corrected trace proves identity as well as arithmetic. A's original **BulkHaul25**, admitted tick9 for17/25/41, genuinely picks17 at196. Keep7 becomes effective after one ordinary tick. At197, the raw83 row has effective76 and C has7 free. B's native **TakeInventory32** retires only the25 source: A remains job25, cursor1/toil1, with the invalid/zero queue slot retained, the41 reservation and raw83 row unchanged. No A end event occurs at handoff. The same paused tick shows A effective51, B0 and C free32. C's actual **BulkHaul34** requests precisely32; effective51+32 fills83. All capacity probes guard authoritative state and RNG equality.

## Physical execution and stability

A25 succeeds at373 with58 held and51 surplus. B32 succeeds at558 with the original25 held and untagged. C34 succeeds at1085 after splitting32 from the distant40, leaving8 on the ground. The original three productive jobs each have exactly one successful end event.

A unload40 starts374, carries51 while preserving7, fills the original67 stack to75 at1137, and places43 in the second cell at1154. It ends **Incompletable at1156** after all51 have physically reached storage. This end condition is retained as observed; it is not relabelled Succeeded. C unload64 places its32 into the second stack at1719 and succeeds1721. Neither a repeated productive pickup nor cargo loss follows.

The final inventory is exactly **190 =150 stored in two75 stacks + A's7 kept + B's25 personal +8 ground**. No hand cargo remains. All ten physical transitions conserve190. Native settled observation spans1721–2023 (**302 ticks**); final reservations and claim rows are empty and B remains untagged. The scene continuously checks actual pawn health and ambient temperature; native warm-up reaches12.1972628°C before controlled actors/work.

## Exact checkpoint admitted

The original native save is pre-command at tick8: actors **Human37528/37531/37534**, native current **Wait21/22/23**, empty inventories/queues, no source reservations, nextJobID24 and nextThingID37542. Raw XML contains the genuine source stacks **Cloth37538×17,37539×25,37540×41,37541×40**, existing stored **37537×67**, and the exact Cloth-only Critical stockpile cells `(154,0,107)` and `(155,0,107)`. Save-before/save-after native state is equal. The fixture does not write job/Thing/ID counters, hauling tags, plans or capacity rows. The controlled mean temperature is a saved fixture input; it is not claimed to be the original quicktest world.

| Input | SHA-256 |
| --- | --- |
| Original native save / retained original | `E10F40C2418349AF3D7A46E108A2E31F2FB4B437590DFBE17A58193C08AA15CD` |
| checkpoint.txt | `2B68F85CA73838AC1442D03C4CABDA47F2DF84C34608EA84E510D712C40C70F9` |
| replay-boundaries.txt | `F58F5AA7745AF0E2A7828225F3BD2D4E7F8A9AD3B81A7EF99A34F11ACA4F492B` |
| Passing result | `545D11304FB93C97964855B436352E7CCF694ECF575E5548538BF8ECA4E46AA0` |

All eleven ordered boundary lines exactly match their raw event details: one-tick-ready, A before/after, first pickup, Keep before/after, handoff-ready, B before/after, C before/after. Per-command seeds43801–43804 and inner RNG states are retained without normalizing job IDs, queue order, counters, targets, ownership or counts. Native ticks between commands remain real. The replay controller requires this original successful producer, protects its save/record/result/log/manifest/boundaries and all ten images, and verifies exact copied hashes. LoadedGame must show saved tick8 equality before the first tick; subsequent commands must match every line. These are requirements for the next capture, not results already claimed here.

## Logs and isolation disposition

Reviewed the1,858-line Player.log, initialization/profiler output and allocator tail, plus complete HD debug log. There are two known Mono fallback notices, a Header.png mipmap advisory and Direct3D timing advisories; no captured Unity error, unresolved save reference, native exception, reservation-release error or handoff fallback warning appears. Four active packages and ten actual images match the manifest; discovered Workshop entries in the mod inventory are not extra active providers.

Native **29536** and private controller **12536** joined exit0, with Default input desktop throughout, no desktop switch, no surviving owned processes and no cleanup errors. Verify retains **protectedChanges=[]** and its manual-review/log flags. Its generic missing `scenario-observed` notice does not conceal a missing F38 trace: this dedicated fixture emits `f38-*` events and the complete successful terminal chain is present. The controller's `not-verified` status is preserved; this document supplies only the bounded human acceptance above.
