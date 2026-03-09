namespace MemDumpAnalyzer.Core.Models;

public record HeapTypeStats(
    string TypeName,
    long InstanceCount,
    long TotalSizeBytes,
    long OwnSizeBytes,
    int Generation
);
