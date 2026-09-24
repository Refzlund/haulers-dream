# F07 native overlapping-item setup correction

Failed v5 run `e825778899b2423fa20b458d6ddc479e` remains retained. It passed actual editor re-enable and HD provider/scanner query purity, then failed the fixture assumption that the clicked cell contained both its uranium and plasteel. This is a setup failure, not an established product defect.

Actual native evidence under `%TEMP%/hd-f07-20260920/native-api/`:

- `Verse.GenSpawn.cs:152–166`: spawning an item onto a full cell despawns an existing item and places it nearby. Ordinary ground admits one item, so spawning two different stacks at the same cell does not preserve overlap.
- `RimWorld.FloatMenuContext.cs`: obtains the list through actual `GenUI.ThingsUnderMouse`.
- `Verse.GenUI.cs:439–512`: items are selected from the clicked cell. The 0.8 radius is for pawns; adjacent items require a multi-item cell and distance to the actual UI pointer, so merely passing an adjacent-cell midpoint would not be a sound no-input correction.
- Installed `Buildings_Furniture.xml` defines the native `ShelfSmall` as a one-cell, three-slot shelf. `GenPlace` and `GenSpawn` respect actual cell slot capacity.

The full one-file correction is `native-overlap.diff` SHA256 `5CFA7495E7AC5899DAAB0BBDE939068BE2202B3D7D868DB5B04AFF9E5E0D9734`. It creates one owned native granite small shelf at the existing initial source cell. Low-priority shelf storage accepts uranium and plasteel; the unchanged Critical recipient accepts plasteel only. Uranium has no better destination while plasteel is actionable. The setup requires both real stacks to remain spawned in the same actual three-slot cell. The existing native context must contain both, and the actual scanner must reject uranium; it emits the actual native ordering rather than manufacturing one. All menu collection and command actions stay native. No pointer mutation, cached-list insertion, item-limit override or storage predicate bypass is used.

The shelf is added to existing owned-object cleanup. Material counts still track only the same plasteel; shelf construction is a fixture setup, not a new product behavior test. Later batches occupy unchanged separate positions, all inside the arena. I inspected the remaining helper/apply/reset flow, native non-idle queued handoff, personal-stock isolation in actual `DepositSwept`, and save/restart observations; no additional concrete setup mismatch was found. Execution still decides their outcomes.

Prior source `38BC1ED5` is retained as `src/RimmsCommand.v5.cs.txt`; prior build and raw failure are untouched. Current source SHA256 `3F12ADEA3B532D052D7E54889A057C449DCF591E34D6986F2E0577EF128C5A7C`.

Fresh build-v6 compiled once with **zero warnings/errors**. Complete build log, selected source pins, products and actual metadata are in `%TEMP%/hd-f07-20260920/build-v6`:

- DLL `EE45CE5E40B9F3F7F83C46568ED9061C55C4B6F1847CB46E0EF15D15D90AB004`
- PDB `546170B7491920959AD80B61F3A7A6DD996C766F028050779EF0EBF4D2DDB016`
- Actual MVID `36513846-a96d-49f9-82f3-bd6f66cd7770`

Reuse existing producer/restart commands, changing only HarnessAssembly to `build-v6/Assemblies/HaulersDream.RuntimeHarness.dll`. Product, host Bootstrap/project, controller and complete acquired packages remain unchanged. No Prepare or native launch was performed by the author. Native command/delivery and restart acceptance remain pending.
