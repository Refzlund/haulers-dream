# F11 actual slider-drag correction

The preserved v12 native run `0963c4686744474590bcadd7c97ce0d0` passed 54 checks after 47 acknowledged input actions. Request 48 reached the real slider as a matching MouseDown, but the amount remained 15. This was a failed slider postcondition; the generic timeout text does not establish that the event was absent.

The installed `Widgets.HorizontalSlider` and `UnityGUIBugsFixer.MouseDrag` explain the result: on Windows a press takes ownership, and an actual MouseDrag changes the value. The original test sent only a click. The retained native source is in `../v12-control-input/UnityGUIBugsFixer.native.txt`.

V13 changes only `F11Scene.Ui.cs` and the private-window worker. For the existing slider endpoints, the worker posts center movement, center button-down, displaced held-button movement and button-up to the exact owned inactive window. The observer requires the actual slider MouseDrag at the requested endpoint. Both the actual worker receipt and unchanged amount/physical-drop postconditions remain mandatory. Other control gestures are unchanged. No product, controller, global input, foreground or desktop switch is introduced.

Build succeeded in 5.16 seconds with zero warnings/errors. Host SHA `D215F8E88756D22E4C2DA8D242F67E024B504B248EB9D4979FB2AB20A0A0DF86`, MVID `8c032e23-9da7-4dbf-a542-912c96846f78`; selection SHA `1CA8BB9205C4139A880761F44401BEEA0A5E51987F27FE86AC7410B5EA6407EF`. `audit.json` confirms 647 unchanged selected inputs, 85 actual reference pairs and 142 preserved native files. Eight focused checks cover exact click/drag messages and rejected invalid gestures. All earlier failures remain retained. Native v13 acceptance is pending independent review and execution.
