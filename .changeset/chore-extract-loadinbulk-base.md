---
"haulers-dream": patch
---

Internal hardening (no behavior change): the three bulk-loading jobs that fill transporters/shuttles, map portals, and Vehicle Framework vehicles shared a near-identical multi-phase scaffold — sweep loose stock into the backpack, carry it to the target, then deposit while conserving exact item counts. That scaffold is the most safety-critical code in the mod (a mistake means lost or duplicated player cargo), and the three copies had already begun to drift apart. It is now a single shared base class (`JobDriver_LoadInBulkBase`) with only the genuinely target-specific deposit step left per job, removing ~640 lines of duplicated logic and the drift risk. The pack-animal loader and the carrier-unload job were deliberately left separate (they predate this design and differ too much to fold in safely). Save games and in-game behavior are byte-for-byte unchanged.
