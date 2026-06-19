---
"haulers-dream": patch
---

fix: colonists no longer freeze "standing" next to a transport pod (or map portal) being loaded. When the remaining manifest was something the one-trip bulk sweep couldn't pick up — pawns/corpses to board, or items that are forbidden, out of the loading radius, or too heavy — Hauler's Dream told the game "there is loading work here" but then built no job, so vanilla issued a target-less haul that ended and re-fired every tick (the "started 10 jobs in one tick" error → forced wait). The "is there work?" check now builds the actual bulk job first and only claims work when one exists, otherwise letting vanilla's own loading decide — so the answer can never disagree with what gets built, and the loop is gone.
