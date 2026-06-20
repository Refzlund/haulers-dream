---
"haulers-dream": minor
---

feat: full localization + translations for 9 languages. Every piece of player-facing text now goes through RimWorld's translation system (the last few hardcoded fallback strings were externalized), and the mod ships with translations for **Chinese (Simplified), Danish, German, French, Russian, Japanese, Thai, Dutch and Polish** alongside English — settings, menus, alerts, planners, job reports, everything.

The non-English translations are a complete first pass (AI-assisted, using RimWorld's established per-language terminology); native-speaker corrections and additional languages are very welcome via a quick pull request — see the new CONTRIBUTING translation guide. A build-time parity check (`scripts/check-translations.ts`) guarantees every language defines exactly the English key set with matching placeholders, so a translation can never silently fall out of sync.
