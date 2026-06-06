using System.Text.Json.Serialization;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Reporting;

/// <summary>
/// Source-generated JSON metadata for all report root types.
/// Replaces reflection-based serialization so the CLI is trim/AOT compatible.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnalysisResult))]
[JsonSerializable(typeof(DiffResult))]
public partial class ReportJsonContext : JsonSerializerContext
{
}
