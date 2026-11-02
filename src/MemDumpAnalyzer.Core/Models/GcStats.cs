namespace MemDumpAnalyzer.Core.Models;

public record GcSegmentInfo(
    int Generation,
    string Kind,
    long CommittedBytes,
    long ReservedBytes,
    double FillRatio,       // (allocated object range − free objects) / committed
    long FreeBytes = 0      // bytes in free objects (fragmentation) found by the heap walk
);

public record GcStats(
    long TotalHeapBytes,
    long Gen0Bytes,
    long Gen1Bytes,
    long Gen2Bytes,
    long LohBytes,
    long PohBytes,
    double LohFragmentationPercent,     // LOH free-object bytes / LOH allocated range
    IReadOnlyList<GcSegmentInfo> Segments,
    long LohFreeBytes = 0,
    long Gen2FreeBytes = 0,
    double Gen2FragmentationPercent = 0 // Gen2 free-object bytes / Gen2 allocated range
);
