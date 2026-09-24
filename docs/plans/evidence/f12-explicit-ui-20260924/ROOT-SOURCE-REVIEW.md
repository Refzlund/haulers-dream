# Root review of additive point-haul UI v2

Root read all five new C# files, English keyed text and the backend command, order, comp persistence and cleanup joins. The UI holds only local selections, validates actual selected pawn/map/source identity/count/cell/forbidden state and submits through the existing authoritative commands. Queue choice is captured at the actual menu/gizmo invocation and retained in the amount dialog/target closure. Canceling a dialog or targeter allocates no order. No source blocker found in this slice.

The supported-robot gizmo is a separate local source-targeting route; it does not relax backend control/role rules or modify native CanTakeOrder. Resume requires a blocked order with no live linked job; Cancel uses the same backend and permits retrying retained physical cleanup without reviving canceled intent. Stored ground remainders need no extra cleanup command, while an exceptionally retained private piece keeps Cancel available. Actual disabled/enabled controls and all native event paths remain to be exercised.

Prompt width/wrapping and screen clamping address the native unbounded-label behavior. The amount and order dialogs measure translated text, reserve bottom controls, scroll content and restore GUI state. Structural checks cover16 locales and35 keys; this is not a claim that every translation has been visually reviewed. The actual English/Italian rendered and input checks in RENDERED-ACCEPTANCE.md remain required.

UIv2's complete37 additions stay frozen separately from the accepted backendv5 native chain. Exact shelf targeting remains F13 and is not implied by this bare-cell interface. Root has not accepted native UI behavior, actor integration or Multiplayer from compilation alone.
