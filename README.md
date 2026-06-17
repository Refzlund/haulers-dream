# Hauler's Dream

A RimWorld **1.6** mod. Colonists use their inventories smartly when moving items, and carry out
their tasks more efficiently: fewer round-trips, less walking back and forth, more time actually
working. On top of that core idea it adds planning tools and a layer of quality-of-life
micro-management — all optional and tunable.

- **Steam Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652
- **Requires:** [Harmony](https://github.com/pardeike/HarmonyRimWorld) (`brrainz.harmony`)
- **Safe to add to existing saves**, and safe to remove (carried goods are never stranded).
- Everything is behind mod-settings toggles; defaults preserve or improve vanilla behaviour.

> **How this mod is made:** Hauler's Dream is largely implemented by AI models (Fable 5 and Opus
> 4.8), human-reviewed, and developed in the open. See [the disclaimer](#how-this-mod-is-made) below
> or [About/Disclaimer.md](About/Disclaimer.md) for the full note.

This README is the long-form documentation. The
[Steam Workshop description](About/SteamDescription.txt) is a trimmed-down version of the same thing.

---

## What it replaces

Hauler's Dream re-implements the behaviour of a number of standalone hauling/efficiency mods inside
one unified, optimized system, so you can drop the originals. It does **not** depend on any of them
(in particular, no Pick Up And Haul dependency — the inventory-hauling is HD's own).

| Mod it replaces | What Hauler's Dream does instead |
| --- | --- |
| **Pick Up And Haul** | A hauling pawn sweeps everything haulable nearby into its inventory and delivers the lot in one trip (feature 2), plus a right-click "Pick up X" order (feature 25). |
| **Harvest and Haul** | Work yields (plants, mining, deep drills, deconstruction, animals) are scooped into the worker's inventory and dropped off in one trip (feature 1). |
| **Auto Strip on Haul** / **Haul After Stripping** | A corpse haul strips the body first — gear into the inventory, body in hands, one trip — with configurable tainted-apparel policy (feature 3). |
| **Everyone Hauls** | An optional override lets any pawn haul regardless of backstory, traits, genes or titles (feature 28). |
| **Allow Dumb Labor** | The same override extended to cleaning and plant-cutting, so any pawn can do dumb labor — in vanilla work too (feature 28). |
| **Haul to Stack** | Haulers top up existing stacks instead of starting new ones, and several pawns can fill one tile at once (feature 10). |
| **Bulk Load For Transport** | One engine bulk-loads pods, shuttles, map portals, pack animals and refuelables, with an exact multi-hauler claim-ledger (features 15–18). |
| **While You're Up** | Grab-it-on-the-way pickup, consumer-aware storage routing, storage-building filters, closest-destination unload ordering, and a master switch (features 11–14, 29). |
| **Meals on Wheels** | A hungry colonist eats food another pawn or pack animal is carrying when no map food is reachable (feature 7). |
| **Haul After Slaughter** | Fresh slaughtered/hunted carcasses are hauled to a freezer or corpse stockpile instead of rotting where they fell (feature 9). |

## Compatibility

Compatibility is built deliberately, by studying each mod's architecture and adding a dedicated
integration layer. Soft dependencies are reflection-only and **inert when the other mod is absent.**

- **Combat Extended** — CE's weight / bulk / encumbrance rules take over the carry math entirely;
  the overload curve defers to CE, and bulk pack-animal unloading is CE weight/bulk aware.
- **Vehicle Framework** — a vehicle's designated cargo bulk-loads like any other manifest (aerial
  vehicles included), and colonists eat from and build from a parked vehicle's cargo (feature 19).
- **Adaptive Storage Framework** / **LWM's Deep Storage** — haul-to-stack works into modded storage
  units, and the optional storage-building filter is aware of slow/deep-storage deposit delays.
- **Common Sense** — when Common Sense owns the crafting-ingredient hauling or advanced-cleaning
  flow, Hauler's Dream steps aside so the two never fight or loop.
- **Allow Tool** / **Keyz' Allow Utilities** — "Haul Urgently" runs Hauler's Dream's bulk sweep
  instead of one-stack-at-a-time vanilla hauling.
- **Simple Sidearms**, **Smart Medicine**, hygiene mods — items those mods keep in a pawn's
  inventory are auto-detected and never auto-unloaded as "surplus".
- **Perfect Pathfinding** — the grab-it-on-the-way detour check uses its pathing accuracy when set
  to the Pathfinding mode.
- **Autocast**, **While You Are Nearby**, and many others — compatible.

---

## Features

Every feature has its own mod-settings toggle (most also have sub-options). The numbering matches the
Workshop description.

### Smart inventories

How pawns fill their pockets: work yields, hauls and loot ride in the inventory, so one trip does the
work of many.

1. **Harvest and haul** — a pawn that harvests plants, mines ore or deconstructs a building scoops
   the drops into its inventory as it works, then makes one storage trip at the end of the run.
   Realistic by default: the yield hits the floor first, then gets scooped. Each work type (plants,
   mining, deep drills, deconstruction, animals) has its own toggle.
2. **Pick up and haul** — a pawn sent to haul one item picks up everything haulable around it
   (weapons, apparel, meals, chunks) and delivers the lot in one trip, planned the moment the haul
   starts. Two modes: every haul sweeps the area, or (default) hauls you order manually stay surgical
   unless you order a second one nearby.
3. **Strip on haul** — a corpse haul strips the body first: the gear goes into the hauler's
   inventory, the body into its hands, and one trip moves both. No more strip orders after every
   battle. You choose which hauls strip, whether your own dead are left alone (they are, by default),
   and what happens to tainted apparel: take it, leave it on the body, forbid it, or destroy it
   (smeltable and non-smeltable configured separately). Stripping a living prisoner works the same
   way, with a follow-up strip queued in case the target dresses itself again.
4. **Fewer round-trips** — a builder or cook gathers everything the job needs into its inventory in
   one sweep and walks to the bench or site once. A geothermal generator's 340 steel no longer takes
   five hand-carry trips.
5. **Shared inventories** — a pawn carrying goods works like a walking stockpile: workers take what
   they need straight from the carrier, an idle carrier even walks out to meet them halfway, and
   everyone uses their own carried stock first (a cook can cook with the berries it just picked).
   Optional: builders may claim materials from a hauler mid-transit.
6. **Build from inventory** — a constructing pawn sources build materials from carried stock — its
   own inventory, other colonists', and pack animals' / caravan cargo — not just loose stacks on the
   ground. Order a wall or sandbag on a raid and it builds straight from caravan-carried steel, no
   manual drop-off. Floor stacks are still preferred; an opt-in partial-build lets a frame progress
   on whatever a single carried stack provides.
7. **Meals on wheels** — when there's no food on the map a hungry colonist can reach, it eats
   acceptable food carried in another colonist's (or a pack animal's) inventory instead of trekking
   to a far stockpile or going hungry. Vanilla map/own/pack-animal food is always preferred first;
   drafted, downed and berserk carriers are left alone, a parent's in-progress baby feeding is never
   interrupted, and a carried meal about to spoil is grabbed first.
