---
"haulers-dream": patch
---

Common Sense compatibility: Hauler's Dream now detects when Common Sense's "haul ingredients" / "advanced cleaning" takes over the vanilla crafting flow and steps aside, fixing the rare infinite loop where a crafter would repeatedly pick up ingredients, walk to the bench, then unload them again. Also hardened HD so it never ships a bill's ingredients off to storage while the crafter is about to consume them — protecting against any mod that rewrites the bill flow.
