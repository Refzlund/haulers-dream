---
"haulers-dream": patch
---

Ordering a builder to construct now reserves the site's materials for that builder, so other pawns stop piling on.

Previously, when you told a colonist to construct a building, another pawn (or a work drone) could start hauling the same materials to the frame at the same time. The ordered builder would walk to the site, find it not yet buildable, and wander off, over and over, until the helper finished. It could happen even when the builder was already carrying the exact materials needed. The ordered builder now takes the frame over from any pawns already hauling to it (interrupting their redundant trips) and claims the delivery for itself, the way vanilla already does for a forced haul. Autonomous hauling is unchanged, so unordered deliveries still coordinate normally.
