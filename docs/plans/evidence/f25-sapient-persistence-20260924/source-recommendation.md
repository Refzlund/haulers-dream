# F25: generated sapient races omit the HD inventory component

Source investigation supports a small provider-conditional XML definition that adds `HaulersDream.CompHauledToInventory` to Big & Small's supported component whitelist. No product change, build, Prepare, game launch or feedback refresh was performed. Native reproduction and acceptance remain open.

## Scope and retained inputs

Read all four frozen records: C023, C025, GH263 and GH263-C5360067014 in `docs/plans/scoped-feedback-register.json`. The reduced report explicitly uses a lifterbot with HD, Sapient Animals and their requirements. Animal and mech conversion, save/load, full application restart, and the race-change observation belong to the same report campaign. F05/F06 robot acceptance does not cover this provider.

No installed or earlier task-owned Big & Small provider was located. Root authorized acquiring public provider inputs without subscribing, downloading through Steam, or touching player/workshop content. Selected source and the repository's shipped DLLs are retained at:

`C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f25-provider-source-20260924`

`source-audit.json` binds 48 retained provider/native/product/source files. The provider selection JSONs retain each repository commit, blob identity, source URL, size and SHA-256.

| Input | Identity |
| --- | --- |
| Current Framework | `RedMattis/BigSmall_Framework`, commit `4b7d47520a580e7e93ae759667bc6cba0d5d2f59`, 2026-09-18; About version `3.0.0.0`, package `RedMattis.BetterPrerequisites` |
| Current shipped 1.6 DLL | `55A5941DAE0BD7CC3ED3CC36168E3ACFCF27B7A19BE0397B38BBB06E88DD7CD0` |
| Latest public commit before the report | `249e727168653a72c0a11efb9387f7bddfd8ef32`, 2026-07-07; DLL `A731F5655DF36D1ACBFA2F1ECFBE7B67F10088138B9509C6A5CEAAED21079521` |
| Sapient Animals | `RedMattis/BigSmall-Sapient-Animals`, commit `e6fd696d0ce21bb11dcfe34a11500e4df0c007df`, 2026-06-21; package `RedMattis.SapientAnimals` |
| Native reference | Installed `Assembly-CSharp.dll` SHA `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`; installed version `1.6.4871 rev590`, report says `1.6.4871 rev591` |

The historical repository selection is not claimed to be the reporter's exact Workshop bytes. Both retained Framework DLLs decompile to **identical** complete `HumanlikeAnimalGenerator` and `RaceMorpher` bodies. Their relevant XML whitelist is also byte-identical. These inputs are a selected source investigation, not a complete loadable provider package.

## Concrete failure chain

1. HD's `Patches/HaulersDream_Pawns.xml` adds its component to XML pawn definitions. It does not patch definitions generated later by another mod.
2. Big & Small's `BSCore.RunBeforeGenerateImpliedDefs` calls `HumanlikeAnimalGenerator.GenerateHumanlikeAnimals`. The generator merges the human and original animal/mech component properties, then filters them through the union of every `HumanlikeAnimalSettings.compWhitelist`. It matches either the properties type name or the component class name. It assigns that filtered list to the generated ThingDef. See current source `HumanlikeAnimalGenerator.cs:182–200`, shipped decompilation `HumanlikeAnimalGenerator.shipped.cs.txt:176–190`.
3. Its shipped `HumanlikeAnimalGenerator.xml` whitelist does not contain HD's component. The public repository code search found no HD-specific integration and the only 1.6 XML whitelist is this retained definition. Consequently the generated sapient definition excludes HD, even though the original Human/animal definition contains it.
4. The real Sapienator effect (`CompUseEffect_SwapThingDef.DoEffectOn`) calls `RaceMorpher.SwapAnimalToSapientVersion`. That creates a fresh **Human colonist**, then swaps its definition to the generated sapient race. The converted pawn has a new identity relative to the original animal; a fixture must bind the identity after conversion, not insist that conversion preserves the animal's original ID.
5. `RaceMorpher.AddMissingComps` returns immediately if the target definition has no components. Otherwise it removes live components that do not match the target definition, including HD, by mutating `pawn.AllComps` directly. It does not update native `compsByType`. See source `RaceMorpher.cs:720–778` and shipped `RaceMorpher.shipped.cs.txt:662–713`.
6. Installed native `ThingWithComps.InitializeComps` creates the component list and `compsByType` dictionary together. `GetComp<T>` can return a cached component that has subsequently been removed from `AllComps`, provided the live list still has at least three entries. With fewer entries native takes a different direct-list branch. If the generated list is empty and the provider returns early, the original human's list survives instead. These distinct source paths explain why fresh conversion can appear functional despite the generated definition lacking HD; the actual reproduction must record which path occurs rather than assume stale-cache use in every reduced mod list.
7. Native saving enumerates **the live component list**. Loading calls `InitializeComps` from the saved ThingDef during `LoadingVars`, then enumerates the rebuilt list. It never reconstructs a removed component merely because old HD fields occur in XML. A generated definition that omits HD therefore loses the component on load/full restart regardless of whether fresh conversion retained it live or exposed a cached instance. `ThingWithComps.installed.cs.txt:123–158,193–249` retains these exact installed bodies.
8. Both HD automatic availability (`BulkHaul.cs:590`) and bulk job construction (`BulkHaul.cs:720`) explicitly require `GetComp<CompHauledToInventory>() != null`. `YieldRouter.IsCandidate` also requires it; its race eligibility reads live race properties rather than a persistent per-pawn eligibility cache. Big & Small explicitly assigns human intelligence, human work settings/think trees and human flesh type for converted mechs. There is no source basis for broadening HD's animal/mech eligibility settings to repair this missing component.

