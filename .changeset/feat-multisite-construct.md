---
"haulers-dream": minor
---

Multi-site construction delivery: automatic and shift-clicked ("Prioritize construct") builds now load materials for several nearby sites into inventory in one trip, instead of serving one site per trip — previously only the route planner did this. When a pawn delivers to a cluster of same-material build sites within 8 tiles whose combined demand exceeds one armful, it loads the whole cluster's demand at once and delivers to each site in turn, far fewer stockpile trips for a fence line, a row of sandbags, or a batch of small builds. It still finishes the site it's already working before making a load trip (no abrupt interruptions), and a single-material-per-job rule keeps deliveries clean. Default on; a new "Load several nearby sites' materials in one trip" toggle sits under "Carry materials in inventory for big single builds" in the settings (and requires it).
