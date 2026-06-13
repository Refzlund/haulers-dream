---
"haulers-dream": patch
---

**Compatibility: never strand items in a non-human pawn's inventory.** Bulk hauling (and the
caravan pack-animal loader) now use the same "who may haul into inventory" rule as scooping and
unloading — humanlike colonists, or colony mechs when *Allow mechanoids* is on. Previously bulk
hauling only excluded disallowed mechanoids, so a modded **non-mechanoid "robot/animal worker"**
race (one that's set up to do colony hauling, e.g. the Housekeeper Cat) could have items swept into
its inventory that the auto-unload — which never services non-human pawns — would then refuse to put
away, leaving them stranded. Such pawns now simply keep vanilla single-stack hauling, untouched by
Hauler's Dream. Normal colonists and mech haulers are unaffected.

Also documented (no behavior change): verified compatible with **Allow Tool** and **Keyz' Allow
Utilities** "Haul Urgently" (their urgent hauls are never swept by HD; HD is never mistaken for Pick
Up And Haul; HD auto-honors Keyz' "Do Not Haul"), and with **Adaptive Storage Framework** / **Neat
Storage** (HD composes through the same vanilla storage validators ASF extends). See COMPATIBILITY.md.
