---
"haulers-dream": patch
---

Hardened against future RimWorld updates: Hauler's Dream now applies each of its game patches independently, so if a single hooked vanilla method is renamed or removed in a future RimWorld build, only that one feature is disabled (with a clear log line) instead of the whole mod failing to load. Also made the partial-build "deliver from inventory" feature resolve its one reflected field lazily so a future rename degrades to vanilla behavior rather than erroring.
