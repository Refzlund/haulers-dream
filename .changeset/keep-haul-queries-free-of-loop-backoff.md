---
"haulers-dream": patch
---

Fixed repeated haul availability checks incorrectly triggering the "bulk-hauled without moving" warning and delaying valid work. The same backoff could make RimWorld report that a workgiver offered a target but returned no job. Existing protections for actual failed hauls, failed placement and observed foreign retargeting remain.
