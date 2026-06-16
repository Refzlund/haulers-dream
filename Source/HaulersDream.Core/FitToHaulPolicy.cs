namespace HaulersDream.Core
{
    /// <summary>
    /// Decides whether a pawn is fit to START a new haul/scoop/sweep right now, given its live bleed
    /// rate and the bleeding-gate settings. Faithful to While-You're-Up's <c>AmBleeding</c> safety gate
    /// (a badly bleeding pawn should get treated, not detour into a sweep): WYU's literal check is
    /// <c>BleedRateTotal &gt; 0.001f</c> (Source/OpportunityDetour.cs:91, via Mod.cs:109), reproduced
    /// here as the configurable <paramref name="threshold"/> with the same STRICT <c>&gt;</c> — a pawn
    /// exactly AT the threshold is still fit (WYU parity), only one strictly above it stands down.
    ///
    /// Pure (Verse-free) so it can be unit-tested without a loaded game; the runtime callers
    /// (<c>YieldRouter</c> / <c>BulkHaul</c>) pull <c>pawn.health.hediffSet.BleedRateTotal</c> (a per-day
    /// rate; <c>BleedRateTotal</c> already returns 0 for dead / can't-bleed pawns) and call this.
    ///
    /// CRITICAL (conflict guard G1): this gate may ONLY ever REMOVE candidacy when bleeding, and is
    /// applied as an AND-clause at the explicit scoop/sweep/bulk INTAKE entry points — never on any
    /// unload/adopt/alert/recovery path. A pawn already carrying scooped goods must always be able to
    /// unload them; gating an unload on bleeding would strand the load (a permanent "black hole").
    /// </summary>
    public static class FitToHaulPolicy
    {
        /// <returns>
        /// <c>true</c> (fit to start a haul) unless the gate is enabled AND the pawn is bleeding strictly
        /// above the threshold. With <paramref name="gateEnabled"/> false this is always <c>true</c>
        /// (byte-identical to the no-gate behavior).
        /// </returns>
        public static bool FitToStartHaul(bool gateEnabled, float bleedRate, float threshold)
        {
            if (!gateEnabled)
                return true;
            // Strict >: exactly at the threshold is still fit (WYU's `BleedRateTotal > 0.001f`).
            return !(bleedRate > threshold);
        }
    }
}
