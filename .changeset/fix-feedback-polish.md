---
"haulers-dream": patch
---

Player-feedback fixes & polish:

- **Microstutters fixed.** The automatic bulk-haul planner no longer runs its full "is this sweep worth it?" computation (and allocations) for every loose item a colonist considers — a cheap allocation-free pre-check rejects the common no-sweep case first, the cross-pawn claim scan is cached per tick, and scratch buffers are reused. Smooths the camera/character jitter on cluttered maps with several haulers.
- **Bulk-sweep keeps tidy stacks.** When a pawn sweeps many loose stacks of the same thing (e.g. scattered harvested food) into its inventory, they now consolidate into one stack instead of staying as many small ones — without ever merging into the pawn's own carried/personal stock.
- **Crafting-loop conflict (with Common Sense) fully closed.** Completes the Common Sense compatibility: the last ingredient-sharing path now also cedes to Common Sense when it owns the crafting flow, so the "gather → walk to bench → turn back → empty inventory" loop can no longer occur with both mods' default options on.
- **"Allow mechanoids" setting now has a description** explaining it governs mech scooping/hauling (vanilla mech construction delivery is separate).
- Clarified the carry-limit setting tooltip: the limit is a mass budget over apparel + equipment + inventory; items carried in the hands are not counted.
