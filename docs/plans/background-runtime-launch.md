# Background runtime tests

The user requested that runtime tests stop taking focus while they play games. Existing launchers already requested a hidden window, but Unity later displayed its own window. Startup hide/minimize flags do not constrain every later window-show call.

Use [run-on-test-desktop.py](../../scripts/run-on-test-desktop.py) around an existing bounded test controller. It creates one private Win32 desktop and passes its actual name through `CreateProcessW` / `STARTUPINFO.lpDesktop`. It never calls `SwitchDesktop`, activates a window, injects a DLL, or sends input. A private job object owns only the launched command and its descendants; suspended startup lets job assignment finish before code runs. Failures/timeouts stop that owned tree, and cleanup errors remain failures. It records only owning PIDs of windows on its own desktop, plus input-desktop names, not the user's window contents.

Keep the existing controller's normal windowed graphics and private `runInBackground=True` setting. `-batchmode` / `-nographics` cannot stand in for these Layout/Repaint-dependent GUI tests. Visual or physical input checks that cannot run in isolation remain pending; never switch desktops or restore a test to the user's active desktop as an automatic fallback.

The executable path must be absolute; pass arguments as distinct values after `--`. Use a fresh output directory per invocation and an outer timeout longer than the existing controller's native deadline and cleanup allowance. Existing frozen controllers and prepared test inputs remain unchanged.

```powershell
& $python -E -B -X utf8 './scripts/run-on-test-desktop.py' `
  --output $freshOutputDirectory --cwd (Get-Location).Path --timeout 450 `
  -- $python -E -B -X utf8 $existingController Launch `
  --run $preparedRun --review $reviewPath --review-sha256 $reviewHash
```

20 September process checks: actual Python controller and subprocess grandchild inherited the same private desktop while the input desktop remained `Default`. An owned timeout correctly returned failure, joined the command and left zero active job processes. Two intermediate launcher-check failures are retained: asynchronous job-exit accounting needed a bounded settle interval; an empty desktop enumeration returned zero without an error. Both were corrected without changing native test behavior. Evidence: `%TEMP%/haulersdream-background-launch-20260920/`.

The first actual RimWorld validation succeeded: existing PB-only clone run `750cc902a47641a4b9c0ad0491559f95` completed all 12 cells, 14 Update callbacks and 23 GUI callbacks, with complete cleanup and no captured errors. Native PID 30916 was observed as the owner of a window on `HaulersDreamTests-1977656192e74c46931b00d72679867e`; the input desktop remained `Default` throughout the sampled run. Both native process and controller joined with exit 0; the owned job was empty at cleanup. The helper never switched or activated a desktop. Actual launch evidence is `%TEMP%/haulersdream-background-launch-20260920/pb-clone-native/desktop-launch.json`. This validates this GUI fixture in isolation; it does not claim physical input or verify unrelated future visual tests.

Launcher committed as `e6c037f`. The complete small launcher-probe and actual-launch receipts are also retained in [evidence/background-runtime-20260920](evidence/background-runtime-20260920/). The unchanged native controller's subsequent Verify returned no problems, input/private/gate changes or read errors; interpretation of the PB clone observation remains its separate report review.

References: [Microsoft desktops](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops), [STARTUPINFO](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfow), [Unity 2022.3 player arguments](https://docs.unity3d.com/2022.3/Documentation/Manual/PlayerCommandLineArguments.html).

The reused PowerShell controller was also checked before its first background game launch: its exact Start-Process -WindowStyle Hidden -PassThru pattern launched a harmless Python child on the same private desktop (HaulersDreamTests-98485ec742c7447f83b74b84ce02d2c9). Input desktop remained Default, exit was zero and the owned job was empty. Retained evidence: evidence/background-runtime-20260920/powershell-inheritance and powershell-inheritance-source.

