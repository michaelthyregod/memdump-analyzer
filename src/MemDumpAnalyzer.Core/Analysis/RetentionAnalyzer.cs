using System.Diagnostics;
using MemDumpAnalyzer.Core.Models;
using Microsoft.Diagnostics.Runtime;

namespace MemDumpAnalyzer.Core.Analysis;

/// <summary>
/// Finds who keeps the largest objects alive: the chain from a GC root down to the object, like WinDbg
/// <c>!gcroot</c>. A single dump cannot tell a leak from a big cache, but the chain names the owner.
/// </summary>
/// <remarks>
/// <para>Targets are the largest objects on the heap — usually the backing arrays of the biggest caches,
/// dictionaries and buffers, which is where "why is the heap so big" starts. Roots are searched in the order that explains most:
/// static fields (named), strong and pinned handles, the finalizer queue, and — only when asked — thread stacks,
/// which ClrMD enumerates at ~0.6 s per thread (over an hour on a 7,500-thread dump).</para>
/// <para>One depth-first walk with a shared visited set, so every object is followed at most once whatever the number of
/// roots; it stops when every target has a path. The walk follows plain reference addresses and looks up field names
/// only for the links of a path it found. The path found is the first one, not necessarily the shortest.
/// A time limit stops the search, and the report then says it is partial.</para>
/// </remarks>
public static class RetentionAnalyzer
{
    /// <summary>Objects smaller than this are not worth a path (85,000 bytes and up go to the large object heap).</summary>
    public const long MinObjectBytes = 85_000;
    public static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(60);


    /// <param name="includeStackRoots">Also search thread stacks (slow with many threads), after every other root.</param>
    /// <param name="timeLimit">Stop after this long and report a partial result; null = no limit.</param>
    public static RetentionReport Analyze(
        ClrRuntime runtime,
        HeapWalkResult walk,
        bool includeStackRoots = false,
        TimeSpan? timeLimit = null,
        CancellationToken cancellationToken = default)
    {
        var targets = walk.Largest
            .Where(x => x.Size >= MinObjectBytes)
            .ToDictionary(x => x.Address, x => new RetentionTarget(x.TypeName, x.Address, x.Size));
        string rootsSearched = includeStackRoots
            ? "static fields, handles, finalizer queue, thread stacks"
            : "static fields, handles, finalizer queue (not thread stacks)";
        if (targets.Count == 0)
            return RetentionReport.None with { RootsSearched = rootsSearched, StackRootsSearched = includeStackRoots };

        var watch = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeLimit is { } t) limit.CancelAfter(t);

        // Static fields, handles and the finalizer queue are cheap to list. Knowing which objects they hold directly lets a
        // path that took a detour be re-rooted at the last object on it that a root holds (see Search.Check).
        var roots = EnumerateRoots(runtime).ToList();
        var directlyHeld = new Dictionary<ulong, string>();
        foreach (var (obj, root) in roots)
            directlyHeld.TryAdd(obj.Address, root);

        var search = new Search(runtime.Heap, targets, directlyHeld, limit.Token);
        bool complete = runtime.Heap.CanWalkHeap;
        string? note = complete ? null : "The dump was captured during a GC; references may be incomplete.";

        try
        {
            foreach (var (obj, root) in includeStackRoots ? roots.Concat(EnumerateStackRoots(runtime, limit.Token)) : roots)
            {
                search.From(obj, root);
                if (search.AllFound) break;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            complete = false;
            note = $"Stopped after {timeLimit?.TotalSeconds:N0} s (time limit); the objects without a path may still have one.";
        }

        var bySize = targets.Values.OrderByDescending(x => x.SizeBytes).ToList();
        var notFound = bySize.Where(x => !search.Found.ContainsKey(x.Address)).ToList();
        var paths = bySize.Where(x => search.Found.ContainsKey(x.Address)).Select(x => search.Found[x.Address]).ToList();

        return new RetentionReport(paths, notFound, complete, rootsSearched, includeStackRoots, watch.Elapsed.TotalSeconds, note);
    }

