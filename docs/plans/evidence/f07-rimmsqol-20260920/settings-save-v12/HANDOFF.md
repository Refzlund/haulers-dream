# F07 v12 product: settings saves preserve queued work

Ready for root's paired native replay. No Prepare or native execution was performed here. The v11 failed run `2fee343c1edd4ca4a9de57c8f23762b6`, authority-v10 product and v11 host remain untouched.

The only changed product input is `Patch_ScribeSaver_InitSaving.cs`, selected SHA-256 `2F61888DABCC4D0EB6731D8229FE5B2B6D75967215141524230172994B31A695`. It restricts existing queued-HD-job cleanup to the native `savegame` document root. Settings and exported-object writes no longer cancel jobs. Real game-save policy and current-job protection are unchanged. See `product.diff` and the independent `source-review.md`, which separately identifies the unresolved explicit-command persistence policy on actual game saves.

`build-product.ps1` copied all **461** authority-v10 frozen inputs, changing exactly this one source and preserving the other **460** byte-for-byte. The actual build passed once with **zero warnings/errors**, 27.68 seconds; owned process **20300** joined exit 0 with no deadline. Original inputs, selected copies and the absent deployment guard were checked after build. All **99** runtime files were retained from that product except the four freshly compiled DLL/PDB outputs. F20, F35, H&H, F36 and other newer working-tree changes were not imported.

Product: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f07-settings-save-v12-20260924/Product`.

| Image | SHA-256 | Actual MVID |
|---|---|---|
| HaulersDream | `82198D99C56155B664AD9F4239B0CF4B0EBF8D64852B445667AF12B9D1AE2B3E` | `3be32c3e-1e4b-40d4-98a2-67d703026a25` |
| HaulersDream.Core | `F24B0569229718941ECFC83EDC1FFA9C664DD64F8EC299B1E4C34C87795CD040` | `52980237-98b1-41c1-b94c-47d95a90508d` |
| Reused v11 host | `CED49D6F48DB4D056DC4E153FE748B038F3A08A22A945509068FA6DF9B002C49` | `77e3ef79-946a-4e95-a32b-6a2bd5acb46b` |

The host is the exact existing `%TEMP%/hd-f07-20260920/build-v11/Assemblies/HaulersDream.RuntimeHarness.dll`; it was not rebuilt. Successful reflection-only metadata receipts for both new DLLs are retained here. Core source is unchanged; its output identity changes with the fresh deterministic build path.

Use `../controller-v12/scripts/runtime-test.ps1`, SHA-256 `FF809C8B570E38BC4B55E7920AEF529A5275A896BB4F865EBB542A0FFC834A9F`. The complete 21-file controller was copied from v10; only the main script differs, with exactly four product-hash replacements. Its parser reported no errors. `controller.diff` and `controller-inputs.json` retain the comparison.

Use `../launch-v12.ps1`, SHA-256 `B74F2D2C8C20E72B4CFB11874420B533A657E7620ED93F32D2480BB03D2307F8`. Its only difference from v10 is selecting controller-v12. Root must still invoke it through the established inactive-desktop owner. Other F07 producer/restart parameters and the acquired RIMMS selection remain unchanged.

The next producer must retain the actual queued job after RIMMS saves the revoked permission, then allow the real predecessor Goto to finish and observe native CanBeginNow rejecting the queued command. It must also complete the unchanged interruption and real transit-save stages. Only an accepted producer can supply the bound restart. Do not weaken the oracle or disable cleanup to make it pass. The root-name gate is not evidence that queued explicit commands survive a real game save; that separate limitation is explicitly retained for later policy work.
