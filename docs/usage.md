# Using memdump-analyzer

Examples use the published `MemDumpAnalyzer.Cli.exe`. From a clone, replace it with
`dotnet run --project src/MemDumpAnalyzer.Cli --`.

Run from the folder that holds `known-assemblies.txt` (and your `skins\`), because both are found
relative to the current folder.

## 1. Capture dumps

Full dumps only (`procdump -ma`, or Task Manager → *Create dump file*). Slowness, high CPU and memory
growth need **two or three dumps about a minute apart** — a single dump is one moment in time.

```cmd
procdump64.exe -ma -n 3 -s 60 w3wp.exe mem-dumps\w3wp.dmp        :: 3 dumps, 60 s apart
procdump64.exe -c 80 -s 2 -ma -n 3 w3wp.exe mem-dumps\cpu.dmp    :: 3 dumps when CPU > 80% for 2 s
```

More in [mem-dumps/README.md](../mem-dumps/README.md).

## 2. Analyse one dump

```cmd
:: HTML and JSON (the default formats) into report\
MemDumpAnalyzer.Cli.exe analyze mem-dumps\w3wp.dmp

:: Every format, to a folder of your choice
MemDumpAnalyzer.Cli.exe analyze mem-dumps\w3wp.dmp --format html,md,json,pdf --output reports\2026-11-02

:: Your own assembly filter (otherwise known-assemblies.txt in the current folder is used)
MemDumpAnalyzer.Cli.exe analyze mem-dumps\w3wp.dmp --known-assemblies my-filter.txt

:: More heap types in the tables (default 100)
MemDumpAnalyzer.Cli.exe analyze mem-dumps\w3wp.dmp --top 300
```

| Option | Short | Meaning |
|---|---|---|
| `--format` | `-f` | Comma-separated: `html`, `md`, `json`, `pdf`. Default `html,json`. |
| `--output` | `-o` | Output folder. Default `.\report`. |
| `--known-assemblies` | `-k` | Assembly filter file for the *Application code hotspots* section. |
| `--top` | | Number of heap types listed. Default 100. |
| `--skin` | `-s` | Look and feel — see below. |
| `--prepared-for` | | "Prepared for" line, e.g. the customer. |

## 3. Compare two dumps

```cmd
MemDumpAnalyzer.Cli.exe diff mem-dumps\w3wp_1.dmp mem-dumps\w3wp_2.dmp --format html,md,json
```

All types are compared; `--top` (default 100) only limits the rows shown. Types that keep growing
between the dumps are the leak candidates. The diff report takes `--skin` and `--prepared-for` too
(no PDF).

## 4. Skins: look and feel

```cmd
:: Built-in reference skin for a fictional company
MemDumpAnalyzer.Cli.exe analyze mem-dumps\w3wp.dmp --format html,pdf --skin northwind --prepared-for "Example Customer A/S"

:: A skin folder (contains skin.json, optional logo and fonts)
MemDumpAnalyzer.Cli.exe analyze mem-dumps\w3wp.dmp --format html,pdf --skin skins\my-company
```

Built-in skins: `default` (no `--skin` needed) and `northwind`. How to make your own: [skins.md](skins.md).

## 5. Choose which assemblies count as "your code"

`known-assemblies.txt` hides framework and platform assemblies, so the *Application code hotspots*
section shows the code you can change. The shipped file hides .NET, ASP.NET, Azure, Entity Framework
and Sitecore (but keeps `Sitecore.SessionProvider` visible).

```text
# comment
-Sitecore                  hide every assembly starting with "Sitecore"
+Sitecore.SessionProvider  …but show this one (include rules win)
StackExchange.Redis        a bare prefix also hides
```

## 6. Read the results

- **Health score and findings** first: each finding has an explanation, evidence, a recommendation
  and the full technical details (thread stacks, lock sites, heap tables).
- **Thread patterns** group live threads by what they are doing; the largest groups usually tell the
  story (lock contention, waiting on SQL/Redis/HTTP, blocking on async code).
- A banner says when the dump was captured during a garbage collection — heap numbers are then
  partial; capture another dump.
- What the rules are based on: the "What it's based on" section of the [Readme](../Readme.md).

## Exit codes

`0` success, `1` error (dump not found or unreadable, a report format failed, bad skin), `130` cancelled.

## Keep dumps safe

Dumps and reports contain production data (connection strings, session tokens, customer data).
Keep them on your machine or in approved storage, and never commit them.
