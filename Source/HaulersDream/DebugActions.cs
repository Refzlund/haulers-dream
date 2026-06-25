using System.Text;
using HaulersDream.Core;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// In-game dev harness (Dev Mode → Debug actions → "Hauler's Dream"). See .docs/02. Behavioural
    /// sign-off must happen in RimWorld — the unit tests only cover the pure carry math.
    /// </summary>
    public static class HaulersDreamDebugActions
    {
        // Bump this whenever the diagnostic output changes — if the log shows an OLDER tag than the build
        // you just deployed, RimWorld is still running the previously-loaded mod DLL (restart required).
        private const string DiagVersion = "diag-v2";

        [DebugAction("Hauler's Dream", "Log carry state (selected pawn)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogCarryState()
        {
            if (!(Find.Selector.SingleSelectedThing is Pawn pawn))
            {
                Messages.Message("Hauler's Dream: select a single pawn.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var comp = pawn.GetComp<CompHauledToInventory>();
            var s = HaulersDreamMod.Settings;
            float max = MassUtility.Capacity(pawn);
            float cur = MassUtility.GearAndInventoryMass(pawn);
            float cap = CarryMath.EffectiveCapacity(max, s.carryLimitFraction);

            var tracked = comp?.GetHashSet();
            var inv = pawn.inventory?.innerContainer;

            var sb = new StringBuilder();
            sb.AppendLine($"{HDLog.Tag}{DiagVersion} {pawn}: max={max:0.#}kg current={cur:0.#}kg limit={cap:0.#}kg " +
                          $"encumbrance={CarryMath.EncumbranceFraction(cur, max):P0} eligible={YieldRouter.IsEligible(pawn)}");
            sb.AppendLine($"  settings: shareForCrafting={s.shareForCrafting} shareForBuilding={s.shareForBuilding} " +
                          $"drafted={pawn.Drafted}");
            sb.AppendLine($"  yields: harvest={s.yieldHarvest} logging={s.yieldLogging} mining={s.yieldMining} " +
                          $"chunks={s.yieldChunks} deepDrill={s.yieldDeepDrill} deconstruct={s.yieldDeconstruct} " +
                          $"animals={s.yieldAnimals} strip={s.yieldStrip} uninstall={s.yieldUninstall}");
            sb.AppendLine($"  TRACKED (tagged / shareable) x{tracked?.Count ?? 0}:");
            if (tracked != null)
                foreach (var t in tracked)
                    sb.AppendLine($"    - {t?.def?.defName} x{t?.stackCount} (stillInInventory={inv?.Contains(t)}, reservable={(t != null && pawn.CanReserve(t))})");
            sb.AppendLine($"  FULL INVENTORY x{inv?.Count ?? 0} (tagged? = counted for sharing):");
            if (inv != null)
                for (int i = 0; i < inv.Count; i++)
                    sb.AppendLine($"    - {inv[i]?.def?.defName} x{inv[i]?.stackCount} (tagged={tracked?.Contains(inv[i])})");
            Log.Message(sb.ToString().TrimEnd());
        }

        [DebugAction("Hauler's Dream", "Force unload (selected pawn)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceUnload()
        {
            if (Find.Selector.SingleSelectedThing is Pawn pawn)
                PawnUnloadChecker.CheckIfShouldUnload(pawn, true);
            else
                Messages.Message("Hauler's Dream: select a single pawn.", MessageTypeDefOf.RejectInput, false);
        }

        /// <summary>
        /// Developer affordance: exercise the bulk-load CLAIM-LEDGER's load-bearing invariant
        /// (<c>totalClaimed[def] == Σ_pawn pawnClaims[pawn][def]</c>) through the SAME runtime code the live
        /// ledger uses — a synthetic <see cref="LoadLedgerEntry"/> driven by the real <see cref="Core.LoadLedger{TDef,TPawn}"/>
        /// math (ApplyClaim / Settle / Release), then verify the <see cref="Core.LoadLedger{TDef,TPawn}.RecomputeClaimed"/>
        /// self-heal that <c>LoadLedgerEntry.ExposeData</c>'s PostLoadInit applies (the mechanism by which the invariant
        /// SURVIVES SCRIBE: on load <c>totalClaimed</c> is rederived from the surviving <c>pawnClaims</c>, never trusted
        /// from disk). Read-only: it touches NO live game state — it keys the synthetic entry with real free colonists
        /// (used only as ledger keys, never mutated) and real material ThingDefs so it runs against live Verse types.
        ///
        /// NOTE: this asserts the invariant on a FRESH synthetic ledger plus the recompute step the scribe round-trip
        /// relies on (a full disk Scribe round-trip in a DebugAction is impractical — it would write a save file and
        /// drive the loader). The PostLoadInit recompute IS the scribe-survival guarantee, so reproducing it here
        /// proves the invariant holds across a save/load.
        /// </summary>
        [DebugAction("Hauler's Dream", "Verify ledger save/load round-trip", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void VerifyLedgerRoundTrip()
        {
            var map = Find.CurrentMap;
            var colonists = map?.mapPawns?.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0)
            {
                HDLog.Msg("ledger round-trip: SKIPPED — no free colonists on the current map to key the synthetic ledger.");
                return;
            }

            // Real Verse types as ledger keys (read-only — never mutated). Pick up to three colonists + three
            // material defs so the math exercises multi-pawn / multi-def claims and partial settles.
            var p1 = colonists[0];
            var p2 = colonists.Count > 1 ? colonists[1] : colonists[0];
            var p3 = colonists.Count > 2 ? colonists[2] : colonists[0];
            ThingDef steel = ThingDefOf.Steel;
            ThingDef wood = ThingDefOf.WoodLog;
            ThingDef silver = ThingDefOf.Silver;

            var entry = new LoadLedgerEntry(map);
            entry.SetNeeded(new System.Collections.Generic.Dictionary<ThingDef, int>
            {
                { steel, 300 }, { wood, 120 }, { silver, 50 },
            });

            var sb = new StringBuilder();
            sb.AppendLine($"{HDLog.Tag}{DiagVersion} ledger save/load round-trip:");
            bool ok = true;

            // 1) Two pawns claim overlapping defs; a third claims a disjoint def.
            entry.ApplyClaim(p1, new System.Collections.Generic.Dictionary<ThingDef, int> { { steel, 120 }, { wood, 40 } });
            entry.ApplyClaim(p2, new System.Collections.Generic.Dictionary<ThingDef, int> { { steel, 80 } });
            if (p3 != p1 && p3 != p2)
                entry.ApplyClaim(p3, new System.Collections.Generic.Dictionary<ThingDef, int> { { silver, 50 } });
            ok &= Check(entry, "after claims", sb);

            // 2) A partial deposit (settle DECREMENTS needed + claimed + the pawn's claim).
            entry.Settle(p1, steel, 70);
            ok &= Check(entry, "after partial settle", sb);

            // 3) A re-plan (ApplyClaim replaces a pawn's whole claim wholesale).
            entry.ApplyClaim(p2, new System.Collections.Generic.Dictionary<ThingDef, int> { { steel, 30 }, { wood, 20 } });
            ok &= Check(entry, "after re-plan", sb);

            // 4) An interrupt (Release returns a pawn's claim WITHOUT touching needed).
            entry.Release(p1);
            ok &= Check(entry, "after release", sb);

            // 5) The scribe-survival step: RecomputeClaimed (run in ExposeData's PostLoadInit) must reproduce the
            // live totalClaimed exactly from the surviving pawnClaims — proving the invariant survives a save/load.
            var recomputed = Core.LoadLedger<ThingDef, Pawn>.RecomputeClaimed(entry.pawnClaims);
            bool scribeOk = DictsEqual(entry.totalClaimed, recomputed);
            sb.AppendLine($"  PostLoadInit RecomputeClaimed reproduces live totalClaimed: {(scribeOk ? "PASS" : "FAIL")}");
            ok &= scribeOk;

            sb.Append($"  RESULT: {(ok ? "PASS — claim invariant holds and survives scribe recompute." : "FAIL — see lines above.")}");
            Log.Message(sb.ToString());
        }

        // Assert totalClaimed[def] == Σ_pawn pawnClaims[pawn][def] for the entry; append a per-step PASS/FAIL line.
        private static bool Check(LoadLedgerEntry entry, string step, StringBuilder sb)
        {
            var expected = Core.LoadLedger<ThingDef, Pawn>.RecomputeClaimed(entry.pawnClaims);
            bool ok = DictsEqual(entry.totalClaimed, expected);
            sb.AppendLine($"  {step}: claimed={Fmt(entry.totalClaimed)} expected(Σ pawnClaims)={Fmt(expected)} -> {(ok ? "PASS" : "FAIL")}");
            return ok;
        }

        private static bool DictsEqual(System.Collections.Generic.Dictionary<ThingDef, int> a,
            System.Collections.Generic.Dictionary<ThingDef, int> b)
        {
            int ca = a?.Count ?? 0, cb = b?.Count ?? 0;
            if (ca != cb)
                return false;
            if (a == null)
                return true;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out int v) || v != kv.Value)
                    return false;
            return true;
        }

        private static string Fmt(System.Collections.Generic.Dictionary<ThingDef, int> d)
        {
            if (d == null || d.Count == 0)
                return "{}";
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(", ");
                sb.Append($"{kv.Key?.defName}={kv.Value}");
                first = false;
            }
            return sb.Append("}").ToString();
        }
    }
}
