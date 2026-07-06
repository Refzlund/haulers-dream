---
"haulers-dream": patch
---

Fix the Ingredient Threshold mod's repeat-mode option disappearing from bills when Hauler's Dream is installed.

Hauler's Dream rebuilds the bill repeat-mode dropdown to add its batch modes, and Ingredient Threshold rebuilds the same dropdown to add its own "ingredient threshold" mode. Only one of those rebuilds can take effect, so depending on mod load order one mod's rebuild replaced the other's and its modes went missing, which is why the Ingredient Threshold option could not be selected after installing Hauler's Dream. Hauler's Dream now re-adds Ingredient Threshold's mode to its own dropdown and ensures its rebuild is the one that runs, so both mods' modes are available together. This is the same approach Hauler's Dream already uses to keep Everybody Gets One and Compositable Loadouts modes visible.
