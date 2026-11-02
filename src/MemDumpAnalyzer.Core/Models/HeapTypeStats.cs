namespace MemDumpAnalyzer.Core.Models;

public record HeapTypeStats(
    string TypeName,
    long InstanceCount,
    long TotalSizeBytes,
    long OwnSizeBytes,
    int Generation      // generation holding most of this type's bytes (3 = LOH/POH/frozen, -1 = unknown)
);
