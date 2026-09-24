---
"haulers-dream": minor
---

Choose how many items to drop from a pawn's inventory using the ordinary Gear-tab Drop action. The amount dialog includes a slider, integer input and the remaining quantity, with cancellation and checks for a changed pawn or stack.

Retain hauling tags and Keep settings for items still carried, settle actual partial placement without retrying already dropped items, and refresh inventory capacity immediately while paused. Add translated controls and messages in all supported languages.

If a native split or placement callback throws, return only this command's surviving, ownerless fragments to the original inventory. Preserve the original exception, existing tag ages and provider state; never recreate merged items or retry units already placed.
