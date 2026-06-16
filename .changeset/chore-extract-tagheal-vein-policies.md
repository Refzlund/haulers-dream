---
"haulers-dream": patch
---

Internal hardening (no behavior change): the inventory "self-heal" that decides which carried stacks Hauler's Dream owns — the single most load-bearing piece of logic in the mod — and the vein-mining route-extension decision are now pure, unit-tested functions in the Core library instead of being tangled inside Verse runtime code. This adds 32 oracle tests pinning the historically bug-prone cases (a single scoop landing across several inventory stacks, a stack merge destroying a tag's last reference, a Simple Sidearms weapon that must never be auto-unloaded, a harvested-vs-personal medicine def overlap, and the per-tick re-heal gate) so a future edit can no longer silently regress them. The runtime behavior is byte-for-byte identical.
