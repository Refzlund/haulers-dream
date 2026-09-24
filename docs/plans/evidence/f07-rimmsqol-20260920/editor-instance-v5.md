# F07 native editor instance lifecycle correction

Run `1f3cf7be0f2640ac92e7b354ff08797d` remains failed and fully retained. It validated the previous navigation fix, actual setting application, reset, both enabled undrafted hauling options, and the default drafted exclusion. The next re-enable failed because the fixture retained an editor instance that RIMMSqol had removed from its storage list during config saving. No product failure or acceptance is inferred from this fixture error.

The actual selected `RIMMSqol.dll` (`1152B0C1`, MVID `05d66edb-d146-4021-9c0b-f45f1303b323`) shows the complete lifecycle:

- `SettingsInstance.reset()` adopts the original values and sets active/dirty/localDirty false.
- `QOLMod.WriteSettings()` calls ApplySettings, then native config saving.
- `SettingsStorage.ApplySettings()` restores the actual WorkGiver from its saved original when the stored instance is inactive, then removes that override.
- `SettingsStorage.ExposeData()` in Saving calls `RemoveAll(si => !si.getActive())` on the actual stored list.
- The real selection page's `generateListForSelection(properties)` recreates missing entries in that list. Setting fields on the old detached instance cannot recreate registration.

The retained selected-binary decompiles are in `%TEMP%/hd-f07-20260920/native-api/`: `RIMMSqol.genericSettings.SettingsInstance.cs`, `RIMMSqol.genericSettings.SettingsStorage.cs`, and `RIMMSqol.QOLMod.cs`. The newly read SettingsInstance binary agrees with the package source; its bounded metadata-only reader joined successfully.

The complete correction is `editor-instance.diff` (`871C4705017D5828390F1F80490E86E712767C9931286E6CD850BE365BCD627B`). Before every helper Apply, it reacquires the unique HD instance through the actual public selection-list generator and verifies that exact object is registered. Native `set` and `WriteSettings` still perform all mutation, followed by strict actual WorkGiver readback. The event records whether the selected object changed. There is no direct WorkGiver write or manual insertion into storage.

The remaining GUI sequence uses that same Apply helper for drafted permission, direct-order permission and auto-pick priority changes; queued revocation/re-enable also uses it. Active edited instances remain registered across saving. The final producer save therefore retains the current active instance. Restart already obtains its model through the actual selection generator and checks native loaded fields before changes. After the final restart reset there is no further Apply; cleanup may reset the now-detached model again, but the successful native reset/save has already restored and removed the live override. No additional lifecycle correction is needed in those paths. The earlier actual editor screenshots need no positioning change.

Prior source `A4FDD976` is preserved as `src/RimmsCommand.v4.cs.txt`; prior builds and failed runs remain untouched. Current source is `38BC1ED57F7FE29A2AD19605FD86F14E4A5585D0F87437068BD8173B516679D4`.

Fresh build-v5 completed once with **zero warnings and errors**, retaining its complete build log, source/output pins and actual metadata under `%TEMP%/hd-f07-20260920/build-v5`:

- DLL `46A2C87D73FA810B69D7E502D5AD76B4A5A5FB6BE8DC0ACE93720B84ACA90910`
- PDB `AD7BDEF33C23953D977864F1023F93A32DA74DE4E8F5105C04E1C0BAB3A80EF8`
- Actual MVID `a4a62149-2837-4080-b7d4-10cb93c4a03e`

Reuse the existing producer/restart commands with only `HarnessAssembly` changed to `build-v5/Assemblies/HaulersDream.RuntimeHarness.dll`. Product, Bootstrap, project, controller and packages are unchanged. No Prepare or native execution was performed. Downstream native commands and the actual transit restart still need execution and review.
