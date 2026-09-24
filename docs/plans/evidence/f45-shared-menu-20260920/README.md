# F45 shared-provider menu verification

All three actual shared-provider profiles are now independently accepted: **52 cases** covering native, Periodic Bills and Ingredient Threshold menu creators. See [runtime review](runtime-review.md). Runs `4e81a0a0ccb746cd877d9c74893c9894`, `d8b30e4ad2aa40bf88a48c90bd79a8e5` and `30e0884c46a94688b489d055fe03eba4` retain their complete evidence under `native/`. This resolves this menu slice; report-level closure and final integration are tracked separately.

The executed successor is `src-v2` / `Assemblies-v2` / `controller-v3`, incorporating the [native rejection-message overlay correction](../f45-menu-20260920/message-overlay-correction.md) and a byte-identical dependency copy at a shorter path. The earlier controller-v2 run failed before admission because Mono could not read a 278-character source path; its failure remains retained. The build passed with 40 DTO warnings and zero errors. The original source, build and selection below remain historical records. No profile or product behavior changed in either correction.

20 September 2026. The actual companion build passed: **40 CS0649 warnings confined to JSON input DTO fields, zero errors**, SDK 8.0.421. Full output is in `build.log`; `build-result.json` records exit 0, certificate generation disabled, and the nonexistent deployment guard. No Prepare, Launch or native game execution occurred in this build/selection task.

The source includes root's exact `src-v2` correction: the actual SmeltWeapon recipe is uncountable but batchable. `source.diff` compares the current six C# files and unchanged project with that corrected baseline. The independent [source review](source-review.md) accepts this exact compiled source. That review is not runtime acceptance.

Three profiles use the actual original F45 HD `6828B52A…` / Core `0C46DD59…`, plus the privately acquired packages listed in `provider-packages.json`:

| Profile | Cells | Private copy entries | Expected loaded images |
|---|---:|---:|---:|
| `candidate-original-ego-cl` | 16 | 1,733 | 18 |
| `candidate-all-pb-first` | 18 | 1,751 | 20 |
| `candidate-all-it-first` | 18 | 1,751 | 20 |

The first profile observes both actual EGO/CL transpiler contributions on the native creator. The mixed profiles require PB and IT respectively to be the actual observed winning creator. They record original delegates, countability guards, CL's three setup writes before HD completion, narrow repairs to IT proxies, and preservation of previously unknown contributions. Package order alone cannot pass the creator assertion. No priority override is installed.

Whole acquired provider packages preserve LoadFolders and layered assets. TD Find Lib's four unconditionally loaded support images (Royalty, Ideology, Biotech, Odyssey) and required TDS Bug Fixes are included. There are 1,772 input pins, 23 fixed pins and 15 source trees. The private-runtime protections are unchanged.

The actual companion is `Assemblies/HaulersDream.PbMenuFixture.dll`, SHA-256 `B2F0DF23496CC699387FAD41A97C13DB69A43436D18A2BBBF3C69E8D4EA610C5`; PDB `08E96BE5A9F30A9429EE12995236474700AD6BDFE266B38CCB8E718CBD7A1DBD`. A reflection-only read measured MVID `397e0536-6ebf-4cdd-ad70-b00534368d4c` (`compiled-metadata.json`); the selection pins it.

The six executable files in `controller` are byte-identical to `f45-menu-20260920/controller-v2`. Only selection, manifest and action-contract data differ. Selection SHA-256 is `26D52246D5B6062514C4D2544AC4410030D4E7FF3AEF002BBA33EB127CA379E2`; manifest is `A592A1D20694C11DB01878A18D7AEE46D0F3D75DCEC72E3318FAEC543DC03B48`.

Use the same bundled Python `C:/Users/Arthur/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe` with `-E -B -X utf8`, this directory's `controller/run.py`, and the existing operations:

```text
Prepare --profile <one of the three names above> --review <actual root review record> --review-sha256 <its actual hash>
Launch --run <actual prepared private run directory> --review <actual root review record> --review-sha256 <its actual hash>
Verify --run <same actual directory, after native exit/join> --review <actual root review record> --review-sha256 <its actual hash>
```

One unchanged observer comment describes the ordinary read-only constructor observations. The two explicitly labelled synthetic unknown-contribution cells deliberately append their owned stimulus before HD composition; their events disclose this and snapshots then require original object/delegate/presentation preservation. They are recurrence controls, not new real provider claims.

The original 19 pending identifiers remain untouched. Dynamic query/tag state, IT enumeration of a new Def, nested/error controls, rendering/editors, scheduling, persistence, multiplayer and final combined-product integration remain separate evidence gaps. A passing menu slice cannot close whole F45.
