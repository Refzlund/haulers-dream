---
"haulers-dream": patch
---

Stop hauling from getting in the way of doctoring, rescue, and firefighting.

Two bugs made colonists neglect urgent medical and emergency work while they were carrying items for Hauler's Dream. First, a pawn holding scooped goods that was about to tend a wounded pawn, rescue a downed one, or fight a fire could be sent to drop its load at storage first, so after a fight nobody tended the bleeding and rescues basically never happened, even with those jobs on priority 1. Hauler's Dream now never diverts a pawn away from doctoring, rescue, or firefighting to unload. Ordinary work like hauling, mining, and cleaning still drops off a load on the way, exactly as before.

Second, a colonist waiting in bed for treatment could be pulled upright over and over: it would go fetch a meal from another colonist's inventory, or be told to unload, and then the game would send it back to bed, so it kept standing up and lying down (reproducible with an anesthesia operation on a patient set to no medicine). Hauler's Dream now leaves a colonist that should be resting for medical care in its bed. A doctor still brings that patient a carried meal, and the "unload now" button still works.

Also hardened the settings dirty-check so a fresh default configuration is not mislabeled as "Custom (unsaved)" because of harmless differences from saving and reloading the config.
