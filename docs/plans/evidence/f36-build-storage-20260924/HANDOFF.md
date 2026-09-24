# F36 — Build From Storage pairing

**Initial source investigation: one narrow HD route defect found.** The later authorized three-guard correction and matched builds are ready in [fixture/HANDOFF.md](fixture/HANDOFF.md); no Prepare or native test was performed by this agent. At the time of the investigation below, no product edit or build had been made. The ordinary pairing is complementary by implementation, but the historical blanket compatibility answer should not become runtime acceptance until the route boundary below is checked and repaired.

## Exact scoped question

C029, talias, 12 August 2026 19:22:13 Copenhagen, asks: “Does this replace Build From Storage or at least is compatible?” C028, the developer, replies at 20:20:29 that it does not replace it and should be compatible. These are a question and a tentative compatibility answer, not a failure report or post-fix recurrence. Bodies and timestamps were read from `docs/plans/scoped-feedback-register.json`.

Build From Storage and Build From Inventory are different features. Build From Storage chooses an existing packed building when the player places a matching building from the architect menu. HD supplies raw construction materials from floor stock or inventories. HD does not implement the packed-building replacement feature.

## Actual provider inspected

Workshop item [3523011187](https://steamcommunity.com/sharedfiles/filedetails/?id=3523011187) was acquired with an isolated copy of SteamCMD. The complete current package has **118 files / 422,042 bytes**, including the provider's published source and Git metadata. Manifest **4189085856690658494**, About version **1.0.4**, package ID `buildfromstorage.programmerlily.com`, Harmony-only required dependency. Original SteamCMD bootstrap hash was unchanged; owned process 19228 joined with exit 0. The separate Steam console and workshop logs record a successful item download. The later workshop-details cache warning does not undo the downloaded bytes or installed manifest.

Package root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/acquisition/f36-build-storage-20260924/content/steamapps/workshop/content/294100/3523011187`.

The selected `Assemblies/BuildFromStorage.dll` is SHA-256 **47864AFBC11D68D4DB5D54825D0CD430515158AEAF187F004B416A709E216286**, assembly **1.0.0.0**, MVID **340d662d-9447-4fad-be99-111ab4adfe9b**. `actual-metadata.json` is from the existing metadata-only worker. The DLL is byte-identical to the binary bundled at upstream commit [5b101af12e382adac7cc5e5d98432c0163b0b67e](https://github.com/sam2332/Rimworld---BuildFromStorage/tree/5b101af12e382adac7cc5e5d98432c0163b0b67e). The actual DLL was decompiled with the native game references, retained as `BuildFromStorage.resolved.cs.txt`. Current upstream source changes the availability tooltip to translation keys while the actual bundled binary still has English text; compatibility conclusions below use the actual binary, not an assumption that all source and binary details agree.

The binary has three patches:

1. `Designator_Build.DesignateSingleCell` prefix finds a spawned matching `MinifiedThing` and matching stuff, excludes one already assigned an install blueprint, validates placement, and calls native `PlaceBlueprintForInstall`. It returns false only after performing that placement or rejecting it. If no match exists, ordinary blueprint placement proceeds.
2. `DrawPanelReadout` postfix displays availability.
3. `ResourceDeliverJobFor` prefix observes ordinary `Blueprint_Build` and logs when packed stock is available. It always returns true and never changes the returned job. It does not retrofit an old build blueprint into an install blueprint.

“In storage” is the provider's naming, not a promise about every storage system: its finder checks accepted spawned stock on ordinary storage buildings/zones and also permits unforbidden loose packed items. It neither searches pawn inventories nor enumerates opaque storage containers. It does not perform a pawn-specific reach/reservation check during designation; native installation later owns those checks. No new support claim is made for those cases.

## HD interaction and precise defect

The five HD `ResourceDeliverJobFor` postfixes explicitly reject `Blueprint_Install`: shared inventory, hand-haul sharing, inventory construction delivery, batching and supplies routing. Actual native `WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor` handles installs first and returns its own `HaulToContainer` job with the packed building as target A, the install blueprint as target B and count 1. The provider's prefix neither skips nor replaces that path. HD does not patch the provider's architect designation method. These source boundaries support the ordinary complementary pairing.

HD's **Plan Route** also includes install blueprints: `WorkKindResolver.TryResolveConstruction` accepts all blueprints, and `RouteSelection` includes them in the Constructible scope. Its per-stop scanner can correctly return a native install job. However, three raw-material paths in `RouteExecutor.cs` do not distinguish installation:

- In `Execute`, the `alsoBuild` total-demand loop calls `TotalMaterialCost()` on each constructible stop (current line 120).
- The suffix-demand loop calls `ThingCountNeeded()` for every returned job's constructible stop (current line 166), before restricting assignment to HD construction jobs. Native `Blueprint.ThingCountNeeded()` calls `TotalMaterialCost()`.
- If the native install scanner returns null, `BuildJobForStop` falls back to `TryDeliverFromOwnStock`, which calls the install blueprint's `TotalMaterialCost()` (current line 294).

Actual native `Blueprint_Install.TotalMaterialCost()` logs **“Called MaterialsNeededTotal on a Blueprint_Install.”** and returns an empty list. Thus a successful default haul-and-build route over an install emits errors from both demand passes; an unavailable install emits another error from the inappropriate raw-material fallback. The packed building is not a wood/steel ingredient request. This is a concrete HD boundary error exposed by the provider's ordinary result, and it also applies to native reinstall blueprints. It is not a proven historical user-reported failure.

Smallest correction recommendation: in this single file, exclude `Blueprint_Install` from total raw-material demand, suffix demand, and own-stock fallback. Continue invoking the native scanner and retaining its original install job/count. Do not drop install blueprints from route selection, replace their native driver, suppress the log, or add a provider-specific reflection patch. The queued native install jobs are already excluded from `ConstructTether.RemainingRouteDemand` by its HD-job-definition filter; no broader tether rewrite is justified. ConstructionBatch excludes installs through its `IHaulEnroute` requirement.

Exact pre-correction `RouteExecutor.cs` is retained as `RouteExecutor.before.cs.txt`, SHA-256 **074CE88751E77E7F4AB893FC4DF2FDD18BFB1424D1765006AFF22B49F422476C**. Other construction paths were read for diagnosis but not edited.

## Smallest useful native witness

Reuse the existing initialized-map host and inactive-desktop controller with Harmony, Core, the actual provider, HD and the harness. Preserve a matched pre-correction baseline failure; do not turn expected native error logging into a pass. No new verifier, physical input or broad construction-mod matrix is needed.

1. **One packed building plus one ordinary build in a real route.** In an isolated disposable arena, create one owned packed wooden stool in an accepting stockpile, a capable colonist and enough explicit wood stock for one new stool. Invoke the actual `Designator_Build` designation for a first nearby target; require a native install blueprint tied to that exact packed item/inner building. Before installation, designate a second target of the same definition/stuff. Because the only packed item is already assigned, the provider must leave an ordinary build blueprint. Resolve the actual HD construction route and execute its normal haul-and-build command over the two targets. Do not supply replacement install or construction jobs. Observe the native install job (count 1, exact packed target), HD/native material delivery to the ordinary build, actual completions, and exact building/material identities. The original inner building must be installed unchanged, exactly one distinct new stool constructed, and only one new stool's raw-material cost consumed. The baseline should retain the install material-query errors; candidate must complete without those errors. This tests the provider's distinct feature, no double assignment, normal fallback and both erroneous demand passes without repeating the accepted general construction matrix.
2. **Unavailable install cannot become a materials job.** Designate a second owned packed building through the provider, then explicitly forbid that packed source while leaving the target reachable and raw wood available. Ask the public `RouteExecutor.BuildJobForStop` for that install with the real resolved scanner. Label this as a job-selection check: the native scanner refuses the forbidden source, and HD must return null without a raw-material fallback/query or source mutation. Do not dispatch a fabricated job. Baseline logs the fallback error; candidate should not. Clear only owned state during cleanup.

Observe provider patch identity, blueprint/item linkage, complete native jobs and queue provenance, item movement and counts, terminal logs, and cleanup/protected-state receipts. The first scene should validate productive completion even if the baseline's error capture has already recorded failure. Preserve ordinary HD feature settings needed to exercise its normal construction path; explicitly record temporary fixture settings and restore them. Provider-absent general construction was already covered elsewhere and does not need another F36 matrix.

## Player guidance after native acceptance

“**Build From Storage is complementary to Hauler's Dream.** It reuses an existing packed building when you place a matching building, while HD gathers and delivers materials for new construction. Keep Build From Storage if you want that automatic reuse. If no matching packed building is available, normal construction proceeds. An existing build blueprint is not automatically converted when a packed building becomes available later.”

After the narrow route repair is accepted, a final sentence may state that HD construction routes preserve the normal installation job for reused buildings. Do not publish an unqualified all-storage/all-mods guarantee or claim this is a recurring fixed bug. Root owns implementation authorization, native execution, player documentation and ledger disposition.
