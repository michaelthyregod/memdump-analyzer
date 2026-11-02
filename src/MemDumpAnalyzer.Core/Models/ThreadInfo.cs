namespace MemDumpAnalyzer.Core.Models;

/// <summary>What a thread is waiting on, classified from its top stack frames.</summary>
public enum WaitKind
{
    /// <summary>Not waiting (running), or no managed frames to judge from.</summary>
    None,
    /// <summary>Waiting to enter a lock (Monitor.Enter / lock statement) or a SemaphoreSlim gate (synchronous Wait).</summary>
    LockContention,
    /// <summary>Blocking on an async Task (.Result / .Wait / GetAwaiter().GetResult()).</summary>
    SyncOverAsync,
    /// <summary>Waiting on an external dependency: SQL, Redis, HTTP, Solr, network I/O.</summary>
    Dependency,
    /// <summary>Waiting on file I/O.</summary>
    FileIo,
    /// <summary>Waiting on an event, semaphore or Monitor.Wait signal with no more specific context.</summary>
    Signal,
    /// <summary>Thread.Sleep.</summary>
    Sleep,
    /// <summary>Worker thread idling until work arrives (e.g. Sitecore ManagedThreadPool) — noise, not a problem.</summary>
    IdleWorker
}

public record ThreadInfo(
    uint OsThreadId,
    int ManagedThreadId,
    string State,
    string? CurrentException,
    IReadOnlyList<string> StackFrames,
    bool IsGcThread,
    bool IsBackground,
    string? BlockingReason,             // set for every wait except IdleWorker; null when running
    bool IsAlive = true,                // dead threads still appear in the CLR thread list until cleaned up
    bool IsFinalizer = false,
    WaitKind WaitKind = WaitKind.None
);
