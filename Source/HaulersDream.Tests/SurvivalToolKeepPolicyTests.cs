using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SurvivalToolKeepPolicyTests
    {
        // The keep branch exactly as InventorySurplus.SurplusOf composes it, so every test below exercises the SAME
        // shape the runtime uses: the rule decides "is this the pawn's survival-tool kit", and the call site adds the
        // !hdSwept guard that keeps HD's own swept cargo unloadable. (SurvivalToolsCompat.IsCarriedTool supplies the
        // three facts: the reflected types resolving, pawn.RaceProps.Humanlike, and the tool-class + tool-properties
        // identity test.)
        private static bool KeepsStack(bool hdSwept, bool modResolved, bool pawnIsHumanlike, bool thingIsSurvivalTool)
            => !hdSwept && SurvivalToolKeepPolicy.KeepsCarriedTool(modResolved, pawnIsHumanlike, thingIsSurvivalTool);

        // ── The mod's presence is the outermost gate ─────────────────────────────────────────────

        [Test]
        public void ModUnresolved_KeepsNothing()
        {
            // Two situations collapse onto this one input, deliberately. WITHOUT the mod the reflected types never
            // resolve, so the branch cannot fire whatever the pawn carries — the byte-identical-without-the-mod
            // contract. WITH the mod present but a load-bearing type renamed, the shim warns once and lands here too:
            // failing CLOSED is the chosen direction, because the cost is a self-correcting churn loop against the
            // mod's own re-fetch, whereas failing open would pin arbitrary inventory a colonist could never shed.
            Assert.That(SurvivalToolKeepPolicy.KeepsCarriedTool(
                modResolved: false, pawnIsHumanlike: true, thingIsSurvivalTool: true), Is.False);
        }

        // ── The carrier gate: humanlikes only, mirroring the mod's own CanUseSurvivalTools ───────

        [Test]
        public void CarriedToolOnAHumanlike_IsKept()
        {
            // The whole point of the shim: with "unload all surplus" ON, a colonist's pickaxe must stay in its pack
            // instead of being shipped to a shelf for the mod's auto-pickup to fetch straight back.
            Assert.That(KeepsStack(hdSwept: false, modResolved: true, pawnIsHumanlike: true,
                thingIsSurvivalTool: true), Is.True);
        }

        [Test]
        public void ToolLoadedOntoANonHumanlikeCarrier_StillUnloads()
        {
            // A pack animal (or a mech) cannot use survival tools — the mod's own CanUseSurvivalTools requires
            // RaceProps.Humanlike — so a tool HD loaded onto one is ordinary cargo and must reach storage. Keeping it
            // would strand it: nothing but HD would ever take it back out.
            Assert.That(KeepsStack(hdSwept: false, modResolved: true, pawnIsHumanlike: false,
                thingIsSurvivalTool: true), Is.False);
        }

        // ── The identity gate: only the mod's own tools ──────────────────────────────────────────

        [Test]
        public void OrdinaryStackOnAToolUser_IsNotKept()
        {
            // The keep is not "anything a tool-using colonist carries": steel, meals and plain weapons still unload.
            // This is also the guard against widening the keep if only one of the two identity types resolved — the
            // shim requires the tool CLASS and the tool-properties extension, the mod's own IsSurvivalTool pair.
            Assert.That(KeepsStack(hdSwept: false, modResolved: true, pawnIsHumanlike: true,
                thingIsSurvivalTool: false), Is.False);
        }

        // ── The black-hole guard, which is what makes a keep-ALL contract safe here ──────────────

        [Test]
        public void HdSweptTool_StaysUnloadable()
        {
            // A loose tool HD bulk-hauled off the ground is HD's OWN cargo, and HD is the only thing that would ever
            // put it away — so the keep must not pin it. This is what bounds an over-keep to "a tool the pawn
            // acquired for itself", the set the mod would re-fetch anyway, and it is why HD does not need a
            // count-precise keep the way the Compositable Loadouts shim does (#233).
            Assert.That(KeepsStack(hdSwept: true, modResolved: true, pawnIsHumanlike: true,
                thingIsSurvivalTool: true), Is.False);
        }

        // ── All three facts are required, and none of them alone is enough ───────────────────────

        [Test]
        public void KeepsOnlyWhenEveryFactHolds()
        {
            // Full truth table: the rule is an AND of three independent facts, so any one of them being false must
            // release the stack to the ordinary surplus path. Swept over every combination rather than spot-checked,
            // because a keep that fires on a partial match is the strand-causing direction.
            for (int mask = 0; mask < 8; mask++)
            {
                bool modResolved = (mask & 1) != 0;
                bool pawnIsHumanlike = (mask & 2) != 0;
                bool thingIsSurvivalTool = (mask & 4) != 0;
                bool expected = modResolved && pawnIsHumanlike && thingIsSurvivalTool;
                Assert.That(SurvivalToolKeepPolicy.KeepsCarriedTool(modResolved, pawnIsHumanlike, thingIsSurvivalTool),
                    Is.EqualTo(expected),
                    $"modResolved={modResolved}, humanlike={pawnIsHumanlike}, isTool={thingIsSurvivalTool}");
            }
        }
    }
}
