---
"haulers-dream": patch
---

Fixed pawns dropping scooped work-yields on the ground while they keep working — e.g. a grower scoops a harvest, then drops it on the field as it carries on sowing. The anti-stranding auto-drop was treating a Hauling work-type priority of 0 as "can never haul, so this cargo is stranded." But a pawn the player set to never haul (a dedicated grower or crafter) still scoops its yields and still delivers them through Hauler's Dream's own unload trips, which don't use the vanilla hauling job — so its cargo was never actually stranded. A Hauling priority of 0 no longer triggers the drop; only a pawn that is genuinely incapable of hauling, or a stuck mechanoid, does.
