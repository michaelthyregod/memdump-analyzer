using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class StringAnalyzer
{
    private const int TopN = 50;
    private const long MinWastedBytes = 1024 * 10; // 10 KB

    /// <summary>String values stored more than once, ranked by the bytes the extra copies waste.</summary>
    public static IReadOnlyList<StringDuplication> Analyze(HeapWalkResult walk)
    {
        return walk.Strings.Values
            .Where(s => s.Count > 1)
            .Select(s => new StringDuplication(
                Value: s.Preview,
                InstanceCount: s.Count,
                TotalWastedBytes: s.SizeEach * (s.Count - 1)
            ))
            .Where(s => s.TotalWastedBytes >= MinWastedBytes)
            .OrderByDescending(s => s.TotalWastedBytes)
            .Take(TopN)
            .ToList();
    }
}
