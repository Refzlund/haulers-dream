---
"haulers-dream": minor
---

**En-route pickup — grab loose items on the way to a job (opt-in, off by default).** The signature "While You're Up" mechanic, re-expressed on Hauler's Dream's inventory hauling: when a pawn sets off on any job and a loose haulable lies roughly along the path, it scoops the item into its inventory first (serviced by the normal storage-aware unload), so the stray item rides to storage on a trip the pawn was making anyway — zero extra round-trips. The detour is tightly bounded by a trip-ratio check (a faithful port of WYU's `CanHaul` cascade) with a Vanilla/Default/Pathfinding accuracy knob, and it respects the per-pawn auto-haul toggle, the carry-weight ceiling, the bleeding gate, anti-double-haul, and (when enabled) the storage-building filter. Enable it in **En-route & Routing** in settings.
