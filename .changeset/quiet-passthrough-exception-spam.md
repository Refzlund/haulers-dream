---
"haulers-dream": patch
---

Stop the diagnostic log from flooding when another mod faults every tick.

Hauler's Dream tags any error that passes through a method it patches, so a report shows whether the mod was involved (it usually isn't — the tag says so). When another mod's fault repeats every tick, such as a broken pawn whose AI keeps throwing, that tag was written on every occurrence and could fill Hauler's Dream's own report log with hundreds of identical lines, crowding out the rest of its history. The first occurrence is now logged in full, with the stack that names the real source, and the repeats are collapsed to a short recurring note, so the report stays useful. The error itself is still passed through unchanged, exactly as before.
