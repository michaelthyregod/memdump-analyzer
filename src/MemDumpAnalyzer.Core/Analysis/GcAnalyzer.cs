using Microsoft.Diagnostics.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class GcAnalyzer
{
    public static GcStats Analyze(ClrRuntime runtime, HeapWalkResult walk, CancellationToken cancellationToken = default)
    {
        var heap = runtime.Heap;
        var segments = new List<GcSegmentInfo>();

        long gen0 = 0, gen1 = 0, gen2 = 0, loh = 0, poh = 0;
        long lohRange = 0, gen2Range = 0;

        foreach (var seg in heap.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long committed = (long)seg.CommittedMemory.Length;
            long reserved = (long)seg.ReservedMemory.Length;
            long objectRange = (long)seg.ObjectRange.Length;
            long free = walk.FreeBytesBySegment.GetValueOrDefault(seg.ObjectRange.Start);

            string kindStr;
            int genNum;

            switch (seg.Kind)
            {
                case GCSegmentKind.Generation0:
                    kindStr = "Gen0"; genNum = 0; gen0 += committed; break;
                case GCSegmentKind.Generation1:
                    kindStr = "Gen1"; genNum = 1; gen1 += committed; break;
                case GCSegmentKind.Generation2:
                    kindStr = "Gen2"; genNum = 2; gen2 += committed; gen2Range += objectRange; break;
                case GCSegmentKind.Ephemeral:
                    // .NET Framework / pre-regions: one segment hosts gen0, gen1 and part of gen2
                    kindStr = "Ephemeral"; genNum = 0;
                    gen0 += (long)seg.Generation0.Length;
                    gen1 += (long)seg.Generation1.Length;
                    gen2 += (long)seg.Generation2.Length;
                    gen2Range += (long)seg.Generation2.Length;
                    break;
                case GCSegmentKind.Large:
                    kindStr = "LOH"; genNum = 3; loh += committed; lohRange += objectRange; break;
                case GCSegmentKind.Pinned:
                    kindStr = "POH"; genNum = 3; poh += committed; break;
                case GCSegmentKind.Frozen:
                    kindStr = "Frozen"; genNum = 3; poh += committed; break;
                default:
                    kindStr = seg.Kind.ToString(); genNum = -1; break;
            }

            double fillRatio = committed > 0 ? (double)Math.Max(0, objectRange - free) / committed : 0;
            segments.Add(new GcSegmentInfo(genNum, kindStr, committed, reserved, fillRatio, free));
        }

        // Fragmentation = free objects inside the allocated range (what SOS !dumpheap -stat lists as "Free")
        long lohFree = walk.FreeBytesByGeneration[HeapWalker.GenLoh];
        long gen2Free = walk.FreeBytesByGeneration[2];
        double lohFragPercent = lohRange > 0 ? (double)lohFree / lohRange * 100.0 : 0;
        double gen2FragPercent = gen2Range > 0 ? (double)gen2Free / gen2Range * 100.0 : 0;

        long total = gen0 + gen1 + gen2 + loh + poh;

        return new GcStats(total, gen0, gen1, gen2, loh, poh, lohFragPercent, segments,
            LohFreeBytes: lohFree, Gen2FreeBytes: gen2Free, Gen2FragmentationPercent: gen2FragPercent);
    }
}
