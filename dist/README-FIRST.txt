memdump-analyzer — Windows x64
==============================

One self-contained executable: no .NET installation needed. Windows 10/11 x64 (not ARM).
Built from {{COMMIT}} on {{DATE}}.

Get started
-----------
1. Unzip anywhere. You can run the exe from any folder: skins\, mem-dumps\ and known-assemblies.txt
   next to the exe are always found.
2. Put full dumps (.dmp) in mem-dumps\ — see mem-dumps\README.md for how to capture them.
   Take 2-3 dumps about a minute apart when you can.
3. Run:

   MemDumpAnalyzer.Cli.exe analyze w3wp.dmp --format html,md,json,pdf

   A dump given by name is found in mem-dumps\. Reports go to report\ in the current folder
   (change with --output <folder>).

More examples
-------------
   Compare two dumps of the same process:
   MemDumpAnalyzer.Cli.exe diff w3wp_1.dmp w3wp_2.dmp --format html,md

   Look and feel (skins), with the customer on the report:
   MemDumpAnalyzer.Cli.exe skins list
   MemDumpAnalyzer.Cli.exe analyze w3wp.dmp --format html,pdf --skin northwind --prepared-for "Customer A/S"

   --skin <name> picks the skin folder with that name. The standard look (default) is built in;
   skins\northwind is an example for a fictional company. To make your own, copy it under a new name
   (a new version of the tool replaces the example). Skins kept elsewhere: --skins-dir <folder>.
   Other skins in skins\ (if any) are described in skins\README.md.

Settings (so you don't type the same options every time)
--------------------------------------------------------
   MemDumpAnalyzer.Cli.exe settings show
   MemDumpAnalyzer.Cli.exe settings set skin <name>
   MemDumpAnalyzer.Cli.exe settings add dumpsDirs D:\dumps
   MemDumpAnalyzer.Cli.exe settings set formats html,pdf

   Saved per user in %APPDATA%\memdump-analyzer\settings.json, so they survive a new version of the
   tool. --local saves them in the current folder instead (wins over the per-user file, but can be
   overwritten if a new version is unpacked over that folder).

   All options:   MemDumpAnalyzer.Cli.exe analyze --help
   Full guide:    docs\usage.md      Skins: docs\skins.md

Good to know
------------
- known-assemblies.txt decides which assemblies count as "your code" in the hotspots section
  (framework, Azure, Entity Framework and Sitecore are hidden). Edit it freely.
- Dumps and reports contain production data (connection strings, session tokens, customer data).
  Keep them on your machine or in approved storage.
- The first analysis may download the matching .NET debugging component (DAC) from the Microsoft
  symbol server, so internet access can be needed.
- The PDF report uses QuestPDF, whose licence is not open source (see THIRD-PARTY-NOTICES.md); its
  native parts are unpacked to %LOCALAPPDATA%\memdump-analyzer\native on first use. HTML, Markdown
  and JSON don't use it.
- Exit code 0 = success, 1 = error.
