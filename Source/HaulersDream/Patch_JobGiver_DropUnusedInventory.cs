using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// (Issues #62 / #87 — LAYER 1, the primary, most robust guard) Stop vanilla's raw-food drop loop from ever
    /// running while a pawn carries HD-scooped raw-food cargo.
    ///
    /// <para><see cref="JobGiver_DropUnusedInventory.TryGiveJob"/> runs every think pass for a non-drafted player
    /// colonist standing in the Home area. Its FIRST loop drops every inventory item that is
    /// <c>IsIngestible &amp;&amp; !IsDrug &amp;&amp; ingestible.preferability &lt;= 5</c> — raw crops / milk / eggs — but only
    /// once <c>pawn.mindState.lastInventoryRawFoodUseTick + 150000</c> ticks (~2.5 in-game days) have elapsed.
    /// HD scoops exactly those yields into inventory (tagged via <see cref="CompHauledToInventory"/>) to haul them
    /// to storage on its own unload trip, so on an established save the loop dumps the scooped yields the moment a
    /// grower/handler finishes harvesting (issue #62). This bug has recurred across updates because the per-item
    /// veto below relied on the EXACT scooped <c>Thing</c> still being in the tag set, which a stack merge/split
    /// can defeat.</para>
    ///
    /// <para>This prefix removes that fragility by attacking the loop's GATE instead of each drop: if the pawn is
    /// carrying any HD-tagged stack the food loop would drop, it re-arms <c>lastInventoryRawFoodUseTick</c> to the
    /// current tick, so vanilla's gate <c>TicksGame &gt; lastInventoryRawFoodUseTick + 150000</c> is false and the
    /// whole food loop is skipped this pass. It reads the HEALED tag set (<see cref="CompHauledToInventory.GetHashSet"/>),
    /// so a merged/split scooped stack is still recognised by its def, and it decides via the unit-pinned
    /// <see cref="Core.DropUnusedFoodPolicy"/> so the dropped category can never silently drift away from vanilla's.
    /// It also keeps working even if another mod reimplements the loop, as long as that reimplementation honours
    /// vanilla's clock (the usual pattern) — Combat Extended, for instance, only modifies the drop, not the gate.</para>
    ///
    /// <para>The only behavioural change for a pawn carrying HD cargo is that its OWN unwanted raw food (a packed
    /// lunch it would otherwise shed) also stays in the pack a little longer, until the trip empties the HD cargo
    /// and the clock resumes; nothing is lost and the deviation is bounded to the haul. A pawn carrying no tagged
    /// raw food is byte-identical to vanilla — the clock is never touched. The drug loop is untouched here; tagged
    /// drugs are handled by the #81 guard below.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_DropUnusedInventory), "TryGiveJob")]
    public static class Patch_JobGiver_DropUnusedInventory_TryGiveJob
    {
        static void Prefix(Pawn pawn)
        {
            if (pawn?.mindState == null || pawn.inventory == null)
                return;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return;
            // Healed set: a scoop that landed across several stacks, or stacks that merged/split since, are all
            // correctly tagged here — the per-Thing fragility that let this bug recur is gone. Per-tick cached, so
            // the heal cost is paid at most once this think pass (the drug-loop guard below reuses the same cache).
            var tagged = comp.GetHashSet();
            if (tagged.Count == 0)
                return;
            bool holdsTaggedRawFood = false;
            foreach (var thing in tagged)
            {
                var def = thing?.def;
                if (def == null)
                    continue;
                int pref = def.ingestible != null ? (int)def.ingestible.preferability : 0;
                if (Core.DropUnusedFoodPolicy.IsRawFoodDropCandidate(def.IsIngestible, def.IsDrug, pref))
                {
                    holdsTaggedRawFood = true;
                    break;
                }
            }
            if (!holdsTaggedRawFood)
                return;
            // Re-arm the loop's clock to "now" so vanilla's gate (TicksGame > lastInventoryRawFoodUseTick + 150000)
            // is false this pass and the raw-food loop is skipped entirely. Only ever moves the clock FORWARD, and
            // only while HD cargo is aboard, so it can never make the pawn drop food it should keep — only delay a
            // drop it would otherwise make, which HD's own unload trip resolves.
            int nowTick = Find.TickManager?.TicksGame ?? pawn.mindState.lastInventoryRawFoodUseTick;
            if (nowTick > pawn.mindState.lastInventoryRawFoodUseTick)
                pawn.mindState.lastInventoryRawFoodUseTick = nowTick;
        }
    }

    /// <summary>
    /// (Issue #62 — LAYER 2, the per-drop backstop) Protect HD's scooped work-yields from vanilla's "drop unused
    /// inventory" sweep at the actual drop call.
    ///
    /// <para>This complements the Layer-1 gate above: where Layer 1 stops vanilla's raw-food loop from running at
    /// all, this skips the private <c>Drop</c> for ANY HD-tagged stack — covering the drug loop too, and any path
    /// (a foreign mod, a future vanilla change) that reaches <c>Drop</c> without going through the re-armed clock.
    /// Combat Extended's own prefix on this same <c>Drop</c> explicitly PERMITS the food category, and Pick Up And
    /// Haul's prefix protects only PUAH's own comp, so HD's tagged stock was unprotected by either without this.</para>
    ///
    /// <para>It reads the HEALED set (<see cref="CompHauledToInventory.GetHashSet"/>) — the fix for the recurrence:
    /// the old read used the un-healed peek view, so a scooped stack that merged into another same-def inventory
    /// stack lost its tag and dropped anyway. The heal is per-tick cached and Layer 1 already triggered it this
    /// pass, so this adds no real cost. It is a pure no-op for every UNTAGGED item (vanilla byte-identical — a
    /// packed lunch the pawn isn't keeping still drops normally), and only ever protects items HD itself scooped,
    /// which HD always has a storage destination for, so nothing is stranded.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_DropUnusedInventory), "Drop")]
    public static class Patch_JobGiver_DropUnusedInventory_Drop
    {
        // Return false to SKIP vanilla's inventory drop for an HD-tagged stack (it belongs to HD's unload trip) OR a
        // KEPT def (#197: the "Keep N in inventory" order / Gear-tab keep button — the pawn is deliberately holding
        // it). Blocking drop-unused for the WHOLE kept def is intended: any excess above the kept count is unloaded to
        // storage by HD's own surplus-aware unload trip, not dumped on the floor by vanilla's crude drop loop.
        static bool Prefix(Pawn pawn, Thing thing)
        {
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (comp == null || thing == null)
                return true;
            return !comp.GetHashSet().Contains(thing) && !comp.IsKeptDef(thing.def);
        }
    }

    /// <summary>
    /// (Issue #81) Keep an HD-hauled DRUG in inventory until HD's unload trip stores it.
    ///
    /// <para><see cref="JobGiver_DropUnusedInventory.TryGiveJob"/> runs a second, UN-gated loop every think pass that
    /// drops every inventory thing for which <see cref="JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory"/>
    /// returns false — i.e. any drug the colonist isn't scheduled to take, isn't carrying per its drug policy, and
    /// isn't addicted/dependent on. A recreational drug like a smokeleaf joint hits all of those, so the instant a
    /// pawn picks one up via HD's "Pick up X" order or an en-route grab, vanilla drops it back at the pawn's feet
    /// (reported in #81: only drugs are affected; every non-drug haulable rides to storage fine, because the loop
    /// only ever looks at <c>IsDrug</c> things).</para>
    ///
    /// <para>This postfix forces "keep" for any drug HD has tagged, so the joint stays in the pack for HD's
    /// storage-aware unload trip instead of being dumped. <c>ShouldKeepDrugInInventory</c> is the canonical
    /// keep-or-drop gate, so when another mod reimplements this drop loop — inlining the drop and bypassing the
    /// private <c>Drop</c> that the #62 guard vetoes — HD's cargo is still kept as long as that mod consults the
    /// predicate (the usual pattern). That is the case the #81 report (a heavily-modded load order) hits. The same
    /// predicate also gates vanilla's "pick up drug" float-menu on a GROUND stack, which HD never has tagged, so
    /// that UI is unchanged.</para>
    ///
    /// <para>It reads the HEALED set (<see cref="CompHauledToInventory.GetHashSet"/>) so a merged/split tagged drug
    /// stack is still recognised, only ever flips false -&gt; true, and only for a thing already in HD's tagged set
    /// — a colonist's own recreational/personal drugs (never HD-tagged) keep dropping exactly as in vanilla.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_DropUnusedInventory), nameof(JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory))]
    public static class Patch_JobGiver_DropUnusedInventory_ShouldKeepDrug
    {
        static void Postfix(ref bool __result, Pawn pawn, Thing drug)
        {
            if (__result) return; // vanilla already keeps it — nothing to do
            if (pawn == null || drug == null)
                return;
            // UI-PATH GUARD: GetHashSet's self-heal MUTATES synced world state (re-tags the scribed set, re-notifies
            // CE HoldTracker), so it must run ONLY on a synced path. Vanilla calls this predicate from TWO sites: the
            // in-tick drop loop (drug = an INVENTORY thing — synced think path) and FloatMenuOptionProvider_PickUpItem
            // .GetOptionsFor (drug = a GROUND stack the player right-clicked — float-menu generation on the clicking
            // client's UI thread, NOT a synced command). Healing on the UI caller desyncs MP. Gate on "drug is in THIS
            // pawn's inventory": always true on the in-tick caller (heal correct), always false on the float-menu
            // caller (ground stack) — so the heal is skipped there, and since a ground drug is never HD-tagged the
            // result is unchanged anyway. Cheap O(1) ThingOwner.Contains. (PackAnimalLoad.cs documents this same
            // un-healed-view vs healed-view mutation hazard for the alert/OnGUI render path.)
            var inner = pawn.inventory?.innerContainer;
            if (inner == null || !inner.Contains(drug))
                return;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp != null && (comp.GetHashSet().Contains(drug) || comp.IsKeptDef(drug.def)))
                __result = true; // HD-hauled cargo (keep for the unload trip) OR a kept drug (#197 keep-in-inventory)
        }

        /// <summary>
        /// The seam name, minus the per-drug suffix the <see cref="Finalizer"/> appends. Kept as a constant so
        /// the suffix is the ONLY thing that varies between reports and the dedupe stays predictable.
        /// </summary>
        private const string SeamBase = "JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory (HD drop protection)";

        /// <summary>
        /// (Issue #232) Contain a throw out of this predicate and answer "keep it", the safe direction.
        ///
        /// <para>WHY THE SEAM NEEDS ONE. This predicate is vanilla's canonical keep-or-drop gate and it has two
        /// callers, neither of which guards it. The in-tick one is <c>JobGiver_DropUnusedInventory.TryGiveJob</c>,
        /// which does <c>if (!ShouldKeepDrugInInventory(...)) Drop(...)</c> for every drug in the pack, every
        /// think pass, for every undrafted colonist standing in the Home area. The other is
        /// <c>FloatMenuOptionProvider_PickUpItem.GetOptionsFor</c>, which is why the #232 report surfaced as
        /// "right-clicking a colonist onto a drug throws" — the throw aborts the float-menu build, so the
        /// pick-up options never appear.</para>
        ///
        /// <para>THE DEGRADE VALUE IS THE WHOLE POINT. When the original throws, <c>__result</c> is
        /// <c>default(bool)</c> — <c>false</c> — and <c>false</c> is the DESTRUCTIVE answer here: it reads as
        /// "the colonist has no reason to hold this", so the drop loop dumps that stack at their feet, again on
        /// the very next think pass, forever. <c>true</c> is vanilla's own fall-through (the method returns
        /// <c>false</c> only when a six-clause conjunction all holds), so it stops the drop and restores the
        /// pick-up options, and it is stable across BOTH callers, so the two can never oscillate against each
        /// other. It does not weaken the #62/#81/#197 drop protection either: for a tagged or player-kept drug
        /// the postfix above would have forced <c>true</c> anyway.</para>
        ///
        /// <para>ONE HONEST OVER-REACH. A finalizer cannot tell "the original threw" from "a FOREIGN postfix
        /// threw after the original returned <c>false</c>", so in that sub-case a legitimate <c>false</c> is
        /// turned into <c>true</c> and a drug the colonist really should shed stays in the pack until whatever
        /// threw is fixed. A thread-static "the original completed" latch was considered and REJECTED: it is
        /// unreliable under patch ordering (a foreign postfix ordered before HD's leaves it unset either way) and
        /// it adds re-entrancy surface to a UI-thread-reachable path, for a sub-case whose degrade already errs
        /// in the safe direction.</para>
        ///
        /// <para>WHAT IT DOES NOT DO. It never repairs, replaces or edits the drug policy, and it never
        /// remembers that a def throws. A <c>DrugPolicy</c> is shared, SCRIBED state and one caller is the
        /// clicking client's UI thread, so writing to it there would be a save mutation and a multiplayer desync
        /// — the same hazard the UI-path guard above documents for the tag-set self-heal. The throw therefore
        /// still happens on every call; containment costs nothing on the non-throwing path.</para>
        /// </summary>
        /// <param name="__exception">Harmony's observed exception, null on the normal path.</param>
        /// <param name="__result">The predicate's answer, forced to true when a foreign fault is contained.</param>
        /// <param name="pawn">The colonist the check ran for; may be null.</param>
        /// <param name="drug">The drug being checked; read for its <c>defName</c> only, never its label — see the
        /// dedupe comment in the body.</param>
        /// <returns>The exception when HD is implicated (Harmony rethrows), otherwise null (Harmony drops it).</returns>
        static Exception Finalizer(Exception __exception, ref bool __result, Pawn pawn, Thing drug)
        {
            if (__exception == null)
                return null; // the common path — no fault, nothing to report

            // Per-DEF dedupe, mirroring WorkCapabilityProbe's per-(race, work type) key: one malformed drug def
            // reports once instead of being hidden behind the first one, and the count is bounded at one entry per
            // def per session. defName, never Label/LabelCap: a label lookup on the very data that just faulted
            // could throw again and escape this finalizer, defeating the guard (the same care WorkCapabilityProbe
            // documents). It also puts the def name into HDGuard's format-free disk breadcrumb — the line issue
            // #235 proved survives when building the rich message does not.
            string seam = SeamBase + " [drug def: " + (drug?.def?.defName ?? "unknown") + "]";

            // HD's own fault stays loud (the no-swallow rule) — classified from FRAME OBJECTS, never from
            // rendered trace text, because the 0Harmony the game loads collapses a repeat render into a
            // placeholder (issue #236).
            if (HDFault.InvolvesHaulersDream(__exception))
                return HDGuard.SeamThrew(__exception, seam, pawn,
                    "this colonist's keep-or-drop check for that drug failed, so the drop loop and the "
                    + "right-click \"Pick up\" options for it are both unusable.");

            __result = true; // vanilla's own fall-through answer: keep it (see the degrade rationale above)
            return HDGuard.SeamContained(__exception, seam, pawn, ContainedConsequence(__exception));
        }

        /// <summary>
        /// The consequence line for a contained fault: what HD did and what it costs, plus — for the exception
        /// TYPE the one known cause actually produces, and only then — a factual note naming that cause.
        ///
        /// <para>THE TYPE GATE IS THE #236 CONTRACT, not fussiness. <see cref="HDGuard"/> appends its own
        /// attribution clause to this same log line. An ungated "one known source is RimWorld's drug-policy
        /// lookup" would therefore sit directly beside "the innermost frame belonging to another mod is X, so
        /// that is the most likely source" whenever some other mod's postfix threw, say, a
        /// <c>KeyNotFoundException</c> here — two sentences pointing the reader at two different culprits, which
        /// is exactly the steering-the-report failure #236 exists to remove. For every other exception type HD
        /// states only what it did and leaves attribution wholly to that clause. Note the sentence stays
        /// non-exclusive ("one known source") even when it is emitted, because an
        /// <see cref="ArgumentException"/> here could still come from somewhere else.</para>
        /// </summary>
        /// <param name="ex">The contained exception; read for its TYPE only, never dereferenced further.</param>
        /// <returns>The consequence text for <see cref="HDGuard.SeamContained"/>.</returns>
        private static string ContainedConsequence(Exception ex)
        {
            string didAndCost =
                "Hauler's Dream answered \"keep this drug in the pack\", which is RimWorld's own fall-through "
                + "answer for that check, so nothing is dropped and the right-click menu still builds; a failed "
                + "call would otherwise read as \"drop it\" and the colonist would dump that stack at their feet "
                + "on every think pass.";
            string knownCause = ex is ArgumentException
                ? " One known source of this exception here is RimWorld's own drug-policy lookup (DrugPolicy's "
                  + "per-drug indexer), which raises an ArgumentException carrying no message when the colonist's "
                  + "drug policy holds no entry for the drug being checked - that lookup runs before any Hauler's "
                  + "Dream code on this path."
                : string.Empty;
            return didAndCost + knownCause
                + " Hauler's Dream contains this, it never repairs the policy: rewriting saved drug policies is "
                + "not something a hauling mod should do.";
        }
    }
}
