---
"haulers-dream": patch
---

fix: settings window failing to close, and harden the storage search against other mods' malformed storage.

Closing the mod settings window could throw an error and refuse to close when another work-related mod was installed (issue #59). Hauler's Dream was refreshing every colonist's work types on every settings write, which needlessly ran other mods' work-type code each time; if one of those threw, it broke the window close. Hauler's Dream now refreshes work types only when one of its "all pawns can haul / clean / cut plants" overrides actually changed, so a normal settings close no longer pokes unrelated mods.

Hauler's Dream's storage search now skips a storage group that has no settings or no parent building instead of crashing on it (issue #58). Some mods can momentarily expose storage in a half-built or torn-down state; Hauler's Dream already guarded the chosen group this way, and now does so consistently in its storage loops. (The specific crash report behind #58 originates in another mod that evaluates jobs off the main thread; that part is outside Hauler's Dream's control, but this keeps Hauler's Dream's own code from adding to the problem.)
