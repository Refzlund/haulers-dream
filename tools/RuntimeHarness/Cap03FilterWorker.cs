using System.Threading;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // Loaded only by the case-specific XML. No static initializer, Harmony patch,
    // registration exemption or modification of a Thing/filter/native result.
    public sealed class Cap03FilterUnknownWorker : SpecialThingFilterWorker
    {
        private static long constructed, matches, always, canEver;
        public static long Constructed => Interlocked.Read(ref constructed);
        public static long MatchesCalls => Interlocked.Read(ref matches);
        public static long AlwaysMatchesCalls => Interlocked.Read(ref always);
        public static long CanEverMatchCalls => Interlocked.Read(ref canEver);
        public Cap03FilterUnknownWorker() { Interlocked.Increment(ref constructed); }
        public override bool Matches(Thing thing)
        {
            Interlocked.Increment(ref matches);
            return thing != null && thing.def.defName == "RawRice";
        }
        public override bool AlwaysMatches(ThingDef def) { Interlocked.Increment(ref always); return false; }
        public override bool CanEverMatch(ThingDef def) { Interlocked.Increment(ref canEver); return true; }
    }
}
