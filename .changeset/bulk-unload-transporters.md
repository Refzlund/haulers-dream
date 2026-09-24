---
"haulers-dream": minor
---

Add bulk unloading from transport pods and shuttles, based on nullpat's contribution in #267. A saved toggle allows undrafted haulers to move cargo directly from the hold to storage while leaving passengers aboard. A prioritized unloading order repeats only after its previous cargo reaches storage; prioritized loading likewise continues the selected manifest after successful deliveries. New queued orders, drafting, loading conflicts and unavailable storage prevent further unloading trips.

Validate transporter ownership and capacity again before each pull, retain trip cargo and progress through saves, and preserve interrupted cargo without taking personal stock. Transporter commands use multiplayer synchronization, and loading menu probes no longer allocate jobs or update the saved claim ledger. Settings, commands and work reports are available in all supported languages.
