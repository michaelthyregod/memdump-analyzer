using System.CommandLine;
using MemDumpAnalyzer.Cli.Commands;

var rootCommand = new RootCommand("memdump-analyzer — .NET memory dump analysis tool");

rootCommand.Add(AnalyzeCommand.Build());
rootCommand.Add(DiffCommand.Build());

return await rootCommand.Parse(args).InvokeAsync();
