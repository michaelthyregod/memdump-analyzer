# Using memdump-analyzer

Examples use the published `MemDumpAnalyzer.Cli.exe`. From a clone, replace it with
`dotnet run --project src/MemDumpAnalyzer.Cli --`.

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

:: A dump given by name only is also looked for in the dumpsDirs setting and mem-dumps\ next to the exe
MemDumpAnalyzer.Cli.exe analyze w3wp.dmp

:: Every format, to a folder of your choice
MemDumpAnalyzer.Cli.exe analyze w3wp.dmp --format html,md,json,pdf --output reports\2026-11-02

:: Your own assembly filter, and more heap types in the tables (default 100)
MemDumpAnalyzer.Cli.exe analyze w3wp.dmp --known-assemblies my-filter.txt --top 300
```

| Option | Short | Meaning | Default |
|---|---|---|---|
| `--format` | `-f` | Comma-separated: `html`, `md`, `json`, `pdf` | `formats` setting, else `html,json` |
| `--output` | `-o` | Output folder | `outputDir` setting, else `.\report` |
| `--known-assemblies` | `-k` | Assembly filter for *Application code hotspots* | `knownAssemblies` setting, else `known-assemblies.txt` in the current folder, else next to the exe |
| `--top` | | Number of heap types listed | `top` setting, else 100 |
| `--skin` | `-s` | Skin by name — see below | `skin` setting, else `default` |
| `--skins-dir` | | Extra folder with skins; repeat for more | `skinsDirs` setting |
| `--prepared-for` | | "Prepared for" line, e.g. the customer | — |

## 3. Compare two dumps

```cmd
MemDumpAnalyzer.Cli.exe diff w3wp_1.dmp w3wp_2.dmp --format html,md,json
```

All types are compared; `--top` only limits the rows shown. Types that keep growing between the dumps
are the leak candidates. The diff report takes the skin options too (no PDF).

## 4. Skins: look and feel

```cmd
:: Which skins are there, and where they come from
MemDumpAnalyzer.Cli.exe skins list

:: Choose one by name (the name of its folder)
MemDumpAnalyzer.Cli.exe analyze w3wp.dmp --format html,pdf --skin northwind --prepared-for "Example Customer A/S"

:: Skins kept somewhere else: add the folder that contains them
MemDumpAnalyzer.Cli.exe analyze w3wp.dmp --skins-dir D:\team-skins --skin acme
```

- `default` is built in and is used when no skin is chosen.
- Skins are looked for in this order; the first folder with the name wins: `--skins-dir` folders (in the
  order given), then the `skinsDirs` setting, then `skins\` next to the exe.
- `skins\northwind` is an example for a fictional company. Copy it under a new name to start your own
  skin — don't edit it in place, a new version of the tool replaces it. Deleting it is fine.

How skins work: [skins.md](skins.md).

## 5. Settings

Settings save you from typing the same options every time. They live in two JSON files:

| File | Where | Changed with |
|---|---|---|
| per user | `%APPDATA%\memdump-analyzer\settings.json` | `settings set/add/remove/reset …` |
| local | `memdump-analyzer.settings.json` in the current folder | the same, plus `--local` |

Command-line options win over the local file, the local file over the per-user file, and that over the
built-in defaults. A file holds only what differs from the defaults, and is removed when nothing does.

```cmd
MemDumpAnalyzer.Cli.exe settings show                                   :: all settings, values and where they come from
MemDumpAnalyzer.Cli.exe settings add skinsDirs D:\team-skins             :: an extra skins folder
MemDumpAnalyzer.Cli.exe settings set skin acme                           :: skin used when --skin isn't given
MemDumpAnalyzer.Cli.exe settings add dumpsDirs D:\dumps                  :: where dumps given by name are found
MemDumpAnalyzer.Cli.exe settings set formats html,pdf
MemDumpAnalyzer.Cli.exe settings set preparedBy "Performance Team"
MemDumpAnalyzer.Cli.exe settings set top 200 --local                     :: only when run from this folder
MemDumpAnalyzer.Cli.exe settings remove skinsDirs D:\team-skins
MemDumpAnalyzer.Cli.exe settings reset skin                              :: one setting back to default
MemDumpAnalyzer.Cli.exe settings reset                                   :: everything back to default
```

| Setting | Meaning |
|---|---|
| `skinsDirs` | Folders containing skin folders (list) |
| `skin` | Skin used when `--skin` isn't given |
| `preparedBy` | "Prepared by" line on reports |
| `formats` | Report formats when `--format` isn't given |
| `outputDir` | Output folder when `--output` isn't given |
| `knownAssemblies` | Assembly filter file when `--known-assemblies` isn't given |
| `top` | Heap types listed (analyze) / rows per table (diff) |
| `dumpsDirs` | Folders searched for a dump given by name (list) |

Lists combine: entries from the local file come first, then the per-user ones. Relative paths in a
settings file are relative to that file's folder.

> A local settings file in the folder you unpacked the tool into can be overwritten when a new version
> is unpacked over it — prefer the per-user settings, or keep a copy.

## 6. Choose which assemblies count as "your code"

`known-assemblies.txt` hides framework and platform assemblies, so the *Application code hotspots*
section shows the code you can change. The shipped file hides .NET, ASP.NET, Azure, Entity Framework
and Sitecore (but keeps `Sitecore.SessionProvider` visible).

```text
# comment
-Sitecore                  hide every assembly starting with "Sitecore"
+Sitecore.SessionProvider  …but show this one (include rules win)
StackExchange.Redis        a bare prefix also hides
```

## 7. Read the results

- **Health score and findings** first: each finding has an explanation, evidence, a recommendation
  and the full technical details (thread stacks, lock sites, heap tables).
- **Thread patterns** group live threads by what they are doing; the largest groups usually tell the
  story (lock contention, waiting on SQL/Redis/HTTP, blocking on async code).
- A banner says when the dump was captured during a garbage collection — heap numbers are then
  partial; capture another dump.
- What the rules are based on: the "What it's based on" section of the [Readme](../Readme.md).

## Exit codes

`0` success, `1` error (dump not found or unreadable, a report format failed, bad skin or settings), `130` cancelled.

## Keep dumps safe

Dumps and reports contain production data (connection strings, session tokens, customer data).
Keep them on your machine or in approved storage, and never commit them.
