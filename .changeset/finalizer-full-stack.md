---
"haulers-dream": patch
---

Include the full origin stack in the breadcrumb Hauler's Dream logs when an exception passes through a method it patches.

When an error passes through a patched method, Hauler's Dream re-throws it unchanged, which restamps the stack trace at the re-throw point, so the game's own report of that error names the re-throw site instead of the real source. The breadcrumb now captures the true stack the first time each patched method surfaces a given error, once per distinct error type per method so a repeating error does not flood the log. A report where Hauler's Dream only patched the method now shows exactly where the fault actually came from, instead of looking like Hauler's Dream caused it.
