using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /*
        ═══════════════════════════════════════════════════════════════════════════════════════════════
                                     Storage commitments — the ONE seam
        ═══════════════════════════════════════════════════════════════════════════════════════════════
        The single answer to "how much of this storage group may this pawn commit to right now", and the
        only place a commitment is recorded. Every path that sends cargo to a slot group passes through
        here; scripts/check-storage-commit-seam.ts fails the build when a new one does not.

        THE BUG THIS EXISTS FOR (#114 / #248, "fixed" three times). Vanilla's destination cell reservation
        does DOUBLE duty: it hides the cell, and — because HaulAIUtility.HaulToCellStorageJob sums
        GetItemStackSpaceLeftFor only over cells that pass IsGoodStoreCell — it also SHRINKS every other
        pawn's job.count. Hauler's Dream strips that reservation for stackables so several pawns can share
        one tile, and for three releases it gave nothing back: every concurrent hauler priced the same free
        cells into its own count, pocketed a full stack for three units of room, and carried the rest home.

        WHAT REPLACES IT. A per-(pawn, group, def) claim ledger, read by two Harmony adapters (see
        StorageCommitAdapters):
          • HaulAIUtility.HaulToCellStorageJob — the COUNTER. Clamps job.count to what is genuinely free.
          • StoreUtility.IsGoodStoreCell      — the GATE. Hides a group whose units are fully spoken for,
                                                so the pawn is never routed there and never handed a zero.
        Ours is never stricter than the truth: vanilla already hides a cell from every other hauler the
        instant one pawn reserves it, at WHOLE-CELL granularity and blind to counts.

        → KEY: the ledger is IMMEDIATE, never a per-tick snapshot. A commitment written during tick T is
          visible to a pawn planning later in tick T. The previous design memoised the colony's in-flight
          loads per tick, which is why the shipped #114 rule was correct and its answer still wrong.
        → KEY: the row records WHERE and WHAT; the pawn's live state records HOW MUCH. Every read clamps a
          row to StorageEvidence, so a claim cannot outlive its cargo and no deposit hook is needed.
        → GOTCHA: RE-ENTRANCY. Measuring a group calls IsGoodStoreCell, which the gate patches, which calls
          back into here. `insideSpaceScan` makes the gate stand down for the duration; without it the first
          query overflows the stack. This is the single load-bearing correctness detail in the file.
        → GOTCHA: the raw cell measurement is memoised per (tick, group, thing, pawn). A deposit that lands
          mid-tick is therefore invisible to a pawn that already measured the same group earlier in that
          same tick, which can allow one extra unit for one tick. That residual is bounded and self-clearing,
          and it is the price of not re-walking a stockpile once per candidate cell. The LEDGER — the part
          this phase exists to fix — carries no such staleness.
    */

    /// <summary>
    /// The storage commitment seam: what may be committed, and the recording of what was.
    /// </summary>
    internal static class StorageCommitments
    {
        /*
            ──────────────────────────────────────────────
                             Scan budget
            ──────────────────────────────────────────────
        */

        /// <summary>
        /// How many cells one measurement may LOOK at — a scan budget, not a group-size cutoff. A group that
        /// genuinely has room reaches <see cref="EnoughFor"/> within a couple of dozen acceptable cells, so
        /// this only binds on a large group that is nearly full, where the total is a deliberate
        /// UNDER-estimate reported through <see cref="GroupSpace.Truncated"/> so that neither the gate nor
        /// the counter turns an incomplete look into a refusal.
        ///
        /// <para>→ NOTE: kept at the 200 the per-plan scan has always used, deliberately. The design called
        /// for raising it "because the reading is now amortised across every asker" — but the memo below is
        /// keyed on the PAWN and the THING as well as the group (it has to be: IsGoodStoreCell answers per
        /// carrier and per stack), so it is not amortised across askers at all. Raising it would have
        /// tripled the worst-case work-scan cost on a mod that already has microstutter reports, in exchange
        /// for a truncation case both readers now handle correctly anyway.</para>
        /// </summary>
        private const int MaxSpaceScanCells = 200;

        /*
            ──────────────────────────────────────────────
                        Hot-path gate + scopes
            ──────────────────────────────────────────────
        */

        /// <summary>Whether ANY commitment is live. The first thing the per-cell gate asks: one array
        /// length read, so with nothing in flight the whole seam costs nothing.</summary>
        internal static bool AnyClaims => StorageClaimLedger.AnyRows(HaulersDreamGameComponent.storageClaims);

        // Set for the duration of a cell measurement. The measurement calls IsGoodStoreCell, which the gate
        // adapter patches, which calls FreeUnitsFor, which measures — so without this the first query
        // recurses until the stack runs out. [ThreadStatic] because a threading mod may fan the work scan
        // onto worker threads and one shared flag would make two scans hide each other's gate.
        [ThreadStatic] private static bool insideSpaceScan;

        // Depth of the "the player explicitly ordered this haul" scope opened around
        // HaulAIUtility.HaulToStorageJob(..., forced: true). A count rather than a bool so a nested probe
        // cannot close an outer scope early.
        [ThreadStatic] private static int forcedOrderDepth;

        /// <summary>True while a group is being measured — the gate MUST stand down, or measuring it would
        /// require measuring it.</summary>
        internal static bool InsideSpaceScan => insideSpaceScan;

        /// <summary>True while an explicitly player-ordered haul is being built. Both adapters stand down:
        /// a click is the player overriding the standing arbitration, matching HD's existing "forced
        /// overrides the toggle" convention, and a refused order would read as the mod ignoring them.</summary>
        internal static bool InForcedOrder => forcedOrderDepth > 0;

        /// <summary>Open the forced-order scope.</summary>
        internal static void PushForcedOrder() => forcedOrderDepth++;

        /// <summary>Close the forced-order scope. Clamped at zero so an unbalanced close cannot make the
        /// scope permanently "open" by going negative.</summary>
        internal static void PopForcedOrder()
        {
            if (forcedOrderDepth > 0)
                forcedOrderDepth--;
        }

        /*
            ──────────────────────────────────────────────
                       The bind tripwire's off switch
            ──────────────────────────────────────────────
            The strip and its replacement are SEPARATE Harmony patch classes, applied by a loop that catches
            per-class failures (HaulersDreamMod.ApplyPatchesResilient). That resilience is right for an
            optional feature and wrong here: if the two adapters fail to bind while the strip binds, HD
            removes vanilla's destination reservation and puts nothing in its place — the exact bug this
            phase exists to end, shipped inert and visible only in a log line nobody reads.

            → KEY: so the strip does not get to run without its replacement. HaulersDreamMod.VerifyStorageSeam
              checks all three targets at startup and calls Disable() the moment one is unaccounted for; the
              whole seam then stands down and vanilla's own arbitration is back in force, unmodified.
        */

        // Latched at startup by the bind tripwire; never cleared, because a target that did not bind at
        // startup will not bind later in the session. Plain static, not [ThreadStatic]: it is written once on
        // the main thread during mod construction, long before any work scan can read it.
        private static bool seamDisabled;

        /// <summary>
        /// Stand the whole seam down for this session — no commits, no janitor, no adapter narrowing, and
        /// therefore no reservation strip either (<see cref="TryCommit"/> answers false, so
        /// <c>Patch_JobDriver_HaulToCell_NoCellReservation</c> falls through to vanilla).
        ///
        /// <para>ONE switch rather than a check per entry point, because a partial stand-down is worse than
        /// either extreme: a bound COUNTER with no GATE clamps counts at a destination nobody is being kept
        /// away from, and vanilla answers a count of 0 with a red "Invalid count: 0, setting to 1".</para>
        /// </summary>
        internal static void Disable() => seamDisabled = true;

        /// <summary>Whether the seam should arbitrate at all on this map: the bind tripwire, the mod's master
        /// switch and the per-map gate, exactly as every other HD entry point reads the latter two. With HD
        /// inert on a map, vanilla's own arbitration is the only one in play and must not be
        /// second-guessed.</summary>
        /// <param name="map">The map in question; null reads as inert.</param>
        /// <returns>True when HD may arbitrate storage here.</returns>
        internal static bool ActiveOn(Map map)
            => map != null && !seamDisabled && MasterEnable.Active && MapGate.HdActiveOnMap(map);

        /// <summary>
        /// Whether the two Harmony adapters may narrow VANILLA's own storage answers on this map.
        ///
        /// <para>Stricter than <see cref="ActiveOn"/> by the Haul to Stack switch, and that difference is the
        /// whole point. Sharing a destination cell between haulers IS that feature; with it off, HD leaves
        /// vanilla's cell reservation in place (see <c>Patch_JobDriver_HaulToCell_NoCellReservation</c>) and
        /// vanilla's own arbitration is the one in force — so second-guessing its counts and hiding its cells
        /// would be a behaviour change from a switch the player turned OFF.</para>
        ///
        /// <para>The ledger itself keeps filling either way, because HD's bulk sweep pockets cargo without
        /// taking any destination reservation at all whatever this switch says. HD's OWN planner therefore
        /// still prices against it (<c>BulkHaul</c> calls <see cref="FreeUnitsFor"/> directly); only vanilla's
        /// answers are left alone.</para>
        /// </summary>
        /// <param name="map">The map in question; null reads as inert.</param>
        /// <returns>True when the adapters may act.</returns>
        internal static bool GatesVanillaStorage(Map map)
            => ActiveOn(map) && HaulersDreamMod.Settings?.haulToStack == true;

        /*
            ──────────────────────────────────────────────
                          Reading: how much is free
            ──────────────────────────────────────────────
        */

        /// <summary>
        /// Units of <paramref name="def"/> <paramref name="asker"/> may still send to <paramref name="group"/>.
        /// </summary>
        /// <param name="asker">The pawn asking. Its reachability and allowed area shape the measurement, and
        /// its own claims are treated per the possession test below.</param>
        /// <param name="group">The destination's budget identity (<see cref="BulkHaul.BudgetGroupOf"/>).</param>
        /// <param name="def">The def being delivered.</param>
        /// <param name="subject">The stack in question. Doubles as the possession test's subject — whether
        /// the pawn is PLANNING a pickup or DELIVERING cargo it already holds is derived from this, never
        /// passed as a flag, so no caller can forget to set it and no caller can set it wrong.</param>
        /// <returns>Units still free, or <see cref="int.MaxValue"/> when the destination could not be
        /// measured at all. Unknown must stay unknown: reading it as "full" would stop the colony hauling
        /// to any stockpile HD cannot price, which is a worse bug than the one this fixes.</returns>
        internal static int FreeUnitsFor(Pawn asker, ISlotGroup group, ThingDef def, Thing subject)
            => FreeUnitsFor(asker, group, def, subject, out _);

        /// <summary>
        /// <see cref="FreeUnitsFor(Pawn,ISlotGroup,ThingDef,Thing)"/>, additionally reporting whether the
        /// measurement ran out of scan budget.
        /// </summary>
        /// <param name="asker">The pawn asking.</param>
        /// <param name="group">The destination's budget identity.</param>
        /// <param name="def">The def being delivered.</param>
        /// <param name="subject">The stack in question, and the possession test's subject.</param>
        /// <param name="observationTruncated">True when the cell scan stopped on its budget before it had
        /// seen the whole group, which makes the returned figure a conservative UNDER-estimate. A caller
        /// that would REFUSE on the strength of the number must not refuse on a truncated one.</param>
        /// <returns>Units still free, or <see cref="int.MaxValue"/> when the destination is unmeasurable.</returns>
        internal static int FreeUnitsFor(
            Pawn asker, ISlotGroup group, ThingDef def, Thing subject, out bool observationTruncated)
        {
            observationTruncated = false;
            var map = asker?.Map;
            if (map == null || group == null || def == null || subject == null)
                return int.MaxValue;

            int raw = RawSpaceFor(asker, group, def, subject, map, out observationTruncated);

            var rows = HaulersDreamGameComponent.storageClaims;
            bool delivering = IsDelivering(asker, subject);
            int others = StorageClaimLedger.ClaimedByOthers(rows, group, def, asker, Evidence);
            int mine = delivering ? 0 : StorageClaimLedger.ClaimedByPawn(rows, group, def, asker, Evidence);

            // Desire int.MaxValue turns the shared production rule into "what is left" without a second
            // spelling of the subtraction: Commit returns min(Desire, free), and free itself when the
            // destination is unmeasured. The concurrency harness grades this exact function.
            var sight = new HaulSight(asker.thingIDNumber, TickNow, raw, others, mine, int.MaxValue);
            return StorageCommitPolicy.Commit(sight, delivering);
        }

        /// <summary>
        /// Is <paramref name="subject"/> already in <paramref name="asker"/>'s possession? THE structural
        /// answer to exclude-self, derived rather than declared.
        ///
        /// <para>NO — the pawn is PLANNING a fresh pickup, so its own in-flight load counts against it: that
        /// load is going to land in the very space it is now pricing, and excluding it is how a pawn ends up
        /// planning the same units twice.</para>
        ///
        /// <para>YES — the pawn is DELIVERING, so its own claim does not count against it: it is asking
        /// where to put cargo it already reserved room for. This is also the anti-churn guarantee — a pawn
        /// holding goods can always find a home, so the ledger can never strand a load or force a carry-back,
        /// which is the exact failure the reports describe.</para>
        /// </summary>
        /// <param name="asker">The pawn asking.</param>
        /// <param name="subject">The stack in question.</param>
        /// <returns>True when the pawn is carrying it in hands or holding it in inventory.</returns>
        private static bool IsDelivering(Pawn asker, Thing subject)
        {
            if (asker == null || subject == null)
                return false;
            if (asker.carryTracker?.CarriedThing == subject)
                return true;
            var inventory = asker.inventory?.innerContainer;
            return inventory != null && !subject.Spawned && subject.ParentHolder == inventory;
        }

        /*
            ──────────────────────────────────────────────
                       Writing: recording a claim
            ──────────────────────────────────────────────
        */

        /// <summary>
        /// Record that <paramref name="pawn"/> is bringing <paramref name="units"/> of
        /// <paramref name="def"/> to <paramref name="group"/>, replacing whatever it previously claimed for
        /// that def. Non-positive units, or a null group, retire the claim instead.
        /// </summary>
        /// <param name="pawn">The committing pawn.</param>
        /// <param name="group">The destination's budget identity.</param>
        /// <param name="def">The def being delivered.</param>
        /// <param name="units">Units promised.</param>
        /// <param name="path">Which code path decided, for the decision trace.</param>
        internal static void Commit(Pawn pawn, ISlotGroup group, ThingDef def, int units, string path)
        {
            var rows = HaulersDreamGameComponent.storageClaims;
            var next = StorageClaimLedger.Add(rows, pawn, group, def, units);
            if (!ReferenceEquals(next, rows))
                HaulersDreamGameComponent.SetStorageClaims(next);
            Trace(path, pawn, group, def, units);
        }

        /// <summary>
        /// Record a claim and report whether the seam actually took responsibility for the destination.
        ///
        /// <para>This is what makes "strip vanilla's cell reservation without arbitrating" inexpressible:
        /// the reservation may only be skipped when this returned TRUE, so a destination the ledger cannot
        /// arbitrate (a container, a cell with no slot group, a map HD is inert on) keeps vanilla's own
        /// reservation and behaves exactly as the base game does.</para>
        /// </summary>
        /// <param name="pawn">The committing pawn.</param>
        /// <param name="group">The destination's budget identity; null means "not ours to arbitrate".</param>
        /// <param name="def">The def being delivered.</param>
        /// <param name="units">Units promised.</param>
        /// <param name="path">Which code path decided, for the decision trace.</param>
        /// <returns>True when the claim was recorded and the ledger now arbitrates this destination.</returns>
        internal static bool TryCommit(Pawn pawn, ISlotGroup group, ThingDef def, int units, string path)
        {
            if (pawn == null || group == null || def == null || units <= 0 || !ActiveOn(pawn.Map))
                return false;
            // The Haul to Stack switch is read HERE, once, rather than at each caller. Sharing a storage
            // cell between haulers is that feature; with it off, vanilla's own destination reservation is
            // the arbitration in force and this ledger must not offer to replace it. Every caller then asks
            // exactly ONE question — "did the ledger take this on?" — which is what let three hand-written
            // copies of an unstackable carve-out disappear instead of being kept in step by hand.
            if (HaulersDreamMod.Settings?.haulToStack != true)
                return false;
            Commit(pawn, group, def, units, path);
            return true;
        }

        /// <summary>
        /// Forget only what <paramref name="pawn"/> promised for ONE def, leaving its claims for anything
        /// else it is carrying intact.
        ///
        /// <para>→ NOTE: there is deliberately no whole-pawn Release, and no <c>Pawn.DeSpawn</c>/<c>Kill</c>
        /// hook to call one from. A dead, downed or departed pawn has no map, so
        /// <see cref="StorageEvidence"/> reports nothing for it, and every row it holds is worth zero on the
        /// very next read — the janitor then drops the rows themselves. Two Harmony patches to shorten an
        /// array a little sooner would be all cost and no correctness.</para>
        /// </summary>
        /// <param name="pawn">The pawn whose claim is dropped.</param>
        /// <param name="def">The def to forget.</param>
        internal static void DropClaim(Pawn pawn, ThingDef def)
        {
            var rows = HaulersDreamGameComponent.storageClaims;
            var next = StorageClaimLedger.Add(rows, pawn, null, def, 0);
            if (!ReferenceEquals(next, rows))
                HaulersDreamGameComponent.SetStorageClaims(next);
        }

        /// <summary>
        /// Stand every other committer down from <paramref name="group"/> so an explicit player order can
        /// take the space. Mirrors vanilla's own <c>ThingCountTracker.InterruptEnroutePawns</c>: walk the
        /// other committers, drop each claim, and end a haul aimed there with
        /// <see cref="JobCondition.InterruptForced"/> so the pawn immediately picks something else.
        ///
        /// <para>Only PLANNING hauls are ended. An unload in progress is left alone: its cargo is already in
        /// the pawn's pockets, so ending it would strand a load to make room for one the player asked for —
        /// trading the reported bug for a worse one.</para>
        /// </summary>
        /// <param name="group">The contested destination.</param>
        /// <param name="def">The contested def.</param>
        /// <param name="exclude">The forcing pawn, left untouched.</param>
        internal static void InterruptCommittersTo(ISlotGroup group, ThingDef def, Pawn exclude)
        {
            if (group == null || def == null)
                return;
            var rows = HaulersDreamGameComponent.storageClaims;
            if (rows.Length == 0)
                return;

            // Ordered by thingIDNumber so every multiplayer client interrupts the same pawns in the same
            // order. The rows array cannot be trusted for that: its layout follows the order claims were
            // written, which is job-start order for most rows and the JANITOR's adoption order for the rest —
            // and the janitor derives its own from a HashSet walk, which is why it has to sort too. Ending
            // jobs is the loudest side effect in the seam, so it gets the same world-derived order the
            // janitor now uses.
            var victims = InterruptBuffer;
            victims.Clear();
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (!ReferenceEquals(row.Group, group) || !ReferenceEquals(row.Def, def))
                    continue;
                if (!(row.Pawn is Pawn p) || p == exclude)
                    continue;
                victims.Add(p);
            }
            if (victims.Count == 0)
                return;
            victims.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

            for (int i = 0; i < victims.Count; i++)
            {
                var p = victims[i];
                var job = p.CurJob;
                // → GOTCHA: the claim is dropped ONLY for a pawn whose job is actually ended, and the two
                //   must stay in the same branch. Releasing first and then declining to interrupt leaves a
                //   pawn walking with a full load and no claim — the released-before-its-cargo-lands breach
                //   that AClaimReleasedBeforeItsCargoLands_BreaksTheInvariantAtTheDeposit exists to pin. A
                //   pawn mid-UNLOAD is exactly that case: its cargo is already in its pockets, so it keeps
                //   both its job and its claim, and the forced order takes what is left.
                if (job == null
                    || (job.def != JobDefOf.HaulToCell && job.def != HaulersDreamDefOf.HaulersDream_BulkHaul))
                    continue;
                // Scoped to the contested def, not the whole pawn: a hauler interrupted over steel may still
                // be carrying wood somewhere, and forgetting that would hand its wood's destination away too.
                DropClaim(p, def);
                HDLog.Dbg($"storage-commit [forced] {exclude?.LabelShort} took {def?.defName} at "
                          + $"{GroupLabel(group)}; interrupting {p.LabelShort}'s {job.def.defName}.");
                p.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            victims.Clear();
        }

        // Reused buffer for the forced-order interrupt. Main thread only (a forced order is a player click
        // going through the job tracker), so no [ThreadStatic] is needed and one shared list is enough.
        private static readonly List<Pawn> InterruptBuffer = new List<Pawn>();

        /*
            ──────────────────────────────────────────────
                       The janitor (self-heal)
            ──────────────────────────────────────────────
            Correctness never depends on this — every read already clamps a row to live evidence. It keeps
            the array small (so AnyClaims stays an honest gate) and ADOPTS loads that entered the world
            without passing the seam: another mod's haul job, or a save made before this version.
        */

        /// <summary>
        /// Reconcile the ledger against live pawn state on <paramref name="map"/>: drop rows whose pawn has
        /// nothing left or whose group has gone, and adopt a hauling pawn that holds cargo but has no row.
        ///
        /// <para>→ GOTCHA: MULTIPLAYER. This is the one adoption SEQUENCE in the seam, and every
        /// <see cref="Commit"/> below changes what the next probe answers — a fresh row spends empty cells
        /// through <see cref="SpendCrossDefClaims"/> and units through
        /// <see cref="StorageClaimLedger.ClaimedByPawn"/>. So "which pawn, then which of its defs" is game
        /// state, not presentation, and both loops must be walked in an order derived from the WORLD rather
        /// than from anyone's collection layout: pawns by <c>thingIDNumber</c>, cargo by <c>defName</c>.
        /// Neither source is safe as it comes — <c>SpawnedPawnsInFaction</c> is a registration-ordered list
        /// and <see cref="StorageEvidence.Collect"/>'s output follows a <c>HashSet</c> walk, and a host and a
        /// mid-game joiner arrive at both from different histories. The mass adoption after a save load is
        /// precisely when this runs over a whole colony at once.</para>
        /// </summary>
        /// <param name="map">The map to sweep.</param>
        internal static void RunJanitor(Map map)
        {
            if (!ActiveOn(map))
                return;
            var player = Faction.OfPlayerSilentFail;
            if (player == null)
                return;

            var rows = StorageClaimLedger.Reconcile(HaulersDreamGameComponent.storageClaims, Evidence, GroupIsLive);
            if (!ReferenceEquals(rows, HaulersDreamGameComponent.storageClaims))
                HaulersDreamGameComponent.SetStorageClaims(rows);

            // Copied out before sorting: SpawnedPawnsInFaction hands back MapPawns' OWN live list, and
            // reordering that would rewrite vanilla's registration order for every other reader on the map.
            var pawns = janitorPawns ?? (janitorPawns = new List<Pawn>());
            pawns.Clear();
            var spawned = map.mapPawns.SpawnedPawnsInFaction(player);
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null)
                    pawns.Add(spawned[i]);
            pawns.Sort(ByThingId);

            var cargo = janitorCargo ?? (janitorCargo = new List<StorageEvidence.PawnCargo>());
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                StorageEvidence.Collect(p, cargo);
                // Skipped for the single-def pawn that most colonists are: List.Sort wraps a Comparison in a
                // freshly allocated comparer on net48, so this stays off the common path entirely.
                if (cargo.Count > 1)
                    cargo.Sort(StorageEvidence.ByDefName);
                for (int c = 0; c < cargo.Count; c++)
                {
                    var entry = cargo[c];
                    if (entry.def == null || entry.units <= 0)
                        continue;
                    // Already accounted for: a live row is the pawn's own statement of intent and outranks
                    // anything a fresh probe would guess.
                    if (HasRowFor(p, entry.def))
                        continue;
                    var group = entry.knownGroup ?? StorageEvidence.DestinationGroupFor(map, p, entry.sample);
                    if (group == null)
                        continue; // nowhere to go, so it is in flight to nowhere
                    Commit(p, group, entry.def, entry.units, "adopt");
                }
                cargo.Clear();
            }
            pawns.Clear(); // holds live Pawn references; nothing reads it between sweeps
        }

        // The janitor's OWN cargo buffer, deliberately separate from the evidence path's: the adoption probe
        // re-enters IsGoodStoreCell -> the gate -> evidence, and a single shared buffer would be cleared out
        // from under the loop that is still walking it.
        [ThreadStatic] private static List<StorageEvidence.PawnCargo> janitorCargo;

        // The janitor's sorted copy of the map's player pawns. [ThreadStatic] to match janitorCargo beside it
        // — the two are filled and walked together, so a threading mod that ever drove this off the main
        // thread must not have one of them shared and the other not.
        [ThreadStatic] private static List<Pawn> janitorPawns;

        /// <summary>The pawn order the janitor adopts in: ascending <c>thingIDNumber</c>, the same
        /// world-derived tiebreak <see cref="InterruptCommittersTo"/> uses. Cached rather than written as a
        /// lambda at the call site, because each method-group or lambda conversion allocates.</summary>
        private static readonly Comparison<Pawn> ByThingId =
            (a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber);

        /// <summary>Whether the ledger already holds a row for this pawn and def, whatever group it names.</summary>
        /// <param name="pawn">The pawn.</param>
        /// <param name="def">The def.</param>
        /// <returns>True when a row exists.</returns>
        private static bool HasRowFor(Pawn pawn, ThingDef def)
        {
            var rows = HaulersDreamGameComponent.storageClaims;
            for (int i = 0; i < rows.Length; i++)
                if (ReferenceEquals(rows[i].Pawn, pawn) && ReferenceEquals(rows[i].Def, def))
                    return true;
            return false;
        }

        /// <summary>Whether a destination still exists. A stockpile that was deleted, or a shelf that was
        /// deconstructed, has no cells left — which is both the cheapest test and the only one that works
        /// uniformly for a plain slot group and a linked storage group.</summary>
        /// <param name="group">The group under test.</param>
        /// <returns>True when it still has cells.</returns>
        private static bool GroupIsLive(object group)
            => group is ISlotGroup slot && slot.CellsList != null && slot.CellsList.Count > 0;

        /*
            ──────────────────────────────────────────────
                    Evidence: how much a pawn is moving
            ──────────────────────────────────────────────
        */

        /// <summary>The evidence source every ledger read clamps against. A cached delegate instance, so a
        /// per-cell gate query allocates no closure.</summary>
        private static readonly StorageClaimEvidence Evidence = (pawn, def) => UnitsMoving(pawn as Pawn, def as ThingDef);

        // Per-(pawn, def) evidence memo. Keyed on the tick AND on the ledger's write generation: a
        // commitment made mid-tick must invalidate any evidence figure taken before it, or a pawn that
        // committed after another pawn's evidence pass would be read as carrying nothing and its claim would
        // silently vanish — the same-tick blindness this whole seam exists to remove.
        [ThreadStatic] private static Dictionary<(Pawn pawn, ThingDef def), int> evidenceMemo;
        [ThreadStatic] private static int evidenceMemoTick;
        [ThreadStatic] private static int evidenceMemoGeneration;
        [ThreadStatic] private static List<StorageEvidence.PawnCargo> evidenceCargo;

        /// <summary>
        /// Units of <paramref name="def"/> <paramref name="pawn"/> is visibly moving right now.
        ///
        /// <para>Exposed so a caller that knows only ONE stack's worth can still book its whole load. A
        /// pawn placing 75 of the 200 steel in its pockets is bringing 200 to that group, and recording the
        /// 75 would quietly hand the other 125 units of room to somebody else — the reported bug in
        /// miniature, at the one moment the cargo is provably real.</para>
        /// </summary>
        /// <param name="pawn">The pawn to weigh; null carries nothing.</param>
        /// <param name="def">The def in question.</param>
        /// <returns>Units in hands, tagged inventory surplus, or pickup queue.</returns>
        internal static int UnitsMovingOf(Pawn pawn, ThingDef def) => UnitsMoving(pawn, def);

        /// <summary>Units of <paramref name="def"/> <paramref name="pawn"/> can be seen to be moving.</summary>
        /// <param name="pawn">The pawn to weigh; null carries nothing.</param>
        /// <param name="def">The def in question.</param>
        /// <returns>Units in hands, tagged inventory surplus, or pickup queue.</returns>
        private static int UnitsMoving(Pawn pawn, ThingDef def)
        {
            if (pawn == null || def == null)
                return 0;

            int tick = TickNow;
            var memo = evidenceMemo ?? (evidenceMemo = new Dictionary<(Pawn, ThingDef), int>());
            if (tick != evidenceMemoTick
                || evidenceMemoGeneration != HaulersDreamGameComponent.storageClaimGeneration)
            {
                memo.Clear();
                evidenceMemoTick = tick;
                evidenceMemoGeneration = HaulersDreamGameComponent.storageClaimGeneration;
            }
            else if (tick != -1 && memo.TryGetValue((pawn, def), out int cached))
            {
                return cached;
            }

            int units = StorageEvidence.UnitsOf(pawn, def,
                evidenceCargo ?? (evidenceCargo = new List<StorageEvidence.PawnCargo>()));
            // Never memoise against the no-clock sentinel: a quickload can land on an equal tick number, and
            // an entry keyed on -1 would then serve a previous session's pawn (this is the claim TickNow's
            // own doc makes, so it has to be true here as well as in MeasureGroup).
            if (tick != -1)
                memo[(pawn, def)] = units;
            return units;
        }

        /*
            ──────────────────────────────────────────────
                   Raw space: the ONE capacity oracle
            ──────────────────────────────────────────────
            The only place in this assembly that asks a cell how much of a def it can still take. A second
            copy anywhere would be a second, unarbitrated answer to the question this whole file exists to
            make single — which is why the build guard pins it to this file.
        */

        /// <summary>
        /// One group's remaining capacity, split the way a storage group actually behaves: a shared pool of
        /// empty cells (any def may open one) plus per-def top-up room in cells that already hold that def.
        /// </summary>
        internal readonly struct GroupSpace
        {
            /// <summary>Empty, acceptable cells — the pool every def competes for.</summary>
            public readonly int EmptyCells;

            /// <summary>Units of the measured def that fit in cells ALREADY holding it.</summary>
            public readonly int PartialSpace;

            /// <summary>Units of the measured def one empty cell holds: its stack limit for vanilla storage,
            /// more for a deep-storage cell (the figure comes through <c>Building.MaxItemsInCell</c>, which
            /// is where LWM Deep Storage and Adaptive Storage put their per-cell capacity).</summary>
            public readonly int PerCellCapacity;

            /// <summary>No binding limit was measured: the group has no cell grid at all, or it was already
            /// proven roomier than <see cref="ObservedFloor"/> before the walk finished.</summary>
            public readonly bool Unbounded;

            /// <summary>The scan budget ran out before the whole group had been looked at, so every figure
            /// here is a conservative UNDER-estimate. A caller that would REFUSE on the number must not.</summary>
            public readonly bool Truncated;

            /// <summary>When <see cref="Unbounded"/>: units the group was PROVEN to have free before the
            /// walk stopped. A real number, not infinity — which is what lets the invariant mean something
            /// even for a group too roomy to finish measuring. <see cref="int.MaxValue"/> only when the
            /// group could not be measured at all.</summary>
            public readonly int ObservedFloor;

            /// <summary>Record a measurement.</summary>
            /// <param name="emptyCells">Empty acceptable cells.</param>
            /// <param name="partialSpace">Top-up room for the measured def.</param>
            /// <param name="perCellCapacity">Units of the measured def one empty cell holds.</param>
            /// <param name="unbounded">Whether no binding limit was measured.</param>
            /// <param name="truncated">Whether the scan budget ran out first.</param>
            /// <param name="observedFloor">Units proven free when unbounded.</param>
            public GroupSpace(int emptyCells, int partialSpace, int perCellCapacity, bool unbounded,
                bool truncated, int observedFloor)
            {
                EmptyCells = emptyCells;
                PartialSpace = partialSpace;
                PerCellCapacity = perCellCapacity;
                Unbounded = unbounded;
                Truncated = truncated;
                ObservedFloor = observedFloor;
            }
        }

        /// <summary>Units of a def that make a group "roomier than any one plan could fill" — the point the
        /// walk stops early. A whole bulk sweep is bounded at <see cref="BulkHaul.MaxStacks"/> stacks, so
        /// past this no single plan can bind, and the figure doubles as the honest floor the invariant is
        /// measured against for such a group.</summary>
        /// <param name="def">The def being measured.</param>
        /// <returns>Units, saturating (a modded stack limit cannot overflow it).</returns>
        private static int EnoughFor(ThingDef def)
        {
            long enough = (long)BulkHaul.MaxStacks * Math.Max(1, def?.stackLimit ?? 1);
            return enough >= int.MaxValue ? int.MaxValue - 1 : (int)enough;
        }

        /// <summary>The current game tick, or -1 when there is no clock (briefly, across a load). -1 is
        /// never memoised against, so a cross-session quickload landing on an equal tick number cannot serve
        /// a previous session's reading.</summary>
        private static int TickNow => Find.TickManager?.TicksGame ?? -1;

        /// <summary>The storage-building filter that applies right now, or null when it does not. ONE
        /// derivation, shared with <see cref="BulkHaul"/>, so the plan budget and the seam can never
        /// disagree about whether a building is allowed.</summary>
        /// <returns>The active filter, or null for allow-all.</returns>
        internal static StorageBuildingFilter ActiveFilter()
        {
            bool active = StorageBuildingFilter.Enabled
                          && StorageBuildingFilter.CurrentContext != StorageFilterContext.Unload;
            return active ? HaulersDreamMod.Settings?.storageBuildingFilter : null;
        }

        // Per-(tick, group, thing, pawn, filtered) measurement memo. The gate runs PER CELL, so without this
        // pricing a group would walk that group once per cell — quadratic, on the hottest method in the haul
        // system. Keyed on the THING rather than its def because IsGoodStoreCell answers per stack (a
        // stockpile's quality/hit-points filter can accept one steel stack and refuse another), and on the
        // PAWN because it answers per carrier (allowed area, reachability, that pawn's own reservations).
        // [ThreadStatic] per this assembly's convention for hook-reachable scratch (see BulkHaul.planCache).
        [ThreadStatic] private static int spaceMemoTick;
        [ThreadStatic] private static Dictionary<(object group, Thing thing, Pawn pawn, bool filtered), GroupSpace> spaceMemo;

        // Reused budget for the cross-def arithmetic below — one instance per thread instead of an
        // allocation per gate query. Safe to share: nothing between Reset and AvailableFor can re-enter this
        // method (the cell measurement happens BEFORE the reset, and the claim sums only read pawn state).
        [ThreadStatic] private static StorageGroupBudget crossDefBudget;

        // Self-register the per-session clears with the game-load hygiene sweep (see CacheRegistry): the
        // memo holds Thing/Pawn/SlotGroup references, so an equal tick number across a quickload could
        // otherwise serve a previous session's objects. The static ctor runs once, on first touch.
        static StorageCommitments() => CacheRegistry.Register(ClearCaches);

        /// <summary>Drop this thread's measurement memo and the ledger itself — game-load hygiene
        /// (<see cref="CacheRegistry"/>). The ledger goes too: it is derived state that a fresh session
        /// rebuilds on the janitor's first pass, and its rows hold the previous session's pawns.</summary>
        private static void ClearCaches()
        {
            spaceMemo?.Clear();
            spaceMemoTick = -1;
            evidenceMemo?.Clear();
            evidenceMemoTick = -1;
            evidenceCargo?.Clear();
            janitorCargo?.Clear();
            janitorPawns?.Clear();
            HaulersDreamGameComponent.ClearStorageClaims();
        }

        /// <summary>
        /// The group's remaining room for this def, with the cells other DEFS have already spoken for taken
        /// out of the shared empty-cell pool. Claims for the SAME def are not subtracted here — that is the
        /// decision rule's job, and doing it twice would halve every allowance.
        /// </summary>
        /// <param name="asker">The pawn whose reachability and allowed area the measurement is taken for.</param>
        /// <param name="group">The destination's budget identity.</param>
        /// <param name="def">The def being priced.</param>
        /// <param name="subject">The stack being placed; what <c>IsGoodStoreCell</c> is asked about.</param>
        /// <param name="map">The map the group is on.</param>
        /// <param name="truncated">Set when the scan budget ran out, making the figure an under-estimate.</param>
        /// <returns>Units of the def the group can still take, or <see cref="int.MaxValue"/> when it could
        /// not be measured.</returns>
        private static int RawSpaceFor(
            Pawn asker, ISlotGroup group, ThingDef def, Thing subject, Map map, out bool truncated)
        {
            var space = MeasureGroup(asker, subject, group, map);
            truncated = space.Truncated;
            // → NOTE: an unbounded group skips the cross-def subtraction below, deliberately. "Unbounded"
            //   means the walk PROVED at least MaxStacks full stacks of room before it stopped, so there is
            //   space for every def contending at once and taking cells off that floor would only invent a
            //   scarcity the group does not have. Same-def claims are still subtracted, by the decision rule.
            if (space.Unbounded)
                return space.ObservedFloor;

            var budget = crossDefBudget ?? (crossDefBudget = new StorageGroupBudget(0));
            budget.Reset(space.EmptyCells);
            budget.PriceDef(def, space.PartialSpace, space.PerCellCapacity);
            SpendCrossDefClaims(budget, group, def);
            return budget.AvailableFor(def);
        }

        /// <summary>
        /// Take the empty cells that OTHER defs' live claims will occupy out of the shared pool — issue
        /// #138's cross-def contention, in its cross-PAWN form. One empty cell counts as a full stack limit
        /// for EVERY def, so without this a pawn committing steel and a pawn committing wood both read "one
        /// cell free" and only one of them is right.
        ///
        /// <para>Each foreign claim is charged CONSERVATIVELY: whole empty cells at that def's vanilla stack
        /// limit, with no credit for top-up room it might find in a partial stack of its own. Pricing those
        /// exactly would mean a second full cell walk per contending def, on the per-cell gate path, to
        /// shave a claim that is about to be spent anyway.</para>
        ///
        /// <para>→ NOTE: the conservative direction is under-commit, and it self-clears — the foreign hauler
        /// deposits, its claim drops, and the cell is offered again on the next query. The opposite error is
        /// the reported bug.</para>
        ///
        /// <para>→ NOTE: this loop LOOKS like the janitor's order-sensitive one — it walks a collection
        /// spending a shared budget as it goes — and is not. Each foreign def's charge is computed from its
        /// own claim and stack limit alone, and the only shared quantity it touches is the empty-cell count,
        /// which ends at <c>max(0, initial - SUM(charges))</c> whichever order the charges are applied in (the
        /// clamp saturates at zero and stays there). The leftover it hands back is written to the FOREIGN
        /// def's partial room, which the answer for <paramref name="skip"/> never reads. Verified before this
        /// loop was left unsorted, not assumed.</para>
        /// </summary>
        /// <param name="budget">The budget to spend from; already priced for the asker's own def.</param>
        /// <param name="group">The destination's budget identity.</param>
        /// <param name="skip">The asker's own def, whose claims the decision rule subtracts instead.</param>
        private static void SpendCrossDefClaims(StorageGroupBudget budget, ISlotGroup group, ThingDef skip)
        {
            var rows = HaulersDreamGameComponent.storageClaims;
            for (int i = 0; i < rows.Length; i++)
            {
                if (!(rows[i].Def is ThingDef other) || other == skip || !ReferenceEquals(rows[i].Group, group))
                    continue;
                // Several pawns may claim one def; price it once, on the first row that names it.
                if (budget.IsPriced(other))
                    continue;
                int claimed = StorageClaimLedger.ClaimedTotal(rows, group, other, Evidence);
                if (claimed <= 0)
                    continue;
                budget.PriceDef(other, 0, Math.Max(1, other.stackLimit));
                budget.Consume(other, claimed);
            }
        }

        /// <summary>
        /// Walk a storage group's cells and split its remaining room for one stack's def into the shared
        /// empty-cell pool and that def's own top-up room. Memoised per (tick, group, thing, pawn, filter).
        ///
        /// <para>STORAGE-MOD COMPATIBILITY BY CONSTRUCTION (no references, no reflection — verified against
        /// the LWM Deep Storage / KanbanStockpile / SatisfiedStorage / Adaptive Storage Framework sources):
        /// ACCEPTANCE runs through <c>IsGoodStoreCell</c>, hence <c>NoStorageBlockersIn</c>, which every one
        /// of those mods patches, so a cell they call full is skipped here. RAW PER-CELL CAPACITY runs
        /// through <c>GetItemStackSpaceLeftFor</c> -> <c>Building.MaxItemsInCell</c>, the single seam
        /// vanilla's <c>maxItemsInCell</c>, LWM's <c>CompDeepStorage.MaxNumberStacks</c> and ASF's per-cell
        /// limit all funnel through. The only residual is a numeric OVER-estimate for caps that sit off that
        /// seam (Kanban's max-similar-stacks, SatisfiedStorage's fill line, LWM's mass-limited shelves) —
        /// a safe upper bound that the deposit re-gate corrects with bounded churn and never a black hole.</para>
        ///
        /// <para>→ GOTCHA: do NOT "tighten" this with <c>HaulToCellStorageJob</c>'s own count. That count is
        /// per THING (capped at one stack), so folding it in would cap a whole bulk sweep at a single armful.</para>
        /// </summary>
        /// <param name="pawn">The carrier the cells are judged for.</param>
        /// <param name="thing">The stack being placed.</param>
        /// <param name="group">The group to measure.</param>
        /// <param name="map">The map it is on.</param>
        /// <returns>The measurement.</returns>
        internal static GroupSpace MeasureGroup(Pawn pawn, Thing thing, ISlotGroup group, Map map)
        {
            var filter = ActiveFilter();
            int tick = TickNow;
            var memo = spaceMemo ?? (spaceMemo = new Dictionary<(object, Thing, Pawn, bool), GroupSpace>());
            var key = ((object)group, thing, pawn, filter != null);
            if (tick != -1)
            {
                if (tick != spaceMemoTick)
                {
                    memo.Clear();
                    spaceMemoTick = tick;
                }
                else if (memo.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            var measured = MeasureGroupUncached(pawn, thing, group, map, filter);
            if (tick != -1)
                memo[key] = measured;
            return measured;
        }

        /// <summary>The cell walk itself. Sets the re-entrancy flag for its whole duration: every
        /// <c>IsGoodStoreCell</c> below is patched by the gate adapter, which would otherwise ask this same
        /// question again and recurse without bound.</summary>
        /// <param name="pawn">The carrier the cells are judged for.</param>
        /// <param name="thing">The stack being placed.</param>
        /// <param name="group">The group to measure.</param>
        /// <param name="map">The map it is on.</param>
        /// <param name="filter">The active storage-building filter, or null for allow-all.</param>
        /// <returns>The measurement.</returns>
        private static GroupSpace MeasureGroupUncached(
            Pawn pawn, Thing thing, ISlotGroup group, Map map, StorageBuildingFilter filter)
        {
            var def = thing.def;
            int stackLimit = Math.Max(1, def.stackLimit);
            var cells = group.CellsList;
            if (cells == null)
                return new GroupSpace(0, 0, stackLimit, true, false, int.MaxValue);

            int enough = EnoughFor(def);
            long emptyUnits = 0;
            long partial = 0;
            int emptyCount = 0;
            // Cells LOOKED AT, not accepted: IsGoodStoreCell is what this loop actually costs, so the budget
            // has to count every cell it is called for, skipped ones included.
            int scanned = 0;

            bool outer = insideSpaceScan;
            insideSpaceScan = true;
            try
            {
                for (int i = 0; i < cells.Count && scanned < MaxSpaceScanCells; i++)
                {
                    scanned++;
                    var c = cells[i];
                    if (!StoreUtility.IsGoodStoreCell(c, map, thing, pawn, pawn.Faction))
                        continue;
                    // A linked StorageGroup can pool cells from MULTIPLE buildings, so a denied building's
                    // cells must be dropped individually even when the originating group was allowed.
                    if (filter != null && !filter.IsCellAllowed(c, map))
                        continue;
                    int space = c.GetItemStackSpaceLeftFor(map, def);
                    if (space <= 0)
                        continue; // full for this def (a full stack of it, or another def entirely)
                    if (c.GetFirstItem(map) == null)
                    {
                        emptyCount++;
                        emptyUnits += space; // an empty cell: its whole capacity feeds the SHARED pool
                    }
                    else
                    {
                        partial += space; // a partial stack of this def: top-up room reserved to this def
                    }
                    // Proven roomier than any one plan could fill — stop walking. On a big sparse stockpile
                    // that lands a couple of dozen cells in, so the common case costs LESS than a full walk.
                    if (partial + emptyUnits >= enough)
                        break;
                }
            }
            finally
            {
                insideSpaceScan = outer;
            }

            if (partial + emptyUnits >= enough)
                return new GroupSpace(0, 0, stackLimit, true, false, enough);

            bool truncated = scanned >= MaxSpaceScanCells && scanned < cells.Count;
            // Average per-cell capacity of the empty cells (== stackLimit for uniform vanilla cells, larger
            // for deep storage). Used to convert a claimed empty cell into the def's leftover top-up room.
            int perCell = emptyCount > 0 ? (int)(emptyUnits / emptyCount) : stackLimit;
            return new GroupSpace(emptyCount, (int)partial, Math.Max(1, perCell), false, truncated, 0);
        }

        /*
            ──────────────────────────────────────────────
                        The decision trace (0.1)
            ──────────────────────────────────────────────
            One line per hauling decision — a job created, clamped, adopted or declined — routed through
            HDLog.Dbg so it reaches the disk trail a bug report attaches, without the reporter having to
            turn verbose logging on first.

            → GOTCHA: HDLog.Dbg is NOT free when verbose logging is off. The caller builds the string and it
              is always enqueued to the trail. So this may only ever be called on a DECISION — never per
              capacity query, never per cell, never per tick. A trace inside the gate would be a microstutter
              regression, and this mod already has microstutter reports on record.
        */

        /// <summary>
        /// Record one storage decision: who, what, how much, where, what it decided from, and which code
        /// path decided it. The last field is what tells two identical-looking lines apart when a reporter's
        /// log is read months later — it is the answer to "which of the paths that commit cargo did this".
        /// </summary>
        /// <param name="path">The deciding code path.</param>
        /// <param name="pawn">The deciding pawn.</param>
        /// <param name="group">The destination group.</param>
        /// <param name="def">The def.</param>
        /// <param name="units">Units taken; 0 means the pawn stood down.</param>
        /// <param name="free">Free space the decision was taken against, or <see cref="int.MaxValue"/> when
        /// the destination was not measured on this path.</param>
        internal static void Trace(
            string path, Pawn pawn, ISlotGroup group, ThingDef def, int units, int free = int.MaxValue)
        {
            int spokenFor = StorageClaimLedger.ClaimedByOthers(
                HaulersDreamGameComponent.storageClaims, group, def, pawn, Evidence);
            HDLog.Dbg($"storage-commit [{path}] {pawn?.LabelShort ?? "?"} {def?.defName ?? "?"} x{units} "
                      + $"-> {GroupLabel(group)}; free "
                      + $"{(free == int.MaxValue ? "unmeasured" : free.ToString())}, "
                      + $"others enroute {spokenFor}.");
        }

        /// <summary>Units of <paramref name="def"/> pawns other than <paramref name="asker"/> have already
        /// promised <paramref name="group"/>. Exposed so the bulk planner's per-plan budget draws its
        /// in-flight figure from the SAME ledger the adapters do, rather than keeping a second one.</summary>
        /// <param name="asker">The planning pawn, excluded from its own answer.</param>
        /// <param name="group">The destination group.</param>
        /// <param name="def">The def.</param>
        /// <returns>Units already spoken for by others.</returns>
        internal static int ClaimedByOthersFor(Pawn asker, ISlotGroup group, ThingDef def)
            => StorageClaimLedger.ClaimedByOthers(
                HaulersDreamGameComponent.storageClaims, group, def, asker, Evidence);

        /// <summary>A short, stable name for a destination group, for the trace.</summary>
        /// <param name="group">The group; null reads as "none".</param>
        /// <returns>A label.</returns>
        private static string GroupLabel(ISlotGroup group)
        {
            if (group == null)
                return "none";
            if (group is SlotGroup slot && slot.parent != null)
                return slot.parent.ToString();
            return group.ToString();
        }
    }
}
