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

## Translations

Hauler's Dream uses RimWorld's built-in localization system, and **every** piece of player-facing
text lives in the `Languages/` folder — so translating the mod never requires touching code or
building anything. New languages and corrections to existing ones are very welcome.

> The non-English translations were produced with AI assistance and reviewed by the maintainer, so
> native-speaker corrections genuinely improve the mod — even a one-line phrasing fix is worth a PR.

### Layout

```
Languages/
├─ English/                              ← the source of truth (always current)
│  ├─ Keyed/HaulersDream.xml             ← all UI / settings / menu / message / alert text
│  └─ DefInjected/JobDef/Jobs.xml        ← the short "doing X" job-report strings
├─ German/  French/  Russian/  …         ← one folder per language, same two files
```

RimWorld merges the active language's files from every active mod automatically — once the folder is
named correctly and the mod is enabled, your translation just shows up.

### Folder names — use the exact RimWorld name

The folder **must** use RimWorld's English PascalCase language name or the game won't load it:

| Language | Folder | Language | Folder |
|---|---|---|---|
| English (source) | `English` | Korean | `Korean` |
| Chinese (Simplified) | `ChineseSimplified` | Polish | `Polish` |
| Danish | `Danish` | Portuguese (Brazil) | `PortugueseBrazilian` |
| Dutch | `Dutch` | Russian | `Russian` |
| French | `French` | Spanish | `Spanish` |
| German | `German` | Thai | `Thai` |
| Italian | `Italian` | Ukrainian | `Ukrainian` |
| Japanese | `Japanese` | | |

Any other RimWorld language uses the same convention (`ChineseTraditional`, `SpanishLatin`,
`Portuguese`, `Czech`, `Hungarian`, …). Mind the variant splits — `Spanish` vs `SpanishLatin`,
`Portuguese` vs `PortugueseBrazilian` (this mod ships `Spanish` and `PortugueseBrazilian`). The canonical list is the folder names under
`Data/Core/Languages/` in your RimWorld install (and the official
[Ludeon language repos](https://github.com/orgs/Ludeon/repositories)).

### Add a new language

1. Copy the whole `Languages/English/` folder to `Languages/<YourLanguage>/` (e.g. `Languages/ChineseTraditional/`).
2. Translate **only the text between the tags** — keep the structure, file names and paths identical.
3. Open a PR (see the rules below). No build is needed for a translation-only change.

### Correct an existing translation

Edit the value in the relevant `Languages/<Language>/…xml` file and open a PR with a short note on what
you changed and why ("awkward phrasing", "wrong term for X", a typo — all welcome).

### Rules that keep a translation working

- **Never translate the keys/tags** — only the text inside them. In
  `<HaulersDream.Gizmo.UnloadNow>Unload inventory</HaulersDream.Gizmo.UnloadNow>`, translate only
  `Unload inventory`.
- **Keep every placeholder**: `{0}`, `{1}`, `{2}` and the named `{ORIGINAL}` / `{DESTINATION}` are
  filled in by the game. Keep them all; you may reorder them to read naturally.
- **Keep `\n` exactly** (`\n\n` is a paragraph break — don't convert it to a real newline or drop it).
- **Keep XML entities** encoded: `&amp;` = `&`, `&lt;`/`&gt;` = `<`/`>`.
- **UTF-8 without a BOM** (the English files already are).
- **Don't add or remove keys.** Translate the keys that exist; if you notice one missing or extra
  versus `Languages/English/`, mention it in the PR rather than guessing.

### Handy in-game tools (RimWorld dev mode)

With Development mode on, the debug menu's *Output* category has **"Dump translation files"**
(regenerates the full template for the active language so you can see every key) and **"Save
translation report"** (lists missing keys or ones pointing at a renamed def — run it after translating
to confirm nothing's missed).

## How releases work

Releases are fully automated with [changesets](https://github.com/changesets/changesets):

1. Merged PRs accumulate changeset files on `main`.
2. The release workflow maintains a **"chore: release" PR** that bumps `package.json`,
   `About/About.xml` (`<modVersion>`), `Source/Directory.Build.props` (`<Version>`) and
   `CHANGELOG.md`.
3. **Merging that PR is the release.** The workflow then:
   - builds and publishes the mod to the **Steam Workshop** (item `3742459652`) with the
     changelog as the change note, and
   - tags `vX.Y.Z` and creates a GitHub Release with the packaged zip attached.

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
