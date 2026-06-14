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

No change when Simple Sidearms isn't installed.
