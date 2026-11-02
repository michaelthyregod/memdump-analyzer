using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Diagnostics.Runtime;

namespace MemDumpAnalyzer.Core.Analysis;

/// <summary>
/// Walks the managed heap once and collects everything the heap, string, leak and GC analyzers need.
/// </summary>
/// <remarks>
/// When the dump was captured mid-GC, ClrMD reports <c>CanWalkHeap == false</c>. The walk then runs
/// best-effort: it keeps every valid object it can read and stops at the first unreadable region.
/// <see cref="HeapWalkResult.Complete"/> tells callers the numbers are partial.
/// </remarks>
public static class HeapWalker
{
    internal const int SamplesPerType = 3;
    private const int MaxExactStringKeyLength = 512;
    private const int PreviewLength = 120;

    // Index into HeapWalkResult.FreeBytesByGeneration
    internal const int GenLoh = 3;
    internal const int GenPohOrFrozen = 4;

    public static HeapWalkResult Walk(ClrHeap heap, CancellationToken cancellationToken = default)
    {
        var result = new HeapWalkResult { Complete = heap.CanWalkHeap };
        var stringType = heap.StringType;
        ClrSegment? segment = null;

        try
        {
            foreach (var obj in heap.EnumerateObjects())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (obj.IsNull || !obj.IsValid) continue;

                if (segment == null || !segment.ObjectRange.Contains(obj.Address))
                    segment = heap.GetSegmentByAddress(obj.Address);

                long size = (long)obj.Size;
                int gen = GetGeneration(segment, obj.Address);

                if (obj.IsFree)
                {
                    if (segment != null)
                    {
                        var key = segment.ObjectRange.Start;
                        result.FreeBytesBySegment[key] = result.FreeBytesBySegment.GetValueOrDefault(key) + size;
                    }
                    if (gen >= 0) result.FreeBytesByGeneration[gen] += size;
                    continue;
                }

                result.ObjectCount++;
                var typeName = obj.Type?.Name ?? "<unknown>";
                if (!result.Types.TryGetValue(typeName, out var acc))
                    result.Types[typeName] = acc = new TypeAccumulator();
                acc.Count++;
                acc.TotalSize += size;
                if (gen >= 0) acc.BytesByGeneration[gen] += size;
                if (acc.Samples.Count < SamplesPerType) acc.Samples.Add(obj.Address);

                if (stringType != null && obj.Type == stringType)
                    AddString(result, obj, size);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (!result.Complete)
        {
            // Mid-GC heap: objects can be half-moved and the walk can hit garbage. Keep what was read.
        }

        return result;
    }

    private static void AddString(HeapWalkResult result, ClrObject obj, long size)
    {
        string? value;
        try { value = obj.AsString(int.MaxValue); }
        catch { return; }
        if (value == null) return;

        // Short strings are keyed by their exact value. Long ones by length + SHA-256 of the full
        // content, so two long strings that only share a prefix are never counted as duplicates.
        string key = value.Length <= MaxExactStringKeyLength
            ? value
            : $"\0{value.Length}:{Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(value.AsSpan())))}";

        if (result.Strings.TryGetValue(key, out var acc))
        {
            acc.Count++;
        }
        else
        {
            var preview = value.Length > PreviewLength ? value[..PreviewLength] + "…" : value;
            result.Strings[key] = new StringAccumulator { Count = 1, SizeEach = size, Preview = preview };
        }
    }

    /// <summary>0–2 for SOH generations, 3 for LOH, 4 for POH/frozen, -1 when unknown.</summary>
    internal static int GetGeneration(ClrSegment? segment, ulong address)
    {
        if (segment == null) return -1;
        return segment.Kind switch
        {
            GCSegmentKind.Generation0 => 0,
            GCSegmentKind.Generation1 => 1,
            GCSegmentKind.Generation2 => 2,
            GCSegmentKind.Ephemeral => segment.Generation0.Contains(address) ? 0
                                     : segment.Generation1.Contains(address) ? 1
                                     : segment.Generation2.Contains(address) ? 2
                                     : 0,
            GCSegmentKind.Large => GenLoh,
            GCSegmentKind.Pinned => GenPohOrFrozen,
            GCSegmentKind.Frozen => GenPohOrFrozen,
            _ => -1
        };
    }
}

public sealed class HeapWalkResult
{
    /// <summary>False when the heap was not walkable (dump captured mid-GC); all numbers are then partial.</summary>
    public bool Complete { get; init; }
    public long ObjectCount { get; internal set; }
    internal Dictionary<string, TypeAccumulator> Types { get; } = new();
    internal Dictionary<string, StringAccumulator> Strings { get; } = new();
    internal Dictionary<ulong, long> FreeBytesBySegment { get; } = new();
    internal long[] FreeBytesByGeneration { get; } = new long[5];
}

internal sealed class TypeAccumulator
{
    public long Count;
    public long TotalSize;
    public readonly long[] BytesByGeneration = new long[5];
    public readonly List<ulong> Samples = new(HeapWalker.SamplesPerType);
}

internal sealed class StringAccumulator
{
    public long Count;
    public long SizeEach;
    public required string Preview;
}
