---
"haulers-dream": patch
---

Fix scooped crops sometimes not loading onto a caravan, transporter, portal, or vehicle.

When a pawn scooped a harvest that merged into a stack it was already carrying, the bulk loader could decide it had nothing left to load and finish early, leaving that merged stack behind to go to storage instead of onto the carrier. The loader's "is there anything to load" check was reading an outdated list of carried stacks, while the actual loading step read the corrected one, so a stack that had merged after being picked up fell through the gap. The check now reads the same corrected list the loading step uses, so a merged stack is recognized and loaded. This is the loading-side cousin of the dropped-crops bug, on the same merge trigger.
