---
"haulers-dream": patch
---

Load shuttles and transport pods starting from the item nearest the colonist rather than the item nearest the shuttle, to cut out wasteful back-and-forth (issue #171).

When a colonist some distance from a shuttle was told to load it, Hauler's Dream planned the pickup order starting from the item closest to the shuttle, so the colonist would walk past nearby items to grab a far one first and then double back. It now plans the order starting from the colonist's own position and collects along a sensible path, grabbing what is on the way instead of crossing the map and returning. Which items and how many get loaded is unchanged, since this only affects the order they are collected in, so the earlier fix for loading the correct quality and quantity (issue #156) is preserved.
