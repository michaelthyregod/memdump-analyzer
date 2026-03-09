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

## Notes

- Use **full dumps** (`-ma` in ProcDump, or "Full" in Task Manager). Mini-dumps do not include the managed heap and will produce incomplete analysis.
- For diff/comparison analysis, capture two dumps of the same process a few minutes apart and use the `diff` command.
- Dump files are excluded from Git (see `.gitignore`) — they are often several GB and may contain sensitive data such as connection strings or session tokens present in the heap at capture time.
