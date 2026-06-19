---
"haulers-dream": minor
---

feat: new **"Migration"** settings tab — a clean-transition guide that appears only when you still have a mod Hauler's Dream replaces active (Pick Up And Haul, While You're Up, Meals on Wheels, Harvest and Haul, Auto Strip on Haul, Haul After Stripping, Everyone Hauls, Haul to Stack, Bulk Load For Transporters, Haul After Slaughter).

Running one of those alongside Hauler's Dream makes them fight over the same hauling jobs — the usual reason pickup looks broken or flaky right after switching. The tab sits at the bottom of the settings tab list with a warning-amber icon and label, lists exactly which replaced mods you still have on, and offers two ways to fix it: a **"Disable them for me"** button that (after a confirmation warning you to save first) turns the replaced mods off and restarts the game, or the manual safe steps — draft your colonists and save, disable the mods, reload and save, then carry on.

Detection now catches **community translations and continuations**, not just the exact original mods: each active mod is matched both by packageId and by a normalized substring of its name and packageId, so a translated "Pick Up And Haul 日本語" or a "…(Continued)" reupload is still recognized. The tab hides itself automatically once none of the replaced mods are active. No setting and no save data are added.
