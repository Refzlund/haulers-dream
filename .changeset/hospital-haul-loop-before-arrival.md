---
"haulers-dream": patch
---

Fix colonists still looping while carrying a hemogen pack (or another stackable item) toward storage in a prison or hospital, pacing back and forth without ever dropping it off even when storage is free.

The earlier fix only bounded the loop when a hauler kept failing to place an item after reaching a storage cell. This one is different: the destination cell keeps going invalid while the hauler is still walking to the pack or carrying it, so the job fails and drops the pack before the hauler ever arrives, and the work scan starts an identical job right away. Hauler's Dream now also watches for a stackable item whose storage hauls keep failing in quick succession and, after a few, stops offering it to the automatic haul scan for a short while so the pointless pacing ends. It sorts itself out once storage settles, and a manual haul order always works immediately.
