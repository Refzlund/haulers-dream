# F04 Italian rendered review — 20 September 2026

Root directly viewed all seven retained Italian feature/search/help PNGs from native run `3e41898ed99b4f1ebd0a0b10f7d1748c`. The viewer decoded the 2560 × 1440 images and displayed them at 2048 × 1152. This is actual image inspection, not acceptance from capture markers.

- `f04-view-2-features-0.png` through `-3.png`: complete titles, blurbs and toggles remain readable across the four scroll positions. Page 0 includes the long automatic-unloading blurb. Pages 1 and 2 have ordinary viewport-edge cuts, with the affected cards complete on the adjacent page. Page 3 shows the final Storage Filters title, complete blurb and toggle. No card-level text truncation or text/toggle overlap was observed.
- `f04-italian-search.png`: the requested mechanoid setting is visibly present with its complete label and checkbox; other wrapped labels have sufficient row height. The search input displays the visible portion of its long value; this is not a feature-description truncation.
- `f04-italian-help-top.png`: the selected setting is highlighted, with complete wrapped help title and status above the beginning of the long description. The separate help scrollbar is visible.
- `f04-italian-help-end.png`: the same selected setting remains highlighted; the help scrollbar is at the bottom and the final sentence is readable through “modo di un colono.” The body ends above the panel bottom without truncation. The main content position is unchanged.

The window is fully visible at the constrained 1.75 UI scale, using actual Italian keyed strings and Small-font fallback. This verifies rendered layout and retained programmatic help/scroll state. It does not claim physical mouse, hover or wheel input, full language/Def reload, or Vehicle Framework integration. Final assembled-build input smoke checks remain separate.

Verdict: no blocker to resolving the reported settings-description clipping, subject to the independent raw evidence review.
