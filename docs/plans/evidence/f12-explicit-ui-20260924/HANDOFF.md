# F12 point-haul UI — additive source/build handoff v2

Implemented the player-facing selected-source → total quantity → exact bare-cell flow through the existing synchronized explicit-haul command. The menu, robot-compatible source targeter, quantity dialog and progress/Resume/Cancel dialog are connected. Queue intent is captured and preserved; stale actor/map/source/quantity/location/forbidden-state selections are rejected before submission. Right-click/Escape/native dialog cancellation creates no order. One item skips the amount dialog. All16 existing locale folders have35 new keyed strings and the actual JobDef report translation.

This is source/build completion for the UI slice, **not native or rendered acceptance and not F12/F13 closure**. Shelf/F13 support remains a required separate backend/admission slice. The current UI rejects storage/shelf cells and never substitutes automatic storage. Current backend lifecycle, provider and MP acceptance also remain explicit work.

Only37 new files are added: five C# files plus32 XML files. No pre-existing product file, language file, first native fixture or controller is edited. The separate frozen product is based on the exact backendv5 source plus these additions, so it deliberately does not absorb unrelated concurrent changes. Root owns source review, future integration/Prepare/native/staging/commit.

## Review and build artifacts

- `v2/source.diff`: complete additive source/translation diff; `v2/source/` retains the exact37 additions.
- `SOURCE-REVIEW.md`: actual native targeter/menu/pawn/UI API findings and UI authority boundary.
- `RENDERED-ACCEPTANCE.md`: bounded actual-input/rendered checks still required, reusing accepted backend evidence where valid.
- `v2/build.log` and `build-process.json`: native-reference build25.83s,0 warnings/errors, process joined0. Deployment path points to a verified nonexistent private guard directory.
- `v2/audit.json`:694 source/copy pairs,85 actual reference/copy pairs,153 exact runtime files verified;16 locales,35 keyed strings each, matching placeholders, no duplicate new keys. This does not claim translation rendering or gameplay.
- `v1/` preserves the first compiled slice and initial packaging audit failure. Only four generated old DLL/PDB inputs changed on compilation; the original input list and precise classification are retained. Corrected v2 freeze excludes generated output as source; it builds and audits end to end.

## Frozen selection

Product: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f12-explicit-ui-v2-20260924/Product`

- `v2/selection.json`: SHA256 `B6B69CDF0CB53AE55FF426D503D51D7A1248EF0C5B94FAC51A8246A47484133B`.
- HaulersDream: SHA256 `64D2B8A20A1F42D079EC643F0EA14414B62B6BAAA63CBC07E996D4F94B0B7602`, MVID `1f292e19-851a-44a0-bd94-52405ce80306`.
- Core: SHA256 `2633530022F02B73D09A11254E088E6DE52D6F1B05A30C2E0233790B642BA4AA`, MVID `4e3c0516-2754-44c1-a682-45af2567d739`. Core source is unchanged; isolated build-root/debug identity changes its binary identity.
- Actual installed Assembly-CSharp remains SHA256 `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`.

Do not substitute this product into the running backendv5/hostv4 checkpoint chain. A UI/native fixture must explicitly pin this new product and reviewed host in a fresh selection. No Prepare, launch, physical input, default-desktop activation, staging or commit was performed for this slice.
