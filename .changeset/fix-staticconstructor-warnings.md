---
"haulers-dream": patch
---

Silence the startup debug-log warnings about types holding texture/material fields without `[StaticConstructorOnStartup]`. RimWorld structurally checks every type with a static `Texture2D`/`Material` field for that attribute (so its assets are guaranteed to load on the main thread) and logs a warning when it's missing — Hauler's Dream tripped it on three: the per-pawn unload gizmo (`Patch_Pawn_GetGizmos`), the settings window's header/icon textures (`HaulersDreamSettings`), and the route-preview line material (`MapComponent_RoutePreview`). All three now carry the attribute (matching the existing `DetourOverlay` usage), so the warnings are gone; the textures still load lazily on the main thread exactly as before, so there's no behavior change.
