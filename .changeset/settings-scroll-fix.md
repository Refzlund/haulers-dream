---
"haulers-dream": patch
---

Fixed the mod options window losing its scrollbar so the lower settings ran off the bottom and couldn't be reached. The settings list is rendered in a scroll view, but once the content grew past the last measured height (or you toggled an option that added rows, like bulk hauling or auto-strip), the underlying list silently wrapped into a second off-screen column — which collapsed the measured height back to the viewport, removed the scrollbar, and never recovered. The list is now pinned to a single column, so the scrollbar always tracks the real content height and every setting is reachable.
