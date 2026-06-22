---
"haulers-dream": patch
---

fix: prevent a load crash (jobless pawn) when a colonist is saved mid "clean before crafting" with Common Sense installed.

With Common Sense's "clean the area before crafting" turned on, a crafting pawn parks floor filth in its bill job while it cleans before working. If you saved at that exact moment, the game could throw "DoBill on non-Billgiver" while loading and leave the pawn permanently jobless (followed by a cascade of errors). Hauler's Dream now recovers the crafting station from the bill itself when this happens, so the colonist resumes its bill normally and the save loads cleanly. The fix also rescues saves that are already in this broken state, including ones where Common Sense has since been removed. It only changes behavior on the crash path, so normal crafting is unaffected.
