using Microsoft.Diagnostics.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class GcAnalyzer
{
    public static GcStats Analyze(ClrRuntime runtime)
    {
        var heap = runtime.Heap;
        var segments = new List<GcSegmentInfo>();

        long gen0 = 0, gen1 = 0, gen2 = 0, loh = 0, poh = 0;
        long lohCommitted = 0, lohObjectBytes = 0;

        foreach (var seg in heap.Segments)
        {
            long committed = (long)seg.CommittedMemory.Length;
            long reserved = (long)seg.ReservedMemory.Length;
            long objectBytes = (long)(seg.ObjectRange.End - seg.ObjectRange.Start);

            string kindStr;
            int genNum;

            switch (seg.Kind)
            {
                case GCSegmentKind.Generation0:
                    kindStr = "Gen0"; genNum = 0; gen0 += committed; break;
                case GCSegmentKind.Generation1:
                    kindStr = "Gen1"; genNum = 1; gen1 += committed; break;
                case GCSegmentKind.Generation2:
                    kindStr = "Gen2"; genNum = 2; gen2 += committed; break;
                case GCSegmentKind.Ephemeral:
                    // Ephemeral segment hosts all 3 young gens
                    kindStr = "Ephemeral"; genNum = 0;
                    gen0 += (long)(seg.Generation0.End - seg.Generation0.Start);
                    gen1 += (long)(seg.Generation1.End - seg.Generation1.Start);
                    gen2 += (long)(seg.Generation2.End - seg.Generation2.Start);
                    break;
                case GCSegmentKind.Large:
                    kindStr = "LOH"; genNum = 3;
                    loh += committed;
                    lohCommitted += committed;
                    lohObjectBytes += objectBytes;
                    break;
                case GCSegmentKind.Pinned:
                    kindStr = "POH"; genNum = 3; poh += committed; break;
                case GCSegmentKind.Frozen:
                    kindStr = "Frozen"; genNum = 3; poh += committed; break;
                default:
                    kindStr = seg.Kind.ToString(); genNum = -1; break;
            }

            double fillRatio = committed > 0 ? (double)objectBytes / committed : 0;
            segments.Add(new GcSegmentInfo(genNum, kindStr, committed, reserved, fillRatio));
        }

        double lohFragPercent = lohCommitted > 0
            ? (1.0 - (double)lohObjectBytes / lohCommitted) * 100.0
            : 0;

        long total = gen0 + gen1 + gen2 + loh + poh;

        return new GcStats(total, gen0, gen1, gen2, loh, poh, lohFragPercent, segments);
    }
}
