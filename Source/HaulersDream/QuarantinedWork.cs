using HaulersDream.Core;

namespace HaulersDream
{
    /// <summary>
    /// One switched-off work giver, as the player will read it — captured at fault time, when the exception's
    /// frames are still intact, and held until the session ends.
    ///
    /// <para><b>Why the facts are stored and not a finished sentence.</b> The alert is what turns these into
    /// text, in the player's language, from the two <c>HaulersDream.Alert.QuarantineLine.*</c> keys. Baking an
    /// English line here would put presentation inside a Harmony finalizer and freeze the language at the moment
    /// of the fault. Baking the raw facts and letting the alert re-derive attribution would be worse still: by
    /// the time the alert draws, Harmony has re-thrown the exception and reset its stack trace, so the evidence
    /// this type carries no longer exists anywhere.</para>
    ///
    /// <para><b>Invariant:</b> <see cref="Source"/> is non-null exactly when <see cref="Naming"/> is
    /// <see cref="QuarantineNaming.NameTheMod"/> — the producer sets them together from one
    /// <see cref="WorkGiverNamingPolicy"/> verdict. The alert still checks both before printing a name, because a
    /// line that promises a source and renders an empty one is the false-blame shape this whole change removes.</para>
    /// </summary>
    public sealed class QuarantinedWork
    {
        /// <summary>The work giver as a player can recognise it: its def's label with the type name in
        /// parentheses for a bug report, or the bare type name when no <c>WorkGiverDef</c> declares it.</summary>
        public readonly string Work;

        /// <summary>Whether the error itself identified a mod. Decided once, at fault time, by
        /// <see cref="WorkGiverNamingPolicy"/>.</summary>
        public readonly QuarantineNaming Naming;

        /// <summary>The mod the exception's own frames placed at the fault, as
        /// <c>"&lt;Mod Name&gt; (Namespace.Type.Method)"</c> — or null when nothing did, which is the case the
        /// player must be told about rather than filled in with whoever happened to be nearby.</summary>
        public readonly string Source;

        /// <summary>Capture one switched-off work giver.</summary>
        /// <param name="work">The player-facing description of the work giver; never null.</param>
        /// <param name="naming">The naming verdict for this fault.</param>
        /// <param name="source">The named mod and code location, or null under
        /// <see cref="QuarantineNaming.SourceUnknown"/>.</param>
        public QuarantinedWork(string work, QuarantineNaming naming, string source)
        {
            Work = work;
            Naming = naming;
            Source = source;
        }
    }
}
