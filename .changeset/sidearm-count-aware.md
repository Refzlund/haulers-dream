---
"haulers-dream": patch
---

**Fix a hauled weapon being kept (and never put away) when it matches a Simple Sidearms sidearm's type.**

The previous Simple Sidearms fix kept *any* carried weapon whose type+material matched one of a colonist's
sidearms. So if a colonist with, say, a steel ikwa sidearm was told to "haul everything nearby" and that included
a loose steel ikwa, it kept *both* — the unload job found nothing to do and flickered away, leaving the hauled
ikwa stuck in the colonist's pack.

Now Hauler's Dream keeps exactly as many of each weapon type+material as Simple Sidearms actually wants
(it tracks sidearms by count), and treats any extra copies as normal haulable loot:

- A loose steel ikwa hauled while carrying a steel ikwa sidearm → the sidearm is kept, the spare is put away.
- A loose *plasteel* ikwa hauled while carrying a *steel* ikwa sidearm → the steel one is kept, the plasteel
  one is put away (it matches on material, not just type).
- The spare stays tracked, so it still gets put away later even if the colonist is interrupted or drafted
  in the meantime.

It also now always puts away the **actual hauled (or freshly-crafted) weapon**, never the equipped one — even
when the equipped sidearm is higher quality. Previously the auto-pickup and inventory-crafting paths could tag
the colonist's own sidearm by weapon type, so a colonist carrying a 99%-quality steel ikwa that hauled a
3%-quality steel ikwa could end up storing the *good* one and keeping the *bad* one. Now it tracks and stores
the specific item it just picked up or made, so the equipped sidearm is always the one kept.

**Most importantly,** it fixes the case where the matching weapon is the colonist's **equipped main weapon**
(their primary), not a pack sidearm. Simple Sidearms records the equipped primary in its remembered-weapons
list, but that weapon lives in the *equipment slot*, not the pack — so Hauler's Dream was counting it toward
the keep total while never seeing it in the inventory count. The result: a hauled weapon matching your colonist's
equipped weapon computed surplus = `inventory(1) − remembered(1) = 0` and was **never unloaded** — it sat stuck
in the pack (or, on a "haul everything nearby", got scooped into the pack and never taken back out). Hauler's
Dream now subtracts the equipped primary from the keep total (mirroring Simple Sidearms' own unload logic), so a
hauled weapon matching your equipped weapon is correctly put away while the equipped weapon is untouched.

A diagnostic line (gated behind the mod's *verbose logging* setting) now reports the surplus math for carried
weapons, to make any future Simple-Sidearms edge case easy to pin down from a log.

No change when Simple Sidearms isn't installed.
