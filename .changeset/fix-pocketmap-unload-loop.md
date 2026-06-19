---
"haulers-dream": patch
---

fix: pawns inside a Vehicle Framework RV (or any non-home map that has player storage) now unload scooped items into the local shelves/zones instead of looping pick-up → drop forever. The unload routing treated every non-home map as "caravan, load a pack animal", which dead-ended inside an RV that has real storage but no reachable pack animal — and a first attempt to special-case it checked for a "pocket map", which a VF RV interior is not, so the loop persisted. Routing now keys purely on whether the map has player storage; genuine storage-less caravan/raid maps still load pack animals exactly as before.
