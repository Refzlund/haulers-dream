---
"haulers-dream": patch
---

fix: shuttle/transporter pawns stuck "waiting" after loading, and psychic-ritual target being emptied.

Shuttle and transporter loading: after the cargo was fully loaded, the colonists assigned to board would keep "waiting" and had to be forced in. Hauler's Dream's board gate kept blocking based on its own internal loading bookkeeping, which could linger a moment after the goods were already aboard. The gate now lets a pawn board the instant the group's cargo manifest is empty (the game's own "loading done" signal), so pawns board on their own. It still never boards before the cargo is physically in, so nothing launches early.

Psychic rituals: a ritual target could have its inventory emptied as a ritual started, which cancelled the ritual. A previous fix stood Hauler's Dream down for pawns taking part in a ritual, but a ritual TARGET is directed differently and slipped through. Hauler's Dream now also stands its automatic inventory handling down for any pawn driven by a ritual/quest duty (not just full ritual participants), and no longer bulk-empties a pawn that is busy with a directed activity. Explicit player orders are unaffected, and normal pack-animal unloading still works.