8. **Spoiling-first ingredients** — when a colonist picks ingredients for a bill, it reaches for the
   rottable ingredient closest to spoiling, cutting overall waste. Independent Butcher (most-spoiled
   corpse first) and Cook (meals, pemmican and kibble use the most-perishable food first) toggles.
   Recipe satisfaction, the search radius, multi-slot meals and non-perishable crafts (steel, cloth,
   chemfuel, leather) are unaffected; frozen food is left for last.
9. **Haul after slaughter** — a fresh carcass is hauled straight to a freezer or corpse stockpile so
   it doesn't rot where it fell. Two toggles: slaughtered (tamed) carcasses, which vanilla never
   hauls itself, and hunted (wild) carcasses, where the hunter grabs its kill if a hunt was
   interrupted right after the killing blow (a clean hunt already self-hauls, so this never
   double-hauls). Only when a reachable store accepts the body; otherwise left exactly as vanilla.

### Smarter hauling

What happens between picking things up and putting them away.

10. **Haul to stack** — haulers top up existing stacks instead of starting new ones, and several
    pawns can deliver to the same tile at once (destination tiles are no longer reserved). Which
    stockpile wins is still decided by priority and distance; within that room, stacking wins. Works
    on the ground, on shelves, and in modded storage units; outdoor stockpiles consolidate within a
    short radius.
