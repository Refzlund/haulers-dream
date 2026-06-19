---
"haulers-dream": patch
---

fix: pawns inside a Vehicle Framework RV (or any persistent pocket-map sub-base) now unload scooped items into the RV's own shelves/zones instead of looping pick-up → drop forever. The unload routing treated every non-home map as "caravan, load a pack animal", which dead-ended inside an RV that has real storage but no reachable pack animal. Such maps now route to the storage-unload path; genuine caravan/raid maps (no persistent storage) still load pack animals exactly as before.
