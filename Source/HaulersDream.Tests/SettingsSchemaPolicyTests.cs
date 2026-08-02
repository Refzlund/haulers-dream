using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SettingsSchemaPolicyTests
    {
        // The schema this build writes; the load path passes HaulersDreamSettings.CurrentSettingsSchema here.
        private const int Current = 1;

        // A genuine pre-#79 config: no stamp (an absent node reads as 0) AND legacy nodes present -> migrate.
        [Test]
        public void LegacyConfig_NoStampAndLegacyNodes_Migrates()
        {
            Assert.That(SettingsSchemaPolicy.ShouldMigrateLegacyYields(0, Current, true), Is.True);
        }

        // THE #238 CASE: a stamp of 0 with NO legacy node is a config/snapshot that simply never RECORDED its
        // schema (Scribe omits a value equal to its default), not an old one. Migrating it overwrote the nine
        // freshly-loaded yield values with DropThenHaul on every single launch.
        [Test]
        public void NoStampButNoLegacyNodes_DoesNotMigrate()
        {
            Assert.That(SettingsSchemaPolicy.ShouldMigrateLegacyYields(0, Current, false), Is.False);
        }

        // An already-stamped config never migrates, whatever the data looks like — the stamp still short-circuits.
        [TestCase(true)]
        [TestCase(false)]
        public void AlreadyAtCurrentSchema_DoesNotMigrate(bool anyLegacyYieldNodePresent)
        {
            Assert.That(
                SettingsSchemaPolicy.ShouldMigrateLegacyYields(Current, Current, anyLegacyYieldNodePresent),
                Is.False);
        }

        // A config written by a FUTURE build (stamp above ours) is never downgraded through the legacy migration.
        [Test]
        public void FutureSchema_DoesNotMigrate()
        {
            Assert.That(SettingsSchemaPolicy.ShouldMigrateLegacyYields(2, Current, true), Is.False);
        }

        // Idempotence: the load path stamps to CurrentSettingsSchema right after migrating, so the SECOND load of
        // that same (now rewritten) config takes the stamp branch and never migrates again — even though the file
        // may still carry the legacy nodes until the next write.
        [Test]
        public void AfterFirstPassStampsCurrent_SecondLoadDoesNotMigrateAgain()
        {
            int stamp = 0;
            Assert.That(SettingsSchemaPolicy.ShouldMigrateLegacyYields(stamp, Current, true), Is.True);
            stamp = Current; // what ExposeData writes back after the one-time migration
            Assert.That(SettingsSchemaPolicy.ShouldMigrateLegacyYields(stamp, Current, true), Is.False);
        }
    }
}
