---
"haulers-dream": patch
---

Internal hardening (no behavior change): the two largest source files are split into focused `partial class` files by concern. The game component that bundled five unrelated subsystems — the bulk-load claim ledger, batch-bill config, the softlock-drop driver, the vein-reveal driver, and the idle backstop — now lives across one file per subsystem, each with its own scribe block, so editing one can no longer accidentally disturb another's save logic. The 1290-line settings class likewise moves its ~480-line GUI into a separate partial file, leaving the model and persistence on their own. Because each type remains a single compiled class with identical fields, scribe labels, and scribe order, save games and in-game behavior are byte-for-byte unchanged.
