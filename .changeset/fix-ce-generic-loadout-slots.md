---
"haulers-dream": patch
---

Closes #204 — Combat Extended loadout meals (and other generic-slot items like drugs and medicine) were being unloaded to storage and immediately re-fetched by CE's JobGiver_UpdateLoadout, creating a constant loop.

CE loadouts use two kinds of slots: specific (a concrete ThingDef like "MealSimple") and generic (a LoadoutGenericDef whose lambda predicate matches a category, e.g. "any meal" or "any medicine"). The basic generics — GenericMeal, GenericDrugs, GenericMedicine — are added to every CE loadout automatically. HD's LoadoutKeepCount only checked specific slots (LoadoutSlot.thingDef), so generic slots were invisible: the keep count for meals was 0, only FoodKeepCountOf partially shielded them, and the excess was shipped to storage.

LoadoutKeepCount now evaluates generic slots too, invoking the slot's LoadoutGenericDef.lambda predicate on the ThingDef to determine a match. If the lambda accepts the def, the slot's count is added to the keep total, preventing the unload↔refetch loop.
