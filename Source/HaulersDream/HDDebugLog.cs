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
    /// <see cref="ActiveCapBytes"/> and rotated once to <c>*.prev.log</c>, so on-disk usage stays at or below
    /// 2× that cap while still retaining a long look-back window (typically several in-game days of trace).
    ///
    /// Writes happen on a dedicated background thread fed by a lock-free queue, so the game thread (and the
    /// off-main-thread universal exception finalizer) never block on disk I/O. Lines are appended in the order
    /// enqueued; the writer batches and flushes whatever has accumulated each wake-up.
    ///
    /// EXCEPTION POLICY (deliberate, see <c>no-exception-suppression</c>): the writer loop catches an I/O fault,
    /// reports it ONCE via <see cref="Log.Error"/> (so it stays visible, not swallowed), and then disables disk
    /// logging for the rest of the session. A background-thread exception would otherwise terminate the process —
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

        private static volatile bool disabled;
        private static volatile bool started;
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
                    dir = GenFilePaths.SaveDataFolderPath; // RimWorld config/saves root — always valid

                activePath = Path.Combine(dir, "HaulersDream-debug.log");
                prevPath = Path.Combine(dir, "HaulersDream-debug.prev.log");

                var worker = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "HaulersDream-DebugLog",
                };
                started = true;
                worker.Start();
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
