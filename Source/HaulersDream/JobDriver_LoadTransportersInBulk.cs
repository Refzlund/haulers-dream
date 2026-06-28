using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Bulk-load a transporter/shuttle GROUP — the net-new transporter counterpart to
    /// <see cref="JobDriver_LoadPackAnimal"/>. Same three-phase shape (sweep nearby ground stacks into tagged
    /// inventory → walk to the transporter ONCE → deposit every tagged stack the group still needs), but the
    /// deposit goes into a transporter's <c>innerContainer</c> via <c>TryTransferToContainer</c>. The ThingOwner
    /// add auto-fires <c>Notify_ThingAdded → CompTransporter.SubtractFromToLoadList</c>; HD's §F intercept makes
    /// that decrement PRECISE (behind the per-thread <see cref="Global.IsExecutingManagedUnload"/> flag, set in a
    /// try/finally around each transfer — reset even on throw, rethrow, no suppression).
    ///
    /// Concurrency: the CLAIM is recorded in <see cref="JobDriver_LoadInBulkBase.Notify_Starting"/> (so a
    /// built-but-never-started probe never claims); on every non-Success end the claim is RELEASED and the carried
    /// task item is SALVAGED back into inventory (re-tagged, rides HD's normal unload) — never dropped on a temp map,
    /// never stuck. The shared scaffold lives in <see cref="JobDriver_LoadInBulkBase"/>; this subclass supplies the
    /// transporter-group deposit core + the mid-trip group redirect.
    /// </summary>
    public class JobDriver_LoadTransportersInBulk : JobDriver_LoadInBulkBase
    {
        private const TargetIndex TransporterInd = TargetIndex.A; // primary transporter (deposit dest)

        private CompTransporter Transporter => job.GetTarget(TransporterInd).Thing?.TryGetComp<CompTransporter>();

        private static int AiUpdateInterval => Mathf.Max(10, Settings?.bulkLoadAiUpdateFrequency ?? 60);

        protected override string ToilPrefix => "HD_Ltib";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdLtibLoadIndex", 0);
            Scribe_Values.Look(ref depositLoops, "hdLtibDepositLoops", 0);
            Scribe_Values.Look(ref passes, "hdLtibPasses", 0);
        }

        public override string GetReport() => "HaulersDream.LoadTransporter.Report".Translate();

        protected override IManagedLoadable BuildLoadable()
        {
            var comp = Transporter;
            return comp != null ? LoadTransportersAdapter.TryCreate(comp) : null;
        }

        protected override bool FindTargetStillValid()
        {
            var comp = Transporter;
            return comp != null && comp.parent != null && comp.parent.Spawned;
        }

        protected override void OnPreDepositLoop(IManagedLoadable adp)
        {
            // Re-validate the carried items are still needed (mid-trip redirect within the group) every
            // AiUpdateInterval ticks — a no-op redirect just continues; a fully-stale load falls to loopCheck.
            TransportLoadTargetRedirect.ValidateAndRedirectCurrentTarget(this, (LoadTransportersAdapter)adp);
        }

        protected override void DepositOne(Thing thing, ThingOwner inner, CompHauledToInventory hcomp, IManagedLoadable adp, ref bool movedAny)
        {
            var adapter = (LoadTransportersAdapter)adp;
            // Deposit into ONE specific member, clamped to THAT member's remaining for the def — NOT the
            // group total. The member's auto-fired SubtractFromToLoadList only subtracts what its own
            // leftToLoad entry held, so depositing more than one member wants into its container would
            // under-count the manifest (and over-load that pod). The findTransporter loop re-enters to
            // service the next member's share on the following pass. The MANIFEST is authoritative — NOT the
            // group mass cap (vanilla lets a pod load past mass capacity, shown red; the trip-mass budget was
            // already applied during the sweep).
            var member = adapter.ActiveMemberFor(thing);
            if (member == null)
                return; // no member still wants this exact variant (another pawn filled it) — leave it tagged
            int memberRemaining = LoadTransportersAdapter.MemberRemainingFor(member, thing);
            int count = System.Math.Min(InventorySurplus.SurplusOf(pawn, thing), memberRemaining);
            if (count <= 0)
                return;
            var destInner = member.innerContainer;
            if (destInner == null)
                return;

            int moved;
            // Set the per-thread flag so the SubtractFromToLoadList intercept does the PRECISE decrement.
            // try/finally resets it even on throw; the throw RETHROWS (no suppression).
            Global.IsExecutingManagedUnload = true;
            try
            {
                moved = inner.TryTransferToContainer(thing, destInner, count, out Thing _, canMergeWithExistingStacks: false);
            }
            finally
            {
                Global.IsExecutingManagedUnload = false;
            }
            if (moved > 0)
            {
                movedAny = true;
                HaulersDreamGameComponent.Instance?.LoadNotifyDeposited(pawn, adapter, thing.def, moved);
                if (!inner.Contains(thing))
                    hcomp.Deregister(thing); // fully moved -> drop the tag; a partial leaves the remainder tagged
            }
        }

        protected override bool HasDepositable() => HasDepositableForGroup();

        /// <summary>Units the group's manifest still wants for <paramref name="item"/> — summed across members using,
        /// PER MEMBER, the entry vanilla's <see cref="TransferableUtility.TransferableMatchingDesperate"/> (in
        /// <c>PodsOrCaravanPacking</c> mode) would decrement (the SAME 3-tier ladder — identity → <c>TransferAsOne</c>
        /// variant → def-only fallback — that <c>SubtractFromToLoadList</c> and the per-member deposit clamp
        /// <see cref="LoadTransportersAdapter.MemberRemainingFor"/> use). Summing per member (not over the flattened
        /// transferable list) matches how the deposit actually drains the group: each member's own
        /// <c>SubtractFromToLoadList</c> resolves ONE entry against ITS OWN <c>leftToLoad</c>, so an off-quality
        /// fungible item credits each member's def entry via Tier-3 exactly as the deposit will. This keeps the deposit
        /// pre-gate (walk-to-transporter decision) in lock-step with the deposit path — a strict-Tier-2 pre-gate would
        /// wrongly skip the trip for a fungible variant the deposit would still load.</summary>
        private static int GroupRemainingFor(LoadTransportersAdapter adp, Thing item)
        {
            if (adp == null || item?.def == null)
                return 0;
            int sum = 0;
            var group = adp.Group;
            for (int i = 0; i < group.Count; i++)
                sum += LoadTransportersAdapter.MemberRemainingFor(group[i], item);
            return sum;
        }

        /// <summary>True if the pawn holds any tagged surplus stack of a variant the group still wants.</summary>
        private bool HasDepositableForGroup()
        {
            var hcomp = pawn.GetComp<CompHauledToInventory>();
            var inner = pawn.inventory?.innerContainer;
            var adp = EnsureAdapter() as LoadTransportersAdapter;
            if (hcomp == null || inner == null || adp == null)
                return false;
            // HEALED view (not Peek): the deposit driver reads GetHashSet (JobDriver_LoadInBulkBase), so this gate
            // must too — else a scooped stack that MERGED into a same-def inventory stack after tagging is invisible
            // here, the gate says "nothing to deposit", the load ends early, and the merge-survivor cargo never loads
            // onto the transporter (it rides to storage instead). Same #62/#87 stale-view class on the load side.
            foreach (var t in hcomp.GetHashSet())
            {
                if (t == null || t.Destroyed || !inner.Contains(t))
                    continue;
                if (InventorySurplus.SurplusOf(pawn, t) <= 0)
                    continue;
                if (GroupRemainingFor(adp, t) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>How often (ticks) the carried-item re-validation runs — exposed for the redirect helper.</summary>
        internal int RevalidateInterval => AiUpdateInterval;
    }
}
