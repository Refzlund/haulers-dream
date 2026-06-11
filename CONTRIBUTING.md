# Contributing to Hauler's Dream

Thanks for helping out! This page covers the local setup, the workflow, and how releases ship.

## Setup

1. Install [Bun](https://bun.sh) and the [.NET SDK](https://dotnet.microsoft.com/download) 8.0+.
   No RimWorld install is needed to build or test (the game API comes from the
   `Krafs.Rimworld.Ref` reference assemblies).
2. `bun install`
3. To run the mod in-game: copy `.env.example` to `.env`, set `RIMWORLD_MODS_DIR` to your
   `RimWorld/Mods` folder, then `bun run deploy`.

## Scripts

| Command           | What it does                                                        |
| ----------------- | ------------------------------------------------------------------- |
| `bun run build`   | Compile the solution (Release) into `1.6/Assemblies/`               |
| `bun run test`    | Run the headless NUnit tests (decision math in `HaulersDream.Core`) |
| `bun run deploy`  | Build + copy the full mod into `RIMWORLD_MODS_DIR`                  |
| `bun run package` | Stage `dist/HaulersDream` + versioned zip (what CI ships)           |
| `bun changeset`   | Record a changeset for your PR (see below)                          |

## Code expectations

- **Pure logic goes in `HaulersDream.Core`** — no `Verse`/`RimWorld` types — with NUnit coverage
  in `HaulersDream.Tests`. The game-coupled assembly stays thin and delegates decisions to Core.
- **Harmony patches are defensive**: wrap game-state mutations in guards, prefer postfixes over
  overwrites, and never let an exception escape into the game's tick/UI loop.
- **Behavioral changes need an in-game sanity check.** CI only covers the headless math; describe
  what you verified in the PR (a save, the steps, what you observed).
- Match the existing style (tabs, comment density, naming). `.editorconfig` is authoritative.

## Pull requests

1. Branch from `main`, make your change, keep `bun run test` green.
2. If the change is user-facing (features, fixes, balance), add a changeset:

   ```sh
   bun changeset
   ```

   Pick `patch` for fixes/tweaks, `minor` for new features. The summary you type becomes the
   changelog entry and the Steam Workshop change note, so write it for players, not developers.
   Internal-only changes (CI, docs, refactors) don't need a changeset.

3. Open the PR against `main`. CI builds and runs the tests on Windows.

## How releases work

Releases are fully automated with [changesets](https://github.com/changesets/changesets):

1. Merged PRs accumulate changeset files on `main`.
2. The release workflow maintains a **"chore: release" PR** that bumps `package.json`,
   `About/About.xml` (`<modVersion>`), `Source/Directory.Build.props` (`<Version>`) and
   `CHANGELOG.md`.
3. **Merging that PR is the release.** The workflow then:
   - tags `vX.Y.Z` and creates a GitHub Release with the packaged zip attached, and
   - builds and publishes the mod to the **Steam Workshop** (item `3742230809`) with the
     changelog as the change note.

No manual steps. The maintainer only merges PRs.

> The Workshop **page description** (`About/SteamDescription.txt`) is not pushed by the upload
> (SteamCMD updates content + change note only) — paste it on the Workshop page when it changes.

### Publishing secrets (maintainer setup, once)

The release workflow needs two repository secrets:

- `STEAM_USERNAME` — the Steam account that owns the Workshop item.
- `STEAM_CONFIG_VDF` — a logged-in SteamCMD session: run
  `steamcmd +login <username> +quit` locally (answer the Steam Guard prompt), verify a second
  `+login` no longer asks, then copy the contents of `<steamcmd>/config/config.vdf` into the
  secret. Re-generate it if Steam invalidates the session.

## License note

By contributing you agree your contribution is licensed under the repository's
[CC BY-NC-SA 4.0](LICENSE) license: free, non-commercial, share-alike.
