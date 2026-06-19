using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Reachability gate for the UNCLICKED bonus targets that Hauler's Dream sweeps into a bulk/batch job
    /// — the extra haulables, ingredients, load items and construct needers it adds on top of the single
    /// target the pawn was originally assigned or the player explicitly clicked.
    ///
    /// Vanilla models a deadly environment — vacuum especially (Save Our Ship 2 / Odyssey), but also a
    /// fire-room or deadly temperature — as a region <see cref="Danger.Deadly"/> for a concerned pawn (a
    /// suit-less colonist has VacuumResistance &lt; 0.75 and is <c>ConcernedByVacuum</c>). Vanilla's reach
    /// test (<c>HaulAIUtility.PawnCanAutomaticallyHaulFast</c> → <c>CanReach(t, …, pawn.NormalMaxDanger())</c>)
    /// only refuses to path through such a region when the danger ceiling is BELOW Deadly.
    ///
    /// The catch: <c>DangerUtility.NormalMaxDanger</c> raises the ceiling to Deadly while a job is
    /// <c>playerForced</c> OR the float menu is being built (<c>FloatMenuMakerMap.makingFor == pawn</c>).
    /// That exemption is meant for the single target the player explicitly chose — but HD's sweeps run inside
    /// that same window and would inherit it for every BONUS target too (e.g. while the colonist works a
    /// player-prioritized mining/harvest job, or while the right-click menu is open), sending a suit-less
    /// pawn into space to grab scrap it was never told to fetch.
    ///
    /// So for swept extras we cap the reach ceiling at <see cref="Danger.Some"/> — the ordinary autonomous
    /// ceiling — never Deadly. The explicitly-clicked primary keeps vanilla's forced semantics (handled at
    /// its own call sites); only the extras are held to "don't risk your life for a bonus". The drawn
    /// allowed-AREA zone is a separate gate (<c>Thing.IsForbidden(pawn)</c>) that HD already re-applies to
    /// every extra — this only adds the DANGER dimension a forced/menu context defeats.
    /// </summary>
    public static class ExtraSweepReach
    {
        /// <summary>
        /// The danger ceiling to use when reaching for an unclicked swept extra: the pawn's normal ceiling,
        /// but never above <see cref="Danger.Some"/>. The <c>Min</c> (rather than a flat <c>Some</c>) preserves
        /// the rarer <see cref="Danger.None"/> ceiling vanilla uses for a pawn nursing a minor temperature
        /// injury, and strips ONLY the Deadly exemption that a forced job / open float menu injects.
        /// </summary>
        public static Danger Ceiling(Pawn pawn)
            => (Danger)Math.Min((int)pawn.NormalMaxDanger(), (int)Danger.Some);

        /// <summary>
        /// True if <paramref name="pawn"/> can reach an unclicked swept extra without pathing through a
        /// deadly-danger region (vacuum / fire / deadly temperature). Use this ALONGSIDE the normal
        /// eligibility check — it adds only the danger cap. It always evaluates the reach (no short-circuit),
        /// so it is correct both at job-build time AND when re-validating a queued extra at execution time
        /// (where there is no companion <c>PawnCanAutomaticallyHaulFast</c> to have done the reach already).
        /// </summary>
        public static bool Allows(Pawn pawn, LocalTargetInfo target, PathEndMode peMode = PathEndMode.ClosestTouch)
            => pawn.CanReach(target, peMode, Ceiling(pawn));
    }
}
