# F11 v9: bounded windowed presentation discriminator

Ready for root source review; no Prepare or native execution by the author. There is no host or product rebuild.

The only launch behavior change is `-force-d3d11-bitblt-model`, appended only for `F11-PRODUCER`. The actual private preferences remain 1024x768, fullscreen False, UI scale 1. The native v7 host, current product, strict translated screenshot and all input/quantity/save oracles remain byte-identical. RESAVED launch arguments are unchanged.

The existing input worker now passively checks its already bound HWND/PID/creation-time/class/private desktop while waiting for input, at most every 250 ms within its existing 260-second lifetime. It reads complete lines of the existing events stream and records the latest actual `f11-ui-step` sequence/time/name. A receipt under `f11-input/window-samples` is emitted on phase/state changes and at terminal/deadline. This captures the previously unobserved late render boundary. An unfinished trailing event line is not consumed and is read only once after completion. The synthetic stream controls in this directory test that reader only and are not native evidence.

There are no new platform APIs, show calls, focus operations, display changes or synthetic UI callbacks. The original single conditional startup `ShowWindowAsync(4)` remains. The input message/receipt body is byte-identical. Ownership and input-desktop checks remain mandatory; a window already destroyed after a native terminal result is recorded as unavailable, not treated as a successful rendering observation. At any earlier point an ownership/API failure is recorded and fails the worker.

`source.diff` is the complete two-file delta. `before/` preserves the exact prior controller/host source/worker and v8 selection `D84FC32E220B3E328479BE1F709FA55591AAD80E40A1B3ACB57A2023F57B0550`. All 17 files of failed v8 native `8df05540f3d1467b8e9b9e628a0419cf` remain untouched and are hash-checked by `before/capture-pins.json`.

Validation in `audit.json`: PowerShell AST parsing and Python syntax passed; five incremental event-reader controls passed without invoking native main; 736 unchanged selected file pins, 85 reference pairs, all preserved capture/source pins and the existing transport body passed. Current source was copied to the new private `fixture-source-v9` directory. No existing private source snapshot was replaced.

- Selection: `55E31E8CFDF921589AA672BEAAEF6754445D0713512CF06C67AD17880B989762`.
- Worker: `B7D6504E3944E05A2570BBD4E729F3956D50F7FAD2013A262345E06F05878B5F`.
- Controller: `7C8DDCABC58E8BD47F969414A54BEF342685853DE60A1B32E73F76F84E9E4ACE`.
- Unchanged host: `B842099B666BA77FD45FC2D4C2CB170EF42146C380A5F85800367606DFD91A16`, MVID `990d6c08-baea-42ed-81ab-68cd25f6d171`.
- Unchanged HD: `15D6ECE69478A31F8EB2B16F27FA2015C69A00D079918105BB21F483B1370292`; Core: `C09E2132E2B5CA6B1C45D3082AE44B4EF87B062B4FA297A9E5FBD831CB45FE19`.

The official API basis and limited retrospective conclusions are in `../source-review/input-v7/V8-RENDERING-REVIEW.md`. A flag and a visible-window receipt are not evidence that rendering works. Root's fresh native attempt must still observe actual play-UI events, repaint/control geometry, owned requests/receipts, actual control and physical results. Preserve a fresh failure without activation/fullscreen fallbacks.
