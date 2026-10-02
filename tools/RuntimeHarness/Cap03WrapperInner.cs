using System.Threading;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // Only the CAP03-C XML uses this class. Counters never alter native results.
    public sealed class Cap03WrapperInner : Building
    {
        private long stackCalls, hitPointReads;
        public long StackCalls => Interlocked.Read(ref stackCalls);
        public long HitPointReads => Interlocked.Read(ref hitPointReads);
        public int RawHitPoints => base.HitPoints;
        public override int HitPoints
        {
            get { Interlocked.Increment(ref hitPointReads); return base.HitPoints; }
            set { base.HitPoints = value; }
        }
        public override bool CanStackWith(Thing other)
        { Interlocked.Increment(ref stackCalls); return base.CanStackWith(other); }
    }
}
