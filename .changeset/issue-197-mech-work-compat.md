---
"haulers-dream": patch
---

Fix a compatibility crash on malformed modded pawns, and stop foreign errors being blamed on Hauler's Dream (#197).

Some modded pawns, such as a Dead Man's Switch "humanoid mech" summoned by WVC's voidlink, are built in a way that makes RimWorld's own work-type check throw whenever anything asks whether they can do a job (vanilla reads a couple of pawn fields without confirming they exist). Hauler's Dream offers hauling to mechs, so it was one of the things asking, and a single broken pawn could interrupt a hauling scan. Hauler's Dream now treats such a pawn as unable to do that work and moves on, reporting the fault once with the real source named, so one malformed pawn no longer disrupts hauling.

It also no longer stamps its own name onto errors that merely pass through the two work-type methods it lightly patches. When the real cause is vanilla or another mod, the error now keeps its true origin in the log instead of pointing back at Hauler's Dream, so these get reported to the right place. The underlying summon failure itself is a defect in the other mods' pawn setup, not something Hauler's Dream can fix, but it no longer makes things worse or takes the blame.
