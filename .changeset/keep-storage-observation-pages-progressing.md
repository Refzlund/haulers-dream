---
"haulers-dream": patch
---

Storage observations now choose a smaller fresh cell page when incoming cargo would exhaust the predicate budget of a full stockpile scan. Existing carried loads can continue unloading without increasing the work limit or admitting cargo from an uncertified observation.
