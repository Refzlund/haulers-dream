---
"haulers-dream": patch
---

Stop Hauler's Dream from disabling Common Sense's spoiling-ingredient cooking sort.

Both mods reorder a cooking bill's ingredients by rewriting the same piece of game code, and only one can win, so depending on load order Hauler's Dream could win and silently switch off Common Sense's default spoilage-first sort (sometimes with a one-time yellow "[Common Sense] ... patch 0 didn't work" log line). Hauler's Dream now steps aside from that sort whenever Common Sense is installed, so Common Sense's feature always works. Hauler's Dream's own spoilage sort is the same idea Common Sense provides, so a default cook is unaffected, and its separate batch-cooking picker still honors Hauler's Dream's cook-order options either way.

A code-level investigation (cloning Common Sense) also confirmed that running the two mods together is not the cause of the red errors some players attributed to it: no Hauler's Dream fault was found in the interaction. Hauler's Dream tags any error it is responsible for with its own name, so a red error's stack trace shows whether it belongs to Hauler's Dream or elsewhere.
