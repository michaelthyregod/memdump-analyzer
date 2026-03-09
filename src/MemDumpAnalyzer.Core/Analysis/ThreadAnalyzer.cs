using Microsoft.Diagnostics.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class ThreadAnalyzer
{
    private const int GroupSignatureDepth = 8;  // top N frames used to fingerprint a thread's work

    public record ThreadAnalysis(
        IReadOnlyList<ThreadInfo> Threads,
        IReadOnlyList<ThreadGroup> Groups);

    public static ThreadAnalysis Analyze(ClrRuntime runtime)
    {
        var threads = new List<ThreadInfo>();

        foreach (var thread in runtime.Threads)
        {
            var frames = thread.EnumerateStackTrace()
                .Select(FormatFrame)
                .ToList();

            string? blockingReason = thread.LockCount > 0 ? $"Holding {thread.LockCount} lock(s)" : null;
            // Supplement: detect blocking pattern even when LockCount == 0
            blockingReason ??= DetectBlockingFromFrames(frames) is { } wr ? $"Waiting on {wr}" : null;

            string? currentException = thread.CurrentException != null
                ? $"{thread.CurrentException.Type?.Name}: {thread.CurrentException.Message}"
                : null;

            threads.Add(new ThreadInfo(
                OsThreadId: thread.OSThreadId,
                ManagedThreadId: thread.ManagedThreadId,
                State: BuildStateString(thread),
                CurrentException: currentException,
                StackFrames: frames,
                IsGcThread: thread.IsGc,
                IsBackground: false,
                BlockingReason: blockingReason
            ));
        }

        var groups = BuildGroups(threads);
        return new ThreadAnalysis(threads, groups);
    }

    // ── Frame formatting ─────────────────────────────────────────────────────

    private static string FormatFrame(ClrStackFrame f)
    {
        if (f.Method != null)
        {
            var sig = f.Method.Signature ?? f.Method.Name ?? "<unnamed>";
            var assembly = GetAssembly(f);
            return assembly != null ? $"{sig}  [{assembly}]" : sig;
        }
        if (f.FrameName != null)
            return $"[{f.FrameName}]";
        return $"[frame @ 0x{f.InstructionPointer:x}]";
    }

    private static string? GetAssembly(ClrStackFrame f)
    {
        var modulePath = f.Method?.Type?.Module?.Name;
        if (string.IsNullOrEmpty(modulePath)) return null;
        return Path.GetFileNameWithoutExtension(modulePath);
    }

    // ── Wait-reason detection ─────────────────────────────────────────────────
    // Looks at the top frames of a thread and returns a human description of what it is waiting on.

    internal static string? DetectBlockingFromFrames(IReadOnlyList<string> frames)
    {
        foreach (var frame in frames)
        {
            // Async blocking
            if (frame.Contains("TaskAwaiter.GetResult") || frame.Contains("Task`1.get_Result") ||
                frame.Contains("Task.Wait(") || frame.Contains("Task.WaitAll") || frame.Contains("Task.WaitAny"))
                return "blocking async Task (.Result / .Wait) — causes thread-pool starvation";

            // Monitor / lock
            if (frame.Contains("Monitor.Wait(") || frame.Contains("Monitor.ObjWait"))
                return "CLR Monitor.Wait — thread released the lock and is waiting for a pulse";
            if (frame.Contains("Monitor.Enter(") || frame.Contains("Monitor.TryEnter(") ||
                frame.Contains("Monitor.ReliableEnter"))
                return "CLR Monitor.Enter — contended lock acquisition";

            // SQL
            if (frame.Contains("SqlCommand") || frame.Contains("SqlDataReader") ||
                frame.Contains("SqlConnection.Open") || frame.Contains("TdsParser"))
                return "SQL database I/O (ADO.NET / System.Data.SqlClient)";

            // HTTP / web
            if (frame.Contains("HttpWebRequest") || frame.Contains("HttpClient") ||
                frame.Contains("WebClient") || frame.Contains("ConnectStream"))
                return "outbound HTTP I/O";

            // Sockets / network
            if (frame.Contains("Socket.Receive") || frame.Contains("Socket.Accept") ||
                frame.Contains("Socket.Connect") || frame.Contains("NetworkStream"))
                return "network socket I/O";

            // File I/O
            if (frame.Contains("FileStream") || frame.Contains("StreamReader") ||
                frame.Contains("File.ReadAll") || frame.Contains("BinaryReader"))
                return "file I/O";

            // WaitHandle family
            if (frame.Contains("WaitHandle.WaitOne") || frame.Contains("WaitHandle.WaitAny") ||
                frame.Contains("EventWaitHandle") || frame.Contains("ManualResetEvent") ||
                frame.Contains("AutoResetEvent"))
                return "OS wait handle (event / semaphore)";

            if (frame.Contains("SemaphoreSlim.Wait") || frame.Contains("Semaphore.WaitOne"))
                return "semaphore";

            if (frame.Contains("Thread.Sleep("))
                return "Thread.Sleep";

            if (frame.Contains("GC.WaitForFullGCComplete") || frame.Contains("GC.Collect("))
                return "explicit GC.Collect / WaitForFullGC";

            // ASP.NET / IIS
            if (frame.Contains("HttpApplication.ExecuteStep") || frame.Contains("AspNetSynchronizationContext"))
                return "ASP.NET request pipeline";
        }
        return null;
    }

    // ── Thread grouping ───────────────────────────────────────────────────────

    private static IReadOnlyList<ThreadGroup> BuildGroups(IReadOnlyList<ThreadInfo> threads)
    {
        int total = threads.Count;
        if (total == 0) return Array.Empty<ThreadGroup>();

        // Fingerprint = top N non-runtime frames joined; empty string = no managed frames
        var bySignature = threads
            .GroupBy(t => BuildSignature(t.StackFrames))
            .OrderByDescending(g => g.Count())
            .Take(20)  // report top 20 patterns
            .ToList();

        return bySignature.Select(g =>
        {
            bool hasNoManagedFrames = g.Key == string.Empty;
            IReadOnlyList<string> representative = hasNoManagedFrames
                ? new[] { "(no managed stack — thread is idle or executing native/unmanaged code)" }
                : g.First().StackFrames;

            var sampleIds = g.Take(5).Select(t => t.OsThreadId).ToList();
            string? waitReason = hasNoManagedFrames
                ? "idle or native (no managed frames decoded)"
                : DetectBlockingFromFrames(representative);

            return new ThreadGroup(
                ThreadCount: g.Count(),
                Percentage: (double)g.Count() / total * 100,
                WaitReason: waitReason,
                RepresentativeStack: representative,
                SampleOsThreadIds: sampleIds
            );
        }).ToList();
    }

    private static string BuildSignature(IReadOnlyList<string> frames)
    {
        // Use the first GroupSignatureDepth frames that aren't pure runtime/OS transitions
        var meaningful = frames
            .Where(f => !f.StartsWith("[frame @") && !f.StartsWith("[NativeTransition]") &&
                        !f.StartsWith("[GCFrame]") && !f.StartsWith("[HelperMethod]"))
            .Take(GroupSignatureDepth);
        return string.Join("|", meaningful);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private static string BuildStateString(ClrThread thread)
    {
        var parts = new List<string>();
        if (thread.IsAlive) parts.Add("Alive");
        if (thread.IsGc) parts.Add("GC");
        if (thread.IsFinalizer) parts.Add("Finalizer");
        if (thread.LockCount > 0) parts.Add($"Locks:{thread.LockCount}");
        return parts.Count > 0 ? string.Join("|", parts) : "Unknown";
    }
}
