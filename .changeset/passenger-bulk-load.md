---
"haulers-dream": minor
---

feat: shuttle/transporter passengers now bulk-load the very transport they're about to board.

Previously the pawns you selected to ride a shuttle (or load + board a transporter) did vanilla one-stack-at-a-time loading, because Hauler's Dream stands its bulk-load down for any pawn directed by a Lord (which a boarding passenger is). Bulk loading only happened if a separate, non-boarding hauler was free. Now a boarding passenger is allowed to bulk-load the exact transport it's assigned to board: it sweeps its share of the cargo in one trip, deposits it, then boards. The carve-out is tight, only the pawn's own shuttle/portal, so ritual, caravan, and quest inventories stay protected.

If such a passenger is interrupted mid-load (it gets hungry, drafted, has a mental break) while carrying gathered cargo, it now deposits that cargo into the transport on its next step instead of carrying it off, so loading can't get stuck.
