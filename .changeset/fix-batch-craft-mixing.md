---
"haulers-dream": patch
---

fix: batch-crafting now mixes ingredients correctly across repetitions. Recipes that allow mixing (every cooked meal, plus kibble/pemmican/chemfuel/beer) couldn't batch properly — the batch planner froze a single ingredient def per slot, so a meal bill used only potatoes *or* only rat meat and refused to craft when no single ingredient alone covered a serving. The batch planner and driver are now mix-aware: each repetition's ingredient mix is chosen by value from current stock at craft time (mirroring vanilla's own mixing fill), and the batch is sized by total available nutrition. Meals and other mixing recipes batch many reps from one pre-load again, mixing exactly as a normal single craft would.
