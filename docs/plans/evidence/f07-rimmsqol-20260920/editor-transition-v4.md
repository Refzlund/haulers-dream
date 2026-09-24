# F07 editor transition timing correction

Actual failed producer `136ffeff31894541abde074ab4c94ab4` remains failed and retained. It **did** discover the HD WorkGiver in the actual selection page (event 52, `f07/real-editor-discovery`). The later transition into its edit page failed with a missing `flowScope["edit"]`. Initial `QOLMod.NavigateToEditing(properties, null)` is valid native category navigation and remains unchanged.

The actual selected RIMMS binary `1152B0C1`, MVID `05d66edb-d146-4021-9c0b-f45f1303b323`, establishes the cause:

- `SettingsPropertyEditPage.transitionInternal` queues the edit-instance assignment with `Flow.addPostRenderCallback`, then queues navigation.
- `Flow.DoFlowContents` activates queued navigation and calls the edit page's `onNavigationHandler` first; then it renders the current page and executes post-render callbacks.
- Real RIMMS selection buttons call `transition` while their page renders. The original fixture called it afterward in Root.OnGUI's postfix, leaving the required assignment pending until after the next page already accessed it.

Actual decompiles are retained under `%TEMP%/hd-f07-20260920/native-api/`: `RIMMSqol.renderers.Flow.cs`, `RIMMSqol.genericSettings.editPages.SettingsPropertyEditPage.cs`, `RIMMSqol.renderers.PageRenderer.cs`, and the previously retained `RIMMSqol.QOLMod.cs`. All three new metadata-only reader processes joined successfully. The package's shipped source agrees; its main-button category path also calls `NavigateToEditing(props, null)`.

`editor-transition.diff` is the complete one-file correction (`E4383C788A37F7AE884CF480FAF759CAB6882EB4DBE4F39FEDFDB3F7D6C3D597`). The fixture queues a one-shot transition in a postfix of the actual current selection page's `DoPageContents`, during Repaint, before Flow's own callbacks. The exact native transition still populates `edit`. Step 4 additionally waits until the actual `CurrentPage` is a `SettingsPropertyEditPage`, so it cannot inspect edit-page geometry while the selection page is still current. No manual scope assignment, product change, synthetic click or relaxation of acceptance is introduced. Existing own-Harmony cleanup removes the additional observer.

Prior scenario `13E360F5` is retained as `src/RimmsCommand.v3.cs.txt`; build-v3 and the failed runtime are untouched. Current source is `A4FDD97606C8AF18D4A1663482EDB14633609F5A28035A76F9827F0CA95A5AA3`.

The fresh `%TEMP%/hd-f07-20260920/build-v4` compile completed with **zero warnings and errors**. Complete build log, selected sources, products and actual file metadata are retained there:

- DLL `71698872288148DF9723AF4652B1CDE5FC2F823937868B8692FA8EE26ECDBC96`
- PDB `EA73B9DCC96EE9579975EF019CE0A94368129D580B9588C0CEC719E4E423BFB3`
- Actual MVID `a6f5e27f-0502-437c-9c80-47cbd1bbed5b`

Reuse FIXTURE.md's exact producer/restart controller and arguments, changing only `HarnessAssembly` to the actual `build-v4/Assemblies/HaulersDream.RuntimeHarness.dll`. Bootstrap, project, controller, intact packages and product are unchanged. No Prepare or native launch was performed for this correction. Actual edit-page screenshot and downstream command/restart outcomes remain to be tested.
