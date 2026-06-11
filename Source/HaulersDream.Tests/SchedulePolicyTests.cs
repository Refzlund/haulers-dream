using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SchedulePolicyTests
    {
        [Test]
        public void NonPositiveInterval_IsDisabled()
        {
            Assert.That(SchedulePolicy.IntervalDueNow(0, 0f), Is.False);
            Assert.That(SchedulePolicy.IntervalDueNow(2500, -1f), Is.False);
        }

        [Test]
        public void DueOnExactMultiples_OneHour()
        {
            // 1h = 2500 ticks
            Assert.That(SchedulePolicy.IntervalDueNow(0, 1f), Is.True);
            Assert.That(SchedulePolicy.IntervalDueNow(2500, 1f), Is.True);
            Assert.That(SchedulePolicy.IntervalDueNow(5000, 1f), Is.True);
        }

        [Test]
        public void NotDueBetweenMultiples()
        {
            Assert.That(SchedulePolicy.IntervalDueNow(2499, 1f), Is.False);
            Assert.That(SchedulePolicy.IntervalDueNow(2501, 1f), Is.False);
        }

        [Test]
        public void SixHourDefault()
        {
            int interval = 6 * 2500; // 15000
            Assert.That(SchedulePolicy.IntervalDueNow(interval, 6f), Is.True);
            Assert.That(SchedulePolicy.IntervalDueNow(interval - 1, 6f), Is.False);
        }

        [Test]
        public void FractionalHours_RoundToTicks()
        {
            // 0.5h => round(1250) = 1250 ticks
            Assert.That(SchedulePolicy.IntervalDueNow(1250, 0.5f), Is.True);
            Assert.That(SchedulePolicy.IntervalDueNow(1249, 0.5f), Is.False);
        }

        [Test]
        public void TinyInterval_ClampedToAtLeastOneTick()
        {
            // an absurdly small interval must not divide-by-zero; clamped to 1 tick => always "due"
            Assert.That(SchedulePolicy.IntervalDueNow(12345, 0.00001f), Is.True);
            Assert.That(SchedulePolicy.IntervalDueNow(0, 0.00001f), Is.True);
        }

        [Test]
        public void ZeroTicksPerHour_IsDisabled()
        {
            Assert.That(SchedulePolicy.IntervalDueNow(0, 6f, ticksPerHour: 0), Is.False);
        }
    }
}
