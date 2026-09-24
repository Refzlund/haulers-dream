# Feedback completion scope review — 20 September 2026

Independent, read-only review of F04, F10 and F11 against the preserved [C423/C425 source bodies](../feedback-vorshlumpf-2026-09-08.json), current implementation and focused plans. This document recommends scope; root owns changes to the authoritative ledger/register. No product, fixture or runtime was changed or executed.

Feedback-specific completion and final assembled-build verification are separate milestones. A pending combined PR must not erase an accepted item result. Conversely, source review cannot establish rendering or physical inventory behavior.

## F04 — clipped feature descriptions

**Reported requirement:** C423 says some feature descriptions are cut off; no particular description, language or scale is identified.

Current `Source/HaulersDream/SettingsUI.cs:711` measures complete card text with native font metrics. `Source/HaulersDream/HaulersDreamSettings.Window.cs:967` measures and scrolls the complete help document. The [focused plan](settings-description-readability.md) records source/compiled review and 13 passing state controls; those controls do not prove visible fit.

**Smallest remaining validation:** inspect every feature description in the actual settings window, including a constrained supported viewport, a representative long translation/font fallback, search presentation and scrolling the longest help to its end. Check that the affected controls remain usable. This can be one focused native inspection, without a new framework.

Every language × scale × control combination, unrelated texture warnings, multiplayer editing locks, general UI performance and forced rendering exceptions are not prerequisites to resolving this clipping report. Retain any demonstrated affected-layout regression as a concrete obligation.

## F10 — pickup activity wording

**Reported requirement:** C425 observes that picking up an item/stack displays “Haul everything nearby.” This is an observed wording issue, not proof that the reporter requested new scheduler behavior or separate single/bulk jobs.

Current `Source/HaulersDream/BulkHaul.cs:1127` creates the shared `HaulersDream_BulkHaul` job for a single pickup. `Source/HaulersDream/JobDriver_BulkHaul.cs:81` returns the translated activity key. `Defs/JobDefs/Jobs.xml:110` and `Languages/English/Keyed/HaulersDream.xml:425` now say “gathering items into inventory.” An independent read-only XML comparison in this review found one matching JobDef with the expected driver and equal, nonempty keyed/JobDef activity values in all 16 languages.

**Recommendation:** close the wording item after root verifies these current bindings. Record the evidence as source/text verification, without claiming a rendered runtime result. An ordinary pickup screenshot on the final assembled build is a separate integration smoke check.

Custom Alerts discovery/saved-query tests and drafted-command execution remain F08/F07 obligations. Coalescing matrices, a new scheduler and two-client gameplay are not prerequisites for this text correction. See [the combined investigation](hauling-command-integrations.md), whose broader gates should not all attach to F10.

## F11 — dropping a chosen amount

**Reported requirement:** C425 requests dropping a specific amount instead of a whole stack.

The [runtime plan](partial-inventory-drop-runtime.md) records accepted 19-scene command evidence and a real Gear-tab operation dropping seven units and retaining eight. These establish substantial recorded core progress. They must not be discarded because the final PR is incomplete.

**Still justified:** actual cancel/invalid/stale confirmation behavior; retained cargo/keep followed by useful unloading and save/reload; targeted CE cache and Sidearms memory checks; actual multiplayer submission where that integration is claimed. `Source/HaulersDream/InventoryDropCommand.cs:81` and `Source/HaulersDream/Dialog_DropInventoryAmount.cs:131` change these boundaries. Conservation, save behavior and affected compatibility must remain explicit.

Do not repeat accepted conservation/merge/blocked cases without a relevant change or concern, or require every input × language × lifecycle × mod combination. A controller's permanent “not-verified” marker does not override separately accepted native evidence. See [the focused design](partial-inventory-drop.md).

## Historical evidence availability

The historical runtime acceptances above were read from the plans, not freshly revalidated against raw artifacts. At this review, these directories existed but contained no files:

- `C:/Users/Arthur/AppData/Local/Temp/haulersdream-activity-wording-independent-20260908`
- `C:/Users/Arthur/AppData/Local/Temp/haulersdream-quantity-ui-actual-independent-ec9aa8-20260908`
- `C:/Users/Arthur/AppData/Local/Temp/haulersdream-runtime-tests/ec9aa8cfc05741d3a90ba8d3228362fa/evidence`
- `C:/Users/Arthur/AppData/Local/Temp/haulersdream-runtime-tests/5783eda01fe24da8b5db115b9bb7c82a/evidence`

The cited `C:/Users/Arthur/AppData/Local/Temp/haulersdream-quantity-ui-ec9aa8-bounded-acceptance-20260908.json` was absent. Recover retained copies before claiming a fresh audit of those historical runs. Their absence does not invalidate this review's direct verification of current F10 source/XML.
