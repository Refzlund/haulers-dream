# Hauler's Dream

A RimWorld **1.6** mod. Colonists use their inventories smartly when moving items, and carry out
their tasks more efficiently: fewer round-trips, less walking back and forth, more time actually
working. Also adds planning tools and small micro-management improvements that build on these
changes.

- **Steam Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3742230809
- **Requires:** [Harmony](https://github.com/pardeike/HarmonyRimWorld) (`brrainz.harmony`)
- Safe to add to existing saves.
- Compatible with Combat Extended and Adaptive Storage Framework. Replaces Pick-up and Haul,
  Harvest and Haul, Auto Strip on Haul, Haul After Stripping, Everyone Hauls, and Haul to Stack.

## Features

**Smart inventories**

1. **Harvest and haul** — work yields (plants, mining, deep drills, deconstruction, animals) are
   scooped into the worker's inventory; one storage trip at the end of the run.
2. **Pick up and haul** — a hauling pawn sweeps everything haulable nearby into its inventory and
   delivers the lot in one trip.
3. **Strip on haul** — corpse hauls strip the body first: loot in the inventory, body in hand,
   one trip. Configurable tainted-apparel policies.
4. **Fewer round-trips** — builders and cooks gather all job materials in one sweep.
5. **Shared inventories** — carried goods count as available stock; workers take from carriers,
   carriers meet them halfway.

**Smarter hauling**

6. **Haul to stack** — haulers top up existing stacks; destination tiles are no longer reserved.
7. **Drop off in passing** — loads get dropped at storage that is roughly on the way.
8. **Overloaded** — pawns carry past 100% when the slowdown beats another round-trip
   (break-even math; defers entirely to Combat Extended when CE is loaded).

**Planning tools** (right-click → "Plan prioritized [task]…")

9. **Planned crafting** — batch a bill with one consolidated ingredient trip.
10. **Route planning** — travel-optimal routes over whole patches/veins/rooms/zones, with a live
    map preview (exact Held-Karp ordering for short routes, nearest-neighbour + 2-opt for long).
11. **Smarter construction** — ordered builds haul materials and build as one job; plan whole
    fence lines; stock a site before it's buildable.

**Quality of life**

12. **Capable of dumb labor** — planners respect work incapability, plus optional overrides that
    let every pawn haul, clean, or cut plants (changes vanilla too).

Every feature has mod-settings toggles; see the Workshop page
([About/SteamDescription.txt](About/SteamDescription.txt)) for the full description.

## Installing

- **Steam:** subscribe on the [Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3742230809).
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
bun run deploy         # build + copy the whole mod into your RimWorld Mods folder
bun run package        # stage dist/HaulersDream + a versioned zip (what CI ships to Steam)
```

`bun run deploy` reads the destination from `.env` — copy [.env.example](.env.example) to `.env`
and point `RIMWORLD_MODS_DIR` at your `RimWorld/Mods` folder. Plain `dotnet build` works too and
deploys when the path exists (override with `-p:RimWorldModsDir=…`).

## Project layout

```
HaulersDream/
├── About/                     mod metadata, Workshop description, PublishedFileId
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
