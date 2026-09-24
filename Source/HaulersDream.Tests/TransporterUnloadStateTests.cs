using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class TransporterUnloadStateTests
    {
        [Test]
        public void DisabledFlagIsDormantAndLoadTakeoverRemovesItsIntent()
        {
            var state = new TransporterUnloadState();
            state.SetFlag(20, true);
            Assert.That(state.IsActive(20, false), Is.False);
            Assert.That(state.IsFlagged(20), Is.True);
            Assert.That(state.IsActive(20, true), Is.True);
            state.AcceptLoad(true, 3, new[] { 20 });
            Assert.That(state.IsActive(20, true), Is.False);
            Assert.That(state.LoadOwns(3, true, false), Is.True);
        }

        [Test]
        public void FailedTopUpPreservesFlagAndDoesNotAcquireSession()
        {
            var state = new TransporterUnloadState(new[] { 10, 20 });
            state.AcceptLoad(false, 3, new[] { 10, 20 });
            Assert.That(state.CaptureFlags(), Is.EqualTo(new[] { 10, 20 }));
            Assert.That(state.HasSession(3), Is.False);
            state.AcceptLoad(true, 3, new[] { 10, 20 });
            Assert.That(state.CaptureFlags(), Is.Empty);
            state.SetFlag(20, true); // stale intent supplied by an older serialized state
            state.AcceptLoad(true, 3, new[] { 10, 20 });
            Assert.That(state.IsFlagged(20), Is.False, "An existing-group top-up still clears every member.");
        }

        [Test]
        public void EmptyManifestDoesNotReleaseCourierOrLordCustody()
        {
            var state = new TransporterUnloadState();
            state.AcceptLoad(true, 3, new[] { 10 });
            Assert.That(state.LoadOwns(3, false, true), Is.True);
            Assert.That(state.LoadOwns(3, false, false), Is.False);
            state.EndLoad(3);
            Assert.That(state.LoadOwns(3, true, false), Is.True, "Missing/ended history cannot prove a current manifest abandoned.");
            Assert.That(state.LoadOwns(3, false, true), Is.True, "Even teardown cannot override actual outstanding custody.");
        }

        [Test]
        public void OldSaveManifestWaitsForActualFulfilmentOrCancellation()
        {
            var oldSave = new TransporterUnloadState();
            Assert.That(oldSave.HasSession(3), Is.False);
            Assert.That(oldSave.LoadOwns(3, true, false), Is.True);
            oldSave.EndLoad(3); // removing/no-op removing a lord cannot waive the current manifest
            Assert.That(oldSave.LoadOwns(3, true, false), Is.True);
            Assert.That(oldSave.LoadOwns(3, false, false), Is.False);
        }

        [Test]
        public void SaveSnapshotRestoresFlagsAndSessionsWithoutSharingMutableLists()
        {
            var original = new TransporterUnloadState(new[] { 20, 10 }, new[] { 3 });
            var flags = original.CaptureFlags(); var sessions = original.CaptureSessions();
            var restored = new TransporterUnloadState(flags, sessions);
            flags.Clear(); sessions.Clear(); original.SetFlag(20, false); original.EndLoad(3);
            Assert.That(restored.CaptureFlags(), Is.EqualTo(new[] { 10, 20 }));
            Assert.That(restored.LoadOwns(3, true, false), Is.True);
            restored.Prune(id => id == 20, group => false);
            Assert.That(restored.CaptureFlags(), Is.EqualTo(new[] { 20 }));
            Assert.That(restored.CaptureSessions(), Is.Empty);
        }

    }
}
