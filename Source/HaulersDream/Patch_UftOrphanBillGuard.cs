using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Corruption self-heal (fix/mix): an <see cref="UnfinishedThing"/> bound to a bill that is <b>not on any
    /// bill stack</b> (<c>BoundBill.billStack == null</c>) is a poison pill for the whole map's hauling AI.
    ///
    /// <para>HOW THE STATE ARISES: bill-syncing mods (e.g. a workbench-group manager such as WorkbenchConnect)
    /// deep-save <b>live</b> <see cref="Bill_ProductionWithUft"/> objects inside their own map/game component.
    /// <c>Bill.billStack</c> is <c>[Unsaved]</c>, so after a load such a bill exists with a NULL stack — while its
    /// <c>boundUft</c> cross-reference still binds it to a real UnfinishedThing (and the UFT's own <c>bill</c>
    /// cross-reference resolves right back to the stack-less copy, because the copy was saved with the bill's
    /// unique load ID). Mid-session the stashed bill still holds a STALE, non-null <c>billStack</c> pointer, which
    /// is why the session that WROTE the save ran fine and the explosion only starts on the next load.</para>
    ///
    /// <para>WHY IT FREEZES PAWNS: every haul-candidate check reads <c>uft.BoundBill</c>, whose getter first
    /// validates <c>Bill.DeletedOrDereferenced</c> — which dereferences <c>billStack.billGiver</c>: a
    /// NullReferenceException on this state. The read sits inside
    /// <c>HaulAIUtility.PawnCanAutomaticallyHaulFast_NewTemp</c>, called by <c>WorkGiver_HaulGeneral</c> scans AND
    /// by <c>Pawn_JobTracker.TryOpportunisticJob</c> — i.e. by every pawn that finishes ANY job while the bound
    /// UFT sits in <c>listerHaulables</c>. RimWorld reacts to a pawn-tick exception with "Exception ticking …
    /// Suppressing further errors", which stops ticking that pawn: colonists stand frozen with a job shown in the
    /// inspect pane but never move, one after another, until the whole colony is stuck.</para>
    ///
    /// <para>THE HEAL: detach the binding from BOTH sides with plain field writes — the UFT keeps its ingredients
    /// and work and becomes an ordinary loose unfinished item (haulable; a matching bench bill re-adopts it via
    /// <c>BillOnTableForMe</c>), and the foreign component's stored bill object is otherwise untouched. This is
    /// corruption REPAIR at the exact seam where vanilla itself would crash, not exception suppression — the same
    /// contract as <see cref="Patch_Pawn_JobTracker_EndCurrentJob_NullDefGuard"/>.</para>
    ///
    /// <para>Two layers, mirroring the null-def-job pair (prefix guard + once-per-load sweep):
    /// <list type="bullet">
    /// <item>This <c>ExposeData</c> prefix heals at <c>PostLoadInit</c> — it runs for EVERY scribed UFT wherever
    /// it lives (spawned on a map, in a pawn's inventory, in a caravan), right after cross-references resolve and
    /// before the first tick. The <c>Saving</c> branch additionally protects vanilla's own pre-save cleanup
    /// (ExposeData's Saving block reads <c>boundBillInt.DeletedOrDereferenced</c>), which would otherwise NRE and
    /// abort the save if the stack-less state ever exists at save time.</item>
    /// <item><see cref="UftOrphanBillGuard.RepairAfterLoadAndReport"/> (called from
    /// <c>HaulersDreamGameComponent.FinalizeInit</c>) is the backstop sweep over exactly the candidate set the
    /// crashing scans iterate, for modded UFT subclasses whose <c>ExposeData</c> override skips <c>base</c>; it
    /// also emits the single aggregated repair message.</item>
    /// </list></para>
    ///
    /// <para>KNOWN SIBLING (deliberately not handled here): the same duplicated-bill save state can also reach a
    /// pawn saved mid-DoBill via <c>Job.bill</c> (a cross-reference too). That path NREs once in
    /// <c>JobDriver_DoBill</c>'s fail condition, is caught by <c>CheckCurrentToilEndOrFail</c>'s error recovery,
    /// and the pawn recovers on its own — a one-time red log line, not a freeze — so no heal is warranted.</para>
    /// </summary>
    [HarmonyPatch(typeof(UnfinishedThing), nameof(UnfinishedThing.ExposeData))]
    public static class Patch_UnfinishedThing_ExposeData_OrphanBillGuard
    {
        /// <summary>UFTs healed by the PostLoadInit pass. Zeroed in the <c>HaulersDreamGameComponent</c> ctor (the
        /// earliest per-load moment HD owns, so a load aborted between PostLoadInit and FinalizeInit can never
        /// inflate the next load's message), reported and re-zeroed by
        /// <see cref="UftOrphanBillGuard.RepairAfterLoadAndReport"/>.</summary>
        internal static int healedDuringLoad;

        /// <summary>Skip patching entirely (with a visible warning) if the private field this guard repairs
        /// through no longer exists — a lazily-thrown TypeInitializationException from the Saving branch would
        /// otherwise escape mid-serialization and abort the player's save, the one failure mode worse than the
        /// bug. With the probe here, a future rename fails INSIDE the per-class patch isolation instead.</summary>
        static bool Prepare()
        {
            if (UftOrphanBillGuard.FieldBound)
                return true;
            HDLog.Warn("UnfinishedThing.boundBillInt not found (game update?) — the orphan-bound-bill save "
                + "repair is disabled. Saves bound to stack-less bills will crash hauling scans as on vanilla.");
            return false;
        }

        static void Prefix(UnfinishedThing __instance)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (UftOrphanBillGuard.TryUnbind(__instance))
                    healedDuringLoad++;
            }
            else if (Scribe.mode == LoadSaveMode.Saving && UftOrphanBillGuard.TryUnbind(__instance))
            {
                // Rare at save time (see class doc: mid-session the pointer is usually stale-but-non-null), so a
                // per-instance warning cannot spam; and it must be immediate — there is no FinalizeInit after a save.
                HDLog.Warn($"unbound {__instance.ToStringSafe()} from a bill that is not on any work bench "
                    + "(billStack == null) while saving — vanilla's own pre-save cleanup would otherwise crash "
                    + "with a NullReferenceException in UnfinishedThing.ExposeData and abort the save.");
            }
        }
    }

    /// <summary>The unbind primitive + once-per-load backstop sweep. See
    /// <see cref="Patch_UnfinishedThing_ExposeData_OrphanBillGuard"/> for the full story.</summary>
    internal static class UftOrphanBillGuard
    {
        // Bound WITHOUT a throwing static initializer: AccessTools.Field returns null (never throws) on a missing
        // member, so a future rename can only ever DISABLE this guard (Prepare() gates the patch, FieldBound gates
        // the sweep) — it can never surface as a TypeInitializationException from inside a scribe pass.
        private static readonly AccessTools.FieldRef<UnfinishedThing, Bill_ProductionWithUft> BoundBillOf =
            BindBoundBill();

        private static AccessTools.FieldRef<UnfinishedThing, Bill_ProductionWithUft> BindBoundBill()
        {
            var field = AccessTools.Field(typeof(UnfinishedThing), "boundBillInt");
            if (field == null || field.FieldType != typeof(Bill_ProductionWithUft) || field.IsStatic)
                return null;
            return AccessTools.FieldRefAccess<UnfinishedThing, Bill_ProductionWithUft>(field);
        }

        /// <summary>False when <c>UnfinishedThing.boundBillInt</c> did not bind (renamed by a game update): the
        /// guard is then inert everywhere — patch skipped, sweep skipped, TryUnbind a no-op.</summary>
        internal static bool FieldBound => BoundBillOf != null;

        /// <summary>Detach <paramref name="uft"/> from a bound bill that is on NO bill stack. Raw field access on
        /// the UFT side — the <c>BoundBill</c> GETTER is the code that NREs on this state (its validation
        /// dereferences <c>billStack.billGiver</c>), so it must never run here; <c>BoundUft</c>/<c>ClearBoundUft</c>
        /// on the bill side are plain field reads/writes and safe. Returns true when an orphan binding was
        /// cleared; false for the normal cases (no bound bill, or the bill sits on a real stack).</summary>
        internal static bool TryUnbind(UnfinishedThing uft)
        {
            var boundBillOf = BoundBillOf;
            if (boundBillOf == null)
                return false;
            var bill = boundBillOf(uft);
            if (bill == null || bill.billStack != null)
                return false;
            boundBillOf(uft) = null;
            if (bill.BoundUft == uft)
                bill.ClearBoundUft();
            return true;
        }

        /// <summary>Once-per-load backstop: sweep <c>ThingRequestGroup.HaulableEver</c> — exactly the set
        /// <c>listerHaulables</c>-driven scans iterate, so exactly the things that can poison them — for UFT
        /// subclasses whose <c>ExposeData</c> override skips <c>base</c> (the prefix never ran for those). Then
        /// report everything both layers healed in one aggregated message.</summary>
        internal static void RepairAfterLoadAndReport()
        {
            if (!FieldBound)
                return;

            int healed = Patch_UnfinishedThing_ExposeData_OrphanBillGuard.healedDuringLoad;
            Patch_UnfinishedThing_ExposeData_OrphanBillGuard.healedDuringLoad = 0;

            var maps = Find.Maps;
            if (maps != null)
                for (int m = 0; m < maps.Count; m++)
                {
                    var haulables = maps[m]?.listerThings?.ThingsInGroup(ThingRequestGroup.HaulableEver);
                    if (haulables == null)
                        continue;
                    for (int i = 0; i < haulables.Count; i++)
                        if (haulables[i] is UnfinishedThing uft && TryUnbind(uft))
                            healed++;
                }

            if (healed > 0)
                HDLog.Msg($"Save repair: unbound {healed} unfinished thing(s) from bill(s) that are not on any "
                    + "work bench. (A bill-syncing mod — e.g. a workbench-group manager — saved live bill objects "
                    + "inside its own data; after a load such a bill has no bill stack, and every hauling scan "
                    + "that touched a bound unfinished thing crashed, freezing pawns with 'Exception ticking' "
                    + "errors.) The items are now ordinary haulable unfinished things; the bills themselves are "
                    + "untouched. One-time repair for this save.");
        }
    }
}
