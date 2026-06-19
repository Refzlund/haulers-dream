---
"haulers-dream": patch
---

fix: bulk-refuelling a building whose own cell is impassable — a deep drill, a generator, a Save Our Ship 2 engine, a mod's bulk-fed pot — no longer throws a `NullReferenceException`. Vanilla's fuel finder dereferences the *region* of the refuelable's cell with no null check, and an impassable cell has no passable region; Hauler's Dream now detects this up front (mirroring vanilla's own region test) and cleanly defers to vanilla's single-stack refuel, which works from the pawn's position. This removes the continuous SOS2 ship-engine refuel error and the float-menu error. It is a precondition guard, not a swallow: the bulk optimization is simply skipped for such buildings (the refuel still happens), and any other fault still surfaces.
