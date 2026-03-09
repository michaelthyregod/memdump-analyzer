namespace MemDumpAnalyzer.Core.Models;

public record GcSegmentInfo(
    int Generation,
    string Kind,
    long CommittedBytes,
    long ReservedBytes,
    double FillRatio
);

public record GcStats(
    long TotalHeapBytes,
    long Gen0Bytes,
    long Gen1Bytes,
    long Gen2Bytes,
    long LohBytes,
    long PohBytes,
    double LohFragmentationPercent,
    IReadOnlyList<GcSegmentInfo> Segments
);
