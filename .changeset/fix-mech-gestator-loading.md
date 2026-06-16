---
"haulers-dream": patch
---

**Fix: colonists now correctly load the mech gestator.** Previously a colonist would pick up the ingredients, walk to the gestator, fail to deposit them, and carry them back to a stockpile. Autonomous worktables (the mech gestator family) deposit ingredients into the building's own container, which Hauler's Dream's gather-into-inventory routing couldn't satisfy — so those bills are now left on vanilla's native carry-in-hands-and-deposit flow. (Surfaces in combination with mods that act at job-toil transitions, e.g. Grab Your Tool!.) Normal workbenches are unaffected. The subcore scanner was never affected by Hauler's Dream.
