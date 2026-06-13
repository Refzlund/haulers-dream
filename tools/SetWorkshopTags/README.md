# SetWorkshopTags

One-off maintenance tool that sets the Steam Workshop **display tags** (`Mod`, `1.6`) on the
Hauler's Dream workshop item via the Steamworks `SetItemTags` API.

## Why this exists

steamcmd's `workshop_build_item` **silently ignores the `tags` block** — the commit reports
`Success`, but the tags stay empty and `time_updated` doesn't even change (verified against the
Steam Web API). RimWorld's CI publish action (`m00nl1ght-dev/steam-workshop-deploy`) uses that same
steamcmd path, so it can't set tags either. The only way to set them is the Steamworks SDK
(`SetItemTags`), which RimWorld's own in-game uploader also uses but which re-uploads content.

This tool does a **metadata-only** update: it sets the tags and re-uploads **no content**.

## How it works

It reuses RimWorld's bundled Steamworks wrapper + native lib so versions are guaranteed
compatible (no NuGet native-loading risk):

- references `RimWorldWin64_Data/Managed/com.rlabrecque.steamworks.net.dll`
- copies `RimWorldWin64_Data/Plugins/x86_64/steam_api64.dll` next to the exe
- ships `steam_appid.txt` = `294100` (RimWorld) next to the exe

`SetItemTags` **replaces** the full tag set, so the tool passes every tag you want at once.

## Running it

Prerequisites:
- The **Steam desktop client** must be running and **logged on / online**, on the account that
  **owns** the item (the tool attaches to that client session via `SteamAPI.Init` — it does NOT log
  in separately).
- **Do not** run `steamcmd` logged into the same account around the same time: a steamcmd login
  triggers a `'Session Replaced'` disconnect on the desktop client (which then does **not**
  auto-reconnect), and the tool will report `k_EResultNotLoggedOn`. If that happens, restart the
  Steam client (cached creds reconnect automatically) and re-run.

```sh
dotnet build tools/SetWorkshopTags/SetWorkshopTags.csproj -c Release
cd tools/SetWorkshopTags/bin/Release
./SetWorkshopTags.exe                       # defaults: item 3742459652, tags "Mod" "1.6"
./SetWorkshopTags.exe 3742459652 Mod 1.6    # explicit
./SetWorkshopTags.exe 3742459652 Mod 1.6 1.7  # e.g. when adding a new game version later
```

Verify afterwards (tags are metadata, so `time_updated` will NOT change):

```sh
curl -s -X POST "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/" \
  -d 'itemcount=1&publishedfileids[0]=3742459652' | python -m json.tool | grep -A6 '"tags"'
```

## Notes

- The `.csproj` references RimWorld at `C:\Steam\steamapps\common\RimWorld` (this machine's install).
  Adjust the two `HintPath`/`None Include` paths if RimWorld lives elsewhere.
- Exit codes: `0` OK, `2` Init failed, `3` SetItemTags rejected (invalid tag for the app),
  `4` timeout/IO, `5` Steam returned a non-OK result, `6` client not logged on (offline).
