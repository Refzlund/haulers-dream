---
"haulers-dream": patch
---

Vehicle Framework: a vehicle's cargo hold is now treated as the player's to manage. Hauler's Dream no longer sources build materials (build-from-inventory) or meals (meals-on-wheels) out of a parked, loaded vehicle's cargo, so a trip loadout you packed isn't silently undone — matching how it already declines to bulk-unload a vehicle. And when Hauler's Dream's Vehicle Framework support is turned off, it now ignores vehicles entirely, no longer depositing into a vehicle's cargo via the pack-animal loading path (at both job selection and the in-flight deposit loop). Inert without Vehicle Framework.
