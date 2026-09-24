# Current RCV1 controller and profile binding

Authored for independent root review. No candidate PowerShell, Prepare, build, native game, or UI action was executed by this author. This package does not accept full recovery, F09, or the feedback goal.

The current profile is `REFUEL-RCV1-DIRECT-native-E17890-995694-v2`, not the historical 3FD008 profile. Its source manifest is `2121D78357269D1A5D444103E9CDFB2E686071B3C2CC46EF1A76075F909DCE53`; its profile is `C5309968FF4D50E628C81F6973BC347E2C2554E78FA663F80F4BFD14C5A09C1C`.

## Actual sources and products

The approved source selection is `haulersdream-rcv1-destroy-count-root-author-20260920/source-manifest.json`, SHA256 `715532926EFEEE3111E4BA43F11ABC1BC04A22DD16770C9B5D1A4D006D691612`, with addendum `6464A2A3ACF4C72A420E88C2BE832BFE9FF720F7C8EB90A5B844B13FE1F0E818`. Independent source approval is `3E9A65BD75DB59D8958F608DC60DB9CD9ACF8C2DE36981847D4912AD4594BB19`.

The actual completed build receipt is `FC35B3F01BC3133061D4F728CE162439819CB9FFFF1D89DAF6E13F2C6CD2CE48`. The harness DLL is `E1789030BC6600BC319A952CC9FF60147996AFF39883F34F2DFE5722F1708F1B`, PDB `AB6F5A986D579C01F225257D21A7D5F4DD823D52F97FB649B5B720772A4D9B1A`, and MVID `7156ea0f-cc6e-439b-bc8a-023789963761`. Independent compiled approval is `E1F6956A3C7CE67B187CE5058B312EA3662AE3204E03935296F7E9C326B6EA6E`. Its actual compiled bindings are retained as a profile proof.

`evidence/current-native-inputs.json` SHA256 `970C40A82F135B5129308984F88E633F165D7B886C963D76B6DDFD18A10E8A47` explicitly projects the 79 approved source/actual build-copy pairs from that completed build. It is an authored admission binding, not a replacement build receipt. It retains all six native references, the actual private NuGet configuration, source and compiled approvals, actual build receipt, DLL/PDB/MVID, and predecessor manifest as history. Each pair matched actual bytes and SHA256. Existing admission property and cardinality requirements are unchanged.

## Minimal changes

There are 74 selected controller/provenance files and 19 PowerShell scripts. Exactly four selected files differ from the approved 71553292 selection; the other 70 are byte-identical. Complete diffs are in `diffs`.

- `evidence/refuel-rcv1-profile.json`: current profile identity, harness, explicit current native manifest, current provenance/proofs, and the one harness expected-assembly row. Old proof rows remain an ordered prefix. Historical harness approval is retained as `previousHarness`.
- `scripts/runtime-admission-refuel-rcv1.ps1`: only the exact current profile SHA256 and profile ID literals.
- `scripts/runtime-validation-refuel-rcv1.ps1`: only the observer's expected harness MVID literal, from historical `81dd8c0c-e29f-43eb-994d-510b70aa149c` to actual `7156ea0f-cc6e-439b-bc8a-023789963761`.
- `assemble-rcv1-reader.py`: the same single MVID literal, keeping the preserved assembler consistent. It was not invoked.

The hook tokens, owner, assembly, family, priority, before/after lists, and all other reader predicates are unchanged. Both already-reviewed live post-destruction count predicates remain zero; archived payment counts remain 20. The strict lineage reader and controller are otherwise unchanged. Product DLLs (995694D7 and Core9F6AE571), 425 product source pairs, six references, 113 content files, 14 Harmony files, ten assembly rows except the harness identity, DTOs, package order, case selection, game/workshop/player roots, protected roots, and generated preference/English content behavior are preserved. No language setting was introduced.

The complete 74 ordered strict-reporting outcomes are identical between the accepted PS5 and PS7 records (B7D0F684 and E22F6FBC); the existing cross-shell acceptance is `157B6FE056F0EC2E78DE2D25AF4EC4149BF54BA0AA230E547DF4547236C5D255`. These are accepted reporting controls, not a successful new native recovery run. No control was rerun in this authoring step.

## Preserved failure and remaining work

`historical-failed-inputs.json` pins the original top-level and evidence files of failed run `b399a619783b45de9f067a3a7fe40b4f`. The historical record was not changed. Its native payment was destroyed with live count zero; the old fixture's expectation of 20 stopped the scenario before later recovery checks. The new harness fixes those fixture expectations. The full actual scenario still needs to run through its refused decisions, archive, stale duplicate, new native B payment, and after-B/archive checks.

The intentional external ambiguity and Unity error `Ordinary refuel recovery remains unresolved` must retain the public failed outcome. Any eventual nested recovery acceptance must not turn that expected external failure into a public pass. UI/menu/rendering/save/restart claims remain unaccepted.

All copied historical controls, generator scripts, other profiles, `HANDOFF.md`, and `root-actions*` files are provenance only. They are not current execution recipes; this `CURRENT-HANDOFF.md` describes the current binding. The sole proposed next operation is the separately bound finite Prepare wrapper in `haulersdream-rcv1-destroy-count-prepare-wrapper-author-20260920`, after an independent reader adopts this source/profile and that recipe. It must generate its real GUID. Actual prepared-state review and a separately bound Launch/Verify recipe follow later.
