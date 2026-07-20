---
"haulers-dream": patch
---

Clarify the "strict carry weight" and "Drop, then collect" tooltips. A Steam report noted that pawns "carry very large quantities" with strict carry weight on and every yield set to "Drop, then collect", and asked whether it was a bug. It is working as intended: strict carry weight caps a pawn's inventory at 100% of its carry WEIGHT (a full load is still a lot of stacks), and that cap applies the same whether yields go "Collect directly" or "Drop, then collect". The only difference is that "Drop, then collect" leaves each yield on the floor first, where your other haulers and Bulk hauling also sweep it into their packs, each still capped at 100%. The tooltips now say this and point at the "Carry limit" slider as the lever for smaller loads, and a regression test pins the strict cap so it cannot silently drift on that path.
