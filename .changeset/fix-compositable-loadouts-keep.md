---
"haulers-dream": patch
---

Stop the unload and re-pickup loop for items a Compositable Loadouts loadout tells a pawn to keep (#200).

With Compositable Loadouts, a pawn assigned a loadout that keeps something (for example, medicine) would have Hauler's Dream haul it away as surplus, then the loadout would send the pawn to pick it back up, over and over. Hauler's Dream now counts what a pawn's Compositable Loadout keeps as inventory that pawn should hold onto, so it leaves that amount alone and only hauls away the true surplus. The back-and-forth stops.

This reads the loadout through reflection and does nothing when Compositable Loadouts is not installed.
