---
"haulers-dream": patch
---

Fix the "Prioritize hauling" order always sweeping like "Haul everything nearby", ignoring the bulk-haul trigger setting (#223). An internal carve-out that lets an oversized stack ride in the inventory in one trip was also, unintentionally, triggering the full neighborhood sweep, so with stack-size mods almost every ordered haul swept everything and the "only from the second order" setting had no effect. A single ordered haul now honors the bulk-haul trigger: it carries the one (even oversized) stack without sweeping unless bulk hauling is set to Always or a second nearby haul has also been ordered.
