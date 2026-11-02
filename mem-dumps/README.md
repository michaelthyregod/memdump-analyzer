# mem-dumps/

Drop `.dmp` memory dump files here before running the analyzer.

```bash
dotnet run --project src/MemDumpAnalyzer.Cli -- analyze mem-dumps/your-dump.dmp
```

## How to capture a dump

**Task Manager** — right-click a process → *Create dump file*

**ProcDump** (recommended for production):
```cmd
# Capture immediately
procdump -ma w3wp.exe mem-dumps\w3wp.dmp

# Capture on high CPU (>80% for 5 s)
procdump -ma -c 80 -s 5 w3wp.exe mem-dumps\w3wp_highcpu.dmp

# Capture on first-chance OutOfMemoryException
procdump -ma -e 1 -f OutOfMemoryException w3wp.exe mem-dumps\w3wp_oom.dmp
```

**WinDbg / Visual Studio** can also save a full minidump via *Debug → Save Dump As*.

## Capture more than one

A single dump can't show progress — slowness, high CPU and leaks all need **at least two dumps** of the same process a minute or so apart ([makingitfaster.tips](https://makingitfaster.tips/2019/10/09/what-memory-dump-is/)).

```cmd
# 3 dumps when CPU stays above 80% for 2 s (from the KeepSitecoreSimple wiki)
procdump64.exe -c 80 -s 2 -ma w3wp.exe -w -n 3

# 3 dumps 60 s apart, no trigger
procdump64.exe -ma -n 3 -s 60 w3wp.exe
```

Compare them with `diff` (heap growth) or WinDbg `!runaway` (CPU per thread, which only means something as a difference between two dumps).

If the report says the dump was **captured while a GC was running**, the heap couldn't be read completely — capture another one. If that keeps happening, the process is collecting garbage almost all the time: collect a PerfView/ETW allocation trace.

Make sure the disk has at least 3× the process memory free, and on Azure Web Apps copy the dump to `D:\home` before downloading.

## Notes

- Use **full dumps** (`-ma` in ProcDump, or "Full" in Task Manager). Mini-dumps do not include the managed heap and will produce incomplete analysis.
- For diff/comparison analysis, capture two dumps of the same process a few minutes apart and use the `diff` command.
- Dump files are excluded from Git (see `.gitignore`) — they are often several GB and may contain sensitive data such as connection strings or session tokens present in the heap at capture time.
