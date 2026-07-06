---
"haulers-dream": patch
---

Stop pawns from reading books nonstop until they starve to death when another mod (or stale item state) breaks a job check Hauler's Dream participates in.

When a thinking step raises an error, RimWorld logs it once (an entry that is easy to miss among repeats) and skips that step, and a pawn absorbed in a book only rechecks its urgent needs every few hundred ticks. Hauler's Dream adds logic to several of those thinking steps (put the load away before eating or sleeping, eat a meal a colonist is carrying, shed cargo before a mech charges), and that logic touches many other mods' items and compatibility hooks. If any of that raised an error, the whole food check failed every single time, while the recreation check kept handing the pawn its book: the pawn read nonstop, refused every other task, and eventually starved (issue 122). The same failure on the charge check could drain a mech to forced shutdown.

Hauler's Dream's additions to the food, rest, joy, work, unload, and mech charge checks now contain their own failures: the problem is reported once in the log (with the stack trace pointing at the actual culprit), and the vanilla decision stands, so the pawn still eats, sleeps, works, and charges no matter what failed inside the enhancement. The carried-meal search also skips items that are not actually edible instead of tripping over them. A new build guard keeps these boundaries from regressing.
