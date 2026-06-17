using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class KeepWorkingPolicyTests
    {
        // An "overloaded" pawn (paying drag); the exact value is irrelevant as long as it is < 1.
        const float Overloaded = 0.6f;

        static bool Should(float toNextWork, float toStorage, float margin = KeepWorkingPolicy.DefaultMarginCells,
            float speed = Overloaded)
            => KeepWorkingPolicy.ShouldUnloadBeforeNext(speed, toNextWork, toStorage, margin);

        [Test]
        public void NextTargetFar_UnloadsFirst()
        {
            // Next work is far (50) and storage is near (10): detour to drop off pays off.
            Assert.That(Should(toNextWork: 50, toStorage: 10), Is.True);
        }

        [Test]
        public void NextTargetClose_KeepsWorking()
        {
            // Next work is right next to the pawn (3) and storage is far (40): carry the load, keep working.
            Assert.That(Should(toNextWork: 3, toStorage: 40), Is.False);
        }

        [Test]
        public void NotOverloaded_NeverUnloads()
        {
            // speedFactor == 1 (at/under capacity): carrying is free, so never detour even if storage is nearer.
            Assert.That(Should(toNextWork: 80, toStorage: 5, speed: 1f), Is.False);
            // And an out-of-range factor (> 1, defensive) is likewise treated as "not overloaded".
            Assert.That(Should(toNextWork: 80, toStorage: 5, speed: 1.5f), Is.False);
        }

        [Test]
        public void EqualDistances_KeepsWorking()
        {
            // Same distance to next work and to storage: detour saves nothing, so keep the load (and the
            // margin makes it strictly NOT worth it).
            Assert.That(Should(toNextWork: 30, toStorage: 30), Is.False);
        }

        [Test]
        public void Margin_Hysteresis()
        {
            // distToStorage = 20, margin = 5 -> the bar is 25. Next work must EXCEED 25 to unload first.
            Assert.That(Should(toNextWork: 25, toStorage: 20, margin: 5f), Is.False); // exactly at the bar -> keep working
            Assert.That(Should(toNextWork: 26, toStorage: 20, margin: 5f), Is.True);  // one past the bar -> unload first
            // A bigger margin keeps the pawn working over a wider band of "roughly equal" distances.
            Assert.That(Should(toNextWork: 28, toStorage: 20, margin: 10f), Is.False);
            Assert.That(Should(toNextWork: 31, toStorage: 20, margin: 10f), Is.True);
        }

        [Test]
        public void ZeroMargin_StrictGreaterThan()
        {
            // With no hysteresis it's a strict > on the distances.
            Assert.That(Should(toNextWork: 20, toStorage: 20, margin: 0f), Is.False); // equal -> not greater
            Assert.That(Should(toNextWork: 21, toStorage: 20, margin: 0f), Is.True);
        }

        [Test]
        public void NegativeMargin_ClampedToZero()
        {
            // A negative margin must not let a closer-than-storage target trigger an unload; it clamps to 0,
            // so the rule stays a strict > on the raw distances.
            Assert.That(Should(toNextWork: 20, toStorage: 20, margin: -5f), Is.False);
            Assert.That(Should(toNextWork: 21, toStorage: 20, margin: -5f), Is.True);
        }
    }
}
