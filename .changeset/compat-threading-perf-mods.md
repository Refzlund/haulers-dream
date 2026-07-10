---
"haulers-dream": patch
---

Verify and strengthen compatibility with the 1.6 multithreading and performance mods (RimThreaded, RimSmooth).

A code-level investigation (including cloning RimThreaded's source) confirmed Hauler's Dream stays compatible with the multithreading and performance mods available for 1.6: RimThreaded - Continued parallelizes only particles, background random numbers, and sound, and RimSmooth is single-threaded, so neither runs Hauler's Dream's hauling or job code across threads. Hauler's Dream was already built for thread-safety on its busy code paths. As forward-insurance for a future mod that threads pawn AI, two internal per-tick caches were hardened to match their already thread-safe siblings, with no change to how the mod behaves for anyone.
