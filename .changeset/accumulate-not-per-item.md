---
"haulers-dream": patch
---

**Pawns now overload and accumulate, instead of tripping to storage after almost every item.** This
restores the mod's core behaviour: a colonist deconstructing, mining, or harvesting keeps scooping the
materials into its inventory — overloading past 100% up to the smart-overload ceiling (the Overload
slider; ~2× capacity at the default "Fair") — and makes **one** trip when it's full, instead of
hauling each stack to storage and walking back.

Two over-eager triggers were the cause: pawns were unloading the instant they passed 100% capacity
(which defeats the whole point of overloading), and again on the first momentary "no work right now"
between items (constant in a busy colony). Now a pawn unloads only when it's genuinely **full** (at the
overload ceiling) or **done** — i.e. it has stopped picking things up for a while (a new "accumulate
window", ~1 in-game hour by default, adjustable in settings). It still always unloads eventually (when
full, when its work is finished, or on the periodic sweep), so nothing is carried forever.
