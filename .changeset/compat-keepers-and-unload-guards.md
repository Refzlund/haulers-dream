---
"haulers-dream": patch
---

Broadened mod compatibility with a set of general patterns (each helps any mod with the same kind of feature, not just the named one):

- **Item Policy**: Hauler's Dream now respects a pawn's per-pawn Item Policy inventory-stock counts, so it no longer strips items the policy wants kept (which previously fought Item Policy's re-fetch in an unload/re-fetch loop). The kept count feeds Hauler's Dream's existing count-aware keep, so the surplus above the policy amount still unloads normally. Inert without Item Policy.
- **Foreign unload jobs**: Hauler's Dream's inventory-unload substitution now only replaces vanilla's own unload, never another mod's custom unload job (e.g. Common Sense's marked-items unload, or a carrier-unload routed through the work scan such as Bulk Load For Transporters), so those mods' unload flows are left intact.
- **Work-selection ordering**: Hauler's Dream's two opportunistic work-scan hooks now run last, so they react to the final chosen job after a job-substituting mod (e.g. While You Are Nearby) has had its say, instead of racing it.
