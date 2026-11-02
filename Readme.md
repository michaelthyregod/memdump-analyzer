# memdump-analyzer

A command-line tool that reads a Windows memory dump (`.dmp`) of a .NET Framework 4.x or .NET (Core) process — typically an IIS `w3wp` running Sitecore — and turns it into a readable report: what the threads are doing, what fills the heap, and what to look at first. Reports come as HTML, Markdown, JSON and PDF.

## Usage

```bash
# Single dump — auto-detects known-assemblies.txt in the current directory
MemDumpAnalyzer.Cli.exe analyze mem-dumps/dump.dmp --format html,json,md,pdf

# With an explicit assembly filter file
MemDumpAnalyzer.Cli.exe analyze mem-dumps/dump.dmp --known-assemblies known-assemblies.txt

# Compare two dumps of the same process taken a minute or so apart
MemDumpAnalyzer.Cli.exe diff mem-dumps/baseline.dmp mem-dumps/problem.dmp
```

See [mem-dumps/README.md](mem-dumps/README.md) for how to capture good dumps.

## License

memdump-analyzer is released under the [MIT License](LICENSE): use it, fork it and change it freely, at
your own risk. It is provided "as is", without any warranty, and the authors are not liable for how it
is used. Third-party components keep their own licences — see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Note that the PDF report uses **QuestPDF**, whose
licence is not open source and may require a paid licence for larger organisations; the HTML,
Markdown and JSON reports don't use it.

Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).

## Report skins

`--skin` sets the look and feel of the HTML, PDF and diff reports: colours, fonts, logo and texts.
Use a built-in skin (`default`, `northwind`) or a folder with your own `skin.json`, and add the
customer with `--prepared-for`:

```bash
MemDumpAnalyzer.Cli.exe analyze mem-dumps/dump.dmp --format html,pdf --skin northwind --prepared-for "Example Customer A/S"
```

See [docs/skins.md](docs/skins.md) for the format, font fallbacks and font licensing.

## What it's based on

The analysis approach and most rules of thumb come from **Nikolay Mitikov**:

- his blog **[makingitfaster.tips](https://makingitfaster.tips/)** — in particular the WinDbg series on [threads](https://makingitfaster.tips/2019/11/13/windbg-commands-threads/) and [memory](https://makingitfaster.tips/2019/11/14/windbg-commands-memory/), [What memory dump is](https://makingitfaster.tips/2019/10/09/what-memory-dump-is/), [Tackling memory leak](https://makingitfaster.tips/2019/10/28/tackling-memory-leak/), [Myth busting: memory snapshots](https://makingitfaster.tips/2021/01/15/myth-busting-memory-snapshots/) and [Performance crime: non-unique strings](https://makingitfaster.tips/2023/09/06/performance-crime-non-unique-strings/);
- his GitHub work at **[github.com/mitikov](https://github.com/mitikov)** — the [KeepSitecoreSimple wiki](https://github.com/mitikov/KeepSitecoreSimple/wiki) on collecting and reading memory snapshots, and Sitecore.MemoryDiagnostics (thread filtering: live, non-system threads only).

The rest is the author's own experience from analysing production Sitecore dumps. Dumps are read with [ClrMD](https://github.com/microsoft/clrmd).

### Where each rule comes from

| Finding | Rule | Source |
|---|---|---|
| `HighThreadCount` | More than 100 live threads (critical above 200). Read together with CPU: low CPU means threads wait on a dependency (SQL, Redis, web APIs). Dead threads are not counted. | makingitfaster.tips ("Over 100 threads & low CPU likely flags a bottleneck in dependent resource"); 200 = own experience |
| `HighCpuAtCapture` | Thread-pool CPU ≥ 80%; exactly 81% likely means GC is running. CPU is system-wide. | makingitfaster.tips ("81 % CPU flags garbage collection is running") |
| `GcInProgressAtCapture` | Heap not walkable — the snapshot caught a GC; heap numbers are partial. | makingitfaster.tips (snapshots catching GC running) |
| `ThreadPoolStarvation` | No idle workers and active ≥ min threads: the pool only grows slowly from there. | CLR thread-pool behaviour |
| `LockContention` | 10+ threads waiting in `Monitor.Enter` or a synchronous `SemaphoreSlim.Wait` (critical at 50), grouped by the lock site. One dump cannot prove a deadlock. | Own experience; "threads inside lock statements" from makingitfaster.tips |
| `HighLiveObjectCount` | One type over 200 MB. A leak needs two dumps — use `diff`. | makingitfaster.tips (Tackling memory leak) |
| `LargeObjectHeapGrowth`, `LohFragmentationHigh` | LOH over 85 MB; free space over 30% of the LOH. Fragmentation is measured from free objects. Forced LOH compaction is not recommended. | makingitfaster.tips ("Free shows memory fragmentation"; forced compaction "a performance anti-pattern"); thresholds = own experience |
| `DuplicateStringWaste` | Over 50 MB wasted by duplicate string values; deduplicate selectively. | makingitfaster.tips (Non-unique strings); threshold = own experience |

### What a single dump can and cannot tell

A dump is one picture. It shows what every thread is doing, what is on the heap and how the caches are filled — but not how fast anything is changing. Slowness, high CPU and leaks need **at least two dumps** a minute or so apart, compared with the `diff` command or WinDbg (`!runaway` for CPU per thread). The findings say so where it matters.
