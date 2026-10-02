---
"haulers-dream": patch
---

Preserve existing incoming cargo when a storage integration can no longer be measured. Native fallback hauling and inventory unloading now check outstanding cargo of every item type before taking an exclusive destination, while keeping separate unclaimed cells available.

Recheck ownership after reservation callbacks and before withdrawing inventory. If those callbacks invalidate admission, release only the reservations newly acquired by that attempt and preserve the original error and other work.
