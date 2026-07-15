---
"haulers-dream": patch
---

Fix pawns freezing from vanilla JobDriver_Ingest NRE when food Thing reference is null (#207)

Vanilla RimWorld's JobDriver_Ingest.MakeNewToils registers a global FailOn lambda that accesses
IngestibleSource.Destroyed without null-checking — when the food Thing reference is lost (save/load
cycle, mod interaction, or game-state corruption in a heavily-modded save), the lambda NREs. RimWorld
catches it, logs a red error, and starts a 150-tick error-recovery Wait, but the pawn re-thinks into
another Ingest job whose target may also be null, creating a freeze loop (pawns standing still, going
hungry). This adds a prefix on JobDriver.CheckCurrentToilEndOrFail that detects the null-food-target
case for JobDriver_Ingest and ends the job Incompletable — the condition the vanilla lambda would have
returned had it been null-safe — so the pawn cleanly re-evaluates food options without the error spam
or the Wait penalty.
