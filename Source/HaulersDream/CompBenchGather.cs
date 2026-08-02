using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Per-workbench opt-out for HD's "gather this bill's ingredients into inventory before crafting" (issue #230),
    /// surfaced as a <see cref="Command_Toggle"/> on the bench's own command bar. Injected onto every bill-giving
    /// building by <c>Patches/HaulersDream_Benches.xml</c>; the comp needs no <see cref="CompProperties"/> subclass,
    /// so a bare <c>&lt;compClass&gt;</c> entry is the whole def-side wiring.
    ///
    /// <para>WHY a per-bench switch exists at all — and the correction the reporter's own diagnosis needs: the cost
    /// is NOT a pickup delay. Both gather drivers deliberately skip HD's #121 pickup pause (see the comments at
    /// <see cref="JobDriver_BillPrepGather"/>'s and <see cref="JobDriver_BatchCraft"/>'s <c>TakeToInventory</c>
    /// toils; neither driver is among <c>PickupPause.MakeToil</c>'s call sites), so the grabs are instant. The real
    /// cost is STRUCTURAL: the gather is a SEPARATE job that ends AT the bench, and the bill itself only begins on
    /// the pawn's NEXT work scan (see <see cref="Patch_WorkGiver_DoBill_InventoryRoute"/>'s summary). One sweep plus
    /// one extra job boundary beats many trips across a base — but beside a stocked shelf it loses to vanilla's
    /// carry-one-stack-and-start-immediately. Hence: hand THAT bench back to vanilla, keep the rest.</para>
    ///
    /// <para>Scope of the switch — it vetoes BOTH gather routes at this bench, because both go through
    /// <see cref="BillRouteGate.MayRouteToInventory"/>: the plain one-sweep gather
    /// (<see cref="Patch_WorkGiver_DoBill_InventoryRoute"/>) AND batch crafting
    /// (<see cref="Patch_WorkGiver_DoBill_BatchRoute"/>). Covering both matters more than it looks: the plain route
    /// SKIPS any recipe with <c>allowMixingIngredients</c>, and every vanilla meal recipe sets it (CookMealSimple
    /// and CookMealBulk directly; fine/lavish/survival through their abstract bases; likewise pemmican, kibble, beer
    /// and baby food). So on a stove the plain gather never engages on a meal bill at all — what a stove owner sees
    /// gathering is a BATCH bill or a non-mixing recipe, and a switch that only covered the plain route would look
    /// broken exactly where it was asked for.</para>
    /// </summary>
    [StaticConstructorOnStartup]
    public class CompBenchGather : ThingComp
    {
        /// <summary>
        /// The gizmo icon, resolved ONCE at startup — the texture is immutable, so a <see cref="ContentFinder{T}"/>
        /// lookup per selected bench per frame is pure waste. Same vanilla path and <see cref="BaseContent.BadTex"/>
        /// fallback HD already uses for its per-pawn gizmos, so a missing texture degrades to a placeholder rather
        /// than a null-icon exception on the render path.
        /// </summary>
        private static readonly Texture2D GatherIcon =
            ContentFinder<Texture2D>.Get("UI/Buttons/Drop", false) ?? BaseContent.BadTex;

        /// <summary>
        /// Does this bench let HD gather its bills' ingredients into inventory? Default ON, so an untouched bench —
        /// and every bench in a save made before this feature existed — keeps behaving exactly as it did. Scribed
        /// WITH a true default so a pre-feature save's missing node loads as ON rather than as C#'s <c>false</c>
        /// (the same reason <see cref="CompHauledToInventory.autoHaulYields"/> scribes a true default).
        /// Turning it off hands this one bench back to RimWorld's own one-stack-per-trip ingredient flow.
        /// </summary>
        public bool gatherIngredients = true;

        /// <summary>
        /// Persist the player's per-bench choice. The <c>haulersDream</c> key prefix matches every other HD comp
        /// key, so HD's nodes can never collide with a vanilla or foreign comp's key inside the same building.
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref gatherIngredients, "haulersDreamBenchGatherIngredients", true);
        }

        /// <summary>
        /// The only reader the gather gates call: may HD gather ingredients into inventory at this bill giver, as
        /// far as this bench's own switch is concerned? A thin Verse adapter over
        /// <see cref="BenchGatherPolicy.BenchAllowsGather"/>, which owns the veto-not-override invariant.
        ///
        /// <para>FAIL-OPEN is load-bearing: a bill giver the XML patch never reached (no <c>ITab_Bills</c>, a modded
        /// giver added after patch time, a bill giver that is not a building at all) has no comp and therefore no
        /// recorded player choice, so it must read as allowed and behave exactly as it does today. <c>TryGetComp</c>
        /// rather than <c>GetComp</c> so a bill giver that is not a <see cref="ThingWithComps"/> returns null
        /// instead of throwing.</para>
        /// </summary>
        /// <param name="bench">The bill giver being considered; null reads as allowed.</param>
        /// <returns>False only for a bench that carries the switch and has it turned OFF.</returns>
        public static bool Allows(Thing bench)
        {
            var comp = bench?.TryGetComp<CompBenchGather>();
            return BenchGatherPolicy.BenchAllowsGather(comp != null, comp?.gatherIngredients ?? true);
        }

        /// <summary>
        /// The per-bench "Gather ingredients" toggle. No Harmony patch is needed — <c>ThingWithComps.GetGizmos()</c>
        /// already yields every comp's <see cref="CompGetGizmosExtra"/>, which is exactly how Common Sense's own
        /// per-bench clean toggle (the control this feature was modelled on) reaches a workbench's command bar.
        /// </summary>
        /// <returns>The toggle, or nothing at all when this bench should not show one.</returns>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // Cheapest gate first: a settings-level visibility switch (a plain field read). Null settings → hide,
            // since with no settings loaded there is nothing meaningful to render against.
            if (!(HaulersDreamMod.Settings?.showBenchGatherGizmo ?? false))
                yield break;
            // Player-owned buildings only — never render a control on a bench the player cannot command.
            if (parent?.Faction != Faction.OfPlayerSilentFail)
                yield break;
            // → GOTCHA: this MUST be IsRoutableBenchType, NEVER MayRouteToInventory. MayRouteToInventory now folds
            // in THIS comp's own value, so gating visibility on it would make the button disappear the instant the
            // player switched a bench off — a one-way trapdoor with no way back. IsRoutableBenchType is the pure
            // TYPE test (a Building_WorkTable that is not autonomous), which is independent of the switch. It also
            // pays for the XML patch's deliberate over-reach: mech gestators and other non-routable bill givers
            // carry the comp but never show the button, because the gather never applied to them in the first place.
            if (!BillRouteGate.IsRoutableBenchType(parent))
                yield break;

            // State-dependent description (Common Sense's touch on its own bench toggle): the hover text explains
            // what the CURRENT state does and what flipping it would change, rather than one blurb for both states.
            string desc = gatherIngredients
                ? "HaulersDream.Gizmo.BenchGatherDescActive".Translate()
                : "HaulersDream.Gizmo.BenchGatherDescInactive".Translate();
            // #243: this switch governs HD's gather and nothing else — it cannot govern gathering HD is not doing.
            // Under Common Sense with its haul-all-ingredients option on, HD cedes the whole ingredient flow, so
            // the state description below would promise (or deny) behaviour this button has no say over; likewise
            // when HD's own one-sweep gather is switched off in mod options. Lead with the caveat in those cases.
            // The BUTTON deliberately keeps working either way: it still governs batch crafting and the
            // move-ingredients-closer detour at this bench, neither of which the caveats above touch.
            if (!GatherNotice.BenchSwitchGovernsPlainGather)
            {
                string notice = GatherNotice.Text(GatherNotice.Current);
                if (!string.IsNullOrEmpty(notice))
                    desc = notice + "\n\n" + desc;
            }

            // Deliberately NO Order. HD's per-pawn gizmos pin Order = float.MaxValue only because vanilla ABILITY
            // gizmos were wedging the "Unload inventory" button into the middle of the bar (#140); a BUILDING has no
            // ability gizmos, and comp gizmos are already emitted after the building's own, so an Order here would
            // buy nothing and would only fight whatever other mods put on a bench. Don't "fix" this by adding one.
            // Multi-select aggregates, but only PARTIALLY — don't overstate it. Command.GroupsWith merges on
            // hotkey/label/icon/groupKey, all of which are state-INDEPENDENT, so benches in MIXED on/off states still
            // collapse into a single drawn button. But Command_Toggle.InheritInteractionsFrom forwards the click only
            // to commands whose isActive() equals the drawn one's. So box-selecting six stoves and clicking once
            // flips all six only when they already share a state; in a mixed selection it flips just the subset that
            // matches the button as drawn, and a second click then catches the rest.
            yield return new Command_Toggle
            {
                defaultLabel = "HaulersDream.Gizmo.BenchGather".Translate(),
                defaultDesc = desc,
                icon = GatherIcon,
                isActive = () => gatherIngredients,
                // MP: gatherIngredients is a SCRIBED bool (synced world state). A raw click-time flip only mutates
                // the clicking client and desyncs in multiplayer, so the write goes through the synced shim — which
                // runs inline in single-player. isActive still reads the local value (rendering only). We read the
                // current value here and pass the DESIRED value, so the command is idempotent regardless of arrival
                // order (vs. passing "toggle", which two racing clicks would double-flip).
                toggleAction = () => MultiplayerCompat.SetBenchGather(parent, !gatherIngredients)
            };
        }
    }
}
