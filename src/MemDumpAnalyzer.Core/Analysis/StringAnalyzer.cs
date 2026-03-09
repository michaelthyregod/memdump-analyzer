using Microsoft.Diagnostics.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class StringAnalyzer
{
    private const int TopN = 50;
    private const long MinWastedBytes = 1024 * 10; // 10 KB

    public static IReadOnlyList<StringDuplication> Analyze(ClrRuntime runtime)
    {
        var heap = runtime.Heap;
        if (!heap.CanWalkHeap)
            return Array.Empty<StringDuplication>();

        var stringType = heap.StringType;
        var stringCounts = new Dictionary<string, (long Count, long SizeEach)>();

        foreach (var obj in heap.EnumerateObjects())
        {
            if (obj.IsNull || !obj.IsValid || obj.Type == null) continue;
            if (obj.Type != stringType) continue;

            string value;
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            try { value = (string)obj ?? string.Empty; }
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
            catch { value = string.Empty; }
            if (value.Length > 512) value = value[..512];
            var size = (long)obj.Size;

            if (!stringCounts.TryGetValue(value, out var existing))
                stringCounts[value] = (1, size);
            else
                stringCounts[value] = (existing.Count + 1, size);
        }

        return stringCounts
            .Where(kvp => kvp.Value.Count > 1)
            .Select(kvp => new StringDuplication(
                Value: kvp.Key.Length > 120 ? kvp.Key[..120] + "…" : kvp.Key,
                InstanceCount: kvp.Value.Count,
                TotalWastedBytes: kvp.Value.SizeEach * (kvp.Value.Count - 1)
            ))
            .Where(s => s.TotalWastedBytes >= MinWastedBytes)
            .OrderByDescending(s => s.TotalWastedBytes)
            .Take(TopN)
            .ToList();
    }
}
