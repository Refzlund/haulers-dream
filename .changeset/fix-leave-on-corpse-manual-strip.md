---
"haulers-dream": patch
---

Fix "leave on corpse" tainted apparel policy for manual strip orders (#211)

When a player issues a manual Strip order on a corpse, vanilla's Pawn.Strip calls
apparel.DropAll which strips everything — including tainted pieces the player's
per-category policy says to leave on the body. The pieces dropped to the ground
were then forbidden in place by HD's post-strip handler (degraded to DropAndForbid
because it couldn't put them back on the corpse). A prefix on
Pawn_ApparelTracker.DropAll now filters out LeaveOnCorpse pieces when the pawn is
dead, so they stay on the body and travel with the corpse as intended.
