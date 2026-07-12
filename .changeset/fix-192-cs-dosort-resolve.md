---
"haulers-dream": patch
---

Fix a stray Common Sense warning and make "cook with the most-stocked ingredient first" actually apply under Common Sense (follow-up to #192).

The previous release asked some Common Sense users to report a message about a cooking-sort hook that "did not resolve". That was Hauler's Dream looking for the hook in the wrong place: Common Sense keeps it under a slightly different name depending on which version you run. Hauler's Dream now finds it in both, so the message is gone and the most-stocked-first cooking option layers onto Common Sense's order as intended. When a future Common Sense build genuinely moves it, the option quietly falls back to Hauler's Dream's own batch-cook handling with no warning, since it is off by default.
