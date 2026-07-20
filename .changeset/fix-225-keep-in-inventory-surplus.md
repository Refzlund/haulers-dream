---
"haulers-dream": patch
---

Fix surplus not being unloaded after setting a "keep in inventory" amount (#225). If a pawn already carried some of an item and you then told it to keep a number of that item, a save upgraded from an older version could pin the keep amount to the pawn's whole held stack, so the extra above the kept number was never put away. The keep amount now excludes items the pawn is carrying to haul, and the keep order schedules the surplus for unloading, so a pawn keeping 7 of an item while holding 9 now delivers the extra 2.
