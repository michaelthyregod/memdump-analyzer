namespace MemDumpAnalyzer.Core.Models;

/// <summary>
/// An assembly found in thread stacks that is NOT in the known/trusted assembly filter.
/// Represents application-level code that may be contributing to the observed problems.
/// </summary>
public record AssemblyHotspot(
    string Assembly,
    int ThreadCount,         // threads with at least one frame from this assembly
    int BlockedThreadCount,  // subset that are also blocked/waiting
    IReadOnlyList<string> TopMethods  // most frequently seen method signatures (with per-method thread count)
);
