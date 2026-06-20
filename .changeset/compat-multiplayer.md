---
"haulers-dream": minor
---

feat: full **RimWorld Multiplayer** compatibility. Hauler's Dream now works in multiplayer — every feature (smart inventories, bulk loading, route/craft planning, the per-pawn gizmos, batch sizing) runs deterministically across all clients.

Under the hood this routes every player-initiated action that changes saved state (the auto-haul toggle, "Unload inventory", Plan Route, Plan Craft, batch-size edits, carrier unload) through Multiplayer's command-sync, and makes the autonomous hauling/sweeping/bulk-loading logic pick the same targets on every client (deterministic tiebreaks), so the simulation never diverges. Multiplayer support is a soft dependency — it adds nothing and changes nothing when the Multiplayer mod isn't installed.

A note for multiplayer hosts: Hauler's Dream settings are host-authoritative — they sync to everyone when you join (accept Multiplayer's "Apply configs" prompt), and the settings window is locked during a multiplayer session so a mid-game change can't desync the game.
