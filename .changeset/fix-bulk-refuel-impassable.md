---
"haulers-dream": patch
---

Bulk refuel: fix a crash and revive the feature for impassable refuelables (e.g. Advanced Power Plus's advanced nuclear generator). Hauler's Dream anchored its one-trip fuel sweep at the refuelable's own cell, but a generator, deep drill or reactor sits on an impassable footprint with no passable region there — which made RimWorld's fuel finder dereference a null region and throw, freezing colonists in a job-search loop and breaking the building's right-click menu. The sweep now starts from the hauler's own (always-passable) cell, exactly as vanilla's normal refueling does, so it no longer crashes and once again bulk-refuels generators and drills instead of silently falling back to one-stack-at-a-time. Fixes #34.