11. **Drop off in passing** — a pawn heading off on a long trip with a full backpack drops its load
    at a stockpile that's roughly on the way, instead of carrying it across the map and back.
12. **Nearest destination first** — on an unload trip carrying several different items, a pawn empties
    the nearest storage destination fully before walking to the next, instead of going in
    item-category order — less zig-zagging across the base. Items with nowhere to go are never
    stranded; they're just visited last.
13. **Grab it on the way** *(opt-in, off by default)* — when a pawn sets off on any job and a loose
    haulable lies roughly along the path, it scoops the item into its inventory first, so the stray
    rides to storage on a trip the pawn was making anyway — zero extra round-trips. The detour is
    tightly bounded by a trip-ratio check with a Vanilla/Default/Pathfinding accuracy knob, and
    respects the per-pawn auto-haul toggle, the carry ceiling and the bleeding gate.
14. **Move supplies closer to the work** *(opt-in, off by default)* — before a pawn carries a resource
    to a build site or bill, it can relocate the largest nearby stack of that material to storage
    closer to the consuming job, so future fetches are short — plus optional equal-priority
    relocation. Carefully guarded so it never double-acts with the build-from-inventory, batch-craft
    or bulk systems. An optional per-mod storage-building filter lets you choose which storage the
    on-the-way behaviours may use (with curated defaults and LWM Deep Storage awareness); it never
    blocks a pawn from putting its load away.

### Bulk loading

Anything with a manifest — pods, shuttles, portals, pack animals, fuel — filled in one trip instead
of one stack per walk. Several haulers split a single manifest without double-hauling (a per-save
claim-ledger keeps the count exact), and interrupting one returns its share to the pile.

15. **Transport pods & shuttles** — sweep many stacks into inventory and load them in a single trip.
    The manifest decrements exactly, the "loading stalled" alert no longer false-fires, and a shuttle
    won't board or launch while hauling is still in flight. Right-click "Prioritize bulk loading", or
    let it run as ordinary hauling.
16. **Map portals — in and out** — the same engine extended to pit gates, cave/vault exits and "enter
    map" portals; the manifest reaches exactly empty even though each deposited stack teleports away.
    Because a pit gate and the undercave's cave-exit are both portals, this loads loot down through a
    gate *and* hauls it back up out of one.
17. **Pack animals** — vanilla pulls one stack to a hauler's hands per trip; here a hauler pulls many
    stacks into its backpack in a single visit and ships them to storage, so emptying a loaded caravan
    animal is one walk instead of dozens. Combat Extended weight/bulk aware; the carrier stays
    interruptible for roping and caravan-forming. Right-click "Prioritize bulk unloading".
18. **Refuel** — top up a refuelable — a shuttle's chemfuel, deep drills, generators — in one trip
    instead of vanilla's one fuel stack carried in hands per walk. It only kicks in when more than one
    trip's worth of fuel is needed (a single-stack refuel is left to vanilla, which already does it in
    one go), and reuses vanilla's own fuel finder so it picks exactly the stacks vanilla would. Any
    fuel swept over what's needed is put away by the normal unload. Right-click "Prioritize bulk
    refuelling".
19. **Vehicle cargo** *(Vehicle Framework, optional — inert when absent)* — a vehicle's designated
    cargo loads the same way: many stacks in one trip, idle haulers splitting one manifest,
    autonomously the moment you set the cargo, aerial vehicles included. Colonists also eat from and
    build from a parked vehicle's cargo.

### Carrying capacity

20. **Overloaded** — pawns can carry more than their max carry weight, slowed only when it saves time
    over another round-trip. The slowdown follows a gentle curve instead of a straight line — a light
    overload is nearly free, ramping up only as the load gets heavy. At the default ("Fair") a
    colonist fills to ~275% of capacity before it stops paying off and is still moving at ~65% speed
    there. One slider runs from "no slowdown, carry freely" to "never overload"; a strict mode never
    goes past 100%. With Combat Extended loaded, CE's weight, bulk and encumbrance rules take over
    entirely.
