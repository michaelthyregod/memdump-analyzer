# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

`memdump-analyzer` is a CLI tool that loads Windows .NET Framework 4.x / .NET Core process memory dumps (`.dmp`), runs automated analyses, and produces multi-audience reports (HTML, Markdown, JSON, PDF). Supports single-dump analysis and two-dump comparison (diff) mode.

The heuristics are based on Nikolay Mitikov's https://makingitfaster.tips/ and https://github.com/mitikov, plus the owner's own experience — see the "What it's based on" table in `Readme.md`. Consult those sources before changing a rule, and keep the table in `Readme.md` in sync with `FindingEngine`. Sitecore.MemoryDiagnostics has no license: take ideas, don't copy code.

## Solution Structure

```
memdump-analyzer/
├── memdump-analyzer.slnx
├── known-assemblies.txt           # Assembly filter for hotspot analysis (checked in, edit freely)
├── src/
│   ├── MemDumpAnalyzer.Core/
│   │   ├── DumpLoader.cs              # Opens .dmp via ClrMD, validates runtime, reads capture time from the minidump header
│   │   ├── AnalysisEngine.cs          # Orchestrates all analyzers; accepts knownAssembliesPath
│   │   ├── Analysis/
│   │   │   ├── HeapWalker.cs          # ONE heap pass: type stats, strings, samples, free bytes; best-effort when mid-GC
│   │   │   ├── ThreadAnalyzer.cs      # Full stacks; ClassifyWait → WaitKind; groups live threads; reads thread pool
│   │   │   ├── HeapAnalyzer.cs
│   │   │   ├── GcAnalyzer.cs
│   │   │   ├── StringAnalyzer.cs
│   │   │   ├── LeakDetector.cs
│   │   │   ├── DiffAnalyzer.cs
│   │   │   └── ApplicationHotspotAnalyzer.cs  # Filters assemblies not in known list
│   │   ├── Models/
│   │   │   ├── AnalysisResult.cs      # Includes ApplicationHotspots + KnownAssemblyFilters
│   │   │   ├── ThreadInfo.cs          # + IsAlive, IsFinalizer, WaitKind
│   │   │   ├── ThreadGroup.cs         # Thread cluster (stack pattern + count + wait reason)
│   │   │   ├── ThreadPoolStats.cs     # !threadpool data: CPU, min/max, active/idle workers
│   │   │   ├── HeapTypeStats.cs
│   │   │   ├── GcStats.cs
│   │   │   ├── Finding.cs             # Includes TechnicalDetails (full pre-formatted stacks)
│   │   │   ├── AssemblyHotspot.cs
│   │   │   └── DiffResult.cs
│   │   └── Heuristics/
│   │       └── FindingEngine.cs       # Builds rich TechnicalDetails for every finding
│   ├── MemDumpAnalyzer.Reporting/
│   │   ├── HtmlReporter.cs            # Scriban + LoopLimit=0
│   │   ├── MarkdownReporter.cs
│   │   ├── JsonReporter.cs            # CamelCase, source-generated (ReportJsonContext)
│   │   ├── PdfReporter.cs             # QuestPDF, no TechnicalDetails truncation
│   │   └── EmbeddedTemplate.cs        # Full Scriban HTML template (snake_case properties)
│   └── MemDumpAnalyzer.Cli/
│       ├── Program.cs
│       ├── ErrorReporting.cs          # Actionable messages for dump-loading failures
│       └── Commands/
│           ├── AnalyzeCommand.cs      # --known-assemblies option; auto-detects known-assemblies.txt
│           └── DiffCommand.cs
├── tests/
│   └── MemDumpAnalyzer.Tests/
│       ├── FindingEngineTests.cs
│       ├── HeuristicsTests.cs         # Wait classification, thread/lock/GC rules, HTML escaping
│       ├── DiffAnalyzerTests.cs
│       └── ReportingTests.cs
└── mem-dumps/                         # Drop .dmp files here
```

## Build & Test

```bash
dotnet build
dotnet test
```

## Run