    private static IEnumerable<(ClrObject Object, string Root)> EnumerateRoots(ClrRuntime runtime)
    {
        var domains = runtime.AppDomains;

        // Static fields first: their name is the best explanation ("CacheManager.s_caches").
        foreach (var module in runtime.EnumerateModules())
        {
            IEnumerable<ClrType> types;
            try { types = module.EnumerateTypesWithStaticFields().ToList(); }
            catch { continue; }
            foreach (var type in types)
            {
                foreach (var field in type.StaticFields)
                {
                    if (!field.IsObjectReference) continue;
                    foreach (var domain in domains)
                    {
                        ClrObject value;
                        try { value = field.ReadObject(domain); }
                        catch { continue; }
                        if (value.IsValid)
                            yield return (value, $"static {type.Name}.{field.Name}");
                    }
                }
            }
        }

        // Strong handles, strongest explanation first. On .NET Framework, statics live in object[] arrays held by
        // pinned handles — those objects are already visited by now, so the named static wins.
        var handles = runtime.EnumerateHandles()
            .Where(h => h.HandleKind is ClrHandleKind.Strong or ClrHandleKind.Pinned or ClrHandleKind.AsyncPinned
                            or ClrHandleKind.SizedRef
                        || h.HandleKind == ClrHandleKind.RefCounted && h.ReferenceCount > 0)
            .OrderBy(h => h.HandleKind switch
            {
                ClrHandleKind.Strong => 0,
                ClrHandleKind.SizedRef => 1,
                ClrHandleKind.RefCounted => 2,
                ClrHandleKind.Pinned => 3,
                _ => 4
            })
            .ToList();
        foreach (var handle in handles)
            yield return (handle.Object, handle.HandleKind == ClrHandleKind.Pinned && handle.Object.Type?.Name == "System.Object[]"
                ? "Pinned handle (on .NET Framework: an array holding static fields)"
                : $"{handle.HandleKind} handle");

        foreach (var root in runtime.Heap.EnumerateFinalizerRoots())
            yield return (root.Object, "finalizer queue (waiting for its finalizer to run)");
    }

    private static IEnumerable<(ClrObject Object, string Root)> EnumerateStackRoots(ClrRuntime runtime, CancellationToken cancellationToken)
    {
        foreach (var thread in runtime.Threads.Where(t => t.IsAlive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var root in thread.EnumerateStackRoots())
            {
                var frame = root.StackFrame;
                var where = frame?.Method?.Signature ?? frame?.FrameName ?? "unknown frame";
                yield return (root.Object, $"stack of thread {thread.ManagedThreadId} (OS {thread.OSThreadId}) in {where}");
            }
        }
    }