21. **Keep working when full** *(opt-in, off by default)* — a pawn doing a job that scoops yields
    (mining, harvesting, …) keeps working when its inventory fills up instead of breaking off to
    unload — the overflow is left on the ground for haulers. It only makes an unload trip when it's
    about to travel farther than its nearest dropoff (so it regains speed before a long haul) or at
    downtime. Lets a miner keep mining while dedicated haulers move the output.

### Planning tools

Better micro-management via planning: right-click → "Plan prioritized [task]…".

22. **Planned (and batched) crafting** — say you want 12 simple meals. Tapping "prioritize cooking"
    three times makes each run do its own ingredient trips; instead, one order via "Plan prioritized
    crafting…" has the cook fetch all 120 raw food in one trip and cook the dozen in one go. The
    repeat count is capped by what's actually on the map, and the finished products ride back with
    everything else. Prefer it automatic? Set a bill to one of the new **Batch** repeat-modes (do X
    times / until / forever) and every work session does the same by itself — one ingredient trip, the
    whole batch, hauled back together. Because each item finishes individually, an interruption only
    ever loses the single in-progress one, and carried raw food is frozen for the duration so a big
    cooking batch won't rot mid-session.
23. **Route planning** — right-click a work target and the pawn gets an efficient route over the whole
    patch, previewed live on the map with a time estimate (exact Held-Karp ordering for short routes,
    nearest-neighbour + 2-opt for long ones). The selection modes fit the work: the nearest chain, the
    whole touching vein, a radius, whole rooms (cleaning), or a whole growing zone (harvesting).
    "Smart routing" circles the trip back toward storage so the last target ends right next to your
    stockpile. Hand-pick must-visit targets, pin the start and end, pull in unmarked plants once
    they're grown enough; every target type remembers your settings, and a mining route that runs into
    fog extends itself as new ore is revealed.
24. **Smarter construction** — ordering a construction is one job: the pawn hauls the materials (in its
    inventory, fewer trips) and builds right away. Plan a whole fence line as haul-only, stocking the
    sites so several pawns build in parallel, or as haul+build, site by site. A separate order,
    "Prioritize hauling materials to…", stocks a site before it's even buildable.

### Quality of life

25. **Per-pawn controls** — every eligible colonist and work-mech has an "Auto-haul yields" gizmo to
    turn its automatic scooping and bulk-haul sweeping on or off individually — so you can leave a
    skilled miner or grower working and let dedicated haulers move the output, without touching the
    global settings. Forced orders still work regardless. Plus a right-click "Pick up X" to send a
    pawn to grab that stack (and fit more) into inventory in one tracked trip, and an optional toggle
    for Haul-trained colony animals to carry multiple stacks like colonists (default off).
26. **High-capacity haulers** — work-mechanoids are no longer capped at exactly 100%; they use the
    same smart-overload as colonists (and are slowed for it by the same slider), so a high-capacity
    hauler like a Tunneller fills a worthwhile load before its trip instead of leaving on a single
    stack. A mech carrying picked-up goods delivers them to storage (or drops them nearby if there's
    nowhere to put them) before it sits on a charger, so the goods don't spoil or take up its capacity.
27. **Don't haul while bleeding** *(on by default)* — a pawn bleeding above a small threshold won't
    *start* a new scoop or sweep — it should get treated, not detour to tidy up. A pawn already
    carrying scooped goods still unloads them normally, and explicit Strip orders you give still scoop
    their gear.
28. **Capable of dumb labor** — the planners respect work incapability: a pawn that won't clean is
    never offered "Plan prioritized cleaning". And if "incapable of dumb labor" pawns annoy you, three
    optional overrides (off by default) make every pawn able to haul, clean, or cut plants — whatever
    their backstory, traits, genes or titles say. The game itself follows along: work tab, right-click
    prioritizing and automatic work.
29. **Master switch & settings** — one master toggle (on, no restart) stops Hauler's Dream starting
    its automatic hauling behaviours — handy for troubleshooting — without stranding carried goods or
    hiding the "Unload inventory" button. A pawn diverting to grab something en route shows
    "… (on the way to …)" in its job text.

## Settings & profiles

