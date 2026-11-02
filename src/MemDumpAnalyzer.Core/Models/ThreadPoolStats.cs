namespace MemDumpAnalyzer.Core.Models;

/// <summary>
/// CLR worker thread-pool state at capture time — the same data as SOS <c>!threadpool</c>.
/// <see cref="CpuUtilization"/> is system-wide, not just this process.
/// </summary>
public record ThreadPoolStats(
    int CpuUtilization,
    int MinThreads,
    int MaxThreads,
    int ActiveWorkerThreads,
    int IdleWorkerThreads,
    int RetiredWorkerThreads
);
