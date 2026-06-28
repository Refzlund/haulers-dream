---
"haulers-dream": patch
---

Arriving pawns now put hauled cargo into proper storage instead of the nearest pile.

When a pawn arrived somewhere with a full inventory (returning from a caravan, dropping in by pod or transporter, or teleported home by a psycast), the game's "unload everything" routine dropped its cargo into the closest storage it could find, ignoring your storage filters and priorities. Hauler's Dream was meant to step in there and route the cargo it had been hauling to proper storage instead, but a long-standing mix-up of two similarly named jobs meant that step never actually ran. It runs now, so cargo a pawn was carrying for Hauler's Dream lands where you'd want it on arrival, the same as a normal haul trip.

This only takes over on maps where there is somewhere to put the cargo (your home base, or an away map you've set up storage on). On a bare map with no storage, the cargo keeps riding home in the pawn's inventory exactly as before, so a pawn arriving on, say, an escape-ship visit just goes about its business instead of getting stuck retrying an unload it can't finish.
