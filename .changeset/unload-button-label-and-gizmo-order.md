---
"haulers-dream": patch
---

Fix the per-pawn "Unload inventory" button setting reading as "Hide" in every translation, and stop the button from sitting among a pawn's ability gizmos.

The checkbox that controls the per-pawn "Unload inventory" button turns the button on when it is checked, but every translated language still labelled it "Hide the ... button" (only the English text had been updated to say "Show"). All languages now read "Show", matching what the toggle actually does.

The "Unload inventory" and per-pawn auto-haul buttons now declare a deliberate position instead of falling into the unordered default group. Left unset, they shared that group with the pawn's role and ability commands, and with a gizmo-reordering mod the unload button could end up wedged between abilities like a leader's speech and accusation. They now sit together in their own slot below the ability gizmos.
