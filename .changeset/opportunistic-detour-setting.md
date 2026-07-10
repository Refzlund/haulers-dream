---
"haulers-dream": patch
---

Add two "detour distance" settings that control how far pawns step out of their way to take a free opportunity, each showing the extra-tile budget.

Two separate behaviors let a colonist make the most of a trip it is already taking, and each now has its own knob. Both use the same four levels, and the picker shows the extra-tile budget so the choice is not just a bare word (Off = 0, Short = about 4, Standard = about 10, Long = about 20 extra tiles of travel):

- "Grab-on-the-way detour" (under Routing) sets how far a pawn already walking to storage will step aside to grab a loose item it passes. Default Standard (about 10 tiles). Only applies while en-route pickup is on.
- "Unload detour during important work" (its own section in the Unloading tab, tied to automatic unloading) sets how far a pawn on non-emergency medical, rescue, or warden work will step aside to drop a scooped load at storage, typically on the trip out to fetch the operation's medicine. Default Short (about 4 tiles), so a doctor sheds the load on a near-free pass-by instead of only when storage is exactly on the path.

Each level is measured as extra straight-line tiles over going straight, so a little goes further on a long haul. Off turns the behavior off entirely: a hauler leaves items it walks past, and a doctor carries its load through the whole task. True emergencies such as tending the wounded are never diverted, whatever the unload setting.
