using System.Text.Json;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Reporting;

public static class JsonReporter
{
    public static string Render(AnalysisResult result)
        => JsonSerializer.Serialize(result, ReportJsonContext.Default.AnalysisResult);

    public static string Render(DiffResult diff)
        => JsonSerializer.Serialize(diff, ReportJsonContext.Default.DiffResult);

    public static async Task WriteAsync(AnalysisResult result, string outputPath, CancellationToken cancellationToken = default)
        => await File.WriteAllTextAsync(outputPath, Render(result), cancellationToken);

    public static async Task WriteAsync(DiffResult diff, string outputPath, CancellationToken cancellationToken = default)
        => await File.WriteAllTextAsync(outputPath, Render(diff), cancellationToken);
}
