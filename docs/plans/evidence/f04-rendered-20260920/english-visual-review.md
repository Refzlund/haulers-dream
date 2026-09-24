# F04 English and Vehicles component visual review

Reviewed actual retained run `3e41898ed99b4f1ebd0a0b10f7d1748c`. I opened all seven images below with `view_image` and inspected their pixels, then independently verified each PNG and fully decoded its image data using Pillow with truncated-image loading disabled. All seven are valid RGB PNGs at **2560×1440**. The image viewer displayed scaled 2048×1152 previews; the original files were not modified.

**Finding: no feature-card description clipping or text/toggle overlap in these seven images.** All 18 active English hub cards are completely visible somewhere in each three-page sequence. This is a visual decision for these captures, not an inference from the run's assertion count.

| Images | Matching page events | Direct visual findings |
| --- | --- | --- |
| `f04-view-0-features-0.png`, `f04-view-1-features-0.png` | Sequences 80/129; scroll 0; complete ordinals 0–5 | Master, Automatic unloading, Bulk hauling, Tidy up while working, Bulk-haul urgent items and Bulk-unload pack animals are fully displayed. Automatic unloading's two-line description ends with “per-pawn button.” The second line clears the next card. Icons and green toggles remain separate from the text. |
| `f04-view-0-features-1.png`, `f04-view-1-features-1.png` | Sequences 83/132; scroll 542 / 541.8572; complete ordinals 6–12 | Load transporters & shuttles, Load map portals, Bulk refuel, Build from inventory, Craft from carried materials, Carry materials for big builds and Meals on Wheels display complete titles and blurbs. Section headers have clear spacing. |
| `f04-view-0-features-2.png`, `f04-view-1-features-2.png` | Sequences 86/135; scroll 898 / 897.8572; complete ordinals 11–17 | The repeated construction/meals rows and both planners are complete. Grab items on the way, Move supplies closer to where they're used and the final Storage filters card have complete text and toggles. Storage filters' final blurb, “Restrict which storage buildings hauling may use,” is wholly above the viewport bottom. |

There are deliberate viewport-edge cuts: Load transporters is partial at the bottom of page 0, Route planner is partial at the bottom of page 1, and the previous Craft from carried materials row is partial at the top of page 2. Their complete versions appear on adjacent pages. These cuts occur at the scroll viewport boundary, not inside an allocated card description. They are not evidence of the reported description-truncation defect.

Native-view events 79/128 report the same 900×700 dialog, card width **414.56** and viewport height **590**. Config 0 uses English/Tiny at scale **1**, UI **2560×1440**. Config 1 uses English/Tiny at scale **1.75**, UI **1463×823**. The screenshots show the centered window fully inside the physical display at both scales; neither is a 760×650 fixture view or a 1024×768 physical-resolution capture.

One nonblocking diagnostic limitation: event 128 records the prior window origin `(830,370)` before the fixture recenters for the new UI scale. `SettingsRendered.cs:117` centers after the first repaint, then waits again and checks bounds before capture. The pixels show the later centered window. Do not interpret that early event's origin as the location of the config-1 screenshots; its dimensions and local card/viewport sizes remain applicable.

`f04-italian-vehicle-helper-only.png` visibly labels itself “Supplemental Vehicles card helper only” and says Vehicle Framework is absent. The disabled `Carico dei veicoli` card has its complete two-line blurb, “Carica in blocco il carico dei veicoli (serve una mod di veicoli)”; both lines stay inside the grey card, with visible separation from the red toggle. Event 203 records actual Small fallback, card height 76, name height 22 and blurb height 38. This approves the supplemental card's text layout only; it does not approve Vehicles activation, actual conditional hub rendering, or compatibility. The underlying help window is partly obscured by this owned supplemental window and is outside this image's purpose.

File identities (SHA256):

```text
f04-view-0-features-0.png 91C72894B622322F91E1848351578CB713C9D894B0404698540518D2C1171FD8
f04-view-0-features-1.png 7EA3C27DD0EAD6F31C760E2B7AABCF17EE0CFA4DCC4B8E97AA3192A8B4B058CF
f04-view-0-features-2.png 02C00F4D95833E3F9033ED3B70B3B79A57E5AC2C971B39FACB9177A315D79498
f04-view-1-features-0.png 819230BAD890B79B47F95AC445CC5D48D716E9A05C9CDE49C02637C7F2A20907
f04-view-1-features-1.png 612B480CDC64E97D00599FBC2100433653FA561B6B479399D66CAC99E7E0C275
f04-view-1-features-2.png AD2AEB31164B350B3676B8C324A941C4247E4ACC2A0C8A629CE537308FFCCD18
f04-italian-vehicle-helper-only.png 8F92147AD68A557B78C4736AB852F6EDC5F14D243B5DDE5DCA07B1CFC560D39D
```

The remaining Italian hub/search/help images and full raw/process/restoration/log acceptance belong to the separate assigned reviews. No rerun, synthetic input, or source edit was performed for this visual review.
