using System.Collections.Generic;
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

        private CraftBatchPlan cachedPlan;
        private string planSig;
        private Vector2 billScroll;

        // Bump when the planner/job behaviour changes, so a "still broken" report can be told from a stale DLL.
        public const string BuildTag = "F-Craft1";

        private const float RowH = 30f;
        private const int TicksPerHour = 2500; // GenDate.TicksPerHour

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
        }

        public override Vector2 InitialSize => new Vector2(480f, 560f);

        private void SelectBill(Bill bill)
        {
            selected = bill;
            maxAvailReps = Mathf.Clamp(CraftBatchPlanner.MaxAvailableReps(pawn, bench, bill), 1, 500);
            reps = Mathf.Clamp(reps, 1, maxAvailReps);
            planSig = null; // force a re-plan
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
            Text.Font = GameFont.Medium;
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            Widgets.Label(titleRect, "HaulersDream.PlanCraft.Title".Translate(bench.LabelShortCap));
            Text.Font = GameFont.Small;

            if (bills.Count == 0)
            {
                var none = new Rect(inRect.x, titleRect.yMax + 8f, inRect.width, inRect.height - 80f);
                Widgets.Label(none, "HaulersDream.PlanCraft.NoBills".Translate());
                DrawButtons(inRect, btnH, confirmEnabled: false);
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

            l.Label("HaulersDream.PlanCraft.Repeat".Translate(reps));
            reps = Mathf.RoundToInt(l.Slider(reps, 1f, maxAvailReps));

            l.Label(timeoutHours <= 0f
                ? "HaulersDream.PlanCraft.TimeoutOff".Translate()
                : "HaulersDream.PlanCraft.Timeout".Translate(timeoutHours.ToString("0.#")));
            timeoutHours = Mathf.Round(l.Slider(timeoutHours, 0f, 8f) * 2f) / 2f;

            l.Gap(6f);
            DrawSummary(l);

            l.End();

            DrawButtons(inRect, btnH, confirmEnabled: cachedPlan != null && cachedPlan.feasible);
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

        private void DrawButtons(Rect inRect, float btnH, bool confirmEnabled)
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
        }

        private void Confirm()
        {
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
