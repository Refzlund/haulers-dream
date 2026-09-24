# F11 owned drop-fragment recovery v2

Compiled source correction ready for independent review; native acceptance pending. The normal native count-TryDrop call is unchanged. A thread-scoped base SplitOff observer retains the exact requested split and its actual descendants while that call executes. Afterward, only still-live, unspawned, ownerless originals/fragments return through the original inventory's real no-merge TryAdd. Spawned, foreign-owned and destroyed/merged output is untouched. No placement retry or quantity rollback occurs. The original exception remains primary; secondary recovery errors attach to it.

The original input also belongs to this scoped set exactly once. Native count-TryDrop ordinarily removes it only after successful GenDrop, but GenSpawn removes its holder before SpawnSetup; a removal callback can throw with it already ownerless. This precise additional path prompted v2. V1 source/build remains retained. Full-count partial merge is not incorrectly described as detaching the original.

Recovery uses a new partial comp helper to snapshot/restore only original tag membership and optional age. It never creates a tag for untagged input, renews lastYieldTick/CE Hold, or changes Keep. Existing post-command settlement still runs, including when recovery restored the complete count but changed stack identities. Removed quantity includes all exact original/descendant units still owned, preventing a recovered tail from being reported as delivered.

Owned workspace files are InventoryDropCommand.cs, new InventoryDropRecovery.cs and new CompHauledToInventory.InventoryDrop.cs. The isolated original F11 source adds only the structural partial keyword to its original comp declaration; it imports no F12 order/backend code. `source.diff` contains all four isolated differences; `after/` binds the three owned working files. The original runtime/source remains unchanged.

Build passed16.85seconds, zero warnings/errors, joined0, absent deployment guard. `audit.json` passes1,340 checks over505 source inputs,85 actual reference pairs, original inputs and copied runtime content. The read-only compiled comparison resolves member/type signatures and preserves locals/exception handlers. All800 Core methods and surface match. All3,572 unaffected existing HD methods match; the only changed existing method is DropInventoryCountSynced, plus14 new methods in the narrowly reviewed recovery/tag types. The old review tool's hardcoded F02 `expected` flags are not an acceptance oracle; all actual differences are retained.

- Product selection: `509016D6BE86F2324239BE02B60AE07C60A9089D3E7C840AE4C33084090553FC`.
- HD: `3089EBD9A4E1A8691590567E924A1258B2B3A8BCD9F3D7200270FBFC03059C39`; MVID `bad66a40-8e81-43c1-ba73-c0961818ac86`.
- Core: `31E6D6864D0C8B39095BAE50732010A82B3F6C2D8CAB96681801D0B5A12DA2BC`; MVID `de076e4d-aeff-43f3-b0ee-f67b0b80a7e9`. Its changed PE/build identity does not conceal a method or surface change.
- Runtime is the exact new root in selection (`f11-fault-recovery-20260924/runtime-v2`). Original15D6/C09 remains the native negative-control product.

The separate native-fixture contract defines exact split, partial/complete merge, oversized descendant and whole-original callback controls plus the five normal CE/Sidearms commands. This source/build handoff closes none of those native obligations. No Prepare, native launch, staging, commit or ledger change was performed.