The settings window is a three-pane layout (icon navigation · options · a contextual info panel that
updates as you hover any control), with ten categories led by a **Features** page that puts every
"incorporated mod" family on one page as on/off cards — so you can switch off any family you don't
want at a glance. Multiple-choice options are inline segmented buttons (each choice shows its
description on hover), sliders show a value readout, the Smart-overload page draws a live
move-speed-vs-carry-weight curve, and sub-options stay visible but greyed when their master is off.

**Settings profiles** let you save the current settings as a named preset and switch between them from
a dropdown beside the header; the built-in **Default** profile is immutable and doubles as "reset to
defaults". Profiles can be **copied and pasted as a compact share code** — the code stores the mod
version plus only the settings that differ from that version's defaults, so it stays short, and
pasting it recreates the profile (you pick the name, pre-filled from the code).

---

## How this mod is made

This mod is largely implemented by **Fable 5** and **Opus 4.8** (Anthropic's Claude models), with
relatively few direct interventions from a human developer (me). For many users this is an important
distinction, so it is stated plainly here.

All code is human-reviewed by me, is fully available in this repository, and every update ships
through a Pull Request — so the complete history and every change are open to inspection.

Hauler's Dream covers a great many features, with the deliberate goal of intertwining them into a
single, unified system backed by optimized algorithms that improve colonist efficiency. Compatibility
with other mods is built by cloning those mods and actively studying their architecture, then
incorporating dedicated compatibility layers into Hauler's Dream.

For reference, a mod of this scope would otherwise take many months — if not years — of manual work;
the accelerated development is what makes it feasible. The whole thing is held together by a large
headless test suite and a repo-wide performance pass (the per-tick logic is allocation-tested), so all
of those features stay light and don't stutter even in big, heavily-modded colonies.

(This note also lives in [About/Disclaimer.md](About/Disclaimer.md).)

---

## Installing

- **Steam:** subscribe on the [Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652).
- **Manual:** download `HaulersDream-vX.Y.Z.zip` from the
  [latest release](https://github.com/refzlund/haulers-dream/releases/latest) and extract it into
  your `RimWorld/Mods` folder.

## Building from source

Prerequisites: [Bun](https://bun.sh) and the [.NET SDK](https://dotnet.microsoft.com/download)
8.0+ (the mod targets `net48` and builds via reference assemblies — no RimWorld install or Visual
Studio required; `Krafs.Rimworld.Ref` provides the game API).

```sh
bun install            # once: installs the changesets tooling
bun run build          # compile everything (Release) -> 1.6/Assemblies/
bun run test           # headless unit tests for the decision math
bun run test:perf      # allocation/perf-category tests
bun run deploy         # build + copy the whole mod into your RimWorld Mods folder
bun run package        # stage dist/HaulersDream + a versioned zip (what CI ships to Steam)
```

`bun run build` also runs two static guards: a settings-default drift check
([scripts/check-settings-drift.ts](scripts/check-settings-drift.ts)) and the Steam-description length
check ([scripts/check-steam-description.ts](scripts/check-steam-description.ts), 8000-char limit).

`bun run deploy` reads the destination from `.env` — copy [.env.example](.env.example) to `.env`
and point `RIMWORLD_MODS_DIR` at your `RimWorld/Mods` folder. Plain `dotnet build` works too and
deploys when the path exists (override with `-p:RimWorldModsDir=…`).

## Project layout

```
HaulersDream/
├── About/                     mod metadata, Workshop description, Disclaimer, PublishedFileId
├── 1.6/Assemblies/            compiled output (gitignored; built by bun run build)
├── Defs/  Patches/  Languages/  LoadFolders.xml
├── Source/
│   ├── HaulersDream.Core/     pure decision math, no game types (unit-tested)
│   ├── HaulersDream.Tests/    NUnit tests for Core
│   └── HaulersDream/          game-coupled assembly (Harmony patches, jobs, UI)
└── scripts/                   bun scripts for build/test/deploy/package/versioning
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Releases are fully automated: PRs carry
[changesets](https://github.com/changesets/changesets), merging the release PR publishes to
GitHub Releases and the Steam Workshop.

## License

[CC BY-NC-SA 4.0](LICENSE) — you may copy, modify, and share this mod, with attribution, for
**non-commercial** purposes, and derivatives must stay under the same license. In short: fork it,
learn from it, build on it, but this code stays free — nobody gets to sell it or any derivative
of it.
