using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Locks the one invariant of the per-bench gather switch (issue #230) that IS expressible without Verse: it is
    /// a VETO, never an override. A bench without the component, and a bench with it left on, both read as
    /// "allowed" — only an explicitly switched-off bench blocks. Getting the no-component case wrong would silently
    /// disable gathering on every bill giver the XML patch never reached.
    ///
    /// <para>BE CLEAR ABOUT WHAT THESE TESTS DO NOT COVER, because the policy itself is thin and the invariant that
    /// actually protects the feature lives one layer out, in Verse: the bench GIZMO must decide its visibility with
    /// <c>BillRouteGate.IsRoutableBenchType</c> and NEVER with <c>MayRouteToInventory</c>. The latter now reads this
    /// very switch, so using it would hide the button the instant a player switched a bench off — a one-way trapdoor
    /// with no way back. That is guarded in code by a <c>→ GOTCHA</c> comment in
    /// <c>CompBenchGather.CompGetGizmosExtra</c>; it is repeated here because this file is where the next person
    /// editing the policy will look, and no test here can fail if it is broken.</para>
    /// </summary>
    [TestFixture]
    public class BenchGatherPolicyTests
    {
        [Test]
        public void BenchAllowsGather_NoComp_True()
        {
            // An un-patched bill giver has no recorded player choice, so it must behave exactly as before the
            // feature existed. benchFlag is meaningless here; false proves it is genuinely not consulted.
            Assert.That(BenchGatherPolicy.BenchAllowsGather(hasComp: false, benchFlag: false), Is.True);
        }

        [Test]
        public void BenchAllowsGather_CompOn_True()
        {
            // The default state every bench loads in (including pre-feature saves).
            Assert.That(BenchGatherPolicy.BenchAllowsGather(hasComp: true, benchFlag: true), Is.True);
        }

        [Test]
        public void BenchAllowsGather_CompOff_False()
        {
            // The only blocking case: the player switched this one bench back to vanilla behaviour.
            Assert.That(BenchGatherPolicy.BenchAllowsGather(hasComp: true, benchFlag: false), Is.False);
        }
    }
}
