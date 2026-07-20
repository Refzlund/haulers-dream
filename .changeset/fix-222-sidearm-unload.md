---
"haulers-dream": patch
---

Fix pawns trying to unload a carried sidearm weapon (#222). With Simple Sidearms (or Grab Your Tool) and "put away surplus inventory" turned on, a pawn carrying both a remembered sidearm and a looted copy of the same weapon could tag its own sidearm for unloading and put it away. Remembered sidearms and carried tools are now excluded from surplus adoption, matching every other place Hauler's Dream already protects them, so a colonist keeps the weapons it chose to carry and only the looted duplicate is hauled off.
