using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    public partial class HaulersDreamSettings
    {
        private static string OverloadLevelLabel(int lv)
        {
            lv = OverloadTuning.ClampLevel(lv);
            if (lv == 0) return "HaulersDream.Overload.Free".Translate();
            if (lv >= OverloadTuning.OffLevel) return "HaulersDream.Overload.Off".Translate();
            if (lv == OverloadTuning.FairLevel) return "HaulersDream.Overload.Fair".Translate();
            return lv < OverloadTuning.FairLevel
                ? "HaulersDream.Overload.Eager".Translate(lv)
                : "HaulersDream.Overload.Cautious".Translate(lv);
        }

        private static string EnRoutePathCheckerLabel(EnRoutePathChecker c)
        {
            switch (c)
            {
                case EnRoutePathChecker.Default: return "HaulersDream.Setting.EnRoutePathDefault".Translate();
                case EnRoutePathChecker.Pathfinding: return "HaulersDream.Setting.EnRoutePathPathfinding".Translate();
                default: return "HaulersDream.Setting.EnRoutePathVanilla".Translate();
            }
        }

        private static string AutoStripModeLabel(AutoStripMode m)
        {
            switch (m)
            {
                case AutoStripMode.AllHauls: return "HaulersDream.Setting.AutoStripAll".Translate();
                case AutoStripMode.DisposalOnly: return "HaulersDream.Setting.AutoStripDisposal".Translate();
                default: return "HaulersDream.Setting.AutoStripOff".Translate();
            }
        }

        private static string TaintedPolicyLabel(TaintedApparelPolicy p)
        {
            switch (p)
            {
                case TaintedApparelPolicy.LeaveOnCorpse: return "HaulersDream.Setting.TaintedLeave".Translate();
                case TaintedApparelPolicy.DropAndForbid: return "HaulersDream.Setting.TaintedForbid".Translate();
                case TaintedApparelPolicy.Destroy: return "HaulersDream.Setting.TaintedDestroy".Translate();
                default: return "HaulersDream.Setting.TaintedTake".Translate();
            }
        }

        private static FloatMenu TaintedPolicyMenu(System.Action<TaintedApparelPolicy> set)
            => new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(TaintedPolicyLabel(TaintedApparelPolicy.Take), () => set(TaintedApparelPolicy.Take)),
                new FloatMenuOption(TaintedPolicyLabel(TaintedApparelPolicy.LeaveOnCorpse), () => set(TaintedApparelPolicy.LeaveOnCorpse)),
                new FloatMenuOption(TaintedPolicyLabel(TaintedApparelPolicy.DropAndForbid), () => set(TaintedApparelPolicy.DropAndForbid)),
                new FloatMenuOption(TaintedPolicyLabel(TaintedApparelPolicy.Destroy), () => set(TaintedApparelPolicy.Destroy)),
            });

        // ===== Tabbed settings window (ported from While You're Up's tabbed dialog) =====
        // The settings list long ago outgrew Dialog_ModSettings' fixed height. Rather than one giant scroll,
        // the ~94 controls are split across tabs (each its OWN scroll view) so the window stays navigable.
        // PURE UX: every control is rendered under EXACTLY ONE tab; no field, Scribe key, or behavior changes.
        private enum SettingsTab
        {
            General,        // master enable, carry limit, pickup, unload defaults, overload/strict/keep-working, C1 rows
            Sharing,        // share carried materials, construction/crafting delivery, meet-in-middle, meals-on-wheels, tether/site
            Bulk,           // bulk haul + trigger + sweep, pack-animal load/unload, transporter/portal/vehicle bulk loading
            Routing,        // C2 en-route + C3 storage routing + C4 storage filter
            Sources,        // work-types, work-overrides, who, auto-strip + haul-after-slaughter + spoiling-first
            Planners,       // route/craft planner, unload grace/interval, cannot-unload alert, verbose logging, dev detour
        }

        private static SettingsTab currentTab = SettingsTab.General;
        // Per-tab scroll position + measured content height. Keyed by (int)SettingsTab so each tab scrolls
        // independently and keeps its own height (no shared-state collapse between tabs). Sized to the enum.
        private static readonly Vector2[] tabScroll = new Vector2[6];
        private static readonly float[] tabHeight = { 1400f, 1400f, 1400f, 1400f, 1400f, 1400f };
        // Reused per-frame tab-record list (allocated once; cleared+refilled each draw).
        private static readonly List<TabRecord> tabRecords = new List<TabRecord>(6);

        public void DoWindowContents(Rect rect)
        {
            // --- "Reset to defaults" button in the top-right of the free 32px header strip (above the tab row).
            // The tabs are drawn ABOVE tabRect's top edge (rect.y + 32) on the LEFT, so a right-aligned button here
            // never overlaps them. Confirmed before resetting so a stray click can't wipe the player's config. ---
            float btnW = 160f, btnH = 28f;
            var resetRect = new Rect(rect.xMax - btnW, rect.y + 2f, btnW, btnH);
            if (Widgets.ButtonText(resetRect, "HaulersDream.Setting.ResetToDefaults".Translate()))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "HaulersDream.Setting.ResetToDefaultsConfirm".Translate(),
                    ResetToDefaults, destructive: true));

            // --- tab row (vanilla DrawMenuSection + TabRecord row, like WYU). The row sits 32px down from the
            // top of rect; the tab body fills the rest. TabDrawer draws the tabs ABOVE tabRect's top edge. ---
            var tabRect = new Rect(rect.x, rect.y + 32f, rect.width, rect.height - 32f);
            Widgets.DrawMenuSection(tabRect);

            tabRecords.Clear();
            tabRecords.Add(new TabRecord("HaulersDream.Tab.General".Translate(), () => currentTab = SettingsTab.General, currentTab == SettingsTab.General));
            tabRecords.Add(new TabRecord("HaulersDream.Tab.Sharing".Translate(), () => currentTab = SettingsTab.Sharing, currentTab == SettingsTab.Sharing));
            tabRecords.Add(new TabRecord("HaulersDream.Tab.Bulk".Translate(), () => currentTab = SettingsTab.Bulk, currentTab == SettingsTab.Bulk));
            tabRecords.Add(new TabRecord("HaulersDream.Tab.Routing".Translate(), () => currentTab = SettingsTab.Routing, currentTab == SettingsTab.Routing));
            tabRecords.Add(new TabRecord("HaulersDream.Tab.Sources".Translate(), () => currentTab = SettingsTab.Sources, currentTab == SettingsTab.Sources));
            tabRecords.Add(new TabRecord("HaulersDream.Tab.Planners".Translate(), () => currentTab = SettingsTab.Planners, currentTab == SettingsTab.Planners));
            TabDrawer.DrawTabs(tabRect, tabRecords, 1);
            tabRecords.Clear();

            // --- the active tab's scroll view ---
            // A small inner inset keeps content off the menu-section border.
            var inner = tabRect.ContractedBy(8f);
            int t = (int)currentTab;
            var view = new Rect(0f, 0f, inner.width - 16f, tabHeight[t]);
            var scroll = tabScroll[t];
            Widgets.BeginScrollView(inner, ref scroll, view);
            tabScroll[t] = scroll;
            var l = new Listing_Standard
            {
                // CRITICAL (unchanged from the pre-tab window): without this the scroll view silently breaks.
                // Listing_Standard.NewColumnIfNeeded wraps into a SECOND column the instant content exceeds
                // listingRect.height (= tabHeight[t]) in any frame — which happens whenever a tab's options
                // grow past the last measured height (initial 1400 too small, or toggling bulk-haul / auto-strip
                // adds rows). After a wrap, CurHeight (curY) reports only the short wrapped column, so the
                // measured height collapses to the Mathf.Max floor (inner.height = the viewport). The view then
                // equals the viewport, the scrollbar vanishes, and the wrapped columns run off-screen — and it
                // re-wraps from the collapsed height every frame, so it never recovers. maxOneColumn forbids
                // the wrap, so CurHeight is always the true single-column height and tabHeight[t] tracks it.
                maxOneColumn = true
            };
            l.Begin(view);

            switch (currentTab)
            {
                case SettingsTab.General: DrawGeneralTab(l); break;
                case SettingsTab.Sharing: DrawSharingTab(l); break;
                case SettingsTab.Bulk: DrawBulkTab(l); break;
                case SettingsTab.Routing: DrawRoutingTab(l); break;
                case SettingsTab.Sources: DrawSourcesTab(l); break;
                case SettingsTab.Planners: DrawPlannersTab(l); break;
            }

            tabHeight[t] = Mathf.Max(l.CurHeight + 12f, inner.height);
            l.End();
            Widgets.EndScrollView();
        }

        // ===== General =====
        // Master enable (NEW, top), carry limit, pickup mode, unload defaults (mark/before-downtime/item-rules/
        // surplus), bulk smart-overload + strict + keep-working + opportunistic unload, and the C1 safety rows.
        private void DrawGeneralTab(Listing_Standard l)
        {
            // Master enable — NEW control at the very top. Disables HD's automatic behaviors for troubleshooting
            // without a restart; a pawn already carrying scooped goods still unloads (never a black hole).
            l.CheckboxLabeled("HaulersDream.Setting.MasterEnabled".Translate(), ref masterEnabled,
                "HaulersDream.Setting.MasterEnabledDesc".Translate());

            l.GapLine();
            l.Label("HaulersDream.Setting.CarryLimit".Translate(carryLimitFraction.ToStringPercent()),
                tooltip: "HaulersDream.Setting.CarryLimitDesc".Translate());
            carryLimitFraction = l.Slider(carryLimitFraction, CarryMath.MinFraction, CarryMath.MaxFraction);

            l.GapLine();
            bool dropThenHaul = pickupMode == PickupMode.DropThenHaul;
            l.CheckboxLabeled("HaulersDream.Setting.DropThenHaul".Translate(), ref dropThenHaul,
                "HaulersDream.Setting.DropThenHaulDesc".Translate());
            pickupMode = dropThenHaul ? PickupMode.DropThenHaul : PickupMode.DirectToInventory;
            l.CheckboxLabeled("HaulersDream.Setting.MarkForUnload".Translate(), ref markForUnload);
            l.CheckboxLabeled("HaulersDream.Setting.UnloadAllSurplus".Translate(), ref unloadAllSurplus,
                "HaulersDream.Setting.UnloadAllSurplusDesc".Translate());
            // Stockpile-style per-item picker: keep all / keep at most N / always unload, per item def.
            int ruleCount = ItemRuleCount;
            string itemBtn = ruleCount > 0
                ? "HaulersDream.Setting.ItemUnloadButtonN".Translate(ruleCount)
                : "HaulersDream.Setting.ItemUnloadButton".Translate();
            if (l.ButtonText(itemBtn))
                Find.WindowStack.Add(new Dialog_ItemUnloadSettings(this));
            if (markForUnload)
            {
                // "Put it away before relaxing" — unload before each downtime activity (bypassing the accumulate window).
                l.CheckboxLabeled("HaulersDream.Setting.UnloadBeforeSleep".Translate(), ref unloadBeforeSleep,
                    "HaulersDream.Setting.UnloadBeforeSleepDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.UnloadBeforeLeisure".Translate(), ref unloadBeforeLeisure,
                    "HaulersDream.Setting.UnloadBeforeLeisureDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.UnloadBeforeEating".Translate(), ref unloadBeforeEating,
                    "HaulersDream.Setting.UnloadBeforeEatingDesc".Translate());
            }

            // --- smart overload / strict / keep-working / opportunistic unload + the C1 safety rows ---
            l.GapLine();
            l.Label("HaulersDream.Setting.Overload".Translate(OverloadLevelLabel(overloadLevel)));
            overloadLevel = Mathf.RoundToInt(l.Slider(overloadLevel, 0f, OverloadTuning.MaxLevel));
            l.Label("HaulersDream.Setting.OverloadDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.StrictCarryWeight".Translate(), ref strictCarryWeight,
                "HaulersDream.Setting.StrictCarryWeightDesc".Translate());
            // Keep working when full (opt-in, default OFF). Distinct from strict carry weight: this keeps the
            // overload-and-accumulate ceiling but stops a full pawn breaking off to unload — it works on and
            // only sheds the load before a long relocation. The margin slider is the hysteresis for that rule.
            l.CheckboxLabeled("HaulersDream.Setting.KeepWorkingWhenFull".Translate(), ref keepWorkingWhenFull,
                "HaulersDream.Setting.KeepWorkingWhenFullDesc".Translate());
            if (keepWorkingWhenFull)
            {
                l.Label("HaulersDream.Setting.KeepWorkingMargin".Translate(keepWorkingMarginCells),
                    tooltip: "HaulersDream.Setting.KeepWorkingMarginDesc".Translate());
                keepWorkingMarginCells = Mathf.RoundToInt(l.Slider(keepWorkingMarginCells, 0f, 30f));
            }
            l.CheckboxLabeled("HaulersDream.Setting.OpportunisticUnload".Translate(), ref opportunisticUnload,
                "HaulersDream.Setting.OpportunisticUnloadDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.SkipHaulWhileBleeding".Translate(), ref skipHaulWhileBleeding,
                "HaulersDream.Setting.SkipHaulWhileBleedingDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ClosestDestUnloadOrder".Translate(), ref closestDestinationUnloadOrder,
                "HaulersDream.Setting.ClosestDestUnloadOrderDesc".Translate());
        }

        // ===== Sharing & Delivery =====
        // Share carried materials between colonists: construction/crafting sources + delivery, meet-in-middle,
        // batch/inventory delivery, hand-hauled claim, meals-on-wheels, haul-to-stack, construct tether + haul-to-site.
        private void DrawSharingTab(Listing_Standard l)
        {
            l.Label("HaulersDream.Setting.ShareHeader".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareForBuilding".Translate(), ref shareForBuilding);
            l.CheckboxLabeled("HaulersDream.Setting.BuildFromInventory".Translate(), ref buildFromInventory,
                "HaulersDream.Setting.BuildFromInventoryDesc".Translate());
            if (buildFromInventory)
                l.CheckboxLabeled("HaulersDream.Setting.BuildFromInventoryPartial".Translate(), ref buildFromInventoryPartial,
                    "HaulersDream.Setting.BuildFromInventoryPartialDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareForCrafting".Translate(), ref shareForCrafting,
                "HaulersDream.Setting.ShareForCraftingDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.InventoryCraftDeliver".Translate(), ref inventoryCraftDeliver,
                "HaulersDream.Setting.InventoryCraftDeliverDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareMeetInMiddle".Translate(), ref shareMeetInMiddle);
            l.CheckboxLabeled("HaulersDream.Setting.BatchWorkDeliveries".Translate(), ref batchWorkDeliveries,
                "HaulersDream.Setting.BatchWorkDeliveriesDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.InventoryConstructDeliver".Translate(), ref inventoryConstructDeliver,
                "HaulersDream.Setting.InventoryConstructDeliverDesc".Translate());
            if (inventoryConstructDeliver)
                l.CheckboxLabeled("HaulersDream.Setting.MultiSiteConstructDeliver".Translate(), ref multiSiteConstructDeliver,
                    "HaulersDream.Setting.MultiSiteConstructDeliverDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareHandHauled".Translate(), ref shareHandHauledToStorage,
                "HaulersDream.Setting.ShareHandHauledDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.MealsOnWheels".Translate(), ref mealsOnWheels,
                "HaulersDream.Setting.MealsOnWheelsDesc".Translate());

            // Haul-to-stack (prefer topping up existing stacks; destination tiles unreserved) — a delivery tweak.
            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.HaulToStack".Translate(), ref haulToStack,
                "HaulersDream.Setting.HaulToStackDesc".Translate());

            // Ordered construction (F16): tether haul+build, and the haul-to-site right-click order.
            l.Gap(6f);
            l.CheckboxLabeled("HaulersDream.Setting.ConstructTether".Translate(), ref orderedConstructTether,
                "HaulersDream.Setting.ConstructTetherDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.HaulToSiteOption".Translate(), ref haulToSiteOption,
                "HaulersDream.Setting.HaulToSiteOptionDesc".Translate());
        }

        // ===== Bulk & Carriers =====
        // Bulk hauling (the native Pick-Up-And-Haul) + trigger + nearby/oversized/manual-pickup/sweep, pack-animal
        // load + auto-divert + bulk-unload-carriers, and transporter / portal / vehicle bulk loading.
        private void DrawBulkTab(Listing_Standard l)
        {
            l.CheckboxLabeled("HaulersDream.Setting.BulkHaul".Translate(), ref bulkHaul,
                "HaulersDream.Setting.BulkHaulDesc".Translate());
            if (bulkHaul)
            {
                if (l.RadioButton("HaulersDream.Setting.BulkHaulSecond".Translate(),
                        bulkHaulTrigger == BulkHaulTrigger.SecondTasked,
                        tooltip: "HaulersDream.Setting.BulkHaulSecondDesc".Translate()))
                    bulkHaulTrigger = BulkHaulTrigger.SecondTasked;
                if (l.RadioButton("HaulersDream.Setting.BulkHaulAlways".Translate(),
                        bulkHaulTrigger == BulkHaulTrigger.Always,
                        tooltip: "HaulersDream.Setting.BulkHaulAlwaysDesc".Translate()))
                    bulkHaulTrigger = BulkHaulTrigger.Always;
                l.CheckboxLabeled("HaulersDream.Setting.HaulNearbyOption".Translate(), ref haulNearbyOption,
                    "HaulersDream.Setting.HaulNearbyOptionDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.HaulOversized".Translate(), ref haulOversizedInInventory,
                    "HaulersDream.Setting.HaulOversizedDesc".Translate());
            }

            // The "Pick up X" right-click order is independent of the bulk-haul sweep (its provider gates only on
            // manualPickupOption), so it lives OUTSIDE the bulkHaul block — reachable even with bulk-haul turned off.
            l.CheckboxLabeled("HaulersDream.Setting.ManualPickup".Translate(), ref manualPickupOption,
                "HaulersDream.Setting.ManualPickupDesc".Translate());

            l.CheckboxLabeled("HaulersDream.Setting.SweepNearbyWhileWorking".Translate(), ref sweepNearbyWhileWorking,
                "HaulersDream.Setting.SweepNearbyWhileWorkingDesc".Translate());

            l.CheckboxLabeled("HaulersDream.Setting.LoadPackAnimalBulk".Translate(), ref loadPackAnimalBulk,
                "HaulersDream.Setting.LoadPackAnimalBulkDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AutoDivertToPackAnimal".Translate(), ref autoDivertToPackAnimal,
                "HaulersDream.Setting.AutoDivertToPackAnimalDesc".Translate());

            l.CheckboxLabeled("HaulersDream.Setting.EnableBulkUnloadCarriers".Translate(), ref enableBulkUnloadCarriers,
                "HaulersDream.Setting.EnableBulkUnloadCarriersDesc".Translate());
            if (enableBulkUnloadCarriers)
            {
                l.Label("HaulersDream.Setting.MinFreeSpaceToUnloadCarrier".Translate(minFreeSpaceToUnloadCarrierPct.ToStringPercent()));
                minFreeSpaceToUnloadCarrierPct = Mathf.Round(l.Slider(minFreeSpaceToUnloadCarrierPct, 0.1f, 0.9f) * 20f) / 20f;
                l.CheckboxLabeled("HaulersDream.Setting.ReserveCarrierOnUnload".Translate(), ref reserveCarrierOnUnload,
                    "HaulersDream.Setting.ReserveCarrierOnUnloadDesc".Translate());
                l.Label("HaulersDream.Setting.VisualUnloadDelay".Translate(visualUnloadDelay));
                visualUnloadDelay = Mathf.RoundToInt(l.Slider(visualUnloadDelay, 0f, 30f));
            }

            l.CheckboxLabeled("HaulersDream.Setting.EnableBulkLoadTransporters".Translate(), ref enableBulkLoadTransporters,
                "HaulersDream.Setting.EnableBulkLoadTransportersDesc".Translate());
            if (enableBulkLoadTransporters)
            {
                l.Label("HaulersDream.Setting.BulkLoadAiUpdateFrequency".Translate(bulkLoadAiUpdateFrequency));
                bulkLoadAiUpdateFrequency = Mathf.RoundToInt(l.Slider(bulkLoadAiUpdateFrequency, 10f, 240f) / 10f) * 10;
            }

            l.CheckboxLabeled("HaulersDream.Setting.EnableBulkLoadPortal".Translate(), ref enableBulkLoadPortal,
                "HaulersDream.Setting.EnableBulkLoadPortalDesc".Translate());

            l.CheckboxLabeled("HaulersDream.Setting.EnableBulkRefuel".Translate(), ref enableBulkRefuel,
                "HaulersDream.Setting.EnableBulkRefuelDesc".Translate());

            // Vehicle Framework compat — only shown when VF is loaded (the rows are pure noise otherwise; every VF
            // consumer also gates on VehicleFrameworkCompat.IsActive, so a hidden row never changes behaviour). The
            // master's tooltip explains the §1 toggle semantics: the safety guards are NOT gated on it.
            if (VehicleFrameworkCompat.IsActive)
            {
                l.CheckboxLabeled("HaulersDream.Setting.EnableVehicleFramework".Translate(), ref enableVehicleFramework,
                    "HaulersDream.Setting.EnableVehicleFrameworkDesc".Translate());
                if (enableVehicleFramework)
                    l.CheckboxLabeled("HaulersDream.Setting.EnableBulkLoadVehicles".Translate(), ref enableBulkLoadVehicles,
                        "HaulersDream.Setting.EnableBulkLoadVehiclesDesc".Translate());
            }

            // --- Bulk Load For Transporters parity ---
            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.CleanupOnSave".Translate(), ref cleanupOnSave,
                "HaulersDream.Setting.CleanupOnSaveDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.SoftlockDrop".Translate(), ref enableSoftlockDrop,
                "HaulersDream.Setting.SoftlockDropDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.OpportunisticLoad".Translate(), ref enableOpportunisticLoad,
                "HaulersDream.Setting.OpportunisticLoadDesc".Translate());
            if (enableOpportunisticLoad)
            {
                l.Label("HaulersDream.Setting.LoadOpportunityRadius".Translate(loadOpportunityScanRadius));
                loadOpportunityScanRadius = Mathf.RoundToInt(l.Slider(loadOpportunityScanRadius, 5f, 100f));
            }
            l.CheckboxLabeled("HaulersDream.Setting.ContinuousLoading".Translate(), ref enableContinuousLoading,
                "HaulersDream.Setting.ContinuousLoadingDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.LoadHybridPathing".Translate(), ref loadHybridPathing,
                "HaulersDream.Setting.LoadHybridPathingDesc".Translate());
            if (loadHybridPathing)
            {
                l.Label("HaulersDream.Setting.LoadPathfindingCandidates".Translate(loadPathfindingCandidates));
                loadPathfindingCandidates = Mathf.RoundToInt(l.Slider(loadPathfindingCandidates, 2f, 24f));
            }
            l.CheckboxLabeled("HaulersDream.Setting.AutoOpenTransporterContents".Translate(), ref autoOpenTransporterContents,
                "HaulersDream.Setting.AutoOpenTransporterContentsDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AutoOpenCarrierGear".Translate(), ref autoOpenCarrierGear,
                "HaulersDream.Setting.AutoOpenCarrierGearDesc".Translate());
        }

        // ===== Hauling Sources & Who =====
        // Which work scoops yields, work-incapability overrides, who may haul (drafted/mechs/animals/incapable/
        // non-home/gizmo), auto-strip-while-hauling + haul-after-slaughter + spoiling-first ingredient selection.
        private void DrawSourcesTab(Listing_Standard l)
        {
            l.Label("HaulersDream.Setting.AutoStripHeader".Translate());
            if (l.ButtonText("HaulersDream.Setting.AutoStripMode".Translate(AutoStripModeLabel(autoStripMode))))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(AutoStripModeLabel(AutoStripMode.AllHauls), () => autoStripMode = AutoStripMode.AllHauls),
                    new FloatMenuOption(AutoStripModeLabel(AutoStripMode.DisposalOnly), () => autoStripMode = AutoStripMode.DisposalOnly),
                    new FloatMenuOption(AutoStripModeLabel(AutoStripMode.Off), () => autoStripMode = AutoStripMode.Off),
                }));
            if (autoStripMode != AutoStripMode.Off)
                l.CheckboxLabeled("HaulersDream.Setting.StripColonists".Translate(), ref stripColonistCorpses,
                    "HaulersDream.Setting.StripColonistsDesc".Translate());
            // Cross-reference: clarify that "never" only disables strip-WHILE-hauling, not the separate
            // manual-strip gear haul (haulStrip, in the work-types section below). Shown always — it explains
            // the "never" option, which is only meaningful when the mode is Off. NO gating-logic change.
            l.Label("HaulersDream.Setting.AutoStripCrossRef".Translate());
            // The tainted policies also govern corpse strip ORDERS (the haulStrip scoop), so they stay
            // visible while either consumer is on — hiding an active Destroy policy would be a trap.
            if (autoStripMode != AutoStripMode.Off || haulStrip)
            {
                if (l.ButtonText("HaulersDream.Setting.TaintedSmeltable".Translate(TaintedPolicyLabel(taintedSmeltablePolicy))))
                    Find.WindowStack.Add(TaintedPolicyMenu(p => taintedSmeltablePolicy = p));
                if (l.ButtonText("HaulersDream.Setting.TaintedNonSmeltable".Translate(TaintedPolicyLabel(taintedNonSmeltablePolicy))))
                    Find.WindowStack.Add(TaintedPolicyMenu(p => taintedNonSmeltablePolicy = p));
            }

            // Haul after slaughter — independent of the auto-strip mode (a fresh KILL hauls the carcass to
            // storage so it doesn't rot in place). Two independent toggles, both default ON.
            l.CheckboxLabeled("HaulersDream.Setting.HaulTamedSlaughter".Translate(), ref haulTamedSlaughter,
                "HaulersDream.Setting.HaulTamedSlaughterDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.HaulWildKills".Translate(), ref haulWildKills,
                "HaulersDream.Setting.HaulWildKillsDesc".Translate());

            // Spoiling-first ingredient selection — prefer the most-perishable already-valid candidate when a
            // colonist picks ingredients for a bill (butcher = corpse; cook = rottable food). Two toggles, both ON.
            l.CheckboxLabeled("HaulersDream.Setting.ButcherSpoilingFirst".Translate(), ref butcherSpoilingFirst,
                "HaulersDream.Setting.ButcherSpoilingFirstDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.CookSpoilingFirst".Translate(), ref cookSpoilingFirst,
                "HaulersDream.Setting.CookSpoilingFirstDesc".Translate());

            // --- which kinds of work scoop their yields ---
            l.GapLine();
            l.Label("HaulersDream.Setting.WorkTypesHeader".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.HaulHarvest".Translate(), ref haulHarvest);
            l.CheckboxLabeled("HaulersDream.Setting.HaulMining".Translate(), ref haulMining);
            l.CheckboxLabeled("HaulersDream.Setting.HaulDeepDrill".Translate(), ref haulDeepDrill);
            l.CheckboxLabeled("HaulersDream.Setting.HaulDeconstruct".Translate(), ref haulDeconstruct);
            l.CheckboxLabeled("HaulersDream.Setting.HaulAnimals".Translate(), ref haulAnimals);
            l.CheckboxLabeled("HaulersDream.Setting.HaulStrip".Translate(), ref haulStrip,
                "HaulersDream.Setting.HaulStripDesc".Translate());
            // Cross-reference back to "Auto-strip while hauling" — independent control, not coupled. NO gating change.
            l.Label("HaulersDream.Setting.HaulStripCrossRef".Translate());

            // --- work-incapability overrides ---
            l.GapLine();
            l.Label("HaulersDream.Setting.WorkOverrideHeader".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AllCanHaul".Translate(), ref allPawnsCanHaul,
                "HaulersDream.Setting.AllCanHaulDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AllCanClean".Translate(), ref allPawnsCanClean,
                "HaulersDream.Setting.AllCanCleanDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AllCanCutPlants".Translate(), ref allPawnsCanCutPlants,
                "HaulersDream.Setting.AllCanCutPlantsDesc".Translate());

            // --- who may scoop & haul ---
            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.PauseWhileDrafted".Translate(), ref pauseWhileDrafted);
            l.CheckboxLabeled("HaulersDream.Setting.AllowMechanoids".Translate(), ref allowMechanoids,
                "HaulersDream.AllowMechanoidsDesc".Translate());
            l.CheckboxLabeled("HaulersDream.AllowAnimals".Translate(), ref allowAnimals,
                "HaulersDream.AllowAnimalsDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AllowIncapable".Translate(), ref allowIncapable);
            l.CheckboxLabeled("HaulersDream.Setting.EnableOnNonHomeMaps".Translate(), ref enableOnNonHomeMaps);
            l.CheckboxLabeled("HaulersDream.Setting.HideGizmo".Translate(), ref hideGizmo);
        }

        // ===== En-route & Routing =====
        // The While You're Up parity routing clusters: C2 en-route pickup, C3 consumer-aware storage routing,
        // C4 storage-building permit/deny filter.
        private void DrawRoutingTab(Listing_Standard l)
        {
            // En-route pickup (C2) — default OFF (a behavior-changing feature). When on, a path-checker float-menu
            // selects how strictly the "is the store on the way?" check runs (Vanilla = cheap, the A* modes opt-in).
            l.CheckboxLabeled("HaulersDream.Setting.EnRoutePickup".Translate(), ref enRoutePickup,
                "HaulersDream.Setting.EnRoutePickupDesc".Translate());
            if (enRoutePickup)
            {
                if (l.ButtonText("HaulersDream.Setting.EnRoutePathChecker".Translate(EnRoutePathCheckerLabel(enRoutePathChecker))))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        new FloatMenuOption(EnRoutePathCheckerLabel(EnRoutePathChecker.Vanilla), () => enRoutePathChecker = EnRoutePathChecker.Vanilla),
                        new FloatMenuOption(EnRoutePathCheckerLabel(EnRoutePathChecker.Default), () => enRoutePathChecker = EnRoutePathChecker.Default),
                        new FloatMenuOption(EnRoutePathCheckerLabel(EnRoutePathChecker.Pathfinding), () => enRoutePathChecker = EnRoutePathChecker.Pathfinding),
                    }));
                l.Label("HaulersDream.Setting.EnRoutePathCheckerDesc".Translate());
            }

            // Consumer-aware storage routing (C3) — MASTER off by default (a behavior-changing feature). When on,
            // the 4 sub-toggles (supplies/ingredients closer, equal-priority, stockpile targets) become editable.
            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.StorageRouting".Translate(), ref storageRouting,
                "HaulersDream.Setting.StorageRoutingDesc".Translate());
            if (storageRouting)
            {
                l.CheckboxLabeled("HaulersDream.Setting.RouteSupplies".Translate(), ref routeSupplies,
                    "HaulersDream.Setting.RouteSuppliesDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.RouteIngredients".Translate(), ref routeIngredients,
                    "HaulersDream.Setting.RouteIngredientsDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.RouteToEqualPriority".Translate(), ref routeToEqualPriority,
                    "HaulersDream.Setting.RouteToEqualPriorityDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.RouteToStockpiles".Translate(), ref routeToStockpiles,
                    "HaulersDream.Setting.RouteToStockpilesDesc".Translate());
            }

            // Storage building permit/deny filter (C4) — MASTER off by default. When on, the sub-toggles and
            // the per-building/per-mod dialog (the one shared filter) become editable.
            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.StorageFilters".Translate(), ref storageFiltersEnabled,
                "HaulersDream.Setting.StorageFiltersDesc".Translate());
            if (storageFiltersEnabled)
            {
                l.CheckboxLabeled("HaulersDream.Setting.StorageFilterUseDefaults".Translate(), ref storageFilterUseDefaults,
                    "HaulersDream.Setting.StorageFilterUseDefaultsDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.StorageFilterDenyLwm".Translate(), ref storageFilterDenyLwmForOpportunistic,
                    "HaulersDream.Setting.StorageFilterDenyLwmDesc".Translate());
                if (l.ButtonText("HaulersDream.Setting.StorageFilterButton".Translate()))
                    Find.WindowStack.Add(new Dialog_StorageBuildingFilter(storageBuildingFilter));
            }
        }

        // ===== Planners & Advanced =====
        // The right-click planning tools (route / crafting planner + their global defaults), unload timing
        // (grace / interval), the cannot-unload safety alert, verbose logging, and the dev-only detour overlay.
        private void DrawPlannersTab(Listing_Standard l)
        {
            // Planner enables + batch defaults.
            l.CheckboxLabeled("HaulersDream.Setting.PlanRoutes".Translate(), ref planRoutes,
                "HaulersDream.Setting.PlanRoutesDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.PlanCrafting".Translate(), ref planCrafting,
                "HaulersDream.Setting.PlanCraftingDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.BatchByDefault".Translate(), ref batchByDefault,
                "HaulersDream.Setting.BatchByDefaultDesc".Translate());
            l.Label("HaulersDream.Setting.DefaultBatchSize".Translate(defaultBatchSize));
            defaultBatchSize = Mathf.RoundToInt(l.Slider(defaultBatchSize, 1f, 200f));

            l.GapLine();
            l.Label("HaulersDream.Setting.RouteMaxAmount".Translate(routeMaxAmount));
            routeMaxAmount = Mathf.RoundToInt(l.Slider(routeMaxAmount, 5f, RouteSelection.HardCap));

            // Route calculation (global defaults; each are also overridable per target type in the route dialog).
            l.Label("HaulersDream.Setting.RouteCalcHeader".Translate());
            if (l.ButtonText("HaulersDream.Setting.RouteSelection".Translate(SelectionMethodLabel(routeSelectionMethod))))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(SelectionMethodLabel(RouteSelectionMethod.MostStopsPerTravel), () => routeSelectionMethod = RouteSelectionMethod.MostStopsPerTravel),
                    new FloatMenuOption(SelectionMethodLabel(RouteSelectionMethod.NearestToTarget), () => routeSelectionMethod = RouteSelectionMethod.NearestToTarget),
                }));
            if (l.ButtonText("HaulersDream.Setting.RouteDistance".Translate(DistanceBasisLabel(routeDistanceBasis))))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(DistanceBasisLabel(RouteDistanceBasis.StraightLine), () => routeDistanceBasis = RouteDistanceBasis.StraightLine),
                    new FloatMenuOption(DistanceBasisLabel(RouteDistanceBasis.WalkingPath), () => routeDistanceBasis = RouteDistanceBasis.WalkingPath),
                }));
            l.Label("HaulersDream.Setting.RouteOrderEffort".Translate(routeOrderExactMax));
            routeOrderExactMax = Mathf.RoundToInt(l.Slider(routeOrderExactMax, 8f, 14f));

            l.Label(craftBatchTimeoutHours <= 0f
                ? "HaulersDream.Setting.CraftBatchTimeoutOff".Translate()
                : "HaulersDream.Setting.CraftBatchTimeout".Translate(craftBatchTimeoutHours.ToString("0.#")));
            craftBatchTimeoutHours = Mathf.Round(l.Slider(craftBatchTimeoutHours, 0f, 8f) * 2f) / 2f;

            // --- unload timing ---
            l.GapLine();
            l.Label("HaulersDream.Setting.UnloadGrace".Translate(unloadGraceTicks));
            unloadGraceTicks = Mathf.RoundToInt(l.Slider(unloadGraceTicks, 0f, 7500f) / 50f) * 50;
            l.Label(intervalUnloadHours <= 0f
                ? "HaulersDream.Setting.IntervalUnloadOff".Translate()
                : "HaulersDream.Setting.IntervalUnload".Translate(intervalUnloadHours.ToString("0.#")));
            intervalUnloadHours = Mathf.Round(l.Slider(intervalUnloadHours, 0f, 24f) * 2f) / 2f;

            // --- black-hole safety-net alert ---
            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.AlertCannotUnload".Translate(), ref alertCannotUnload,
                "HaulersDream.Setting.AlertCannotUnloadDesc".Translate());
            if (alertCannotUnload)
            {
                l.Label("HaulersDream.Setting.AlertStuckHours".Translate(alertStuckHours.ToString("0.#")));
                alertStuckHours = Mathf.Round(l.Slider(alertStuckHours, 1f, 72f) * 2f) / 2f;
            }

            // Verbose logging + dev-only detour overlay — both shown only in Dev Mode (parity with BLFT, which
            // gates its debug logging on Dev Mode). The verboseLogging field STAYS scribed (a dev's choice
            // persists), but the control is hidden — and HDLog.Dbg also requires Prefs.DevMode — so a normal
            // player never sees the toggle or any debug spam.
            if (Prefs.DevMode)
            {
                l.GapLine();
                l.CheckboxLabeled("HaulersDream.Setting.VerboseLogging".Translate(), ref verboseLogging);
                // Dev-only detour overlay — a transient diagnostic, not serialized.
                l.CheckboxLabeled("HaulersDream.Setting.DrawDetourLines".Translate(), ref drawDetourLines,
                    "HaulersDream.Setting.DrawDetourLinesDesc".Translate());
            }
        }
    }
}
