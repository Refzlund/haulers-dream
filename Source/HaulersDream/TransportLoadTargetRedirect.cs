using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Mid-trip re-validation — the ONE piece of the bulk-load path with no HD precedent. Periodically (every
    /// <c>bulkLoadAiUpdateFrequency</c> ticks) it confirms the carried tagged stock is still wanted by the
    /// transporter group; if the PRIMARY deposit target (TargetIndex.A) no longer needs anything but ANOTHER group
    /// member still does, it redirects the job's target to that member so the deposit walk lands somewhere useful.
    /// When nothing in the group needs the carried stock at all, it does nothing here (the deposit loop's per-stack
    /// "still needed?" check leaves such stock tagged → it rides HD's normal unload; the salvage finish action
    /// reconciles on any end). This keeps a courier from dumping surplus on the floor when its target fills from
    /// another pawn.
    /// </summary>
    public static class TransportLoadTargetRedirect
    {
        // Reused scratch for the carried-surplus-defs gather, replacing a fresh HashSet<ThingDef> per revalidate.
        // [ThreadStatic] + lazy-init per the repo's hook-reachable scratch convention; Cleared at use, never trusted
        // empty. SAFETY: filled once in ValidateAndRedirectCurrentTarget then only READ (MemberNeedsAny) within that
        // one rate-limited deposit-toil call (no re-entrant gather) before the next reuse.
        [System.ThreadStatic] private static HashSet<ThingDef> scratchCarriedDefs;

        /// <summary>
        /// Re-validate + (if needed) redirect the current deposit target within the group. Rate-limited to the
        /// driver's revalidate interval. Returns true if a redirect was applied. Carried live pawns (downed
        /// colonists) would take top priority — not applicable to the transporter item-load path (item-only here).
        /// </summary>
        public static bool ValidateAndRedirectCurrentTarget(JobDriver_LoadTransportersInBulk driver, LoadTransportersAdapter adapter)
        {
            if (driver == null || adapter == null)
                return false;
            var pawn = driver.pawn;
            if (pawn == null || !pawn.IsHashIntervalTick(System.Math.Max(10, driver.RevalidateInterval)))
                return false;

            var job = driver.job;
            var current = job.GetTarget(TargetIndex.A).Thing?.TryGetComp<CompTransporter>();
            // What defs does the pawn carry as tagged surplus right now?
            var carriedDefs = CarriedSurplusDefs(pawn);
            if (carriedDefs.Count == 0)
                return false; // nothing carried -> nothing to redirect (the deposit loop / loopCheck handles it)

            // Does the CURRENT target still need any carried def?
            if (current != null && MemberNeedsAny(current, carriedDefs))
                return false; // current target is still useful — no redirect

            // The current target is done with our cargo. Find a group member that still needs a carried def.
            foreach (var member in adapter.Group)
            {
                if (member?.parent == null || !member.parent.Spawned || member == current)
                    continue;
                if (MemberNeedsAny(member, carriedDefs))
                {
                    job.SetTarget(TargetIndex.A, member.parent);
                    return true;
                }
            }
            return false; // no member needs our cargo — the deposit loop leaves it tagged (rides the normal unload)
        }

        private static HashSet<ThingDef> CarriedSurplusDefs(Pawn pawn)
        {
            var defs = scratchCarriedDefs ?? (scratchCarriedDefs = new HashSet<ThingDef>());
            defs.Clear();
            var comp = pawn.GetComp<CompHauledToInventory>();
            var inner = pawn.inventory?.innerContainer;
            if (comp == null || inner == null)
                return defs;
            foreach (var t in comp.PeekHashSet())
                if (t != null && !t.Destroyed && inner.Contains(t) && t.def != null
                    && InventorySurplus.SurplusOf(pawn, t) > 0)
                    defs.Add(t.def);
            return defs;
        }

        private static bool MemberNeedsAny(CompTransporter member, HashSet<ThingDef> defs)
        {
            var ltl = member?.leftToLoad;
            if (ltl == null)
                return false;
            for (int i = 0; i < ltl.Count; i++)
            {
                var tr = ltl[i];
                if (tr != null && tr.CountToTransfer > 0 && tr.ThingDef != null && defs.Contains(tr.ThingDef))
                    return true;
            }
            return false;
        }
    }
}
