using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The always-on, disk-backed debug trace behind <see cref="HDLog"/>. Every HD log line (including verbose
    /// <see cref="HDLog.Dbg"/>, which a normal player never sees in the console) is appended here so an in-game
    /// issue report can carry Hauler's Dream's own recent history WITHOUT the player having to turn verbose logging
    /// on first. It is written to DISK (not RAM) so a long session can't grow an unbounded in-memory buffer, and it
    /// is size-capped with a single rotation so it can never grow a huge file either: the active file is capped at
    /// <see cref="ActiveCapBytes"/> and rotated to <c>*.prev.log</c>, so on-disk usage stays at or below 2× that
    /// cap. The active file is ALSO rotated once at session start (see <see cref="ConfigureDirectory"/>), so each
    /// game run's trace starts fresh in <c>HaulersDream-debug.log</c> (the previous run kept in <c>*.prev.log</c>)
    /// rather than being appended after older runs: a report or a hand-read of the active file reflects the current
    /// run, and the look-back window spans the current run plus the previous one.
    ///
    /// Writes happen on a dedicated background thread fed by a lock-free queue, so the game thread (and the
    /// off-main-thread universal exception finalizer) never block on disk I/O. Lines are appended in the order
    /// enqueued; the writer batches and flushes whatever has accumulated each wake-up.
    ///
    /// EXCEPTION POLICY (deliberate, see <c>no-exception-suppression</c>): the writer loop catches an I/O fault,
    /// reports it ONCE via <see cref="Log.Error"/> (so it stays visible, not swallowed), and then disables disk
    /// logging for the rest of the session. A background-thread exception would otherwise terminate the process;
    /// a logger must never crash the game it is logging for. The ONE exception caught WITHOUT being logged is
    /// <see cref="ThreadAbortException"/>: that is the runtime aborting this background thread during normal
    /// shutdown / AppDomain teardown (a benign lifecycle signal, not a disk fault), so logging it would post a
    /// scary red "I/O error" for an ordinary game exit. This is NOT suppression: a genuine I/O fault is a
    /// different exception type that still hits the general catch and is surfaced loudly + latches logging off.
    /// The main-thread reader (<see cref="GetReportTail"/>)
    /// intentionally has NO try/catch, so a read fault surfaces normally in the report flow.
    /// </summary>
    public static class HDDebugLog
    {
        /// <summary>Active-file size cap. At/after this the file rotates to <c>*.prev.log</c> and a fresh active file
        /// starts, so total on-disk usage stays at or below 2× this value.</summary>
        public const long ActiveCapBytes = 8L * 1024 * 1024; // 8 MB

        /// <summary>How much of the trail (newest first) the in-game reporter attaches as the "Hauler's Dream" log.</summary>
        public const int ReportTailBytes = 512 * 1024; // 512 KB

        // Backstop only: if the writer ever stalls (e.g. it was never configured), stop growing the in-memory queue
        // past this many pending lines rather than risk an unbounded allocation. In normal operation the writer
        // drains continuously and the queue stays near-empty.
        private const int MaxPendingLines = 100_000;

        private static readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent signal = new AutoResetEvent(false);
        private static readonly object startLock = new object();

        // Report-time synchronous flush handshake (see FlushBlocking): the reporter (main thread) resets
        // flushCompleted, sets flushRequested, wakes the writer, and waits; the writer, right after its next
        // drain+flush, acknowledges by setting flushCompleted. ManualResetEventSlim so the wait is cheap and
        // re-armable; flushLock serialises the handshake (the report path is rare and main-thread, but the lock
        // keeps concurrent calls provably safe). Distinct from stopRequested: this keeps the writer ALIVE.
        private static readonly object flushLock = new object();
        private static volatile bool flushRequested;
        private static readonly ManualResetEventSlim flushCompleted = new ManualResetEventSlim(false);

        private static volatile bool disabled;
        private static volatile bool started;
        // Set by FlushAndClose to ask the writer loop to drain what remains, flush, and exit cleanly (game quit).
        private static volatile bool stopRequested;
        // The writer thread handle, kept so FlushAndClose can Join it (drain-and-flush guarantee on quit).
        private static Thread writerThread;
        private static string activePath;
        private static string prevPath;
        private static int pending; // approximate queue depth (Interlocked-maintained)

        /// <summary>
        /// Resolve the log directory and start the writer. Call ONCE from the mod constructor (main thread) so the
        /// path is captured where Unity/RimWorld path APIs are safe to read. <paramref name="consoleLogPath"/> is
        /// <c>UnityEngine.Application.consoleLogPath</c> (the full path to <c>Player.log</c>); the debug files are
        /// written alongside it, falling back to RimWorld's save-data folder if it is unavailable.
        /// </summary>
        public static void ConfigureDirectory(string consoleLogPath)
        {
            lock (startLock)
            {
                if (started || disabled) return;

                string dir = null;
                if (!string.IsNullOrEmpty(consoleLogPath))
                    dir = Path.GetDirectoryName(consoleLogPath);
                if (string.IsNullOrEmpty(dir))
                    dir = GenFilePaths.SaveDataFolderPath; // RimWorld config/saves root (always valid)

                activePath = Path.Combine(dir, "HaulersDream-debug.log");
                prevPath = Path.Combine(dir, "HaulersDream-debug.prev.log");

                // Start every game session with a FRESH active trace so a new run's log is never mixed with the
                // previous run's (diagnostic clarity: an issue report or a hand-read of HaulersDream-debug.log then
                // reflects THIS run only). Rotate any existing active file to *.prev.log, preserving the previous run
                // for look-back; the writer opens a fresh, empty active file below. A rotate fault must not block
                // logging startup (logger-never-throws policy), so on failure the writer simply appends as before.
                try
                {
                    Rotate();
                }
                catch (Exception e)
                {
                    Log.Warning(HDLog.Tag + "could not rotate the debug log at startup; this run's trace will be "
                        + "appended to the existing file instead of starting fresh. " + e);
                }

                writerThread = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "HaulersDream-DebugLog",
                };
                started = true;
                writerThread.Start();
            }
        }

        /// <summary>Append one already-formatted line (no trailing newline). Cheap and lock-free on the caller; the
        /// actual disk write happens on the background thread. A no-op once disk logging has been disabled.</summary>
        public static void Enqueue(string line)
        {
            if (disabled || line == null) return;
            if (Volatile.Read(ref pending) >= MaxPendingLines) return; // backstop against an unconfigured/stalled writer
            Interlocked.Increment(ref pending);
            queue.Enqueue(line);
            signal.Set();
        }

        /// <summary>
        /// Drain every queued line to disk and cleanly close the writer, BLOCKING the caller until the writer has
        /// flushed and exited (or <paramref name="timeoutMs"/> elapses). Wire this to game exit
        /// (<c>UnityEngine.Application.quitting</c>, which fires on the main thread BEFORE the runtime aborts
        /// background threads): the writer normally runs until Unity injects a <see cref="ThreadAbortException"/> at
        /// teardown, and that abort path deliberately does not flush, so the last lines enqueued right before quit
        /// (often the exact evidence being captured) would never reach the file. This asks the writer to do one
        /// final drain+flush+dispose first. Idempotent and a no-op when logging was never started or is already
        /// disabled. Never throws: a logger must not disrupt the game's own shutdown.
        /// </summary>
        public static void FlushAndClose(int timeoutMs = 2000)
        {
            if (!started || disabled)
                return;
            stopRequested = true;
            signal.Set(); // wake the writer now so it drains, flushes, and exits its loop instead of waiting
            try
            {
                writerThread?.Join(timeoutMs);
            }
            catch
            {
                // Join can only fault on a thread handle already torn down by the runtime at this point; nothing
                // actionable remains at quit time, and per this class's exception policy the logger never throws
                // out of the game's shutdown path. Any un-drained tail is bounded by the per-batch flush above.
            }
        }

        /// <summary>
        /// Block until every line enqueued so far has reached disk, WITHOUT stopping the writer (unlike
        /// <see cref="FlushAndClose"/>, which the game outlives after a report). Call on the main thread right
        /// before reading the trail for an in-game report (<see cref="GetReportTail"/>) so the report captures the
        /// newest lines instead of missing whatever was still sitting in the background queue. A no-op when logging
        /// was never started or is disabled. Never throws. Bounded by <paramref name="timeoutMs"/>: on a disk stall
        /// it returns anyway and the report attaches whatever did reach disk.
        /// </summary>
        public static void FlushBlocking(int timeoutMs = 2000)
        {
            if (!started || disabled)
                return;
            lock (flushLock)
            {
                flushCompleted.Reset();
                flushRequested = true;
                signal.Set(); // wake the writer to drain + flush the queue now
                try
                {
                    flushCompleted.Wait(timeoutMs);
                }
                catch
                {
                    // A wait fault here is not actionable and must not break the report flow; the report attaches
                    // whatever already reached disk (consistent with this class's logger-never-throws policy).
                }
            }
        }

        /// <summary>The newest <paramref name="maxBytes"/> of the trail (active file, back-filled from the rotated
        /// file when the active one is short), aligned to a line boundary. Null when nothing has been written.
        /// Runs on the main thread during a report and is intentionally not wrapped in try/catch.</summary>
        public static string GetReportTail(int maxBytes)
        {
            if (string.IsNullOrEmpty(activePath)) return null;

            byte[] active = ReadLastBytes(activePath, maxBytes);
            byte[] combined = active;
            if (active.Length < maxBytes)
            {
                byte[] prev = ReadLastBytes(prevPath, maxBytes - active.Length);
                if (prev.Length > 0)
                {
                    combined = new byte[prev.Length + active.Length];
                    Buffer.BlockCopy(prev, 0, combined, 0, prev.Length);
                    Buffer.BlockCopy(active, 0, combined, prev.Length, active.Length);
                }
            }

            if (combined.Length == 0) return null;
            string text = Encoding.UTF8.GetString(combined);
            // Drop a partial leading line if we truncated mid-file, so the report starts on a clean line.
            if (combined.Length >= maxBytes)
            {
                int nl = text.IndexOf('\n');
                if (nl >= 0 && nl + 1 < text.Length) text = text.Substring(nl + 1);
            }
            text = text.Trim('\r', '\n');
            return text.Length == 0 ? null : text;
        }

        // Reads the last `maxBytes` of a file (or the whole file if smaller). Returns an empty array if the file is
        // absent. Opened with FileShare.ReadWrite so it coexists with the live writer.
        private static byte[] ReadLastBytes(string path, int maxBytes)
        {
            if (maxBytes <= 0 || string.IsNullOrEmpty(path) || !File.Exists(path))
                return Array.Empty<byte>();

            // FileShare.Delete so a report read overlapping the writer's rotation (File.Delete(prev) + File.Move
            // active->prev) does not make those throw on the writer thread and latch disk logging off.
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long len = fs.Length;
                int count = (int)Math.Min(len, maxBytes);
                if (count <= 0) return Array.Empty<byte>();
                fs.Seek(len - count, SeekOrigin.Begin);
                var buf = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int n = fs.Read(buf, read, count - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read == count) return buf;
                var trimmed = new byte[read];
                Buffer.BlockCopy(buf, 0, trimmed, 0, read);
                return trimmed;
            }
        }

        private static void WriterLoop()
        {
            FileStream fs = null;
            try
            {
                fs = OpenActive();
                long size = fs.Length;

                while (true)
                {
                    signal.WaitOne(1000); // wake on a new line, or every second as a safety flush
                    bool wrote = false;
                    while (queue.TryDequeue(out string line))
                    {
                        Interlocked.Decrement(ref pending);
                        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                        fs.Write(bytes, 0, bytes.Length);
                        size += bytes.Length;
                        wrote = true;
                    }
                    if (wrote) fs.Flush();

                    // Acknowledge a synchronous flush request (the in-game reporter waiting to read the file):
                    // everything queued up to this wake has now been drained and flushed to disk. Runs even when
                    // nothing was written this wake (the queue was already empty and flushed in a prior iteration),
                    // so the waiting reporter is released promptly either way.
                    if (flushRequested)
                    {
                        flushRequested = false;
                        flushCompleted.Set();
                    }

                    // Clean shutdown requested (FlushAndClose, wired to game exit): drain any straggler enqueued
                    // during the flush above, flush once more, dispose the stream, and end the thread. This is what
                    // guarantees the log TAIL survives a quit. Unity aborts this IsBackground thread during teardown
                    // and the abort path below deliberately does NOT flush, so without this clean path the handful
                    // of lines enqueued right before "Exit game" (frequently the exact moment being diagnosed) would
                    // be lost. Enqueues have effectively stopped by quit time, so this final drain is bounded.
                    if (stopRequested)
                    {
                        bool tail = false;
                        while (queue.TryDequeue(out string line))
                        {
                            Interlocked.Decrement(ref pending);
                            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                            fs.Write(bytes, 0, bytes.Length);
                            tail = true;
                        }
                        if (tail) fs.Flush();
                        // Release any reporter waiting on a concurrent FlushBlocking (a report fired during quit):
                        // the queue is fully drained and flushed, so its data is on disk even as we close.
                        flushRequested = false;
                        flushCompleted.Set();
                        fs.Dispose();
                        fs = null;
                        return;
                    }

                    if (size >= ActiveCapBytes)
                    {
                        fs.Dispose();
                        fs = null;
                        Rotate();
                        fs = OpenActive();
                        size = 0;
                    }
                }
            }
            catch (ThreadAbortException)
            {
                // Benign teardown signal, NOT an I/O fault. The runtime aborts this IsBackground thread during
                // normal shutdown / AppDomain teardown, injecting a ThreadAbortException into the blocking
                // signal.WaitOne or an in-flight fs.Write/Flush. We must NOT Log.Error it (that would post a scary
                // red "I/O error" for an ordinary game exit, masquerading as a disk fault). This is distinct from
                // the genuine I/O catch below and is NOT exception suppression: a real I/O fault is a different
                // exception type that still reaches that catch and is surfaced loudly + disables disk logging.
                // We latch `disabled` so any late Enqueue is a cheap no-op, do NOT call Thread.ResetAbort (we WANT
                // this thread to end; the abort auto-rethrows after this block and terminates it), and do NOT
                // dispose `fs` (a Dispose re-flush could rethrow; the OS/finalizer reclaims the handle on teardown).
                disabled = true;
            }
            catch (Exception e)
            {
                // See EXCEPTION POLICY in the class summary: surface once, then degrade (do NOT rethrow off the
                // background thread, which would crash the game). We deliberately do NOT dispose `fs` here:
                // FileStream.Dispose re-flushes, which on a disk fault would re-throw the same error off this thread.
                // `disabled` latches, so at most one handle is left for the finalizer to reclaim.
                disabled = true;
                Log.Error(HDLog.Tag + "disk debug log writer stopped after an I/O error; disk logging is off for "
                    + "this session (in-game console logging is unaffected). " + e);
            }
        }

        private static FileStream OpenActive()
        {
            return new FileStream(activePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        }

        private static void Rotate()
        {
            if (File.Exists(prevPath)) File.Delete(prevPath);
            if (File.Exists(activePath)) File.Move(activePath, prevPath);
        }
    }
}
