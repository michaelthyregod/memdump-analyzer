using Microsoft.Diagnostics.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class ThreadAnalyzer
{
    private const int GroupSignatureDepth = 8;   // top N frames used to fingerprint a thread's work
    private const int PrimitiveSearchDepth = 8;  // a wait primitive must be this close to the top to count
    private const int ContextSearchDepth = 30;   // how deep to look for what the wait is for

    public record ThreadAnalysis(
        IReadOnlyList<ThreadInfo> Threads,
        IReadOnlyList<ThreadGroup> Groups);

    public static ThreadAnalysis Analyze(ClrRuntime runtime, CancellationToken cancellationToken = default)
    {
        var threads = new List<ThreadInfo>();

        foreach (var thread in runtime.Threads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Dead threads stay in the CLR thread list until cleaned up; they have no stack to read
            IReadOnlyList<string> frames = thread.IsAlive
                ? thread.EnumerateStackTrace().Select(FormatFrame).ToList()
                : [];

            var (kind, description) = ClassifyWait(frames);

            threads.Add(new ThreadInfo(
                OsThreadId: thread.OSThreadId,
                ManagedThreadId: thread.ManagedThreadId,
                State: BuildStateString(thread),
                CurrentException: thread.IsAlive ? TryGetCurrentException(thread) : null,
                StackFrames: frames,
                IsGcThread: thread.IsGc,
                IsBackground: false,
                BlockingReason: kind is WaitKind.None or WaitKind.IdleWorker ? null : $"Waiting on {description}",
                IsAlive: thread.IsAlive,
                IsFinalizer: thread.IsFinalizer,
                WaitKind: kind
            ));
        }

        var groups = BuildGroups(threads);
        return new ThreadAnalysis(threads, groups);
    }

    /// <summary>Reads the CLR worker thread pool (SOS <c>!threadpool</c>); null when the dump doesn't expose it.</summary>
    public static ThreadPoolStats? ReadThreadPool(ClrRuntime runtime)
    {
        try
        {
            var tp = runtime.ThreadPool;
            if (tp == null) return null;
            return new ThreadPoolStats(
                CpuUtilization: tp.CpuUtilization,
                MinThreads: tp.MinThreads,
                MaxThreads: tp.MaxThreads,
                ActiveWorkerThreads: tp.ActiveWorkerThreads,
                IdleWorkerThreads: tp.IdleWorkerThreads,
                RetiredWorkerThreads: tp.RetiredWorkerThreads);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    // ClrThread.CurrentException reconstructs the exception object from the heap and throws
    // InvalidOperationException ("Cannot construct a ClrException with a null Type") when the
    // exception's type can't be resolved from the dump. Swallow that and treat as "no exception".
    private static string? TryGetCurrentException(ClrThread thread)
    {
        try
        {
            var ex = thread.CurrentException;
            return ex != null ? $"{ex.Type?.Name}: {ex.Message}" : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

    // ── Wait classification ───────────────────────────────────────────────────
    //
    // Two steps:
    //  1. The wait primitive — must be near the top of the stack, otherwise the thread is running
    //     and a primitive deeper down is just a caller that happens to be on the stack.
    //  2. The context — the first frame (top-down) that says what the wait is for: a SQL read,
    //     a Redis call, .Result on a Task, an idle worker loop. Searching for context first is
    //     what lets "Task.Wait" win over the Monitor.Wait that implements it.

    private enum Primitive { None, LockEnter, SemaphoreGate, Signal, Sleep, Io }

    private static readonly (string Pattern, Primitive Kind)[] Primitives =
    [
        ("System.Threading.Monitor.Enter(", Primitive.LockEnter),
        ("System.Threading.Monitor.ReliableEnter", Primitive.LockEnter),
        ("System.Threading.Monitor.TryEnter(", Primitive.LockEnter),
        ("[JIT_MonEnter", Primitive.LockEnter),
        ("System.Threading.Monitor.Wait(", Primitive.Signal),
        ("System.Threading.Monitor.ObjWait", Primitive.Signal),
        ("System.Threading.WaitHandle.", Primitive.Signal),
        ("System.Threading.ManualResetEventSlim.Wait", Primitive.Signal),
        ("System.Threading.SemaphoreSlim.Wait(", Primitive.SemaphoreGate),
        ("System.Threading.Thread.Join", Primitive.Signal),
        ("System.Threading.Thread.Sleep", Primitive.Sleep),
        ("System.Threading.Thread.SleepInternal", Primitive.Sleep),
        ("SNINativeMethodWrapper.", Primitive.Io),
        ("System.Net.Sockets.Socket.", Primitive.Io),
        ("System.Net.Sockets.NetworkStream.", Primitive.Io),
        ("System.IO.FileStream.Read", Primitive.Io),
        ("System.IO.FileStream.Write", Primitive.Io),
        ("System.IO.__Error", Primitive.Io),
        ("Win32Native.ReadFile", Primitive.Io),
    ];

    private static readonly (string Pattern, WaitKind Kind, string Description)[] Contexts =
    [
        // Idle workers waiting for work — noise, not a problem (makingitfaster.tips: idle
        // Sitecore.Threading.ManagedThreadPool threads use no CPU)
        ("Sitecore.Threading.Semaphore.P(", WaitKind.IdleWorker, "idle worker (Sitecore ManagedThreadPool)"),
        ("Sitecore.Threading.ManagedThreadPool", WaitKind.IdleWorker, "idle worker (Sitecore ManagedThreadPool)"),
        ("DedicatedThreadPoolPipeScheduler.RunWorkLoop", WaitKind.IdleWorker, "idle worker (StackExchange.Redis scheduler)"),

        // Blocking on async code
        ("System.Threading.Tasks.Task.Wait", WaitKind.SyncOverAsync, "async Task (.Result / .Wait)"),
        ("System.Threading.Tasks.Task.InternalWait", WaitKind.SyncOverAsync, "async Task (.Result / .Wait)"),
        ("System.Threading.Tasks.Task`1.get_Result", WaitKind.SyncOverAsync, "async Task (.Result / .Wait)"),
        ("System.Threading.Tasks.Task`1.GetResultCore", WaitKind.SyncOverAsync, "async Task (.Result / .Wait)"),
        ("System.Runtime.CompilerServices.TaskAwaiter", WaitKind.SyncOverAsync, "async Task (.GetAwaiter().GetResult())"),

        // External dependencies
        ("System.Data.SqlClient.", WaitKind.Dependency, "SQL Server (ADO.NET)"),
        ("Microsoft.Data.SqlClient.", WaitKind.Dependency, "SQL Server (ADO.NET)"),
        ("StackExchange.Redis.", WaitKind.Dependency, "Redis (StackExchange.Redis)"),
        ("SolrNet.", WaitKind.Dependency, "Solr (SolrNet)"),
        ("System.Net.HttpWebRequest", WaitKind.Dependency, "outbound HTTP"),
        ("System.Net.Http.HttpClient", WaitKind.Dependency, "outbound HTTP"),
        ("System.Net.WebClient", WaitKind.Dependency, "outbound HTTP"),
        ("System.Net.ConnectStream", WaitKind.Dependency, "outbound HTTP"),
        ("System.Net.Sockets.", WaitKind.Dependency, "network socket I/O"),
        ("System.IO.FileStream", WaitKind.FileIo, "file I/O"),
        ("System.IO.File.", WaitKind.FileIo, "file I/O"),
    ];

    /// <summary>Classifies what a thread is waiting on from its formatted stack frames (top first).</summary>
    public static (WaitKind Kind, string? Description) ClassifyWait(IReadOnlyList<string> frames)
    {
        var meaningful = frames.Where(IsMeaningful).ToList();
        if (meaningful.Count == 0) return (WaitKind.None, null);

        var primitive = Primitive.None;
        foreach (var frame in meaningful.Take(PrimitiveSearchDepth))
        {
            foreach (var (pattern, kind) in Primitives)
            {
                if (frame.Contains(pattern, StringComparison.Ordinal)) { primitive = kind; break; }
            }
            if (primitive != Primitive.None) break;
        }

        if (primitive == Primitive.None) return (WaitKind.None, null);

        // SemaphoreSlim.Wait is implemented with Monitor.Wait, so the Monitor frame is on top. A synchronous
        // SemaphoreSlim.Wait right below it is a gate (often used as an async-friendly lock): treat it as one.
        if (primitive == Primitive.Signal &&
            meaningful.Take(PrimitiveSearchDepth).Any(f => f.Contains("System.Threading.SemaphoreSlim.Wait(", StringComparison.Ordinal)))
            primitive = Primitive.SemaphoreGate;

        if (primitive == Primitive.SemaphoreGate)
        {
            // An idle worker loop can use a semaphore to wait for work — that's not contention
            foreach (var frame in meaningful.Take(ContextSearchDepth))
            {
                foreach (var (pattern, kind, description) in Contexts)
                {
                    if (kind == WaitKind.IdleWorker && frame.Contains(pattern, StringComparison.Ordinal))
                        return (kind, description);
                }
            }
            var gateSite = GetLockSite(frames);
            return (WaitKind.LockContention, gateSite != null ? $"SemaphoreSlim.Wait in {gateSite}" : "SemaphoreSlim.Wait");
        }

        // A thread waiting to enter a lock is contending, whatever it was going to do with it
        if (primitive == Primitive.LockEnter)
        {
            var site = GetLockSite(frames);
            return (WaitKind.LockContention, site != null ? $"contended lock (Monitor.Enter) in {site}" : "contended lock (Monitor.Enter)");
        }

        foreach (var frame in meaningful.Take(ContextSearchDepth))
        {
            foreach (var (pattern, kind, description) in Contexts)
            {
                if (frame.Contains(pattern, StringComparison.Ordinal))
                    return (kind, description);
            }
        }

        return primitive switch
        {
            Primitive.Sleep => (WaitKind.Sleep, "Thread.Sleep"),
            Primitive.Io => (WaitKind.Dependency, "I/O"),
            _ => (WaitKind.Signal, "an event / semaphore / Monitor.Wait signal")
        };
    }

    /// <summary>The method that called Monitor.Enter or SemaphoreSlim.Wait — i.e. where the contended lock is.</summary>
    public static string? GetLockSite(IReadOnlyList<string> frames)
    {
        int lastMonitor = -1;
        for (int i = 0; i < frames.Count && i < ContextSearchDepth; i++)
        {
            if (frames[i].Contains("System.Threading.Monitor.", StringComparison.Ordinal) ||
                frames[i].Contains("System.Threading.SemaphoreSlim.", StringComparison.Ordinal) ||
                frames[i].StartsWith("[JIT_MonEnter", StringComparison.Ordinal))
                lastMonitor = i;
            else if (lastMonitor >= 0 && IsMeaningful(frames[i]))
                return frames[i];
        }
        return null;
    }

    /// <summary>Kept for callers that only need the description.</summary>
    internal static string? DetectBlockingFromFrames(IReadOnlyList<string> frames) => ClassifyWait(frames).Description;

    private static bool IsMeaningful(string frame) =>
        !frame.StartsWith("[frame @", StringComparison.Ordinal) &&
        !frame.StartsWith("[NativeTransition]", StringComparison.Ordinal) &&
        !frame.StartsWith("[GCFrame]", StringComparison.Ordinal) &&
        !frame.StartsWith("[HelperMethod]", StringComparison.Ordinal) &&
        !frame.StartsWith("[HelperMethodFrame", StringComparison.Ordinal);

    // ── Thread grouping ───────────────────────────────────────────────────────

    private static IReadOnlyList<ThreadGroup> BuildGroups(IReadOnlyList<ThreadInfo> threads)
    {
        // Dead threads have no stack and say nothing about what the process is doing
        var alive = threads.Where(t => t.IsAlive).ToList();
        int total = alive.Count;
        if (total == 0) return [];

        // Fingerprint = top N non-runtime frames joined; empty string = no managed frames
        var bySignature = alive
            .GroupBy(t => BuildSignature(t.StackFrames))
            .OrderByDescending(g => g.Count())
            .Take(20)  // report top 20 patterns
            .ToList();

        return bySignature.Select(g =>
        {
            bool hasNoManagedFrames = g.Key == string.Empty;
            IReadOnlyList<string> representative = hasNoManagedFrames
                ? ["(no managed stack — thread is idle or executing native/unmanaged code)"]
                : g.First().StackFrames;

            var sampleIds = g.Take(5).Select(t => t.OsThreadId).ToList();
            string? waitReason = hasNoManagedFrames
                ? "idle or native (no managed frames decoded)"
                : ClassifyWait(representative).Description;

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
        var meaningful = frames.Where(IsMeaningful).Take(GroupSignatureDepth);
        return string.Join("|", meaningful);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private static string BuildStateString(ClrThread thread)
    {
        var parts = new List<string>();
        parts.Add(thread.IsAlive ? "Alive" : "Dead");
        if (thread.IsGc) parts.Add("GC");
        if (thread.IsFinalizer) parts.Add("Finalizer");
        if (thread.LockCount > 0) parts.Add($"Locks:{thread.LockCount}");
        return string.Join("|", parts);
    }
}
