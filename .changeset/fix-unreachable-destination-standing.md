---
"haulers-dream": patch
---

Fix a colonist standing still for a long time instead of getting on with the rest of its load. When a colonist set off to put something away and then could not actually walk to the destination — a shelf that has just been walled in, a stockpile behind a doorway someone is standing in, a container another mod has moved somewhere awkward — RimWorld ends the trip and has the colonist wait a few seconds. Hauler's Dream then dropped the load on the floor, picked it straight back up, and sent the colonist at the same unreachable destination again, on exactly the same rhythm as that wait. The colonist could stay on "Standing" for hours with a full pack.

Now a stack whose destination cannot be reached is put back in the pack instead of dropped, set aside for the rest of that trip, and left alone for about ten seconds before anything offers it again. The colonist carries on delivering everything else it is holding and comes back to that one later, once the way is clear. If several destinations in a row turn out to be unreachable, the colonist ends the trip early and keeps the load rather than pacing. Nothing is lost or forgotten either way — the goods stay tracked in the pack and go out on the next trip. A plain RimWorld haul that fails to reach its cell now counts towards the same protection, so it too is left for a moment rather than retried on the spot.

The same "skip this one and keep going" recovery now also covers the trips that gather goods for a transporter, a map portal, a vehicle, a pack animal, a refuelling job or a construction site: one pile that cannot be reached no longer throws away everything the colonist had already collected for that trip.

A room of shelves that has been sealed off permanently was already handled correctly and is unchanged. Colonists never pick storage they cannot reach in the first place, so they simply leave those goods for storage they can get to.
