memdump-analyzer — Windows x64
==============================

One self-contained executable: no .NET installation needed. Windows 10/11 x64 (not ARM).
Built from {{COMMIT}} on {{DATE}}.

Get started
-----------
1. Unzip anywhere and open a terminal IN THIS FOLDER (known-assemblies.txt and skins\ are found
   relative to the current folder).
2. Put full dumps (.dmp) in mem-dumps\ — see mem-dumps\README.md for how to capture them.
   Take 2-3 dumps about a minute apart when you can.
3. Run:

   MemDumpAnalyzer.Cli.exe analyze mem-dumps\w3wp.dmp --format html,md,json,pdf

   Reports go to report\ (change with --output <folder>).

More examples
-------------
   Compare two dumps of the same process:
   MemDumpAnalyzer.Cli.exe diff mem-dumps\w3wp_1.dmp mem-dumps\w3wp_2.dmp --format html,md

   Look and feel (skins), with the customer on the report:
   MemDumpAnalyzer.Cli.exe analyze mem-dumps\w3wp.dmp --format html,pdf --skin northwind --prepared-for "Customer A/S"

   See which skins are available:
   MemDumpAnalyzer.Cli.exe skins list

   --skin <name> uses the folder skins\<name>. The standard look (default) is built in; skins\northwind
   is an example for a fictional company. To make your own, copy it under a new name and edit the copy
   (a new version of the tool replaces the example). Other skins in skins\ (if any) are described in
   skins\README.md.

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
