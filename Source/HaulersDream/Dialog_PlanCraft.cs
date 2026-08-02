using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The "Plan prioritized crafting" dialog for a workbench: pick one of the bench's bills, choose how many
    /// times to repeat it (clamped to the ingredients actually available — you can't ask for 3 when there's only
    /// stock for 2), and a wall-clock timeout so a long recipe can't trap the pawn for days. On confirm it orders
    /// a single <see cref="JobDriver_BatchCraft"/> that pre-loads every repetition's ingredients in one trip,
    /// crafts them all without leaving the bench, collects the products into inventory, and unloads when done.
    /// This is the station counterpart to the route planner (which makes no sense for a stationary bench).
    /// </summary>
    public class Dialog_PlanCraft : Window
    {
        private readonly Pawn pawn;
        private readonly Building_WorkTable bench;
        private readonly List<Bill> bills = new List<Bill>();

        private Bill selected;
        private int reps = 4;
        private float timeoutHours;
        private int maxAvailReps = 1;

        // Pending text for the two numeric boxes (issue #237). Null means "no pending text — re-read the
        // value". EVERY writer of reps/timeoutHours other than its own box must null the matching buffer in the
        // same place, or the box keeps showing a stale number. There are exactly three such writers: the two
        // slider adoptions in DoWindowContents, and SelectBill's re-clamp of reps.
        private string repsBuf;
        private string timeoutBuf;

        private CraftBatchPlan cachedPlan;
        private string planSig;
        private Vector2 billScroll;

        // Bump when the planner/job behaviour changes, so a "still broken" report can be told from a stale DLL.
        public const string BuildTag = "F-Craft1";

        private const float RowH = 30f;
        private const int TicksPerHour = 2500; // GenDate.TicksPerHour

        // The repetition box accepts numbers ABOVE the slider's ceiling on purpose. That ceiling is
        // maxAvailReps — a snapshot of what the ingredients on the map support, measured ONCE per bill
        // selection — and this dialog does not pause the game, so the colony keeps hauling and crafting while it
        // sits open and the snapshot goes stale. Clamping typed input to it would refuse a batch the job would
        // happily run. Asking for too many is already handled honestly downstream: CraftBatchPlanner.Resolve
        // mins the request against live availability, mass and the timeout, and the summary names the binding
        // cap ("limited by available ingredients") instead of silently swallowing the number.
        private const int RepsBoxMin = 1;
        private const int RepsBoxMax = 500;

        // The timeout is persisted (craftBatchTimeoutHours) and its settings-window slider snaps to half hours,
        // and the plan cache is keyed on the value formatted to one decimal — so a free-floating float would
        // show up as a value the settings window cannot represent and would churn the cache. Typing is clamped
        // live but only SNAPPED on settle, where the correction is visible rather than fighting the keystroke.
        private const float TimeoutMin = 0f;
        private const float TimeoutMax = 8f;
        private const float TimeoutStep = 0.5f;
        private const string TimeoutFormat = "0.#";

        public Dialog_PlanCraft(Pawn pawn, Building_WorkTable bench)
        {
            this.pawn = pawn;
            this.bench = bench;
            var stack = bench?.BillStack?.Bills;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                    if (CraftBatchPlanner.CanPawnBatch(pawn, stack[i]))
                        bills.Add(stack[i]);

            timeoutHours = Mathf.Clamp(HaulersDreamMod.Settings?.craftBatchTimeoutHours ?? 2f, 0f, 8f);
            if (bills.Count > 0)
                SelectBill(bills[0]);

            forcePause = false;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            draggable = true;
            // Handle Return ourselves (see DrawButtons). With the default closeOnAccept = true, vanilla closes
            // the window on Return BEFORE DoWindowContents runs — so Enter silently CANCELLED a plan the player
            // had just finished setting up. That was already poor; with a text field to type a repetition count
            // into, it is actively wrong. Enter now means Prioritize. Escape still cancels via closeOnCancel.
            closeOnAccept = false;
        }

        public override Vector2 InitialSize => new Vector2(480f, 560f);

        private void SelectBill(Bill bill)
        {
            selected = bill;
            maxAvailReps = Mathf.Clamp(CraftBatchPlanner.MaxAvailableReps(pawn, bench, bill), 1, 500);
            reps = Mathf.Clamp(reps, 1, maxAvailReps);
            repsBuf = null;  // reps was written from outside its box, so the box must re-read it
            planSig = null;  // force a re-plan
        }

        private void RefreshPlan()
        {
            string sig = $"{selected?.GetUniqueLoadID()}|{reps}|{timeoutHours:0.0}";
            if (sig == planSig && cachedPlan != null)
                return;
            planSig = sig;
            cachedPlan = (selected == null)
                ? null
                : CraftBatchPlanner.Resolve(pawn, bench, selected, reps, Mathf.RoundToInt(timeoutHours * TicksPerHour));
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float btnH = 36f;

            // Read Return BEFORE anything draws: a text field would otherwise consume it, and the no-bills path
            // below returns early yet still has to swallow the key (see DrawButtons).
            bool enterPressed = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            Text.Font = GameFont.Medium;
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            Widgets.Label(titleRect, "HaulersDream.PlanCraft.Title".Translate(bench.LabelShortCap));
            Text.Font = GameFont.Small;

            if (bills.Count == 0)
            {
                var none = new Rect(inRect.x, titleRect.yMax + 8f, inRect.width, inRect.height - 80f);
                Widgets.Label(none, "HaulersDream.PlanCraft.NoBills".Translate());
                DrawButtons(inRect, btnH, confirmEnabled: false, enterPressed: enterPressed);
                return;
            }

            // ---- bill picker (scrollable radio list) ----
            float listTop = titleRect.yMax + 6f;
            float listH = Mathf.Min(bills.Count * RowH + 6f, 170f);
            var listOuter = new Rect(inRect.x, listTop, inRect.width, listH);
            Widgets.DrawMenuSection(listOuter);
            var viewRect = new Rect(0f, 0f, listOuter.width - 16f, bills.Count * RowH);
            Widgets.BeginScrollView(listOuter.ContractedBy(2f), ref billScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < bills.Count; i++)
            {
                var row = new Rect(0f, y, viewRect.width, RowH);
                if (Mouse.IsOver(row))
                    Widgets.DrawHighlight(row);
                bool sel = selected == bills[i];
                if (Widgets.RadioButtonLabeled(row, bills[i].LabelCap, sel) && !sel)
                    SelectBill(bills[i]);
                y += RowH;
            }
            Widgets.EndScrollView();

            // ---- sliders + summary ----
            RefreshPlan();
            var body = new Rect(inRect.x, listOuter.yMax + 10f, inRect.width, inRect.height - listOuter.yMax - btnH - 18f);
            var l = new Listing_Standard();
            l.Begin(body);

            // Both rows are caption → slider → box, in that order, every frame: the slider nulls the box's
            // buffer when it moved, and the box re-seeds from the new value in the same frame. Each box is
            // carved out of its slider's own row, so the dialog keeps its height.
            l.Label("HaulersDream.PlanCraft.Repeat".Translate(reps));
            var repsRow = l.GetRect(HDNumberField.SliderRowH);
            l.Gap(HDNumberField.SliderRowGap);
            HDNumberField.SplitRow(repsRow, out var repsSliderRect, out var repsBoxRect);
            // The slider still stops at what the ingredients currently allow — that is its job as the "safe"
            // control — while the box may go past it (see RepsBoxMax). Feeding the slider a clamped COPY is what
            // lets reps legally sit above maxAvailReps without the slider dragging it back down every frame.
            // → NOTE: while reps is ABOVE maxAvailReps the slider already reads as pinned at its right end, so
            //   dragging the thumb to that same end changes nothing and cannot pull the number back down. Any
            //   other position does, and so does the box. That falls out of the changed-only test and is
            //   harmless — just not obvious from the code.
            int fedReps = Mathf.Clamp(reps, 1, maxAvailReps);
            int fromRepsSlider = Mathf.RoundToInt(Widgets.HorizontalSlider(repsSliderRect, fedReps, 1, maxAvailReps, middleAlignment: true));
            if (fromRepsSlider != fedReps)
            {
                reps = fromRepsSlider;
                repsBuf = null;
            }
            HDNumberField.Int(repsBoxRect, "HD_PlanCraftReps", ref reps, ref repsBuf, RepsBoxMin, RepsBoxMax,
                "HaulersDream.PlanCraft.RepeatBoxTip".Translate(maxAvailReps));

            l.Label(timeoutHours <= 0f
                ? "HaulersDream.PlanCraft.TimeoutOff".Translate()
                : "HaulersDream.PlanCraft.Timeout".Translate(timeoutHours.ToString(TimeoutFormat)));
            var timeoutRow = l.GetRect(HDNumberField.SliderRowH);
            l.Gap(HDNumberField.SliderRowGap);
            HDNumberField.SplitRow(timeoutRow, out var timeoutSliderRect, out var timeoutBoxRect);
            float fedTimeout = Mathf.Clamp(timeoutHours, TimeoutMin, TimeoutMax);
            // Compare the slider's RAW return, and snap only once it has actually moved. Widgets.HorizontalSlider
            // hands its input straight back unless the player is dragging this very slider, so a raw comparison is
            // exact — there is no wobble to defend against.
            // → GOTCHA: snapping the COMPARAND instead breaks the test the moment `timeoutHours` sits off the
            //   lattice, which is exactly where the box puts it mid-entry. Typing ".25" onto "2" passes through
            //   2.2; Snap(2.2) = 2.0 != 2.2 would read as a slider move, adopt 2.0 and null the buffer under the
            //   caret, so the player asking for 2.25 would end up somewhere else entirely.
            float rawTimeout = Widgets.HorizontalSlider(timeoutSliderRect, fedTimeout, TimeoutMin, TimeoutMax, middleAlignment: true);
            if (rawTimeout != fedTimeout)
            {
                timeoutHours = NumberEntryPolicy.SnapFloat(rawTimeout, TimeoutStep, TimeoutMin, TimeoutMax);
                timeoutBuf = null;
            }
            HDNumberField.Float(timeoutBoxRect, "HD_PlanCraftTimeout", ref timeoutHours, ref timeoutBuf,
                TimeoutMin, TimeoutMax, TimeoutStep, TimeoutFormat,
                "HaulersDream.PlanCraft.TimeoutBoxTip".Translate(TimeoutMin.ToString(TimeoutFormat), TimeoutMax.ToString(TimeoutFormat)));

            l.Gap(6f);
            DrawSummary(l);

            l.End();

            DrawButtons(inRect, btnH, confirmEnabled: cachedPlan != null && cachedPlan.feasible, enterPressed: enterPressed);
        }

        private void DrawSummary(Listing_Standard l)
        {
            if (cachedPlan == null)
                return;
            if (!cachedPlan.feasible)
            {
                GUI.color = ColorLibrary.RedReadable;
                l.Label("HaulersDream.PlanCraft.Infeasible".Translate(cachedPlan.blockReason ?? ""));
                GUI.color = Color.white;
                return;
            }

            int n = cachedPlan.resolvedReps;
            // Resolved reps + which cap is binding (if it trimmed the request).
            if (n < cachedPlan.requestedReps)
            {
                string why;
                switch (cachedPlan.BindingLimit)
                {
                    case CraftBatchLimit.Resources: why = "HaulersDream.PlanCraft.LimitResources".Translate(); break;
                    case CraftBatchLimit.Mass: why = "HaulersDream.PlanCraft.LimitMass".Translate(); break;
                    case CraftBatchLimit.Timeout: why = "HaulersDream.PlanCraft.LimitTimeout".Translate(); break;
                    case CraftBatchLimit.BillRepeat: why = "HaulersDream.PlanCraft.LimitBillRepeat".Translate(); break;
                    default: why = ""; break;
                }
                l.Label("HaulersDream.PlanCraft.ResolvedTrimmed".Translate(n, why));
            }
            else
            {
                l.Label("HaulersDream.PlanCraft.Resolved".Translate(n));
            }

            // Ingredients carried up front. A MIXING recipe (cooked meals etc.) has no frozen per-slot def list —
            // ingredientDefs/perRepCounts are empty because the driver picks each rep's mix from current stock — so
            // show a generic "brings a mix" note instead of a per-def breakdown (which would be blank/misleading).
            if (cachedPlan.mixingRecipe)
            {
                l.Label("HaulersDream.PlanCraft.IngredientsMixed".Translate());
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < cachedPlan.ingredientDefs.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append((cachedPlan.perRepCounts[i] * n).ToString());
                    sb.Append("× ");
                    sb.Append(cachedPlan.ingredientDefs[i].label);
                }
                l.Label("HaulersDream.PlanCraft.Ingredients".Translate(sb.ToString()));
            }

            // Time estimate (work only; excludes the fetch trip).
            float hours = (cachedPlan.ticksPerRep * (float)n) / TicksPerHour;
            l.Label("HaulersDream.PlanCraft.EstTime".Translate(hours.ToString("0.#")));
        }

        /// <summary>
        /// Draw the Prioritize/Cancel pair and resolve a Return keystroke against them.
        /// </summary>
        /// <param name="inRect">The window's content area; the buttons sit along its bottom edge.</param>
        /// <param name="btnH">Button height.</param>
        /// <param name="confirmEnabled">Whether the current plan can actually be ordered — Prioritize is greyed
        /// out and Enter does nothing when it can't.</param>
        /// <param name="enterPressed">Whether Return was down at the top of this frame, read there so a focused
        /// text field couldn't eat it first.</param>
        private void DrawButtons(Rect inRect, float btnH, bool confirmEnabled, bool enterPressed)
        {
            float w = (inRect.width - 8f) / 2f;
            var confirmRect = new Rect(inRect.x, inRect.yMax - btnH, w, btnH);
            var cancelRect = new Rect(inRect.x + w + 8f, inRect.yMax - btnH, w, btnH);

            bool prev = GUI.enabled;
            GUI.enabled = confirmEnabled;
            if (Widgets.ButtonText(confirmRect, "HaulersDream.PlanCraft.Confirm".Translate()) && confirmEnabled)
                Confirm();
            GUI.enabled = prev;

            if (Widgets.ButtonText(cancelRect, "HaulersDream.PlanCraft.Cancel".Translate()))
                Close();

            // Enter = Prioritize. Consumed either way, so the key can never leak past this dialog: when the plan
            // isn't feasible (or there are no bills at all) it deliberately does nothing rather than closing,
            // which is what vanilla's closeOnAccept did and what made Enter read as a silent cancel.
            if (enterPressed)
            {
                Event.current.Use();
                if (confirmEnabled)
                    Confirm();
            }
        }

        private void Confirm()
        {
            // Settle both entry values FIRST, so the re-plan below, the persisted timeout and the dispatched
            // order all see the same settled numbers. Enter reaches this method with a numeric box still
            // focused, so the on-blur settle may never have run — this is where a typed "3.7" becomes the
            // half-hour 3.5 that the setting and the plan cache can actually represent. Settling is idempotent,
            // so the untouched case is a no-op.
            reps = NumberEntryPolicy.ClampInt(reps, RepsBoxMin, RepsBoxMax);
            timeoutHours = NumberEntryPolicy.SnapFloat(timeoutHours, TimeoutStep, TimeoutMin, TimeoutMax);
            // Both values were just written from outside their boxes; if the feasibility gate below bails and
            // the dialog stays open, the boxes must show the settled numbers rather than the pre-settle text.
            repsBuf = null;
            timeoutBuf = null;

            // Re-resolve against CURRENT stock for the dialog's OWN feasibility gate: the cached plan can be stale if
            // ingredients were consumed or hauled while the dialog sat open (the cache key is bill|reps|timeout, not
            // live stock), which would otherwise dispatch a doomed order and show a misleading "Started N×". Forcing a
            // fresh plan keeps the order honest. This is a LOCAL preview only — the authoritative plan that the job
            // actually uses is re-resolved INSIDE the synced command (StartBatchCraftSynced) so every Multiplayer
            // client computes an identical plan; we never ship the un-serializable CraftBatchPlan over the wire.
            planSig = null;
            RefreshPlan();
            if (cachedPlan == null || !cachedPlan.feasible || selected == null)
            {
                // Stock changed out from under a stale, still-feasible-looking preview (a rare last-frame race).
                if (selected != null)
                    Messages.Message("HaulersDream.PlanCraft.CouldNotStart".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // Persist the chosen timeout as the new default for next time. This is a LOCAL settings write (per-client
            // preference), not synced world state, so it stays here outside the synced command.
            if (HaulersDreamMod.Settings != null)
            {
                HaulersDreamMod.Settings.craftBatchTimeoutHours = timeoutHours;
                HaulersDreamMod.Settings.Write();
            }

            // Hand the order to the SYNCED entry point: in Multiplayer it runs as a command on every client (so the
            // plan re-resolve, the end-running-batch, the BatchCraftHandoff.Set, and the TryTakeOrderedJob all execute
            // identically everywhere — fixing the static-handoff desync); in single-player it runs directly, unchanged.
            // The synced method owns the player-facing "Started"/"CouldNotStart" toasts (gated to the issuing client),
            // so we don't toast here. Pass the timeout in TICKS (the planner/job unit), matching RefreshPlan's own
            // conversion above.
            JobDriver_BatchCraft.StartBatchCraftSynced(pawn, bench, selected, reps, Mathf.RoundToInt(timeoutHours * TicksPerHour));
            Close();
        }
    }
}
