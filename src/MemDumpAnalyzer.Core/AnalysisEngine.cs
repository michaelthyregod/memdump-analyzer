using MemDumpAnalyzer.Core.Analysis;
using MemDumpAnalyzer.Core.Heuristics;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core;

public static class AnalysisEngine
{
    public static AnalysisResult Analyze(
        string dumpPath,
        int topN = 100,
        string? knownAssembliesPath = null,
        CancellationToken cancellationToken = default)
    {
        using var loaded = DumpLoader.Load(dumpPath, cancellationToken);
        var runtime = loaded.Runtime;

        var threadAnalysis  = ThreadAnalyzer.Analyze(runtime, cancellationToken);
        var heapTypes       = HeapAnalyzer.Analyze(runtime, topN, cancellationToken);
        var gc              = GcAnalyzer.Analyze(runtime, cancellationToken);
        var strings         = StringAnalyzer.Analyze(runtime, cancellationToken);
        var leaks           = LeakDetector.Analyze(runtime, heapTypes, cancellationToken);

        var knownPrefixes   = ApplicationHotspotAnalyzer.LoadFilter(knownAssembliesPath);
        var hotspots        = ApplicationHotspotAnalyzer.Analyze(threadAnalysis.Threads, knownPrefixes, cancellationToken);

        var (findings, score) = FindingEngine.Evaluate(
            threadAnalysis.Threads, threadAnalysis.Groups,
            heapTypes, gc, strings, leaks, cancellationToken);

        var dumpInfo = new FileInfo(dumpPath);

        return new AnalysisResult(
            DumpPath: dumpPath,
            CaptureTime: dumpInfo.LastWriteTimeUtc,
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
            HealthScore: score
        );
    }
}
