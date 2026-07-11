---
"haulers-dream": patch
---

Fix a frame-rate drop when typing in the settings search box.

Typing in the settings-window search field could stutter the game: the search re-scored every registered option from scratch on each keystroke (allocating heavily inside its typo-tolerance matching) and rebuilt its grouped results every frame. The search now reuses its scratch buffers, lowercases the option text once when the list is built, and caches the grouped results per query, so typing stays smooth.

Note: a hard cap to 60 FPS while any text field is focused comes from a separate frame-limiter mod, not from Hauler's Dream.
