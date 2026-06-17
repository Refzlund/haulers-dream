---
"haulers-dream": patch
---

Robustness pass (mostly internal): clearer diagnostics and a settings-integrity guard. Hauler's Dream now logs a one-line warning when a supported mod (Combat Extended, Vehicle Framework, Common Sense) is present but an expected member it integrates with isn't found — so partial incompatibilities show up in the log for bug reports instead of failing silently. Corpse-hauling auto-strip now follows the same race-eligibility rule as every other auto-haul (so it correctly includes Haul-trained animals when "allow animals" is enabled). A build-time check now guards the 108 settings against default-value drift across their save/reset wiring.
