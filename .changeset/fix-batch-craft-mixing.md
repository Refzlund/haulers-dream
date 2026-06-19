---
"haulers-dream": patch
---

fix: batch-crafting now mixes ingredients correctly. Recipes that allow mixing (every cooked meal, etc.) were forced to a single ingredient per slot by the batch planner — so a meal bill used only potatoes *or* only rat meat and refused to craft when no single ingredient alone covered a serving. Mixing recipes are now excluded from the fixed-plan batch path and cook through the normal mix-aware path instead (still gathering all ingredients in one trip), so meals mix and craft as expected.
