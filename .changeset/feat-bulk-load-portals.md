---
"haulers-dream": minor
---

Bulk load map portals: extends bulk loading to pit gates, cave/vault exits, and "enter map" portals, reusing the transporter loading engine (same claim-ledger, planner and sweep). Items are swept into inventory and deposited through the portal in one trip, with the manifest reaching exactly empty even though each deposited stack teleports away. Portal-side anti-conflict (no false "loading stalled" alert, no premature enter) and the vanilla single-item portal-load option replacement are included, independently gated by a new toggle (default ON). Right-click a portal for "Prioritize bulk loading". (Completes the Bulk Load for Transport replacement.)
