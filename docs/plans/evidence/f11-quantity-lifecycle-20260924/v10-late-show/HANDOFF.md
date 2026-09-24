# F11 v10 late visibility timing correction

Ready for root source review and a fresh native attempt. This changes only `native-input.py`; no host/product/controller build, Prepare or native launch was performed. The unchanged v9 controller retains its producer-only BitBlt flag and strict windowed1024x768 preferences. All v7 native UI, input, quantity, screenshot and save oracles remain unchanged.

The v9 receipts establish that the early shown window became hidden again within 0.3 seconds and remained hidden at the actual render/open-gear phase. See `DIAGNOSIS.md` for exact original receipt identities/times. This revision removes the startup show decision. Early HWND discovery and state capture are passive. The single existing conditional ShowWindowAsync(4) decision runs only after the actual native render/open-gear event has been read, before any input is posted. It still requires the same PID/creation time/HWND/class/thread/private desktop, unchanged input desktop and visible/noniconic1024x768 result. Already-visible windows receive no show call; the decision cannot repeat after failure.

The exact render event sequence/UTC/detail is added to `window-visibility.json`. `window-early.json` proves the early observation issued no operation; existing passive phase/state receipts continue. Earlier terminal results exit with an explicit no-show receipt. A request cannot be posted until this render-phase window admission succeeds. No additional show, activation, focus, display-setting operation, synthetic callback or default-desktop fallback exists.

`source.diff` contains the full delta, including moving the original show/wait block into its one-use render gate. `before/` preserves v9 selected sources and selection `55E31E8CFDF921589AA672BEAAEF6754445D0713512CF06C67AD17880B989762`. All 12 original v9 evidence files and the full 22-file retained capture, including raw, manifest, Verify and process receipts, were pinned. Nothing in failed run `036de394b7d14ecebb137b3457868797` was edited.

Validation: Python syntax passed; seven isolated executions of the actual gate function passed with in-memory substitutes for platform operations. They cover hidden/visible windows, terminal before admission and during observation, refused platform operation, absent render phase and input already sent. Second decisions are rejected. No native main, Windows API or game ran during these controls. Exact existing process/window helper ASTs and the transport message/receipt body remain identical. Audit rechecked 737 unchanged selected pins and85 reference pairs, and wrote a new private fixture-source-v10 snapshot.

- Selection: `352E258D43A50AE82B8464BCCBFFDAAE70F6A22D0BE2605528687CCDB0DDABCF`.
- Worker: `C0A157C1E485F72F29C0D2A862425F10BE688EDBF9531068A41DECFB0C50496A`.
- Diff: `42074838774419E2B895DD085E0F13E874F342B8B2ABD40BB55B7CBF70316289`.
- Unchanged host: `B842099B666BA77FD45FC2D4C2CB170EF42146C380A5F85800367606DFD91A16`, MVID `990d6c08-baea-42ed-81ab-68cd25f6d171`.
- Unchanged controller: `7C8DDCABC58E8BD47F969414A54BEF342685853DE60A1B32E73F76F84E9E4ACE`.

Fresh native acceptance still requires real play-UI event/repaint, geometry, actual control/input and physical results. A late visible-window receipt alone proves none of those. Keep any new failure without retrying the show decision.

The retained v9 Verify is **not-verified**, including an honest protected-source drift flag for `native-input.py`: v10 development was authorized after v9 execution ended but before the delayed Verify ran. This flag remains unchanged; there was no source restoration or rerun to hide it. V9 native PID7768 and creation time13:08:09.1195145Z come from its exact native-started receipt, and its failure terminates13:09:19.0790867Z. The preserved v9 manifest, immutable `fixture-source-v9`, v10 `before/native-input.py` and v9 worker/process receipts bind the executed v9 source separately from today's edited worker. All22 retained capture files, including that Verify, are pinned. This is provenance for a retained failure, not a clean Verify claim.
