---
"haulers-dream": patch
---

Stop several pawns diverting to the same small transport-loading need.

When a transport pod, drop capsule, or vehicle needed only a small remaining amount of an item, several pawns who were already carrying that item would all divert to deliver it at once. Only one was needed, so the rest arrived to a filled manifest and had to carry their cargo back. An opportunistic delivery now reserves the amount it will bring against that target, so other carrying pawns see the remaining need already covered and keep to their original jobs. The reservation is released if the delivery is interrupted.
