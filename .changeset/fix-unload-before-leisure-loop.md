---
"haulers-dream": patch
---

Stop a colonist from pacing forever instead of relaxing when the only thing they are carrying is something they keep on purpose.

When a colonist finished its work while still carrying hauled goods, Hauler's Dream would send it to put the load away before wandering off to relax. The check for "is there anything to put away" only asked whether a carried stack was in the pack and free to grab, not whether any of it was actually surplus. So if the last thing a colonist was carrying was personal stock it deliberately keeps (its own food, drugs, a loadout item), the put-away trip found nothing to do, ended, and started again a moment later, over and over. The colonist paced in place and never settled into leisure. The end-of-work put-away now uses the same "is there real surplus to store" test as the actual unload and the carry-weight alert, so a colonist holding only keep-stock just heads off to relax and hangs on to it until real surplus turns up.
