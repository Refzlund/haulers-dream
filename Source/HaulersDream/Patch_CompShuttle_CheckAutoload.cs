using HarmonyLib;
using RimWorld;

namespace HaulersDream
{
    /// <summary>
    /// Anti-conflict patch: while HD claims are live for a shuttle's group, suppress its autoload regeneration.
    /// <c>CompShuttle.CheckAutoload</c> (private, runs every 120 ticks from CompTick) CLEARS + regenerates the
    /// transporter's <c>leftToLoad</c> from the required-items list — which would clobber the precise per-claim
    /// counts HD's deposit intercept maintains. The PREFIX snapshots the private <c>autoload</c> field (via
    /// <c>Traverse</c>) and forces it false while a claim is in progress, so CheckAutoload early-returns; the
    /// POSTFIX restores the snapshot UNCONDITIONALLY (never gated on the same AnyClaimsInProgress check — a claim
    /// clearing mid-tick must not leave autoload stuck off).
    /// </summary>
    [HarmonyPatch(typeof(CompShuttle), "CheckAutoload")]
    public static class Patch_CompShuttle_CheckAutoload
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        // __state carries the snapshot from prefix to postfix per invocation. No try/catch: a Traverse fault is a
        // real bug to surface, not a silent warning.
        static void Prefix(CompShuttle __instance, out bool __state)
        {
            __state = false; // default: nothing changed (no live claim / no transporter)
            var transporter = __instance.Transporter;
            if (transporter == null)
                return;
            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger == null || !ledger.LoadAnyClaimsInProgress(transporter.groupID))
                return;
            var autoloadField = Traverse.Create(__instance).Field("autoload");
            bool current = autoloadField.GetValue<bool>();
            if (!current)
                return; // already off — nothing to suppress/restore
            __state = true; // we are flipping it off; the postfix restores it to true
            autoloadField.SetValue(false);
        }

        static void Postfix(CompShuttle __instance, bool __state)
        {
            // UNCONDITIONAL restore: if the prefix flipped autoload off, put it back true now (do NOT re-check
            // AnyClaimsInProgress — a claim that cleared mid-tick would otherwise leave autoload stuck off).
            if (__state)
                Traverse.Create(__instance).Field("autoload").SetValue(true);
        }
    }
}
