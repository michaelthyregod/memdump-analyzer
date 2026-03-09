namespace MemDumpAnalyzer.Core.Models;

public record TypeDelta(
    string TypeName,
    long BaselineCount,
    long ProblemCount,
    long CountDelta,
    long BaselineSizeBytes,
    long ProblemSizeBytes,
    long SizeDeltaBytes
);

public record DiffResult(
    long HeapDeltaBytes,
    long Gen0DeltaBytes,
    long Gen1DeltaBytes,
    long Gen2DeltaBytes,
    long LohDeltaBytes,
    int ThreadDelta,
    IReadOnlyList<TypeDelta> GrowingTypes,
    IReadOnlyList<TypeDelta> ShrinkingTypes,
    IReadOnlyList<string> NewTypes,
    IReadOnlyList<string> DisappearedTypes,
    IReadOnlyList<Finding> Findings
);