    /// <summary>Depth-first search from each root in turn, sharing one visited set.</summary>
    private sealed class Search(
        ClrHeap heap,
        Dictionary<ulong, RetentionTarget> targets,
        Dictionary<ulong, string> directlyHeld,
        CancellationToken cancellationToken)
    {
        private static readonly IEnumerable<ulong> NoReferences = [];
        private readonly VisitedSet _visited = new(heap);
        private readonly List<(ClrObject Object, IEnumerator<ulong> References)> _stack = [];
        private readonly bool _carefully = !heap.CanWalkHeap;
        private int _steps;

        public Dictionary<ulong, RetentionPath> Found { get; } = new();
        public bool AllFound => Found.Count == targets.Count;

        public void From(ClrObject root, string rootDescription)
        {
            if (root.IsNull || !root.IsValid || !_visited.Add(root.Address)) return;
            Push(root);
            Check(root.Address, rootDescription);

            try
            {
                while (_stack.Count > 0 && !AllFound)
                {
                    if ((++_steps & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();

                    var top = _stack[^1];
                    bool moved;
                    try { moved = top.References.MoveNext(); }
                    catch (Exception e) when (e is not OperationCanceledException) { moved = false; }   // unreadable object
                    if (!moved)
                    {
                        top.References.Dispose();
                        _stack.RemoveAt(_stack.Count - 1);
                        continue;
                    }

                    ulong address = top.References.Current;
                    if (address == 0 || !_visited.Add(address)) continue;
                    var child = heap.GetObject(address);
                    if (!child.IsValid) continue;
                    Push(child);
                    Check(address, rootDescription);
                }
            }
            finally
            {
                foreach (var frame in _stack) frame.References.Dispose();
                _stack.Clear();
            }
        }

        private void Push(ClrObject obj) =>
            _stack.Add((obj, (obj.ContainsPointers
                ? obj.EnumerateReferenceAddresses(_carefully, considerDependantHandles: true)
                : NoReferences).GetEnumerator()));

        /// <summary>When the object on top of the stack is a target, the stack is its path.</summary>
        private void Check(ulong address, string rootDescription)
        {
            if (!targets.TryGetValue(address, out var target) || Found.ContainsKey(address)) return;

            // Depth-first paths can take a long detour before reaching the object's real owner. Start the path at the
            // last object on it that a static field or handle holds directly — that root explains it best.
            int start = 0;
            for (int i = _stack.Count - 1; i > 0; i--)
            {
                if (directlyHeld.TryGetValue(_stack[i].Object.Address, out var held))
                {
                    start = i;
                    rootDescription = held;
                    break;
                }
            }

            var chain = new List<RetentionLink>(_stack.Count - start);
            for (int i = start; i < _stack.Count; i++)
            {
                var obj = _stack[i].Object;
                chain.Add(new RetentionLink(obj.Type?.Name ?? "<unknown>", obj.Address, i == start ? null : Via(_stack[i - 1].Object, obj.Address)));
            }
            int omitted = 0;
            if (chain.Count > RetentionPath.MaxLinks)
            {
                omitted = chain.Count - RetentionPath.KeptHeadLinks - RetentionPath.KeptTailLinks;
                chain = [.. chain.Take(RetentionPath.KeptHeadLinks), .. chain.TakeLast(RetentionPath.KeptTailLinks)];
            }
            Found[address] = new RetentionPath(target.TypeName, target.Address, target.SizeBytes, rootDescription, chain, omitted);
        }

        /// <summary>How the parent refers to the child: ".field", "[]" or "[].field" for struct elements, a dependent handle.</summary>
        private string Via(ClrObject parent, ulong child)
        {
            try
            {
                foreach (var reference in parent.EnumerateReferencesWithFields(_carefully, considerDependantHandles: true))
                {
                    if (reference.Object.Address != child) continue;
                    if (reference.IsDependentHandle) return "(dependent handle)";
                    var via = reference.IsArrayElement ? "[]" : reference.Field is { } f ? "." + f.Name : "";
                    for (var inner = reference.InnerField; inner is { } i; inner = i.InnerField)
                        if (i.Field is { } innerField) via += "." + innerField.Name;
                    return via.Length > 0 ? via : "(reference)";
                }
            }
            catch (Exception)
            {
                // Field layout unreadable: show the link without a name
            }
            return "(reference)";
        }
    }

    /// <summary>One bit per possible object start, per heap segment (allocated on first use).</summary>
    private sealed class VisitedSet
    {
        private readonly (ulong Start, ulong End, int Index)[] _segments;
        private readonly ulong[]?[] _bits;
        private readonly int _shift;
        private readonly HashSet<ulong> _outside = [];   // objects outside every segment (should not happen)

        public VisitedSet(ClrHeap heap)
        {
            _segments = heap.Segments
                .Select((s, i) => (s.ObjectRange.Start, s.ObjectRange.End, i))
                .OrderBy(s => s.Start)
                .ToArray();
            _bits = new ulong[]?[_segments.Length];
            _shift = heap.Runtime.DataTarget.DataReader.PointerSize == 8 ? 3 : 2;   // objects are pointer-aligned
        }

        /// <summary>True when the address was not visited before.</summary>
        public bool Add(ulong address)
        {
            int lo = 0, hi = _segments.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                var s = _segments[mid];
                if (address < s.Start) hi = mid - 1;
                else if (address >= s.End) lo = mid + 1;
                else
                {
                    var words = _bits[s.Index] ??= new ulong[(((s.End - s.Start) >> _shift) >> 6) + 1];
                    ulong bit = (address - s.Start) >> _shift;
                    ref ulong word = ref words[bit >> 6];
                    ulong mask = 1UL << (int)(bit & 63);
                    if ((word & mask) != 0) return false;
                    word |= mask;
                    return true;
                }
            }
            return _outside.Add(address);
        }
    }
}
