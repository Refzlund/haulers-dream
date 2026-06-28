---
"haulers-dream": patch
---

Fix a red error when one worker finishes a build another was hauling materials to.

When several mechs or pawns built a large structure together (a substructure floor, for example), one of them could throw an error and drop its task if a different worker finished the exact piece it was still carrying materials toward. Hauler's Dream was asking the build site how much more it needed at the instant that site was completed and removed, which the game cannot answer about something that no longer exists. It now confirms the site is still there first and quietly moves on when it is gone, so the worker just picks its next job with no error and nothing carried is lost.
