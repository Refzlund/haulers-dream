---
"haulers-dream": minor
---

feat: optional batch crafting under Common Sense.

When Common Sense is installed with its "advanced cleaning" or "haul all ingredients" features on (both default on), Hauler's Dream hands the whole cook/craft flow over to Common Sense to avoid an ingredient ping-pong loop, which meant batch-flagged bills fell back to one item at a time.

New opt-in setting "Batch even with Common Sense active" (Mod options, under Crafting batches; only shown when Common Sense is installed, off by default) lets bills you marked for batching still batch while Common Sense keeps handling everything else. This is safe because batch crafting runs on its own separate job that Common Sense never touches, so its ingredient-hauling and cleaning can't interfere; the looping inventory-gather and ingredient-share paths stay handed over to Common Sense regardless. Existing Common Sense users are unchanged unless they turn it on.
