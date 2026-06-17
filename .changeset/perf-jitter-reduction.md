---
"haulers-dream": patch
---

Performance: reduced micro-stutter on busy, heavily-modded colonies.

A repo-wide allocation/CPU audit eliminated per-tick and per-scan heap allocations and redundant recomputation on the hottest paths (the usual cause of RimWorld gen0-GC micro-jitter):

- The movement-speed overload penalty no longer re-walks a pawn's full apparel + equipment + inventory mass every cell it moves — it's computed once per pawn per tick.
- Removed per-frame work and a game-state side effect from the inspect pane when a loaded pawn is selected.
- Eliminated boxed enumerators and throwaway collections from the haul/load work scans, and per-call reflection allocations in the Combat Extended / Common Sense / Vehicle Framework integrations.
- Various smaller allocation cleanups (debug logging, spoiling-first sort, route selection).

Also adds an allocation-assertion performance test harness (`bun run test:perf`) that keeps the pure decision logic provably allocation-free going forward.
