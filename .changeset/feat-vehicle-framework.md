---
"haulers-dream": minor
---

Full Vehicle Framework compatibility (optional, reflection-only soft dependency — inert and byte-identical when Vehicle Framework is not installed, and gated behind a master toggle that defaults on).

- **Bulk-load vehicle cargo.** Colonists load a vehicle's designated cargo the same way they bulk-load transporters and portals — sweeping many stacks into inventory and depositing them in one trip, with idle haulers splitting a single manifest via the shared claim-ledger. It works autonomously the moment you set a vehicle's cargo (HD upgrades the framework's single-stack loader in place), and a right-click "Prioritize bulk loading" is available too. Aerial vehicles load identically. Deposits go through the framework's own event-correct path and are clamped to exactly what you ordered (stuff/quality-precise), so a mixed manifest is never over-loaded.
- **All existing features understand vehicles.** A hungry colonist will eat from a parked vehicle's cargo, a builder will pull construction materials from one, and pack-animal loading routes into a vehicle's cargo when one is present (now event-correct). Defensive guards stop a vehicle from being mistaken for a pack animal by the bulk-unload option, and skip a colonist who is riding inside a vehicle as a food/material source.
- **Configurable.** A master "Vehicle Framework integration" toggle plus a "Bulk-load vehicles" sub-toggle, both default on. The safety guards always apply when Vehicle Framework is present.
