# F11 v6: native Italian archive alias

Ready for root review and a fresh producer. The previous v5 run **4c11fc79d42c4fa6ab3a6b31b79bcd80** stays failed (37/39, U0) before the first input. Its source/selection and failed raw evidence remain preserved; no quantity or input acceptance is inferred.

The installed language is real and available: Core's `Italian (Italiano).tar` and the failed run's actual copied archive both hash to `BE7027F4C413850D69D43C69147D15FB051AF74A6565128FA81E6286ECC48B2C`. Its native `LanguageInfo.xml` names Italiano and `LanguageWorker_Italian`. `native-language-evidence.json` retains this exact archive member and the selected HD Italian XML hash/expected strings.

Actual installed source under `../source-review/language-v6/` explains the failure. `LanguageDatabase.InitAllMetadata` enumerates virtual directories, including language archives. `LoadedLanguage` retains folder name **Italian (Italiano)** and exposes **Italian** as `LegacyFolderName`. `InitLanguageMetadataFrom` merges HD's `Italian` directory using that alias. `AllDirectories` loads both native and legacy mod directories. The old exact `folderName == "Italian"` lookup therefore found none.

`source.diff` changes only this lookup and adds a native parser receipt. It selects the unique legacy alias, calls native `LoadMetadata`/`LoadData`, and requires actual `TryGetTextFromKey` results for the pinned HD title, quantity label and confirm label. This API only reads that language's own non-placeholder keyed replacement; it cannot silently use English fallback. The receipt records the actual full folder name, alias, native friendly name, contributing virtual directories and three strings. No synthetic language metadata, translation substitution, download, product change or input fallback was added. Every existing IMGUI, input, quantity, save and unload oracle stays intact.

Build: 9.68 seconds, zero warnings/errors. Native PS5 reflection-only metadata passes. `audit.json` verifies 647 unchanged product/controller/input pins plus 85 actual reference source/copy pairs; **only `F11Scene.Ui.cs` changed** among fixture sources. Full source snapshot is `fixture-source-v6` beside the existing frozen product. The first metadata-reader invocation used unsupported parameter names and exited before reading/loading any image; the subsequent actual base64-path invocation passed and is retained as `host-v6-metadata.json`.

- Selection SHA-256: `342142DE4E36FBCD291CD334C46C30E4D8F1B1346E1D71AA3B1130A53A74FF11`.
- Host: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f11-quantity-lifecycle-20260924-v1/host-build-v6/Assemblies/HaulersDream.RuntimeHarness.dll`.
- Host SHA-256: `9A238FE12D9DB0A1487DC57A223794379B2AE54D24664DABEBFD90A84E8224FC`.
- MVID: `5f8c8b72-99e0-4d99-b462-0c24114d6807`.
- Product remains exactly `candidate-runtime-v5`; controller, launcher and input worker are unchanged.

Use the same short owned TEMP/TMP root and inactive owned-window input contract. No Prepare or native launch was performed by this author. Native Italian receipt, screenshot, real input delivery and saved-remainder acceptance remain outstanding.
