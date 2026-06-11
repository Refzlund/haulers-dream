---
"haulers-dream": patch
---

Fixed material deliveries to blueprints. Inventory deliveries for big builds and claim-from-hauler handoffs failed with red errors on the first delivery to any new blueprint (the geothermal-generator case): the load arrived but could not be deposited. Deliveries now convert the blueprint to a frame exactly like vanilla and deposit cleanly, including multi-trip loads.
