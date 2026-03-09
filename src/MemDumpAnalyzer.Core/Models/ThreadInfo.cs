namespace MemDumpAnalyzer.Core.Models;

public record ThreadInfo(
    uint OsThreadId,
    int ManagedThreadId,
    string State,
    string? CurrentException,
    IReadOnlyList<string> StackFrames,
    bool IsGcThread,
    bool IsBackground,
    string? BlockingReason
);
