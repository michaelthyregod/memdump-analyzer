# Single dump — auto-detects known-assemblies.txt in cwd
MemDumpAnalyzer.Cli.exe analyze mem-dumps/dump.dmp --format html,json,md,pdf

# With explicit filter file
MemDumpAnalyzer.Cli.exe analyze mem-dumps/dump.dmp --known-assemblies known-assemblies.txt

# Diff mode
MemDumpAnalyzer.Cli.exe diff mem-dumps/baseline.dmp mem-dumps/problem.dmp