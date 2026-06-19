---
"haulers-dream": patch
---

fix: Hauler's Dream no longer empties a pawn's inventory out from under a ritual, ceremony, or other directed group activity. A pawn gathering offerings for a ritual (for example bioferrite for an Anomaly psychic ritual) carries them on purpose, but HD's automatic unload would haul them off to storage before the ritual ran, failing it. HD now stands down its automatic scoop / adopt / unload for any pawn currently engaged in a Lord-directed activity — rituals and ceremonies, caravan forming, parties and gatherings, quest lords (vanilla and DLC) — and resumes normally once the activity ends. Explicit player orders are unaffected.
