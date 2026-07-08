---
"haulers-dream": patch
---

Fix colonists periodically freezing in place for several seconds while harvesting, mining, or deconstructing, especially with several of them working at once.

Hauler's Dream re-checks whether a colonist should drop off its accumulated load on the way to its next job every time it picks up a new one. Once a colonist working through a field, a mineral vein, or a row of walls was carrying enough to make that check worth running, it reran a real storage search on every single plant, chunk, or wall in the run with nothing slowing it down, because that search only ever backed off after an actual drop-off, not after a "not worth it right now" answer. With several colonists doing this at the same time, the searches piled up and the game visibly stalled. The check now backs off for a little while after it runs regardless of the answer, so it can no longer be repeated on every single item in a run.

While looking into this, the same gap issue #152 fixed on the end-of-run drop-off check turned up on two of its siblings, the "drop it off on the way" check and the opt-in "drop it off before a long walk while overloaded" check: both would consider a colonist a candidate to divert as long as it was carrying anything at all, even if none of it was actually surplus to store. Both now require some real surplus first, same as every other drop-off trigger.
