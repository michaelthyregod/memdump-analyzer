namespace MemDumpAnalyzer.Core.Models;

public enum Severity { Info, Warning, Critical }

public record Finding(
    string Id,
    Severity Severity,
    string Summary,
    string Explanation,
    string? Evidence = null,
    string? Recommendation = null,
    IReadOnlyList<string>? TechnicalDetails = null  // multi-line technical breakdown, rendered as preformatted text
);
