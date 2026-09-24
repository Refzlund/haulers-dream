# F07 reviewed feature port

20 September 2026. The reviewed F07 feature is now present in the current workspace and has compiled once in isolation. **No RIMMSqol editor/gameplay acceptance, native launch, deployment or commit is claimed.**

The exact source is the final 44-file proposal in `%TEMP%/haulersdream-rimmsqol-bounded-deposit-author-20260913/proposal`, manifest SHA256 `999ACB1ED98D741A9798449FE44F9F2E2E099DAD8D2FC32120804CB29700ED3E`. This preserves the previously reviewed bounded container-deposit and custody corrections; it is not the older initial command-bridge proposal.

## Port and overlap decision

All six existing destinations were byte-identical to the original command-bridge manifest's pinned bases. There were **no conflicting hunks**. The final proposal replaces those six exact bases and adds 38 previously absent files. All 44 resulting files are byte-identical to the reviewed proposal.

The six modified files are `BulkHaul.cs`, `FloatMenuOptionProvider_HaulNearby.cs`, `HaulersDreamDefOf.cs`, `JobDriver_BulkHaul.cs`, `JobDriver_UnloadHauledInventory.cs` and `NearbyHaulCommand.cs`. New files comprise four C# implementation files, two definitions and 32 definition translations. The change exposes the real editable WorkGiver, keeps native local menu queries outside job-factory authority, reads live drafted/direct-order preferences and gives only successful identified sweeps quantity-limited drafted delivery.

Fresh exact before copies are under `before/Source/HaulersDream/`. Three original author's before-copy paths no longer exist (`HaulersDreamDefOf`, both job drivers), but the current files **and** retained F34 build copies match their complete original pinned hashes. This was checked before applying anything; missing originals were not inferred from a diff.

`port.diff` is the complete before → current feature diff, including additions and missing-terminal-newline markers; SHA256 `145B3C974DBBA2AF2C50BE56ED98C9F08F643684A6A89D739C43710FE036580B`. `port-result.json` records each original/proposal/current identity and the empty conflict list. `product-before.json` / `product-after.json` record the complete product selection: **423 → 461 inputs, six replacements and 38 additions; all other 417 pre-existing inputs unchanged**. Therefore committed F10/F33/F34/F45 changes and other mixed storage/robot/refuel changes were preserved. README, COMPATIBILITY and shared status documents were not edited.

`port-checks.json` records successful parsing of all 34 added XML files and exact agreement of each new WorkGiver gerund with the preserved F10 keyed activity and JobDef report in all 16 locales. The native WorkGiver defaults still disable drafted use until the user enables it; ordinary automatic work remains outside this forced command.

## Actual isolated build

`build-product.ps1` reuses the existing product build command and empty-source NuGet configuration. It copies the entire current selected product, checks all source/copy hashes before and after, sets the known nonexistent `RimWorldModsDir` deployment guard, disables shared compilation/node reuse, and owns one hidden dotnet process with a 300-second bound. It does not copy the older product tree over current source or deploy output.

Actual dotnet PID **30772** joined with exit **0**, without a deadline, on 20 September at 09:51:11 UTC. Complete stdout reports **zero warnings and zero errors**; stderr is empty. All 461 selected inputs/copies were unchanged and the deployment path remained absent. The owned shell also returned exit 0.

Output: `C:/Users/Arthur/AppData/Local/Temp/haulersdream-f07-product-build-20260920/1.6/Assemblies`.

| Artifact | SHA256 |
|---|---|
| HaulersDream.dll | `D0C7FBE00E0C194E640331D98ACDFA507FA191CFBFCD04F623BC68C16029CBC6` |
| HaulersDream.pdb | `5EB6C53C8286868176F0C76FACF333F68387FCC7CF7529D81B9EF2C38A51607B` |
| HaulersDream.Core.dll | `186D88120BFC3ABAE7D37E55FE13C29A790D207AFC0448B40AE8B0DD8899B12A` |
| HaulersDream.Core.pdb | `1A8261D5B3D1AE79DDAFCA514FDE9E99C11C557166A7EF8FC1F4852E0BB54CED` |

`product-inputs.json` gives all actual selected source/copy paths and hashes. `product-build-start.json`, `product-build.json` and full `product-build.stdout.log` / `product-build.stderr.log` retain the actual build. Root's independent current source/compiled review is next. Then use the finite real-editor/command scenario in [f07-next-execution-review.md](../f07-next-execution-review.md); source/build alone cannot close F07.

The port and build scripts refuse to overwrite their completed outputs. They are retained descriptions of the performed operation, not instructions to repeat a successful build. All files here are within the repository's existing ignored evidence tree.