This is a concrete source-backed mechanism matching the report. It is not yet an observed native F25 reproduction. The count-dependent fresh-conversion path and exact physical hauling must be observed.

## Smallest proposed correction

Add one HD-owned XML Def, conditional on `RedMattis.BetterPrerequisites`, using the provider's published extension point:

```xml
<Defs>
  <BigAndSmall.HumanlikeAnimalSettings MayRequire="RedMattis.BetterPrerequisites">
    <defName>HaulersDream_SapientPawnComponents</defName>
    <compWhitelist>
      <li>HaulersDream.CompHauledToInventory</li>
    </compWhitelist>
  </BigAndSmall.HumanlikeAnimalSettings>
</Defs>
```

Suggested product scope: `Defs/Compatibility/BigAndSmall.xml`, a short `COMPATIBILITY.md` entry, and one changeset. The provider combines all these definitions before generating races; a separate named Def avoids rewriting its default list or depending on its default Def's name. Its HashSet and merge logic already deduplicate component identities. Conditional loading keeps the foreign type absent when the provider is absent. Only HD's exact component class is whitelisted; allowing general `Verse.CompProperties` would admit unrelated components and is inappropriate.

This should retain the existing HD instance during fresh conversion and make native reconstruction attach exactly one component on load, including existing converted saves. Native load can then read old fields if present; genuinely missing historical fields receive the component's normal defaults. State already omitted by a previous broken save cannot be invented. No new persistence schema, periodic repair, race override, optional assembly reference or Harmony patch appears necessary.

Do not patch the provider's generic component cache or add broad component injection on `GetComp`. Those would expand beyond F25 and make real saved-state loss harder to detect.

## Bounded native witness recommendation

Acquire a complete pinned loadable provider package only after this source recommendation is accepted, retaining the selected shipped DLL identity. Reuse the proven four-mod private transport and climate controller with the two provider packages and actual owned Biotech support needed for the reported lifter. No HAR or unrelated compatibility matrix is required by the reduced report.

Use one reported lifter and one ordinary animal whose sapient race can perform hauling (for example Monkey), plus an ordinary human reference. Convert through the actual Sapienator component/provider conversion entry; record the new converted identity. Observe original and converted ThingDef component lists, live `AllComps` identities/count, `GetComp` identity/live membership, race intelligence/flesh/mech flags, work capability, inventory and HD preference. Never remove a component, clear a cache or rewrite the generated race to manufacture the gap.

Baseline: require physical inventory pickup/delivery before save when the observed fresh-conversion path permits it; record any earlier component failure honestly. Save the actual colony, retain the exact XML, and load/restart the same converted identities. Require the missing generated/live HD component and actual bulk-planning failure to explain the regression, with adequate native storage and no health/work confound. Preserve this real old-save checkpoint for the candidate.

Candidate: load that original checkpoint and require exactly one generated definition property and one live HD component, with `GetComp` pointing to that same live instance. Require actual multi-stack bulk pickup and physical delivery for both converted pawn types. Apply a real keep amount and opt-out preference, save partial tagged cargo through native saving, then verify the same converted pawn/def, keep/preference, held stock and valid tags through same-process load and full application restart. Resume hauling/unloading and verify exact conservation, kept remainder, no duplicate component and stable completion. An ordinary human remains functional; an unconverted animal with the ordinary animal option off remains subject to its existing eligibility rule. A provider-absent startup/Def load check is sufficient for the optional XML dependency boundary.

The candidate may use normal defaults for HD fields genuinely absent from the baseline save; the witness must distinguish that migration from preservation of fields written by the fixed candidate. Do not assert recovery of vanished historical keep settings or tags without an actual serialized witness.

F25 stays open until these physical lifecycle observations pass and are independently reviewed. The source correction is small enough that broad managed/product builds or a new general-purpose harness are not justified before this bounded native design is selected.
