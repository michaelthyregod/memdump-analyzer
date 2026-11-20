namespace MemDumpAnalyzer.Core.Models;

/// <summary>
/// One object on a retention path. <see cref="Via"/> is how its parent refers to it: a field (".name"),
/// an array element ("[]", plus the struct field when the elements are structs) or a dependent handle;
/// null for the root object.
/// </summary>
public record RetentionLink(string TypeName, ulong Address, string? Via);

/// <summary>
/// Why a large object is still alive: the chain from a GC root down to it, like WinDbg <c>!gcroot</c>.
/// </summary>
/// <param name="Root">What holds the first object: "static Ns.Type.field", "Strong handle", "finalizer queue", …</param>
/// <param name="Chain">Root object first, the object itself last. Very long chains keep their first
/// <see cref="KeptHeadLinks"/> and last <see cref="KeptTailLinks"/> links (the owners nearest the object explain most),
/// with <see cref="OmittedLinks"/> counting those left out in between.</param>
public record RetentionPath(
    string TypeName,
    ulong Address,
    long SizeBytes,
    string Root,
    IReadOnlyList<RetentionLink> Chain,
    int OmittedLinks = 0)
{
    public const int MaxLinks = 40;
    public const int KeptHeadLinks = 5;
    public const int KeptTailLinks = 25;

    /// <summary>One line: root and the types along the way, e.g. "static A.s_cache → Dictionary → Entry[] → Byte[]".</summary>
    public string Summary =>
        $"{Root} → " + string.Join(" → ", OmittedLinks > 0
            ? [.. Chain.Take(KeptHeadLinks).Select(l => l.TypeName), $"… {OmittedLinks:N0} links …", .. Chain.Skip(KeptHeadLinks).Select(l => l.TypeName)]
            : Chain.Select(l => l.TypeName));

    /// <summary>Multi-line form for technical details and reports: the root, then one link per line.</summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = new List<string>
        {
            $"{TypeName}  {SizeBytes:N0} bytes  @ {Address:x16}",
            $"  held by {Root}",
        };
        for (int i = 0; i < Chain.Count; i++)
        {
            if (OmittedLinks > 0 && i == KeptHeadLinks)
                lines.Add($"      … {OmittedLinks:N0} links omitted …");
            var link = Chain[i];
            var marker = i == Chain.Count - 1 ? "   ← this object" : "";
            lines.Add(link.Via == null
                ? $"    {link.TypeName}{marker}"
                : $"    {link.Via} → {link.TypeName}{marker}");
        }
        return lines;
    }
}

/// <summary>A large object for which no retention path was found within the searched roots.</summary>
public record RetentionTarget(string TypeName, ulong Address, long SizeBytes);

/// <summary>
/// Result of the retention-path search for the largest types.
/// </summary>
/// <param name="Complete">False when the time limit ran out or the heap could not be read in full; then
/// <see cref="NotFound"/> may contain objects that do have a path.</param>
/// <param name="RootsSearched">Which kinds of roots were searched, e.g. "static fields, handles, finalizer queue".</param>
/// <param name="StackRootsSearched">Thread stacks were searched too (<c>--gc-roots</c>).</param>
public record RetentionReport(
    IReadOnlyList<RetentionPath> Paths,
    IReadOnlyList<RetentionTarget> NotFound,
    bool Complete,
    string RootsSearched,
    bool StackRootsSearched,
    double Seconds,
    string? Note = null)
{
    public static RetentionReport None { get; } = new([], [], true, "", false, 0);

    public bool IsEmpty => Paths.Count == 0 && NotFound.Count == 0;
}
