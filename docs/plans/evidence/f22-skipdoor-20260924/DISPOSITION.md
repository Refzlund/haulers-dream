# F22 disposition after dependency investigation

2026-09-24. **Source investigation and provider/version guidance are available; native compatibility remains unverified because the required Royalty data is unavailable in the searched local installations/copies.** No fixture was fabricated, no dependency check bypassed, and no native launch or Prepare was attempted.

C001 asks about “Skipdoor Pathing” without an item ID or failure report. Do not silently interpret it as a requirement to implement support for every later independent fork. The three names below are separate products.

The original [VPE Skipdoor Pathing, item 2995157602](https://steamcommunity.com/sharedfiles/filedetails/?id=2995157602) is explicitly titled **[ABANDONED]** and tagged **1.4** on its current primary Workshop page. Its status is sourced from that page, not inferred from age. A complete text capture and its hash are retained in the acquisition folder and `dependency-evidence.json`. No 1.6 compatibility claim is made for that original release.

[Redux, item 3645096243](https://steamcommunity.com/sharedfiles/filedetails/?id=3645096243), has an actual acquired 1.6 implementation that changes native path graphs without substituting the current HD job. It remains the strongest source-backed candidate for productive HD portal movement. The required minimal witness is still the previously described actual two-door bulk pickup/unload with exact cargo accounting, original job/target retention and actual portal crossing. No test result is claimed. Same-map reachability, provider pawn eligibility and HD physical pickup radii remain explicit limits.

[Unlimited, item 3605522450](https://steamcommunity.com/sharedfiles/filedetails/?id=3605522450), has three distinct provider-owned interruptions in its acquired DLL:

1. It rewrites the native path destination to the entry cell without suppressing the resulting final-arrival notification.
2. Its `DoorTeleporter_Teleport_Logic.TryRepositionIfPawn` calls native `Pawn.Notify_Teleported(true, true)`. The actual native method ends the current job with `InterruptForced` when its first argument is true. Fixing entrance arrival alone therefore cannot preserve a productive HD job through teleportation.
3. Its drafted continuation creates a fresh vanilla Goto instead of resuming the original HD target/mode/job.

The provider's saved plan also lacks originating job identity, so cancellation and load restoration need explicit handling if an HD cooperative bridge is pursued. Existing job exclusions permit ordinary walking but are **containment, not resolution of productive portal compatibility**. No broad exclusion bridge has been implemented or accepted as a fix.

An independent future bridge decision should cover all three seams together: bind an HD transit to its originating job/driver and actual target/mode; consume only the entrance waypoint arrival through the provider's existing transit; keep the job through the provider's teleport notification; resume the original native path rather than enqueue a drafted Goto; clear stale plans on cancellation and preserve/validate identity on load. The old PU&H suspend/resume branch remains unsuitable for HD's non-suspendable gather jobs. This is a design outline, not an implemented or proven safe subsystem, and should not become an unrequested fork-support commitment.

## Dependency evidence and actual search boundary

VPE `2842502659` and VEF `2023507013` were acquired privately using anonymous SteamCMD. Owned process 26384 joined exit 0; acquisition receipt, complete packages and selected assembly hashes are retained. Actual VPE `About.xml` requires `Ludeon.RimWorld.Royalty`, Harmony and VEF. No ownership or entitlement assumption was made.

The configured Steam library list contains only `C:/Steam`. The installed RimWorld `Data` directories are **Core, Biotech and Odyssey**. A bounded search tested **2,848 exact Royalty About paths across 712 candidate bases**, including the configured game location and retained task game layouts beneath **274** matching temporary task roots and known QA roots. It found zero. A separate recursive About.xml census under `HaulersDreamQA`, outside Mods folders, found 32 About files and no Royalty game-data path. `royalty-local-search.json` and `royalty-qa-census.json` record the scope. These findings say where data was not found; they do not say that the player does not own Royalty or that no copy exists at an arbitrary unsearched location.

The Redux fixture cannot honestly establish actual VPE compatibility in this environment until a legitimate installed or authorized retained Royalty copy is available. Do not substitute invented expansion definitions, remove the dependency, or report a source review as runtime support. Required action is obtaining that legitimate local game-data prerequisite; no purchase, client install, license bypass or further download was attempted here.

## Proposed player wording now

“The original abandoned VPE Skipdoor Pathing release lists RimWorld 1.4; we do not claim 1.6 support for it. The current Redux implementation preserves Hauler's Dream's native hauling job in source review, but we have not yet completed the actual VPE runtime check. Unlimited uses different movement handling and currently has job-interruption conflicts; excluding HD jobs there makes them walk normally and is a workaround, not portal support. These mods are not interchangeable. HD's normal pickup radii and same-map requirements still apply.”

Root owns whether that evidence-backed answer satisfies C001 as a compatibility question, whether to retain a runtime prerequisite under F22, and any eventual ledger or public-post change. No recurrence or resolved runtime result is established by this investigation.
