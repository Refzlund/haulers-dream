# Independent source review

Root, 24 September 2026. Accepted for a fresh native producer. I read the complete two-file delta, diagnosis and handoff, and checked the installed native GUI.DoControl, Widgets.HorizontalSlider, Window.OnCancelKeyPressed and WindowStack.CloseWindowsBecauseClicked bodies.

The new observation occurs on the actual owned control path before its event is consumed. The matching event type is read, never written or reconstructed. Ordinary buttons require MouseUp, keys require KeyDown, and native slider/outside dismissal require MouseDown because that is when those native controls act. Coordinates are converted within the control's GUI context. The pending control, immutable worker receipt and unchanged actual state/control result must all agree. Invalid Return still needs a positive KeyDown witness, rather than passing from inactivity.

The source-bound dialog is established by the existing DialogDraw observation before owned input is recorded. A null window no longer counts as owned. Root/play Used events remain diagnostic. Removal of the unproductive global delegate does not change the product, actual worker input, display configuration, quantity/custody/Keep or saved-remainder requirements. The preserved v11 result remains failed39/41,U0.

No blocking source finding. The 856-check input audit and successful native-reference build support source custody; they do not accept any unexecuted UI action. Fresh native execution and the original saved-remainder restart remain necessary.
