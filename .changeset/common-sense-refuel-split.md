---
"haulers-dream": patch
---

Fix inventory refueling being disabled when Common Sense inserts its ingredient cleanup into item splitting. Preserve the cleanup's original order while observing the created item and the actual source debit separately, including when a callback throws.
