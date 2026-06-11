using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class HaulToStackPolicyTests
    {
        [Test]
        public void StackCell_BeatsNonStack_EvenWhenFarther()
        {
            Assert.That(HaulToStackPolicy.IsBetter(true, 100f, false, 1f), Is.True);
            Assert.That(HaulToStackPolicy.IsBetter(false, 1f, true, 100f), Is.False);
        }

        [Test]
        public void EqualKind_NearerWins()
        {
            Assert.That(HaulToStackPolicy.IsBetter(true, 4f, true, 9f), Is.True);
            Assert.That(HaulToStackPolicy.IsBetter(true, 9f, true, 4f), Is.False);
            Assert.That(HaulToStackPolicy.IsBetter(false, 4f, false, 9f), Is.True);
        }

        [Test]
        public void EqualDistance_DoesNotChurn()
        {
            // Not strictly better -> keep the incumbent (stable choice).
            Assert.That(HaulToStackPolicy.IsBetter(true, 5f, true, 5f), Is.False);
        }

        [Test]
        public void RadiusScan_OnlyForRoomlessOrMapEdgeRooms()
        {
            Assert.That(HaulToStackPolicy.UseRadiusScan(hasRoom: false, roomTouchesMapEdge: false), Is.True, "no room at all");
            Assert.That(HaulToStackPolicy.UseRadiusScan(hasRoom: true, roomTouchesMapEdge: true), Is.True, "the open outdoors");
            Assert.That(HaulToStackPolicy.UseRadiusScan(hasRoom: true, roomTouchesMapEdge: false), Is.False, "a real room (or enclosed courtyard)");
        }
    }
}
