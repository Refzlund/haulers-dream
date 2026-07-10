---
"haulers-dream": patch
---

Fix the "Unload inventory" button appearing wedged among a leader's ability buttons instead of at the end of the command bar (issue #140).

Hauler's Dream adds two per-pawn command buttons, an auto-haul toggle and an "Unload inventory" action. Their sort position was set to a middling value, which usually put them near the end but could strand them in the middle of the row when a pawn had many buttons whose own sort values straddled it. This was most visible on an ideoligion leader, whose ability buttons pushed the Unload button into their midst. The two buttons now sort to the very end of the command bar, with Unload last, so they no longer interleave with abilities or anything else. An earlier attempt at this blamed an unrelated mod and did not actually move the buttons; this one fixes the sort itself.
