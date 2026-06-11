---
"haulers-dream": patch
---

Fixed a "started 10 jobs in one tick" error loop in Haul to stack when haulers with different allowed areas worked the same items: a stack-destination computed for one pawn could be handed to another pawn it was invalid for. Destinations are now computed per hauler, and large colonies with many same-priority stockpiles find partial stacks more reliably (the scan starts in the right storage group instead of burning its budget elsewhere).
