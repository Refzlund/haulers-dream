---
"haulers-dream": patch
---

Crafters now drop hauled items into storage on the way to the next craft, instead of carrying them through and dropping them on the floor (#201).

When a crafter grabbed a loose item on the way to a workbench (the "while you're up" pickup), it could carry that item all the way through the craft and then stand around before finally dropping it on the ground. Now, when a crafter is about to start a crafting or cooking bill and a stockpile sits roughly on the way to its ingredients, it drops the carried surplus off there first, so the item reaches storage while the pawn is fetching materials for the next craft.

This reuses the same "drop it off on the way" behavior doctors already use during elective surgery, honors the existing unload detour distance setting, and never sheds the materials the imminent craft itself needs.
