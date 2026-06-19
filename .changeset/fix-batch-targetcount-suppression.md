---
"haulers-dream": patch
---

fix: a "Do until you have X" bill no longer silently stops being worked while finished products sit in a colonist's inventory. Hauler's Dream counts products in flight toward storage so the colony doesn't overproduce — but it was also counting products a pawn keeps (food, drugs, loadout) that never reach storage, permanently inflating the count so the bill read "already satisfied" and was never offered to any pawn (the bench appeared dead with no error). Only the surplus actually heading to storage is now counted, so the bill resumes correctly.
