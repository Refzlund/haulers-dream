using System;
using System.Threading;

namespace HaulersDream.Core
{
    // Used by the game's narrow legacy-gate seam. No cross-thread restoration is
    // attempted: that would write a different ThreadStatic slot and leak the owner.
    public sealed class ProjectionFlagScope : IDisposable
    {
        private readonly int ownerThread;
        private readonly bool previous;
        private readonly Action<bool> write;
        private bool disposed;
        public ProjectionFlagScope(Func<bool> read, Action<bool> write)
        {
            if (read == null || write == null) throw new ArgumentNullException();
            ownerThread = Thread.CurrentThread.ManagedThreadId;
            this.write = write; previous = read(); write(true);
        }
        public void Dispose()
        {
            if (ownerThread != Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("Projection suppression must be disposed on its owning thread.");
            if (disposed) return;
            write(previous); disposed = true;
        }
    }
}
