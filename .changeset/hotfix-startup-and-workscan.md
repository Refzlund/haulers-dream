---
"haulers-dream": patch
---

fix: two crash fixes.

Fixed a startup crash ("Could not resolve type ... Multiplayer.API.SyncMethodAttribute") that prevented the game from loading when RimWorld Multiplayer was not installed. Hauler's Dream now registers its Multiplayer sync handlers by name instead of through a baked attribute, so no part of the mod's metadata references the Multiplayer API when that mod is absent. Multiplayer behaviour is unchanged when Multiplayer is installed.

Fixed a case where a broken work giver from another mod could stall a colony's work. RimWorld runs every work giver's eligibility check inside one unguarded loop, so a single work giver that throws there aborts the pawn's entire work selection every tick (all hauling, cleaning, and so on stall). Hauler's Dream now contains such a throw: if a work giver that is not Hauler's Dream's own throws while RimWorld checks whether a pawn can use it, the error is logged once and that one work giver is skipped for that scan, so the rest of the pawn's work keeps running. Hauler's Dream's own work givers still surface their errors normally.
