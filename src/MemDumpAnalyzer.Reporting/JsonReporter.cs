using System.Text.Json;
using System.Text.Json.Serialization;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Reporting;

public static class JsonReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Render(AnalysisResult result)
        => JsonSerializer.Serialize(result, Options);

    public static async Task WriteAsync(AnalysisResult result, string outputPath)
    {
        var json = Render(result);
        await File.WriteAllTextAsync(outputPath, json);
    }
}
