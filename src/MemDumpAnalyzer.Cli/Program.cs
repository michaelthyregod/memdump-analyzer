using System.CommandLine;
using MemDumpAnalyzer.Cli;
using MemDumpAnalyzer.Cli.Commands;
using Spectre.Console;

try
{
    NativeDependencies.Prepare();   // single-exe builds only; a no-op otherwise
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException)
{
    AnsiConsole.MarkupLine($"[yellow]⚠ PDF support unavailable: {Markup.Escape(ex.Message)}[/]");
}

var rootCommand = new RootCommand("memdump-analyzer — .NET memory dump analysis tool");

rootCommand.Add(AnalyzeCommand.Build());
rootCommand.Add(DiffCommand.Build());

// Commands report failure through Environment.ExitCode (1 = error, 130 = Ctrl+C); InvokeAsync returns 0
// for an action that completes, and returning that from Main would overwrite the command's exit code.
int exitCode = await rootCommand.Parse(args).InvokeAsync();
return exitCode != 0 ? exitCode : Environment.ExitCode;
