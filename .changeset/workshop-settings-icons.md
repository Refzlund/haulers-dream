---
"haulers-dream": patch
---

fix: settings icons missing on the Steam Workshop build.

The mod-settings window icons (the category and feature icons under Textures/HaulersDream/Settings) showed up in a local install but not in the Steam Workshop version. The Workshop packaging script copied About, Defs, Patches, and Languages but not the Textures folder, so the published mod shipped without any of its icon art (the local deploy, which does copy Textures, hid the gap). The packaging now includes Textures, matching the local deploy. Re-publishing the Workshop item picks up the icons.
