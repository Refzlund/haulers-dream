# F04 current source review — 20 September 2026

**Decision: no blocking source defect found in the current card/help changes. Rendering acceptance remains pending.** This independent review read the complete diffs and relevant current callers; it did not run the product, build, controller or tests.

C423, preserved in [the source record](../../../feedback-vorshlumpf-2026-09-08.json), says “a few of the feature descriptions have the text cut off too soon.” It names no description, language or scale. The requirement is complete readable descriptions, with the affected settings still usable. C425's praise is not a clipping retest. Scope follows [the completion review](../../feedback-completion-scope-review.md).

## Source findings

- `Source/HaulersDream/SettingsUI.cs:711–824`: fixed 54-pixel cards with 22/20-pixel name/blurb boxes become content-sized cards. Measurement and drawing use the same text width, Small/Tiny fonts and wrapping. The full strings are retained; measured text has seven pixels of top/bottom padding, and an eight-pixel horizontal gap before the toggle. Collect and draw share the same geometry. Font, wrapping, anchor and color restoration cover the new rendering path.
- `SettingsUI.cs:49–123` and `HaulersDreamSettings.Window.cs:967–1037`: help selection uses category/control/part identity, refreshes from the current traversal, and survives leaving the card. Title, status, optional graph and complete body share one measured native scroll view. Scroll resets for a different document, not for an unchanged document on every repaint. A vanished control falls back to category help. This addresses the source-level obstruction to reading a long description after moving into the help panel.
- Search identity is preserved: each checkbox, slider, selector, button and card consumes one ordinal in collect, normal and filtered modes; each matrix consumes one per row. Skipped filtered controls neither draw nor take input. Matrix headers/options use separate help parts rather than consuming searchable ordinals. Search and normal cards use the same width. The new early return after search navigation ends the old traversal inside `finally`, preventing stale controls from continuing after a category change.
- `HaulersDreamSettings.Window.cs:104–110,268–316`: all six new instance UI fields are `[NonSerialized]`; `SettingsProfiles.cs:66–71` excludes them from capture/apply. No setting field, profile format or save schema changes here. Width, UI scale, language, Tiny-font availability and actual conditional-layout facts invalidate cached geometry. The foreign-gather notice decision is captured once per event for both collection and drawing.
- Card input remains one native `ButtonInvisible` over the complete card, guarded by `enabled`; the checkbox is still drawn rather than a second independent input target. Other widget input calls and the multiplayer edit guard are unchanged. No new event consumption, keyboard/hot-control assignment or custom wheel handler was introduced. Taller cards intentionally enlarge the existing hit rectangle; they do not change the bound setting.

## Remaining report-specific evidence

Use the real settings window and its native draw/scroll path. Inspect the available feature descriptions through the last card at a constrained supported viewport, one representative long translation/font fallback, and the real filtered search presentation. Record native font measurements for any conditional feature text not present in that roster, rather than loading unrelated compatibility mods solely for this clipping review.

For the longest help document, observe its identity persisting across real frames without a new hover offer and render the actual final line at the native scroll limit. Programmatic selection/scroll positions are sufficient to test layout and retained state, provided the real window draws them and the evidence does not claim mouse input. Screenshots alone of the first page, or measuring a helper without drawing the settings window, would not close F04.

Given the unchanged native input primitives and the source findings above, physical card-button and wheel operation can remain an explicit final assembled-build UI smoke check. A new setting mutation, lost help selection, unreachable final line or incorrect search binding observed in this run would instead be a concrete F04 blocker. Broad language × scale × control, save/restart and multiplayer matrices are not required by this report.

Historical September 8 state/compiled controls remain recorded evidence only; some cited TEMP artifacts are missing. This review does not claim to have rerun or freshly audited them. The forthcoming native run must be assessed on its own actual evidence.

Reviewed current source SHA-256:

- `SettingsUI.cs`: `6DACF3EB3DCBEE350A23EC66445EC88F41845A6BC722340F726C840E9A931E33`
- `HaulersDreamSettings.Window.cs`: `17F9D350A3FD67BFC081BB432E6355E22123CDDDCBA24BE908B517535BA71904`
