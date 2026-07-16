---
"haulers-dream": patch
---

The "Unload inventory" pawn button now unloads immediately on a plain click, and queues on Shift+click.

Previously the button always added the unload behind the pawn's current job, so the pawn finished what it was doing first. Now a plain left-click makes the pawn drop its current job and go unload right away, which is what most people expect from the button. If you would rather keep the old behavior for a specific click, hold Shift while clicking and the unload is added to the job queue to run after the current job instead. There is no new setting, and automatic unloading is unchanged.
