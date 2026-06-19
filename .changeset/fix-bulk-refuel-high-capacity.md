---
"haulers-dream": patch
---

fix: high-capacity refuelables (e.g. a large bulk-fed cooking pot from a mod) can now be bulk-refuelled. The bulk-refuel order required the ENTIRE remaining fuel deficit to be reachable in a single sweep, which rarely holds for a big refuelable, so the order silently did nothing — the "Prioritize bulk refuelling" option no-op'd and the fill couldn't be forced. It now accepts a partial sweep, filling what it can reach now and topping up on a later trip, the same way vanilla's single-stack refuel already tolerates fuelling a little at a time. (Other refuel mods' work — turrets, Combat Extended, Vehicle Framework — is untouched, as before.)
