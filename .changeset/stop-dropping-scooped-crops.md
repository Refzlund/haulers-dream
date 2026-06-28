---
"haulers-dream": patch
---

Stop pawns from dropping scooped crops on the ground again.

Harvested crops, milk, eggs and other raw food a pawn had picked up to haul would sometimes get dumped back at its feet instead of carried to storage. It came back recently with psychoid leaves. The cause is a vanilla routine that clears raw food out of a colonist's inventory after a couple of in-game days, and on established saves it could slip past the old guard when a scooped stack had merged with another one. Now, while a pawn is carrying raw food it picked up, that vanilla routine is held off entirely, and the per-item guard reads the corrected list of carried stacks so a merged stack is no longer missed. So this does not quietly return in a future update, the mod also checks at startup that the protection is actually in place and reports loudly if it is not, the build fails if any layer of the guard is weakened, and a test pins the exact vanilla rule it relies on.
