---
"haulers-dream": patch
---

Fix the Enter key closing the in-game report window instead of starting a new line in the description.

Pressing Enter while writing a bug report (or a reply on the My Reports thread) closed the window rather than adding a line break, so you could not lay a report out across several lines. The window was catching Enter as its accept key before the text box could see it. It no longer does, so Enter starts a new line like any other text field; reports and replies are still sent with the Send button, and Escape still closes the window.
