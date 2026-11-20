using MemDumpAnalyzer.Core.Analysis;
using MemDumpAnalyzer.Core.Heuristics;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core;

/// <summary>One step of an analysis, for progress display: "step <see cref="Number"/> of <see cref="Total"/>".</summary>
public sealed record AnalysisStep(int Number, int Total, string Description);

public static class AnalysisEngine
{
    /// <param name="includeGcRoots">
    /// Also search thread stacks for the retention paths of the largest objects, with no time limit. Off by default:
    /// ClrMD reads stack roots at ~0.6 s per thread, ~80 minutes on a dump with 7,500 threads. Without it the search
    /// covers static fields, handles and the finalizer queue, within <see cref="RetentionAnalyzer.DefaultTimeLimit"/>.
    /// </param>
    /// <param name="progress">Told when each step starts. Called on the analysing thread.</param>
    public static AnalysisResult Analyze(
        string dumpPath,
        int topN = 100,
        string? knownAssembliesPath = null,
        CancellationToken cancellationToken = default,
        bool includeGcRoots = false,
        IProgress<AnalysisStep>? progress = null)
    {
        int total = 6, step = 0;
        void Step(string description) => progress?.Report(new AnalysisStep(++step, total, description));

        Step("Loading the dump");
        using var loaded = DumpLoader.Load(dumpPath, cancellationToken);
        var runtime = loaded.Runtime;

        Step($"Analysing {runtime.Threads.Length:N0} threads");
        var threadAnalysis  = ThreadAnalyzer.Analyze(runtime, cancellationToken);
        var threadPool      = ThreadAnalyzer.ReadThreadPool(runtime);

        Step("Walking the heap");
        var heapWalk        = HeapWalker.Walk(runtime.Heap, cancellationToken);   // one pass for heap, strings, leaks, free space

        Step("GC, strings and memory statistics");
        var heapTypes       = HeapAnalyzer.Analyze(heapWalk, topN);
        var gc              = GcAnalyzer.Analyze(runtime, heapWalk, cancellationToken);
        var strings         = StringAnalyzer.Analyze(heapWalk);

        Step(includeGcRoots
            ? $"Retention paths of the largest objects, including thread stacks — slow with many threads ({runtime.Threads.Length:N0})"
            : $"Retention paths of the largest objects (up to {RetentionAnalyzer.DefaultTimeLimit.TotalSeconds:N0} s)");
        var retention       = RetentionAnalyzer.Analyze(runtime, heapWalk, includeStackRoots: includeGcRoots,
                                  timeLimit: includeGcRoots ? null : RetentionAnalyzer.DefaultTimeLimit, cancellationToken);
        var leaks           = LeakDetector.Analyze(heapTypes, retention);

        Step("Findings and hotspots");
        var knownPrefixes   = ApplicationHotspotAnalyzer.LoadFilter(knownAssembliesPath);
        var hotspots        = ApplicationHotspotAnalyzer.Analyze(threadAnalysis.Threads, knownPrefixes, cancellationToken);

        var (findings, score) = FindingEngine.Evaluate(
            threadAnalysis.Threads, threadAnalysis.Groups,
            heapTypes, gc, strings, leaks, threadPool, heapWalk.Complete, cancellationToken);

        var dumpInfo = new FileInfo(dumpPath);

        return new AnalysisResult(
            DumpPath: dumpPath,
            CaptureTime: loaded.CaptureTimeUtc ?? dumpInfo.LastWriteTimeUtc,
            DumpFileSizeBytes: dumpInfo.Length,
            ClrVersion: loaded.ClrVersion,
            AppDomainName: loaded.AppDomainName,
            OsVersion: loaded.DataTarget.DataReader.DisplayName,
            Threads: threadAnalysis.Threads,
            ThreadGroups: threadAnalysis.Groups,
            HeapTypes: heapTypes,
            GcStats: gc,
            DuplicateStrings: strings,
            LeakSuspects: leaks,
            ApplicationHotspots: hotspots,
            KnownAssemblyFilters: knownPrefixes,
            Findings: findings,
            HealthScore: score,
            HeapWalkComplete: heapWalk.Complete,
            ThreadPool: threadPool,
            Retention: retention
        );
    }
}