```bash
# Single dump — auto-detects known-assemblies.txt in cwd
dotnet run --project src/MemDumpAnalyzer.Cli -- analyze mem-dumps/dump.dmp --format html,json,md,pdf

# With explicit filter file
dotnet run --project src/MemDumpAnalyzer.Cli -- analyze mem-dumps/dump.dmp --known-assemblies known-assemblies.txt

# Diff mode
dotnet run --project src/MemDumpAnalyzer.Cli -- diff mem-dumps/baseline.dmp mem-dumps/problem.dmp
```

## known-assemblies.txt Filter Format

Controls which assemblies appear in the "Application Code Hotspots" report section.
- `-Prefix` (or bare `Prefix`) — exclude: treat as known infrastructure, hide from hotspots
- `+Prefix` — include: force visible even if a broader exclude would hide it (takes priority)
- `#` or `//` lines are comments; blank lines ignored

Example — hide all Sitecore but surface one specific assembly:
```
-Sitecore
+Sitecore.SessionProvider
```

## Report Sections

Every HTML/Markdown/PDF/JSON report contains:
1. Executive Summary (metadata + health score)
2. Findings — severity-ordered, each with Summary, Explanation, Evidence, Recommendation, and **full** TechnicalDetails (complete thread stacks, no truncation)
3. Memory Overview
4. Top Memory Consumers
5. Thread Analysis (grouped by stack pattern with wait-reason detection)
6. GC Health
7. Findings Detail
8. **Application Code Hotspots** — assemblies not suppressed by known-assemblies.txt, ranked by thread count with top methods

## Key Design Decisions

- **No frame truncation**: `ThreadAnalyzer` collects the full stack (no `.Take()` limit). `FindingEngine` emits all frames in `TechnicalDetails`. PDF also shows full details.
- **Thread grouping**: Live threads (dead ones are excluded — a w3wp can carry thousands) are fingerprinted by their top 8 meaningful frames, grouped, and sorted by count. The `ThreadGroup` model carries `WaitReason`, representative stack, and sample OS thread IDs.
- **Wait classification** (`ThreadAnalyzer.ClassifyWait`): a wait primitive (Monitor.Enter/Wait, WaitHandle, Sleep, socket/SNI I/O) must be in the top 8 meaningful frames, otherwise the thread counts as running. Then the first context frame top-down (Task.Wait/.Result, SqlClient, StackExchange.Redis, SolrNet, HttpWebRequest, Sitecore idle-worker loops) decides the `WaitKind`. Monitor.Enter and a synchronous `SemaphoreSlim.Wait(` (under the Monitor.Wait that implements it) are `LockContention` unless an idle-worker context matches; the lock site is the first frame below the Monitor/SemaphoreSlim frames. `BlockingReason` is null for running threads and idle workers.
- **Mid-GC dumps**: `CanWalkHeap == false` means the snapshot caught a GC. The walk continues best-effort, `AnalysisResult.HeapWalkComplete` is false, findings say "partial heap walk", and every report shows a banner.
- **Single dump ≠ trend**: no leak findings from one dump (use `diff`); lock contention is never called a deadlock. Diff compares all types (`topN = int.MaxValue`), not the top N.
- **Fragmentation** is free-object bytes / allocated range (what `!dumpheap -stat` lists as Free), not committed-vs-range.
- **HTML template**: Scriban does not auto-escape — every text value must go through `| html.escape` (type names contain `<>`, heap strings contain arbitrary content).
- **TechnicalDetails per finding**: `FindingEngine` builds rich multi-line preformatted blocks — blocked thread stacks with assembly names (`[AssemblyName]` suffix), thread pattern tables, LOH segment lists, etc.
- **Scriban LoopLimit = 0**: Required for real dumps with thousands of threads.

## Trim / AOT Compatibility

All projects build with `IsAotCompatible` + trim/AOT analyzers enabled — keep new code warning-free:

- **JSON**: never use reflection-based `JsonSerializer.Serialize(obj, options)`. All report root types are registered in `ReportJsonContext` (source-generated, camelCase, string enums); add new root types there with `[JsonSerializable]`.
- **Scriban**: reads the model graph via reflection at render time, which the trimmer can't see. `MemDumpAnalyzer.Reporting/ILLink.Descriptors.xml` (embedded resource) preserves all of `MemDumpAnalyzer.Core` — new model types are covered automatically. Pass the model via `scriptObj["r"] = result` (no reflection), not `Import(anonymousObject)`. Delegate `Import("name", Func<...>)` calls are fine; their IL2026 is suppressed on `HtmlReporter.Render`.
- **Spectre.Console**: `AnsiConsole.WriteException` is not AOT compatible — print `Markup.Escape(ex.ToString())` instead.
- One known residual warning at trimmed publish: IL2075 inside `QuestPDF.Drawing.Proxy.LayoutDebugging` (library-internal, layout-debugging path only).

