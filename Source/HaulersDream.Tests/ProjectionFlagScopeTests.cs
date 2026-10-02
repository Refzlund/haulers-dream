using System;
using System.Threading;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class ProjectionFlagScopeTests
    {
        [ThreadStatic] private static bool flag;
        private static ProjectionFlagScope Open() => new ProjectionFlagScope(() => flag, value => flag = value);

        [Test]
        public void NestedExceptionRestoresExistingLegacyScopeAndThenOriginalState()
        {
            flag = false;
            using (Open())
            {
                Assert.Throws<InvalidOperationException>(() => { using (Open()) { Assert.That(flag, Is.True); throw new InvalidOperationException(); } });
                Assert.That(flag, Is.True);
            }
            Assert.That(flag, Is.False);
        }

        [Test]
        public void PriorTrueSurvivesAndDuplicateDisposalCannotPopLaterScope()
        {
            flag = true;
            var scope = Open(); scope.Dispose();
            Assert.That(flag, Is.True);
            flag = false;
            using (Open()) { scope.Dispose(); Assert.That(flag, Is.True); }
            Assert.That(flag, Is.False);
        }

        [Test]
        public void WrongThreadDisposalWritesNeitherThreadsFlagAndOwnerCanStillRestore()
        {
            flag = false;
            var scope = Open(); Exception failure = null; bool workerAfter = true;
            var worker = new Thread(() =>
            {
                flag = false;
                try { scope.Dispose(); } catch (Exception error) { failure = error; }
                workerAfter = flag;
            });
            worker.Start(); worker.Join();
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(workerAfter, Is.False);
            Assert.That(flag, Is.True);
            scope.Dispose(); Assert.That(flag, Is.False);
        }
    }
}
