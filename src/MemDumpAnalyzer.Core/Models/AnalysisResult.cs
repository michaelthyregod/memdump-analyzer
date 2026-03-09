namespace MemDumpAnalyzer.Core.Models;

public record StringDuplication(
    string Value,
    long InstanceCount,
    long TotalWastedBytes
);

public record LeakSuspect(
    string TypeName,
    long InstanceCount,
    long TotalSizeBytes,
    IReadOnlyList<string> RootPaths
);

public record AnalysisResult(
    // Metadata
    string DumpPath,
    DateTime CaptureTime,
    long DumpFileSizeBytes,
    string ClrVersion,
    string AppDomainName,
    string OsVersion,

    // Sub-analyses
    IReadOnlyList<ThreadInfo> Threads,
    IReadOnlyList<ThreadGroup> ThreadGroups,           // threads clustered by stack pattern
    IReadOnlyList<HeapTypeStats> HeapTypes,
    GcStats GcStats,
    IReadOnlyList<StringDuplication> DuplicateStrings,
    IReadOnlyList<LeakSuspect> LeakSuspects,
    IReadOnlyList<AssemblyHotspot> ApplicationHotspots, // app assemblies not in known filter
    IReadOnlyList<string> KnownAssemblyFilters,         // prefixes loaded from filter file

    // Findings
    IReadOnlyList<Finding> Findings,
    int HealthScore
);
