using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class ApplicationHotspotAnalyzer
{
    /// <summary>
    /// Scans all thread stacks, extracts frames from assemblies that are NOT suppressed by the
    /// filter rules, and returns them ranked by how many threads reference each assembly.
    ///
    /// Filter rule syntax (one per line in the filter file):
    ///   -Prefix   or just   Prefix  — exclude (treat as known/trusted infrastructure)
    ///   +Prefix                     — explicit include (show in hotspots even if a broader
    ///                                 exclude rule would otherwise suppress it)
    /// Include rules take priority over exclude rules, so "+Sitecore.SessionProvider" combined
    /// with "-Sitecore" will surface only Sitecore.SessionProvider.* assemblies.
    /// </summary>
    public static IReadOnlyList<AssemblyHotspot> Analyze(
        IReadOnlyList<ThreadInfo> threads,
        IReadOnlyList<string> filterLines,
        CancellationToken cancellationToken = default)
    {
        if (filterLines.Count == 0 || threads.Count == 0)
            return [];

        // Parse into include (+) and exclude (-) prefix lists
        var includes = new List<string>();
        var excludes = new List<string>();
        foreach (var line in filterLines)
        {
            if (line.StartsWith('+'))      includes.Add(line[1..].TrimStart());
            else if (line.StartsWith('-')) excludes.Add(line[1..].TrimStart());
            else                           excludes.Add(line);
        }

        var threadSets   = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
        var blockedSets  = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
        // method → number of threads (not frames) that contain this method
        var methodThreads = new Dictionary<string, Dictionary<string, HashSet<uint>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var thread in threads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool blocked = thread.BlockingReason != null;
            // Track which assemblies and methods we've already counted for this thread
            var seenAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenMethods    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var frame in thread.StackFrames)
            {
                var assembly = ExtractAssembly(frame);
                if (assembly == null || IsKnown(assembly, includes, excludes)) continue;

                if (seenAssemblies.Add(assembly))
                {
                    GetOrAdd(threadSets, assembly).Add(thread.OsThreadId);
                    if (blocked) GetOrAdd(blockedSets, assembly).Add(thread.OsThreadId);
                }

                var method = ExtractMethod(frame);
                if (seenMethods.Add($"{assembly}|{method}"))
                {
                    var asmMethods = GetOrAddDict(methodThreads, assembly);
                    GetOrAdd(asmMethods, method).Add(thread.OsThreadId);
                }
            }
        }

        return threadSets
            .OrderByDescending(kv => kv.Value.Count)
            .Select(kv =>
            {
                var asmMethods = methodThreads.TryGetValue(kv.Key, out var m) ? m : new Dictionary<string, HashSet<uint>>();
                var topMethods = asmMethods
                    .OrderByDescending(p => p.Value.Count)
                    .Take(10)
                    .Select(p => $"{p.Key}  ({p.Value.Count} thread{(p.Value.Count == 1 ? "" : "s")})")
                    .ToList();

                return new AssemblyHotspot(
                    Assembly: kv.Key,
                    ThreadCount: kv.Value.Count,
                    BlockedThreadCount: blockedSets.TryGetValue(kv.Key, out var bs) ? bs.Count : 0,
                    TopMethods: topMethods
                );
            })
            .ToList();
    }

    /// <summary>
    /// Reads filter prefixes from a text file — one prefix per line, lines starting with # or // are comments.
    /// An assembly is considered "known/trusted" if its name starts with any of the returned prefixes.
    /// </summary>
    public static IReadOnlyList<string> LoadFilter(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return [];

        return File.ReadAllLines(filePath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("//"))
            .ToList();
    }

    // Frames are formatted as "Method.Signature()  [AssemblyName]"
    private static string? ExtractAssembly(string frame)
    {
        int bracketStart = frame.LastIndexOf("  [");
        int bracketEnd   = frame.LastIndexOf(']');
        if (bracketStart < 0 || bracketEnd <= bracketStart + 3) return null;
        return frame.Substring(bracketStart + 3, bracketEnd - bracketStart - 3);
    }

    private static string ExtractMethod(string frame)
    {
        int asmStart = frame.LastIndexOf("  [");
        return asmStart > 0 ? frame[..asmStart] : frame;
    }

    /// <summary>
    /// Returns true when the assembly should be treated as known/trusted (hidden from hotspots).
    /// Include rules take priority: if any include prefix matches, the assembly is NOT known.
    /// </summary>
    private static bool IsKnown(string assembly, List<string> includes, List<string> excludes)
    {
        // An explicit include always wins — expose this assembly regardless of any exclude
        if (includes.Any(p => assembly.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return false;
        return excludes.Any(p => assembly.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<uint> GetOrAdd(Dictionary<string, HashSet<uint>> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v)) dict[key] = v = [];
        return v;
    }

    private static Dictionary<string, HashSet<uint>> GetOrAddDict(
        Dictionary<string, Dictionary<string, HashSet<uint>>> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v)) dict[key] = v = new Dictionary<string, HashSet<uint>>();
        return v;
    }
}