## Key Dependencies

| Package | Purpose |
|---|---|
| `Microsoft.Diagnostics.Runtime` 4.0.x (ClrMD) | Read .dmp files |
| `System.CommandLine` 3.0.0-preview.4 | CLI argument parsing |
| `Scriban` 6.x | HTML report templating |
| `QuestPDF` 2026.x | PDF generation (Community license) |
| `Spectre.Console` | Rich terminal output |

## ClrMD v3.1 API Notes

These properties changed from v2 to v3 — use these:

- `ClrThread.IsGc` (not `IsGC`), `ClrThread.LockCount` (no `BlockingObjects`), no `IsBackground`
- `ClrSegment.Kind` (type `GCSegmentKind`): `.Large`, `.Pinned`, `.Frozen`, `.Ephemeral`, `.Generation0/1/2`
- No `ClrHeap.GetGeneration()` — use `heap.GetSegmentByAddress(addr)?.Kind` instead
- `ClrSegment.CommittedMemory.Length`, `ClrSegment.ObjectRange`, `ClrSegment.Generation0/1/2` (sub-ranges on Ephemeral)
- String value from object: `clrObject.AsString(int.MaxValue)` — `(string)clrObject` and `AsString()` stop at 4,096 chars (verified on a real dump)
- `DataTarget.LoadDump(path)` static, `DataTarget.DataReader.DisplayName`
- `ClrRuntime.Threads` enumerates all threads, **including dead ones** (`thread.IsAlive`); `thread.EnumerateStackTrace()` for frames
- `ClrRuntime.ThreadPool` (nullable): `CpuUtilization`, `MinThreads`, `MaxThreads`, `ActiveWorkerThreads`, `IdleWorkerThreads`, `RetiredWorkerThreads`
- `ClrHeap.CanWalkHeap` false = dump taken mid-GC; `ClrObject.IsFree` marks free (fragmentation) objects
- `ClrHeap.EnumerateSyncBlocks()` exists but gave garbage on a mid-GC dump (every block "1000 waiters", no owner) — don't rely on it
- `ClrStackFrame.Method` (nullable), `ClrStackFrame.FrameName` (for runtime special frames), `ClrStackFrame.InstructionPointer`
- Assembly name from frame: `frame.Method?.Type?.Module?.Name` → pass through `Path.GetFileNameWithoutExtension`

## Scriban Template Notes

- Use native arithmetic: `a * b`, `a / b` — NOT `math.times`, `math.divided_by`
- No `math.max` — use ternary: `val > 0 ? val : 1`
- `math.floor(expr)`, `math.round(expr, decimals)` work fine
- Assignment: `{{ x = value }}` — NOT `{{ assign x = value }}`
- Register `Func<T,TResult>` delegates via `scriptObj.Import("name", delegate)` (not via anonymous object)
- Default `MemberRenamer` converts PascalCase → snake_case automatically; do NOT override it
- `TemplateContext { LoopLimit = 0, LimitToString = 0 }` required for large dumps — `LimitToString` defaults to 1 MiB and silently truncates the rendered output with `...`
- `array.limit N` in template loops for display caps (threads table: 300, heap types: 500, segments: 200)

## System.CommandLine 3.0-preview API Notes

Breaking changes from 2.x beta:
- `Argument<T>(string name)` — single name arg
- `Option<T>(string name, params string[] aliases)` — name first, then aliases
- `cmd.Add(arg/option/subcommand)` replaces `AddArgument`, `AddOption`, `AddCommand`
- `cmd.SetAction(Func<ParseResult, CancellationToken, Task>)` for async handlers
- `parseResult.GetValue(argOrOption)` to retrieve values
- `Option<T> { DefaultValueFactory = _ => value }` for defaults
- `rootCommand.Parse(args).InvokeAsync()` to run
